using System.Reflection;
using DynamicQuery.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DynamicQuery.Core.Tests;

/// <summary>
/// Pins the source generator's emitted SELECT/FROM literals against the runtime
/// <see cref="ProjectionRegistry"/> output for the SAME DTO source. One source string per fixture is
/// the single source of truth: it is (a) fed to the generator in-process, and (b) compiled + loaded
/// so the reflection path can produce the runtime value. The generator's emitted literal must equal
/// <c>SymbolDisplay.FormatLiteral(runtimeValue)</c> — so the compile-time and runtime paths cannot
/// drift. This is the guarantee behind "the generator is a fast-path, never a behavior change."
/// </summary>
public class GeneratorByteIdentityTests
{
    private const string SimpleSrc = @"
using DynamicQuery.Core;
namespace Fixtures;
[Projection(""widgets"", ""w"")]
public class SimpleDto
{
    [Column(""w.id"")] public System.Guid Id { get; set; }
    [Column(""w.name"")] public string Name { get; set; } = string.Empty;
}";

    private const string JoinedSrc = @"
using DynamicQuery.Core;
namespace Fixtures;
[Projection(""reviews"", ""r"")]
[LeftJoin(""media"", ""m"", ""r.media_id = m.id"")]
[LeftJoin(""users"", ""u"", ""u.id = r.created_by"")]
public class JoinedDto
{
    [Column(""r.id"")] public System.Guid Id { get; set; }
    [Coalesce(""m.title"", ""r.standalone_title"")] public string? Title { get; set; }
    [JsonbPath(""r.content_json"", 0, ""platform"")] public string? Platform { get; set; }
    [Column(""u.user_name"")] public string? AuthorHandle { get; set; }
}";

    [Theory]
    [InlineData(SimpleSrc, "Fixtures.SimpleDto")]
    [InlineData(JoinedSrc, "Fixtures.JoinedDto")]
    public void Generated_select_and_from_match_runtime_byte_for_byte(string src, string typeName)
    {
        var (genSelectLit, genFromLit) = RunGenerator(src);
        var (rtSelect, rtFrom) = RuntimeValues(src, typeName);

        Assert.Equal(SymbolDisplay.FormatLiteral(rtSelect, quote: true), genSelectLit);
        Assert.Equal(SymbolDisplay.FormatLiteral(rtFrom, quote: true), genFromLit);
    }

    [Fact]
    public void Generated_emits_module_initializer_registration()
    {
        var generated = GeneratedText(SimpleSrc);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", generated);
        Assert.Contains("global::DynamicQuery.Core.ProjectionRegistry.RegisterGenerated(", generated);
        Assert.Contains("typeof(global::Fixtures.SimpleDto)", generated);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────

    private static (string select, string from) RunGenerator(string src)
    {
        var generated = GeneratedText(src);
        return (ExtractConst(generated, "SelectColumns"), ExtractConst(generated, "FromClause"));
    }

    private static string GeneratedText(string src)
    {
        var tree = CSharpSyntaxTree.ParseText(src, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "GenInput",
            new[] { tree },
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ProjectionSourceGenerator());
        driver = driver.RunGenerators(compilation);

        var result = driver.GetRunResult();
        return string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
    }

    private static (string select, string from) RuntimeValues(string src, string typeName)
    {
        var tree = CSharpSyntaxTree.ParseText(src, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "RtInput_" + Guid.NewGuid().ToString("N"),
            new[] { tree },
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success,
            "fixture failed to compile:\n" + string.Join("\n", emit.Diagnostics));

        ms.Position = 0;
        var asm = Assembly.Load(ms.ToArray());
        var type = asm.GetType(typeName)
            ?? throw new Xunit.Sdk.XunitException($"fixture type not found: {typeName}");

        var d = ProjectionRegistry.GetDescriptor(type);
        return (d.SelectColumns, d.FromClause);
    }

    /// <summary>
    /// The generator emits each const on its own line: <c>public const string Name = "...";</c>.
    /// FormatLiteral produces a single-line literal (newlines escaped as <c>\n</c>), so a line scan
    /// recovers the exact literal text including the surrounding quotes.
    /// </summary>
    private static string ExtractConst(string generated, string constName)
    {
        var prefix = $"public const string {constName} = ";
        foreach (var raw in generated.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line.Substring(prefix.Length).TrimEnd().TrimEnd(';');
        }
        throw new Xunit.Sdk.XunitException(
            $"const '{constName}' not found in generated output:\n{generated}");
    }

    // Use the live AppDomain's assemblies as the reference set — the test references DynamicQuery.Core
    // so its assembly (carrying the projection attributes) is loaded and included, which lets both the
    // generator-input compilation bind the attributes and the runtime compilation emit + load.
    private static IReadOnlyList<MetadataReference> References()
        => AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .GroupBy(a => a.Location)
            .Select(g => (MetadataReference)MetadataReference.CreateFromFile(g.Key))
            .ToList();
}
