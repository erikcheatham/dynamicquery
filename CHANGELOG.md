# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-06-21

### Added

- **Roslyn source generator** (the headline pre-1.0 capability, shipped ahead
  of the roadmap's v1.0 slot). Ships *inside* the `DynamicQuery.Core` package as
  an analyzer, so referencing Core gives consumers the attributes AND a
  compile-time fast-path. For every `[Projection]` DTO it emits the SELECT/FROM
  as `const string` literals plus a `[ModuleInitializer]` that pre-registers
  them via `ProjectionRegistry.RegisterGenerated`, so `GetSelectColumns<T>()` /
  `GetFromClause<T>()` return the constant with zero runtime reflection.
- `ProjectionRegistry.RegisterGenerated(Type, string, string)` — the runtime
  hook the generated module-initializers call. First registration wins
  (`TryAdd`); the reflection `Build` path stays the fallback for types the
  generator did not see (runtime-dynamic composition, design-time tooling, DTOs
  that inherit annotated members from a base class).
- Byte-identity test: the generator's emitted literal is pinned against the
  runtime `ProjectionRegistry` output for the same DTO source, so the
  compile-time and runtime paths provably cannot drift.

### Notes

- The generator skips any DTO it cannot emit identically (a property carrying
  more than one of `[Column]`/`[Coalesce]`/`[JsonbPath]`, or a DTO yielding zero
  columns) — those fall through to the runtime `Build`, which throws the precise
  `InvalidOperationException`. The generator never masks a malformed-DTO error.
- Generated registration uses `[ModuleInitializer]` (net5.0+); Core targets
  net8.0, so all supported consumers qualify.

## [0.1.1] - 2026-06-21

### Changed

- **Package split.** `DynamicQuery.Core` is now dependency-free (the Dapper
  dependency was removed), so it is safe to add to DTO-only assemblies consumed
  by WASM / MAUI client bundles without dragging Dapper transitively. The Dapper
  execution layer (`IDbConnection.QueryProjectionAsync<T>`) moved to a new
  **`DynamicQuery.Dapper`** package — server-side consumers add
  `dotnet add package DynamicQuery.Dapper` and `using DynamicQuery.Dapper;`.
  Non-breaking for `DynamicQuery.Core`'s own public API.

## [0.1.0] - 2026-05-19

### Added

- **Initial public release scaffold.** v0.1 runtime-only implementation:
  - `[Projection]`, `[LeftJoin]` class-level attributes for declaring the
    base table + join chain.
  - `[Column]`, `[Coalesce]`, `[JsonbPath]` property-level attributes for
    declaring how each DTO property maps to source columns.
  - `ProjectionRegistry.GetSelectColumns<T>()`, `GetFromClause<T>()`,
    and `GetDescriptor<T>()` cached reflection entry points.
  - `IDbConnection.QueryProjectionAsync<T>()` Dapper extension for
    one-liner read queries.
  - Apache 2.0 license. Public OSS on GitHub at
    [erikcheatham/dynamicquery](https://github.com/erikcheatham/dynamicquery).

### Heritage

- Spiritual successor to `DapperDynamicQueryGenerator` (Erik Cheatham,
  2016), a SQL Server-era library combining EF entity metadata with
  on-the-fly Dapper INSERT/UPDATE/DELETE statement generation. The
  2016 codebase targeted write-side CRUD; DynamicQuery targets the
  modern read-side projection problem (Postgres jsonb, multi-table
  JOINs, COALESCE chains) that EF Core's LINQ surface doesn't always
  translate cleanly to operator-desired SQL.
