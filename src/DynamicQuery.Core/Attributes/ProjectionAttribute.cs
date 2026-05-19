namespace DynamicQuery.Core;

/// <summary>
/// Declares the base table + alias for a DTO's read-side projection.
/// Required on every type that DynamicQuery's <see cref="ProjectionRegistry"/>
/// inspects; the descriptor build throws if missing.
/// </summary>
/// <param name="table">Base table name (raw SQL identifier, including
/// schema-qualifier if needed, e.g. <c>"reviews"</c> or <c>"civic.users"</c>).</param>
/// <param name="alias">Short alias the rest of the projection's SQL
/// fragments reference (e.g. <c>"r"</c>).</param>
/// <example>
/// <code>
/// [Projection("reviews", "r")]
/// public class ReviewDTO { ... }
/// </code>
/// Emits the FROM clause head <c>reviews r</c>.
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProjectionAttribute : Attribute
{
    /// <summary>The base table name.</summary>
    public string Table { get; }

    /// <summary>The alias used by JOINs + column references downstream.</summary>
    public string Alias { get; }

    public ProjectionAttribute(string table, string alias)
    {
        if (string.IsNullOrWhiteSpace(table))
            throw new ArgumentException("Table name cannot be null/whitespace.", nameof(table));
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Alias cannot be null/whitespace.", nameof(alias));

        Table = table;
        Alias = alias;
    }
}
