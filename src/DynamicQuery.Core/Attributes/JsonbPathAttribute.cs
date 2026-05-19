namespace DynamicQuery.Core;

/// <summary>
/// Postgres-specific shortcut for jsonb path extraction. Emits the
/// canonical jsonb path SQL: <c>(&lt;column&gt;::jsonb -&gt; &lt;index&gt;
/// -&gt;&gt; '&lt;key&gt;') AS "&lt;PropertyName&gt;"</c>.
///
/// Use when a DTO property pulls a single field out of a jsonb array
/// column at a known position. For deeper paths or array-of-array
/// access, fall back to <see cref="ColumnAttribute"/> with the raw jsonb
/// SQL expression.
/// </summary>
/// <example>
/// <code>
/// [JsonbPath("r.content_json", 0, "platform")]
/// public string? LinkoutPlatform { get; set; }
/// </code>
/// Emits:
/// <code>
/// (r.content_json::jsonb -> 0 ->> 'platform') AS "LinkoutPlatform"
/// </code>
/// </example>
/// <remarks>
/// This attribute is Postgres-only — the jsonb operators (<c>::jsonb</c>,
/// <c>-&gt;</c>, <c>-&gt;&gt;</c>) are Postgres syntax with no portable
/// equivalent. A SQL Server <c>JsonValue</c> sister attribute is banked
/// for v0.3 multi-dialect support; see ROADMAP.md.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class JsonbPathAttribute : Attribute
{
    /// <summary>The jsonb column reference.</summary>
    public string Column { get; }

    /// <summary>The array index to dereference.</summary>
    public int Index { get; }

    /// <summary>The key to extract as text.</summary>
    public string Key { get; }

    /// <summary>
    /// Constructs a jsonb path projection extracting <paramref name="key"/>
    /// from the array element at <paramref name="index"/> of
    /// <paramref name="column"/>.
    /// </summary>
    /// <param name="column">The jsonb column reference (e.g.
    /// <c>"r.content_json"</c>).</param>
    /// <param name="index">The array index to dereference with <c>-&gt; N</c>.</param>
    /// <param name="key">The key to extract as text with <c>-&gt;&gt; 'key'</c>.</param>
    public JsonbPathAttribute(string column, int index, string key)
    {
        if (string.IsNullOrWhiteSpace(column))
            throw new ArgumentException("Column cannot be null/whitespace.", nameof(column));
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null/whitespace.", nameof(key));

        Column = column;
        Index = index;
        Key = key;
    }
}
