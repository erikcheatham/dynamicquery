namespace DynamicQuery.Core;

/// <summary>
/// Maps a DTO property to a <c>COALESCE(e1, e2, ...)</c> expression.
/// Each argument is a raw SQL fragment evaluated in order; the first
/// non-NULL wins. Useful for multi-source title / thumbnail / etc.
/// fallback chains where the projection joins multiple tables and
/// only one is populated per row.
/// </summary>
/// <param name="sqlExpressions">Two or more raw SQL expressions.
/// Each can be any SQL fragment, including nested function calls
/// like <c>NULLIF(...)</c> or jsonb path extractors.</param>
/// <example>
/// <code>
/// [Coalesce("m.title", "r.standalone_title")]
/// public string? Title { get; set; }
///
/// [Coalesce(
///     "m.poster_path",
///     "NULLIF((r.content_json::jsonb -> 0 ->> 'thumbnail'), '')")]
/// public string? PosterUrl { get; set; }
/// </code>
/// Emits:
/// <code>
/// COALESCE(m.title, r.standalone_title) AS "Title",
/// COALESCE(m.poster_path, NULLIF((r.content_json::jsonb -> 0 ->> 'thumbnail'), '')) AS "PosterUrl"
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CoalesceAttribute : Attribute
{
    /// <summary>The list of SQL expressions, in COALESCE order.</summary>
    public string[] SqlExpressions { get; }

    public CoalesceAttribute(params string[] sqlExpressions)
    {
        if (sqlExpressions is null || sqlExpressions.Length < 2)
            throw new ArgumentException("COALESCE requires at least two expressions.", nameof(sqlExpressions));

        foreach (var expr in sqlExpressions)
        {
            if (string.IsNullOrWhiteSpace(expr))
                throw new ArgumentException("COALESCE expressions cannot be null/whitespace.", nameof(sqlExpressions));
        }

        SqlExpressions = sqlExpressions;
    }
}
