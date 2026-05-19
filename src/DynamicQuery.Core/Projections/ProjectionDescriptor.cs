namespace DynamicQuery.Core;

/// <summary>
/// Immutable result returned by <see cref="ProjectionRegistry"/>. Carries
/// the two composable SQL fragments needed to build a projection query:
/// the column projection (sans leading <c>SELECT</c>) and the FROM block
/// (sans leading <c>FROM</c>).
/// </summary>
/// <remarks>
/// Consumers compose as: <c>SELECT {SelectColumns} FROM {FromClause}
/// WHERE ... ORDER BY ...</c>. The split lets callers add WHERE / GROUP BY
/// / ORDER BY / LIMIT clauses without re-parsing the projection. Plain
/// strings keep the type executor-agnostic — works with Dapper, EF Core
/// <c>FromSqlRaw</c>, raw <c>IDbCommand</c>, or any other execution path.
/// </remarks>
public sealed class ProjectionDescriptor
{
    /// <summary>
    /// The runtime type this descriptor was built for. Useful for
    /// diagnostic logging and cache key inspection.
    /// </summary>
    public Type TargetType { get; }

    /// <summary>
    /// Column projection fragment, without the leading <c>SELECT</c>
    /// keyword. Example shape:
    /// <c>r.id AS "Id", COALESCE(m.title, r.standalone_title) AS "Title"</c>
    /// </summary>
    public string SelectColumns { get; }

    /// <summary>
    /// FROM block fragment, without the leading <c>FROM</c> keyword.
    /// Example shape:
    /// <c>reviews r LEFT JOIN media m ON r.media_id = m.id</c>
    /// </summary>
    public string FromClause { get; }

    public ProjectionDescriptor(Type targetType, string selectColumns, string fromClause)
    {
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
        SelectColumns = selectColumns ?? throw new ArgumentNullException(nameof(selectColumns));
        FromClause = fromClause ?? throw new ArgumentNullException(nameof(fromClause));
    }
}
