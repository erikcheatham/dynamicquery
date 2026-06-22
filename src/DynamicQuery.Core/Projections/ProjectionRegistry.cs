using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace DynamicQuery.Core;

/// <summary>
/// Static entry point for DynamicQuery's reflection-based projection
/// build. Caches a <see cref="ProjectionDescriptor"/> per type for the
/// process lifetime; first call per DTO type does the reflection scan,
/// subsequent calls are dictionary lookups.
/// </summary>
/// <remarks>
/// <para>v0.1 is the runtime-only implementation. v1.0's source generator
/// will emit equivalent constants at compile time and bypass this entry
/// point for annotated types; this runtime path stays load-bearing as a
/// fallback for dynamic composition and design-time tooling. See
/// ARCHITECTURE.md.</para>
/// <para>Thread safety: the cache is a <see cref="ConcurrentDictionary{TKey, TValue}"/>;
/// the descriptor itself is immutable. Concurrent reads + first-write are safe.</para>
/// </remarks>
public static class ProjectionRegistry
{
    private static readonly ConcurrentDictionary<Type, ProjectionDescriptor> Cache = new();

    /// <summary>
    /// Returns the cached <see cref="ProjectionDescriptor"/> for
    /// <typeparamref name="T"/>, building it on first call.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="T"/> lacks the required
    /// <see cref="ProjectionAttribute"/> or has structural attribute
    /// errors (multiple projection-source attributes on one property,
    /// etc.).
    /// </exception>
    public static ProjectionDescriptor GetDescriptor<T>()
        => GetDescriptor(typeof(T));

    /// <summary>
    /// Non-generic accessor. Useful when the type is only known at
    /// runtime (open-generic dispatchers, dynamic projection composition).
    /// </summary>
    /// <param name="type">The DTO type to inspect for projection attributes.</param>
    public static ProjectionDescriptor GetDescriptor(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Cache.GetOrAdd(type, Build);
    }

    /// <summary>
    /// Convenience accessor returning only the column-projection string
    /// (without the leading <c>SELECT</c> keyword).
    /// </summary>
    public static string GetSelectColumns<T>()
        => GetDescriptor(typeof(T)).SelectColumns;

    /// <summary>
    /// Convenience accessor returning only the FROM block string
    /// (without the leading <c>FROM</c> keyword).
    /// </summary>
    public static string GetFromClause<T>()
        => GetDescriptor(typeof(T)).FromClause;

    /// <summary>
    /// Clears the per-type descriptor cache. Primarily for tests; production
    /// callers should not need this.
    /// </summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// Pre-populates the cache with a compile-time-generated descriptor for
    /// <paramref name="type"/>. The source generator emits one
    /// <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute"/> call to this per
    /// annotated DTO, so <see cref="GetDescriptor(Type)"/> returns the generated SQL without ever
    /// running reflection. First registration wins (<c>TryAdd</c>); the runtime reflection
    /// <c>Build</c> path stays the fallback for types the generator did not see (runtime-dynamic
    /// composition, design-time tooling). Byte-identical to the reflection path by construction —
    /// the generator mirrors <c>Build</c>'s emission and is pinned against it by test.
    /// </summary>
    /// <param name="type">The DTO type the generated descriptor describes.</param>
    /// <param name="selectColumns">The generated SELECT projection (no leading <c>SELECT</c>).</param>
    /// <param name="fromClause">The generated FROM block (no leading <c>FROM</c>).</param>
    public static void RegisterGenerated(Type type, string selectColumns, string fromClause)
    {
        ArgumentNullException.ThrowIfNull(type);
        Cache.TryAdd(type, new ProjectionDescriptor(type, selectColumns, fromClause));
    }

    // ─────────────────────────────────────────────────────────────────
    // Internal: build path. Runs once per type per process.
    // ─────────────────────────────────────────────────────────────────
    private static ProjectionDescriptor Build(Type type)
    {
        var projection = type.GetCustomAttribute<ProjectionAttribute>(inherit: false)
            ?? throw new InvalidOperationException(
                $"Type {type.FullName} has no [Projection] attribute. " +
                "Every DynamicQuery DTO must declare its base table + alias via [Projection(table, alias)].");

        var selectColumns = BuildSelectColumns(type);
        var fromClause = BuildFromClause(type, projection);

        return new ProjectionDescriptor(type, selectColumns, fromClause);
    }

    private static string BuildSelectColumns(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var sb = new StringBuilder();
        var first = true;

        foreach (var prop in properties)
        {
            var fragment = BuildPropertyFragment(prop, type);
            if (fragment is null) continue; // Property has no DynamicQuery attribute; skip.

            if (!first) sb.Append(',').Append('\n').Append("    ");
            sb.Append(fragment);
            first = false;
        }

        if (first)
        {
            // No properties contributed. The DTO probably forgot to
            // annotate any column. Surface this as a clear error rather
            // than emit an invalid empty SELECT.
            throw new InvalidOperationException(
                $"Type {type.FullName} has no DynamicQuery column attributes on any property. " +
                "Annotate at least one property with [Column], [Coalesce], or [JsonbPath].");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Composes the SELECT fragment for a single property. Returns null
    /// when the property has no DynamicQuery attribute (skipped by design —
    /// DynamicQuery is opt-in per property so DTOs can carry transient
    /// fields, computed properties, etc. without polluting the projection).
    /// </summary>
    private static string? BuildPropertyFragment(PropertyInfo prop, Type containingType)
    {
        var jsonbPath = prop.GetCustomAttribute<JsonbPathAttribute>(inherit: false);
        var coalesce = prop.GetCustomAttribute<CoalesceAttribute>(inherit: false);
        var column = prop.GetCustomAttribute<ColumnAttribute>(inherit: false);

        var attrCount = (jsonbPath is not null ? 1 : 0)
                      + (coalesce is not null ? 1 : 0)
                      + (column is not null ? 1 : 0);

        if (attrCount == 0) return null;

        if (attrCount > 1)
        {
            throw new InvalidOperationException(
                $"Property {containingType.FullName}.{prop.Name} has multiple " +
                "DynamicQuery projection-source attributes. Only one of " +
                "[JsonbPath], [Coalesce], [Column] is allowed per property.");
        }

        var alias = prop.Name;

        if (jsonbPath is not null)
        {
            return $"({jsonbPath.Column}::jsonb -> {jsonbPath.Index} ->> '{jsonbPath.Key}') AS \"{alias}\"";
        }

        if (coalesce is not null)
        {
            var args = string.Join(", ", coalesce.SqlExpressions);
            return $"COALESCE({args}) AS \"{alias}\"";
        }

        // column is non-null at this point
        return $"{column!.SqlExpression} AS \"{alias}\"";
    }

    private static string BuildFromClause(Type type, ProjectionAttribute projection)
    {
        var sb = new StringBuilder();
        sb.Append(projection.Table).Append(' ').Append(projection.Alias);

        var joins = type.GetCustomAttributes<LeftJoinAttribute>(inherit: false);
        foreach (var j in joins)
        {
            sb.Append('\n').Append("    ");
            sb.Append("LEFT JOIN ").Append(j.Table).Append(' ').Append(j.Alias)
              .Append(" ON ").Append(j.OnCondition);
        }

        return sb.ToString();
    }
}
