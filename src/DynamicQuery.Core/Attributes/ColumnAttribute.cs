namespace DynamicQuery.Core;

/// <summary>
/// Maps a DTO property to a single SQL expression in the SELECT projection.
/// The expression can be any SQL fragment: bare column reference,
/// function call, cast, etc. Emits as
/// <c>&lt;expression&gt; AS "&lt;PropertyName&gt;"</c>.
/// </summary>
/// <example>
/// <code>
/// [Column("r.id")]
/// public Guid Id { get; set; }
///
/// [Column("u.user_name")]
/// public string? AuthorHandle { get; set; }
/// </code>
/// Emits:
/// <code>
/// r.id AS "Id",
/// u.user_name AS "AuthorHandle"
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ColumnAttribute : Attribute
{
    /// <summary>The raw SQL expression mapped to this property.</summary>
    public string SqlExpression { get; }

    /// <summary>
    /// Constructs a column projection mapping the SQL expression to the
    /// property it decorates.
    /// </summary>
    /// <param name="sqlExpression">Raw SQL expression for this column.
    /// Examples: <c>"r.id"</c>, <c>"m.title"</c>,
    /// <c>"COUNT(r.id) OVER (PARTITION BY r.author_id)"</c>.</param>
    public ColumnAttribute(string sqlExpression)
    {
        if (string.IsNullOrWhiteSpace(sqlExpression))
            throw new ArgumentException("SQL expression cannot be null/whitespace.", nameof(sqlExpression));

        SqlExpression = sqlExpression;
    }
}
