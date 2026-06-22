# Roadmap

Phased shipping plan from v0.1 (today) to v1.0 (compile-time emission via
source generator).

> **Note on versioning (2026-06-21):** package SemVer and the feature-phase
> names below are tracked independently. The Roslyn **source generator** — the
> headline capability originally slotted at v1.0 — shipped **early in package
> 0.2.0**, because the byte-identity guarantee made it safe to land ahead of the
> expressive-attribute / multi-dialect work. The v0.2–v0.4 *feature* sections
> below re-sequence into subsequent 0.x minors; **v1.0 remains the API-lock
> milestone**, gated on its exit criteria (incremental-perf validation,
> production-consumer-under-load, attribute API freeze).

## v0.1 — shipping now (public preview)

**Runtime reflection + attribute parsing**, cached per type. The minimum
viable attribute surface:

- Class-level: `[Projection]`, `[LeftJoin]`
- Property-level: `[Column]`, `[Coalesce]`, `[JsonbPath]`

**Public API:**

- `ProjectionRegistry.GetSelectColumns<T>()`
- `ProjectionRegistry.GetFromClause<T>()`
- `ProjectionRegistry.GetDescriptor<T>()`
- `IDbConnection.QueryProjectionAsync<T>(where, parameters, ...)`

**License:** Apache 2.0, public OSS on GitHub.

**Exit criteria for v0.1:**

- AllThruit's `ReviewRepository` migrated to use DynamicQuery for at
  least one read method (dog-food validation).
- xUnit test coverage on emission shape (SELECT + FROM strings match
  pinned canonical outputs).
- README quick-start verified by a contributor unfamiliar with the
  internal design — they can wire a new DTO without reading
  ARCHITECTURE.md.

## v0.1.1 — package split (Core / Dapper) ✅ Shipped 0.1.1 (2026-06-21)

Architectural follow-up banked 2026-05-19 before the v0.1 publish.
Currently `DynamicQuery.Core` takes `Dapper` as a direct package
dependency. That works for v0.1's preview audience (everyone using
DynamicQuery in v0.1 is also using Dapper), but it transitively pulls
Dapper into any project that adds DynamicQuery.Core as a dependency
— including DTO-only projects that get consumed by WASM / MAUI
clients where Dapper has no runtime role and adds bundle weight.

The v0.1.1 split:

- **`DynamicQuery.Core`** — attributes + `ProjectionDescriptor` +
  `ProjectionRegistry`. Zero external dependencies. Safe to add to
  any project including pure-DTO assemblies consumed by client
  bundles.
- **`DynamicQuery.Dapper`** — `DapperProjectionExtensions` +
  `ProjectReference` to `DynamicQuery.Core` + `PackageReference` to
  Dapper. Server-side dependency only.

Semver: v0.1.1 is a non-breaking patch release because
DynamicQuery.Core's public API is unchanged (the `DapperProjectionExtensions`
class moves to a new package; consumers who used it via
`using DynamicQuery.Core;` need to add a `using DynamicQuery.Dapper;`
plus the new package reference). Pre-1.0 API is explicitly fluid per
the hard rules so this minor break is acceptable.

**Until v0.1.1 ships**, downstream consumers wanting to attribute
DTOs in DTO-only assemblies (e.g. AllThruit.Shared) can use the
metadata-partner-class pattern: declare a server-side projection
class with the same property names as the DTO, attribute it, and
reflect over the metadata class via
`ProjectionRegistry.GetSelectColumns<DtoMetadataClass>()`. Dapper
still binds the query result rows to the DTO via the column
aliases. The pattern keeps the Dapper transitive dependency confined
to server-side projects.

## v0.2 — expressive attribute surface

Builds on v0.1's reflection runtime. Adds attributes for cases the v0.1
minimum doesn't cleanly express:

- `[InnerJoin(table, alias, on)]` — required-relationship joins.
- `[CrossJoin(expression)]` — Postgres CROSS JOIN LATERAL for jsonb
  array unfold + other multi-row JOIN patterns.
- `[RawProjection("sql_expression", alias = null)]` — escape hatch for
  any SQL the typed attributes don't yet cover (window functions,
  subqueries, custom aggregates).
