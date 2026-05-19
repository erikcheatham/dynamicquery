# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased] - 2026-05-19

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
