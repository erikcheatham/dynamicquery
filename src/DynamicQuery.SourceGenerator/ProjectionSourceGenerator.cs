using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynamicQuery.SourceGenerator;

/// <summary>
/// Roslyn incremental generator that emits, per <c>[Projection]</c> DTO, the SELECT/FROM strings as
/// compile-time <c>const</c>s plus a <c>[ModuleInitializer]</c> that pre-registers them with
/// <c>DynamicQuery.Core.ProjectionRegistry.RegisterGenerated</c>. The result: a consumer's
/// <c>GetSelectColumns&lt;T&gt;()</c> returns the constant with zero reflection at runtime.
/// </summary>
/// <remarks>
/// <para><b>Byte-identical to the runtime.</b> The string-building here mirrors
/// <c>ProjectionRegistry.Build</c> / <c>BuildSelectColumns</c> / <c>BuildFromClause</c> /
/// <c>BuildPropertyFragment</c> exactly (fragment join = <c>",\n    "</c>; per-join =
/// <c>"\n    LEFT JOIN t a ON on"</c>; fragment formats per attribute). A test pins the generated
/// output against the runtime output so the two can never drift.</para>
/// <para><b>Fail-safe.</b> A DTO the generator cannot emit identically (a property carrying more than
/// one projection-source attribute, or a DTO that would yield zero columns) is skipped — it falls
/// through to the runtime <c>Build</c>, which throws the precise <c>InvalidOperationException</c>.
/// The generator never masks a malformed-DTO diagnostic.</para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ProjectionSourceGenerator : IIncrementalGenerator
{
    private const string ProjectionAttr = "DynamicQuery.Core.ProjectionAttribute";
    private const string LeftJoinAttr   = "DynamicQuery.Core.LeftJoinAttribute";
    private const string ColumnAttr     = "DynamicQuery.Core.ColumnAttribute";
    private const string CoalesceAttr   = "DynamicQuery.Core.CoalesceAttribute";
    private const string JsonbPathAttr  = "DynamicQuery.Core.JsonbPathAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ProjectionAttr,
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) => BuildModel(ctx))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!.Value);

        context.RegisterSourceOutput(models, static (spc, model) => Emit(spc, model));
    }

    // ── transform: symbol → fully-rendered (FQN, select, from) model ──────────────────
    // All-strings model => structural value equality => the incremental pipeline caches it and
    // re-emits only when a DTO's projection shape actually changes.
    private static Model? BuildModel(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type) return null;

        string? table = null, alias = null;
        var joins = new List<(string Table, string Alias, string On)>();

        foreach (var attr in type.GetAttributes())
        {
            switch (attr.AttributeClass?.ToDisplayString())
            {
                case ProjectionAttr when attr.ConstructorArguments.Length >= 2:
                    table = attr.ConstructorArguments[0].Value as string;
                    alias = attr.ConstructorArguments[1].Value as string;
                    break;
                case LeftJoinAttr when attr.ConstructorArguments.Length >= 3:
                    joins.Add((
                        attr.ConstructorArguments[0].Value as string ?? "",
                        attr.ConstructorArguments[1].Value as string ?? "",
                        attr.ConstructorArguments[2].Value as string ?? ""));
                    break;
            }
        }

        if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(alias)) return null;

        // Public instance properties in declaration order — mirrors the runtime's
        // GetProperties(Public | Instance) for the common flat-DTO case. (Inherited members are not
        // walked here; a DTO that annotates base-class properties falls through to the runtime path.)
        var fragments = new List<string>();
        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol p) continue;
            if (p.DeclaredAccessibility != Accessibility.Public || p.IsStatic) continue;

            var frag = Fragment(p, out var malformed);
            if (malformed) return null;     // >1 projection attr on one property → let runtime throw.
            if (frag is not null) fragments.Add(frag);
        }

        if (fragments.Count == 0) return null;   // zero columns → let runtime throw the clear error.

        // SELECT: fragments joined by ",\n    " (BuildSelectColumns).
        var select = string.Join(",\n    ", fragments);

        // FROM: "table alias" + per-join "\n    LEFT JOIN t a ON on" (BuildFromClause).
        var from = new StringBuilder().Append(table).Append(' ').Append(alias);
        foreach (var j in joins)
            from.Append("\n    LEFT JOIN ").Append(j.Table).Append(' ').Append(j.Alias)
                .Append(" ON ").Append(j.On);

        return new Model(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), // typeof target (global::Ns.Type)
            select,
            from.ToString());
    }

    /// <summary>
    /// One property → its SELECT fragment (or null to skip). Precedence + formats mirror
    /// <c>BuildPropertyFragment</c>: [JsonbPath] &gt; [Coalesce] &gt; [Column]; more than one of those
    /// on a single property sets <paramref name="malformed"/> (runtime throws the diagnostic).
    /// </summary>
    private static string? Fragment(IPropertySymbol p, out bool malformed)
    {
        malformed = false;
        AttributeData? jsonb = null, coalesce = null, column = null;

        foreach (var a in p.GetAttributes())
        {
            switch (a.AttributeClass?.ToDisplayString())
            {
                case JsonbPathAttr: jsonb = a; break;
                case CoalesceAttr:  coalesce = a; break;
                case ColumnAttr:    column = a; break;
            }
        }

        var count = (jsonb is null ? 0 : 1) + (coalesce is null ? 0 : 1) + (column is null ? 0 : 1);
        if (count == 0) return null;
        if (count > 1) { malformed = true; return null; }

        var alias = p.Name;

        if (jsonb is not null)
        {
            var col = jsonb.ConstructorArguments[0].Value as string ?? "";
            var idx = jsonb.ConstructorArguments[1].Value;                 // int → ToString() == runtime
            var key = jsonb.ConstructorArguments[2].Value as string ?? "";
            return $"({col}::jsonb -> {idx} ->> '{key}') AS \"{alias}\"";
        }

        if (coalesce is not null)
        {
            var exprs = coalesce.ConstructorArguments[0].Values.Select(v => v.Value as string);
            return $"COALESCE({string.Join(", ", exprs)}) AS \"{alias}\"";
        }

        var expr = column!.ConstructorArguments[0].Value as string ?? "";
        return $"{expr} AS \"{alias}\"";
    }

    // ── emit: model → a generated registrar class with a module initializer ───────────
    private static void Emit(SourceProductionContext spc, Model m)
    {
        var flat = m.FullyQualified
            .Replace("global::", "")
            .Replace('.', '_').Replace('+', '_').Replace('`', '_')
            .Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_');
        var cls = flat + "_DynamicQueryProjection";

        var selectLit = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(m.Select, quote: true);
        var fromLit   = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(m.From,   quote: true);

        var sb = new StringBuilder();
        sb.Append("// <auto-generated/> DynamicQuery.SourceGenerator — do not edit.\n");
        sb.Append("#nullable enable\n");
        sb.Append("namespace DynamicQuery.Generated\n{\n");
        sb.Append("    internal static class ").Append(cls).Append("\n    {\n");
        sb.Append("        public const string SelectColumns = ").Append(selectLit).Append(";\n");
        sb.Append("        public const string FromClause = ").Append(fromLit).Append(";\n\n");
        sb.Append("        [global::System.Runtime.CompilerServices.ModuleInitializer]\n");
        sb.Append("        internal static void Register()\n");
        sb.Append("            => global::DynamicQuery.Core.ProjectionRegistry.RegisterGenerated(\n");
        sb.Append("                typeof(").Append(m.FullyQualified).Append("), SelectColumns, FromClause);\n");
        sb.Append("    }\n}\n");

        spc.AddSource(flat + ".g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private readonly record struct Model(string FullyQualified, string Select, string From);
}
