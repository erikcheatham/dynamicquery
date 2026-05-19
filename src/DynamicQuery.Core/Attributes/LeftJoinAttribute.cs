namespace DynamicQuery.Core;

/// <summary>
/// Appends a <c>LEFT JOIN</c> to the projection's FROM block. Repeatable;
/// joins emit in declaration order on the class.
/// </summary>
/// <param name="table">Joined table name (raw SQL identifier, schema-
/// qualified if needed).</param>
/// <param name="alias">Alias to reference the joined table downstream
/// (e.g. <c>"m"</c> for media).</param>
/// <param name="onCondition">Raw SQL <c>ON</c> condition (without the
/// <c>ON</c> keyword), e.g. <c>"r.media_id = m.id"</c>.</param>
/// <example>
/// <code>
/// [Projection("reviews", "r")]
/// [LeftJoin("media", "m", "r.media_id = m.id")]
/// [LeftJoin("users", "u", "u.id = r.created_by")]
/// public class ReviewDTO { ... }
/// </code>
/// Emits the FROM block:
/// <code>
/// reviews r
/// LEFT JOIN media m ON r.media_id = m.id
/// LEFT JOIN users u ON u.id = r.created_by
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class LeftJoinAttribute : Attribute
{
    /// <summary>The joined table name.</summary>
    public string Table { get; }

    /// <summary>The alias for downstream references.</summary>
    public string Alias { get; }

    /// <summary>The raw SQL ON condition (without leading <c>ON</c>).</summary>
    public string OnCondition { get; }

    public LeftJoinAttribute(string table, string alias, string onCondition)
    {
        if (string.IsNullOrWhiteSpace(table))
            throw new ArgumentException("Table name cannot be null/whitespace.", nameof(table));
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Alias cannot be null/whitespace.", nameof(alias));
        if (string.IsNullOrWhiteSpace(onCondition))
            throw new ArgumentException("ON condition cannot be null/whitespace.", nameof(onCondition));

        Table = table;
        Alias = alias;
        OnCondition = onCondition;
    }
}
