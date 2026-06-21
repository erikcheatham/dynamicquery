# DynamicQuery

> Attribute-driven SQL projection for .NET + Dapper.

Declare your DTO columns once with attributes. DynamicQuery composes the
`SELECT` projection, the `JOIN` block, and (optionally) the full query —
so you stop hand-rolling fragile multi-hundred-line SQL constants and let
the structure of your DTO be the canonical source.

Pairs with [Dapper](https://github.com/DapperLib/Dapper) for query execution,
EF Core for migrations / writes / change tracking. The two are
complementary, not competitive — DynamicQuery solves the read-side
projection problem EF Core's LINQ surface and Dapper's hand-written SQL
both stumble on at scale.

## Status

**v0.1 — runtime-only, public preview.** A source generator that emits the
SELECT / JOIN constants at compile time (eliminating reflection cost AND
the entire verbatim-string-fragility class) is the next milestone — see
`ROADMAP.md`.

## Quick start

```bash
# DynamicQuery.Core carries the attributes and is dependency-free (safe in
# DTO-only assemblies). DynamicQuery.Dapper adds the QueryProjectionAsync
# extensions and brings Core transitively, so for the full quick-start below
# just add Dapper; add Core alone when you only need to annotate DTOs.
dotnet add package DynamicQuery.Dapper
```

```csharp
using DynamicQuery.Core;     // [Projection]/[LeftJoin]/[Column]/[Coalesce]/[JsonbPath]
using DynamicQuery.Dapper;   // IDbConnection.QueryProjectionAsync(...) extension
using Dapper;
using Npgsql;

[Projection("reviews", "r")]
[LeftJoin("media", "m", "r.media_id = m.id")]
[LeftJoin("users", "u", "u.id = r.created_by")]
public class ReviewDTO
{
    [Column("r.id")]
    public Guid Id { get; set; }

    [Coalesce("m.title", "r.standalone_title")]
    public string? Title { get; set; }

    [JsonbPath("r.content_json", 0, "platform")]
    public string? LinkoutPlatform { get; set; }

    [Column("u.user_name")]
    public string? AuthorHandle { get; set; }
}

// ── Compose a query ─────────────────────────────────────────
using var conn = new NpgsqlConnection(connectionString);

var sql = $@"
    SELECT {ProjectionRegistry.GetSelectColumns<ReviewDTO>()}
    FROM   {ProjectionRegistry.GetFromClause<ReviewDTO>()}
    WHERE  r.id = @Id";

var review = await conn.QuerySingleOrDefaultAsync<ReviewDTO>(sql, new { Id });

// ── Or use the one-liner extension ──────────────────────────
var review2 = await conn.QueryProjectionAsync<ReviewDTO>(
    where: "r.id = @Id",
    parameters: new { Id });
```

The generated SELECT for the example above is:

```sql
r.id AS "Id",
COALESCE(m.title, r.standalone_title) AS "Title",
(r.content_json::jsonb -> 0 ->> 'platform') AS "LinkoutPlatform",
u.user_name AS "AuthorHandle"
```

And the FROM block is:

```sql
reviews r
LEFT JOIN media m ON r.media_id = m.id
LEFT JOIN users u ON u.id = r.created_by
```

Dapper auto-binds the snake_case-aliased columns to the C# property names
via the `AS "PropertyName"` aliases DynamicQuery emits.

## Why use this

**The problem DynamicQuery solves.** Production projection SQL accumulates
mass: 30+ columns, 4+ LEFT JOINs, `COALESCE` chains across multiple
sources, Postgres jsonb path extractors, careful column aliasing for
Dapper auto-bind. Hand-rolled as a `@"..."` verbatim string it works
fine — until an edit accidentally embeds an unescaped `"` in a SQL
comment and the parser cascades into ten unrelated-looking errors.

EF Core LINQ projections solve part of this but expand the LINQ surface
in ways that don't always translate cleanly to the SQL the operator
actually wants (jsonb path extractors, custom Postgres functions, raw
`COALESCE` ordering control).

DynamicQuery's bet: keep the SQL expression layer explicit (you write
`m.title`, not `r => r.Media.Title`), but generate the boilerplate
SELECT / JOIN composition from declarative attributes on the DTO.
Refactoring tools see property names → typos become compile errors.
New columns added by editing the DTO, not by editing a verbatim string.

## Attribute reference

### Class-level

- `[Projection(table, alias)]` — declares the base table + alias. Required.
- `[LeftJoin(table, alias, onCondition)]` — adds a `LEFT JOIN`. Repeatable;
  joins emit in declaration order.

### Property-level

- `[Column("sql_expression")]` — single-source projection. The expression
  can be any SQL fragment: bare column reference, function call, cast, etc.
- `[Coalesce("expr1", "expr2", ...)]` — `COALESCE(expr1, expr2, ...)` chain.
  Each expression is raw SQL — you can wrap in `NULLIF(...)`, cast, etc.
- `[JsonbPath("column", index, "key")]` — Postgres jsonb path shortcut.
  Emits `(column::jsonb -> index ->> 'key')`. Compose with `[Coalesce]` for
  jsonb-with-fallback patterns.

Properties without an attribute are NOT included in the projection
(opt-in by default).

## Roadmap

- **v0.1** (current) — runtime reflection + attribute parsing, cached
  per-type. Apache 2.0, public.
- **v0.2** — extended attributes (`InnerJoin`, `RawProjection`, custom
  alias control), expression-builder API for runtime-dynamic projections.
- **v1.0** — Roslyn source generator that emits compile-time SQL constants
  from the same attributes. Eliminates reflection cost AND moves the
  verbatim-string fragility class out of the source tree entirely. The
  generated code is type-checked at compile time and refactor-tool-friendly.

See `ROADMAP.md` for detail.

## Heritage

Spiritual successor to `DapperDynamicQueryGenerator` (Erik Cheatham, 2016),
a SQL Server-era pattern that combined EF entity metadata with on-the-fly
Dapper INSERT/UPDATE/DELETE statement generation. DynamicQuery picks up
the same instinct (declarative SQL generation from entity-shape) and aims
it at the modern bottleneck: Postgres-era read-side projection complexity,
not write-side CRUD (which EF Core already solves well via change tracking
and Npgsql's dialect support).

## License

Apache 2.0. See `LICENSE`.