- `[ColumnAlias("CustomName")]` — override the default property-name alias.
- **Fluent builder API** — `ProjectionDescriptor<T>.Build().Column(...).
  Coalesce(...)` for runtime-dynamic projection composition. Same
  emission rules as attributes, different declaration site. Useful for
  admin / reporting tools where the column set is operator-selected.

**Exit criteria for v0.2:**

- Attribute set covers AllThruit's full `ReviewSelectColumns` shape
  without any escape-hatch `[RawProjection]` usage.
- Fluent builder exercised by a real consumer (admin dashboard with
  dynamic column selection).

## v0.3 — multi-dialect support

v0.1 + v0.2 emit Postgres-leaning SQL plus dialect-portable basics.
v0.3 generalizes:

- `[JsonValue("column", "$.path")]` — SQL Server JSON path equivalent
  to Postgres `[JsonbPath]`. Same attribute pattern, different SQL
  emission per provider.
- **Provider-selector configuration** — `ProjectionRegistry.SetProvider
  (SqlProvider.Postgres | SqlProvider.SqlServer | SqlProvider.SQLite | ...)`
  affects which dialect-specific emission path is used.
- **Identifier quoting** — current code emits `AS "PropName"` (Postgres-
  friendly). v0.3 emits `AS [PropName]` for SQL Server, etc.

**Exit criteria for v0.3:**

- At least two production consumers using different dialects (Postgres
  + SQL Server, or Postgres + SQLite).
- Per-dialect test matrix in CI.

## v0.4 — runtime composition + advanced features

- **Conditional column inclusion** — `[Column("expr", IncludeWhen = "RequestKey")]`
  with a runtime override that pre-prunes the projection. Useful when
  the DTO has expensive columns (joined backdrop URLs, etc.) the caller
  doesn't always want.
- **Sub-query support** — `[SubQuery("sql", alias)]` lets a property
  resolve to a correlated subquery.
- **Aggregations** — `[Count("table.col")]`, `[Sum]`, `[Avg]` for
  summary-table read patterns.

## v1.0 — Roslyn source generator

> **Substrate landed early in package 0.2.0 (2026-06-21).** The incremental
> generator, `ProjectionRegistry.RegisterGenerated`, the `[ModuleInitializer]`
> registration, and the runtime-vs-generated byte-identity test all shipped. The
> generator is packaged as an analyzer *inside* `DynamicQuery.Core`, so a Core
> reference is all a consumer needs. What remains for the **v1.0 API-lock**
> milestone is the exit criteria below — incremental-perf validation,
> production-consumer-under-load, and freezing the attribute contract.

The big win. Replaces v0.1's runtime reflection with compile-time SQL
emission:

- A Roslyn incremental source generator reads the attribute shapes from
  the consumer's DTO definitions at compile time.
- Emits a partial-class file (or a static class per DTO) with the
  `SelectColumns` and `FromClause` as `const string` literals.
- The `ProjectionRegistry` runtime path is preserved as a fallback for
  edge cases (dynamic projection composition, design-time tooling), but
  every annotated DTO gets a generated fast-path that skips reflection
  entirely.

**Benefits:**

- **Zero startup cost** — no first-call reflection.
- **AOT-compatible** — works under Native AOT publish.
- **Refactor-tool friendly** — generated code is visible to "Find All
  References," typos surface as standard C# compile errors.
- **Source generator IS the verbatim-string-fragility fix** — the
  generated code uses C# string literals with proper escape handling,
  and the source generator's emission logic is testable in isolation.

**Exit criteria for v1.0:**

- Source generator passes Microsoft's "incremental generator" performance
  criteria (sub-100ms re-generation on incremental builds).
- Both runtime path AND generated-path return byte-identical SQL for
  every attribute combination in the test matrix.
- AllThruit production usage validates the generated-path under real load.
- Attribute API is locked — v1.0 is the first stable contract.

## Beyond v1.0

Speculative but banked:

- **EF Core integration** — register DynamicQuery projections with EF's
  query pipeline so `_db.Reviews.AsProjection<ReviewDTO>()` works.
- **OpenAPI / NSwag integration** — automatic Swagger schema generation
  for endpoint DTOs that match the projection shape.
- **Migration helper** — a roslyn analyzer that converts hand-rolled
  Dapper SQL constants to attribute-annotated DTOs. Migration tool for
  upgrading existing codebases.
