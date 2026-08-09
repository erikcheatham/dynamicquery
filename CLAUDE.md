# DynamicQuery — AI memory

Project memory for AI assistants contributing to DynamicQuery. Cross-cutting
rules, architectural conventions, and gotchas the codebase has accumulated.
This file is committed and public; anything operator-specific or
infrastructure-related lives in `%USERPROFILE%\private\local.md` (private
memo, never in this tree).

## Startup (restructured 2026-07-14)

The operator hub (private, auto-injected in the operator's sessions) carries identity +
the live queue; public contributors need only this file. Read on demand per task:
`README.md` (pitch + quick start) · `ARCHITECTURE.md` (attributes, ProjectionRegistry,
SQL emission, source-generator plan) · `ROADMAP.md` (v0.1 -> v1.0) · `CHANGELOG.md`.

## Hard rules (non-negotiable)

1. **Apache 2.0 OSS, public repo.** No commercial-only features in this tree.
   `LICENSE` is the canonical copy.

2. **No tight coupling to any specific ORM or query executor.** The Dapper
   extension methods live in `Extensions/DapperProjectionExtensions.cs` as
   a convenience layer; the core `ProjectionRegistry` API
   (`GetSelectColumns<T>` / `GetFromClause<T>`) returns plain strings so
   consumers can use them with any execution path: bare Dapper,
   `IDbConnection.Execute`, EF Core `FromSqlRaw`, custom command pipelines.

3. **Provider-agnostic SQL emission.** v0.1 emits the lowest-common-denominator
   SQL plus Postgres-specific extensions (jsonb path syntax). When new
   dialect-specific attributes land in v0.2+, they MUST be opt-in (e.g.
   `[JsonbPath]` is Postgres-only; a SQL Server `[JsonValue]` would be
   separate). Never emit dialect-specific code from a generic attribute.

4. **Backward compatibility once v1.0 ships.** Attribute shapes + the
   `ProjectionRegistry` API contract are locked at v1.0. Adding new
   attributes is fine; removing or breaking the meaning of existing ones is
   a major version bump. Pre-v1.0 the API is fluid.

5. **The runtime path stays load-bearing even after the source generator
   ships.** v1.0's source generator is a performance + safety enhancement,
   not a replacement. Some consumers (dynamic projection composition, design-
   time tooling, edge cases the generator can't statically analyze) will
   need the runtime path indefinitely. Don't delete it when shipping the
   generator.

6. **AI-driven commits author with `darwincommits` / `darwinsemailinbox@gmail.com`**
   per the operator's canonical AI-author identity across his sibling repos
   (AllThruit, Recto, Verso, AllThruitCoin, dynamicquery). Architectural-
   decision commits and major version cuts commit under Erik's git identity.

7. **No reflection in hot paths.** `ProjectionRegistry` caches its
   per-type results in a `ConcurrentDictionary` so reflection runs exactly
   once per type per process lifetime. Future hot-path optimizations
   (compiled IL emission, source generator) build on top of this contract.

## Repo layout

Corrected 2026-08-09 — the block below had been stale since v0.1.1: it showed a
Dapper dependency on Core (Core is dependency-free), put
`DapperProjectionExtensions.cs` under `Core/Extensions/` (it moved to its own
project), and omitted both `DynamicQuery.Dapper` and `DynamicQuery.SourceGenerator`
entirely. A layout map that lies is worse than no map, because it is the first
thing a contributor trusts.

```
dynamicquery/
├── README.md                            Public pitch + quick start
├── LICENSE                              Apache 2.0
├── CLAUDE.md                            This file
├── ARCHITECTURE.md                      Design doc
├── ROADMAP.md                           Phased plan
├── CHANGELOG.md                         Per-release log
├── .gitignore
├── DynamicQuery.sln                     Solution
├── src/
│   ├── DynamicQuery.Core/               ZERO PackageReferences — by design
│   │   ├── DynamicQuery.Core.csproj     net8.0; also PACKS the generator as an analyzer
│   │   ├── Attributes/
│   │   │   ├── ProjectionAttribute.cs   [Projection("table", "alias")]
│   │   │   ├── LeftJoinAttribute.cs     [LeftJoin("table", "alias", "on")]
│   │   │   ├── ColumnAttribute.cs       [Column("sql_expr")]
│   │   │   ├── CoalesceAttribute.cs     [Coalesce("expr1", "expr2", ...)]
│   │   │   └── JsonbPathAttribute.cs    [JsonbPath("col", N, "key")] (Postgres)
│   │   └── Projections/
│   │       ├── ProjectionDescriptor.cs  Built result: SelectColumns + FromClause (NON-generic)
│   │       └── ProjectionRegistry.cs    Cached reflection entry point + RegisterGenerated
│   ├── DynamicQuery.Dapper/             Dapper 2.1.35 lives HERE, not in Core
│   │   └── DapperProjectionExtensions.cs   QueryProjectionAsync<T> + BuildSql<T>
│   └── DynamicQuery.SourceGenerator/
│       └── ProjectionSourceGenerator.cs Roslyn IIncrementalGenerator; [ModuleInitializer] pre-registration
└── tests/
    └── DynamicQuery.Core.Tests/
        ├── DynamicQuery.Core.Tests.csproj      xUnit
        ├── ProjectionRegistryTests.cs          Coverage on emission shape
        └── GeneratorByteIdentityTests.cs       Generator output == reflection output, byte-for-byte
```

**`DynamicQuery.Dapper` has NO test project.** `BuildSql<T>` — which composes the
entire `WHERE` / `ORDER BY` / `LIMIT` surface — has zero coverage.

## Architectural conventions

- **Attribute classes** live under `Attributes/`, one file per attribute.
  Each attribute is `sealed` + has a parameterless or simple constructor +
  uses `[AttributeUsage]` to declare allowed targets. `AllowMultiple = true`
  is set on the join attributes; everything else is single-use per target.

- **`ProjectionDescriptor<T>`** is the immutable result type returned by
  the registry. Carries `SelectColumns` (the column-projection string,
  with no leading `SELECT `) and `FromClause` (the `table_alias JOIN ...`
  block, with no leading `FROM `). Consumers compose: `SELECT {SC} FROM {FC}
  WHERE ...`.

- **`ProjectionRegistry`** is the static entry point. Lazy-loaded per type,
  thread-safe via `ConcurrentDictionary`. `GetSelectColumns<T>()` and
  `GetFromClause<T>()` are the canonical accessors. `GetDescriptor<T>()`
  returns the full descriptor for callers that need both.

- **Emission rules** are defined in `ProjectionRegistry.Build`:
  - Properties without any DynamicQuery attribute are SKIPPED (opt-in).
  - Property attribute precedence is `[JsonbPath] > [Coalesce] > [Column]`
    (only the first match contributes; multiple attributes on one property
    throw an `InvalidOperationException` at build time).
  - Column aliasing: emit `<expression> AS "<PropertyName>"` so Dapper
    auto-binds by exact property name (Dapper is case-insensitive but we
    quote-emit the case-correct property name for readability + tool
    inspection).

- **Tests** use xUnit + plain `Assert.Equal` on the emitted string. We
  pin the EXACT output so changes are visible in diffs.

## "Update your IM" convention

When the operator says **"Update your IM"**, add the lesson to the most-
scoped place that keeps it discoverable AND respects the public/private
split:

- Generic technical lesson → this file's Gotchas section + the relevant
  module's docstring.
- Operator-environment-specific (sandbox quirks, the operator's tooling) →
  `%USERPROFILE%\private\local.md` (Gotchas section there).
- New convention or rule → Hard rules above.
- Architectural decision → `ARCHITECTURE.md` (with date stamp).
- Feature shipped → `CHANGELOG.md`.
- Future work item → `ROADMAP.md`.
- Identity / infrastructure detail (machine names, PATs, domain names,
  consumer app names, git author identity, hosting topology) → ALWAYS
  `%USERPROFILE%\private\local.md`, never the public tree.

## Gotchas index (public)

Generic technical gotchas only. Operator-environment-specific gotchas
live in `%USERPROFILE%\private\local.md`.

- **C# `@"..."` verbatim strings break on single `"` in prose** — the
  classic SQL-comment-quote trap. The library exists in part to mitigate
  this problem class for downstream consumers; don't reintroduce it in
  the library's own SQL emission code. When the source generator lands
  in v1.0 this becomes a non-issue (generated code uses string literals
  with proper escape sequences); for v0.1's runtime path, we emit raw
  strings concatenated via `StringBuilder`, not verbatim-string
  templates. Banked here as a defensive note for future contributors.

- **Reflection cache: the descriptor must be IMMUTABLE after build.** The
  `ConcurrentDictionary<Type, ProjectionDescriptor>` cache returns the
  same instance to every caller. Mutating it post-build would corrupt
  every subsequent read. `ProjectionDescriptor`'s constructor takes its
  fields by value; properties are get-only. Don't add a mutation API
  unless you're also adding a separate non-cached builder path.

- **THIS LIBRARY IS BLIND TO ORM METADATA, DELIBERATELY — AND THAT HAS A SHARP
  EDGE ON SORTS (banked 2026-08-09).** The library reads exactly one thing off a
  member: `prop.Name`, used as a SQL alias. `PropertyInfo.PropertyType` appears
  nowhere in the codebase. That is hard rule 2 working as intended — plain
  strings out, no ORM coupling — but it means there is **no metadata channel
  through which an EF value converter could ever be discovered**, and one
  consequence is worth stating loudly because it is silent:
  **a converted enum sorted through this library sorts by its PROVIDER type.**
  `HasConversion<string>()` stores `Fermenting`/`Bottled`/`Dumped` as text, so
  `orderBy: "b.status"` returns alphabetical order, not lifecycle order. EF's own
  LINQ has the same behaviour — but EF at least *holds* the `IProperty` and its
  converter and could reason about it. Here there is nothing to reason with, no
  warning, and no fix short of hand-written `ORDER BY CASE ... END`.
  Sisters, same root: a strongly-typed ID (`readonly record struct FooId(Guid)`)
  or a multi-property value object emits perfectly valid SQL and then **fails at
  Dapper materialization**, because this library ships no `SqlMapper.TypeHandler<T>`
  and never inspects the target type. Nested member access (`Volume.Amount`)
  does not exist — `BuildSelectColumns` iterates top-level properties with no
  recursion, so the intended shape is a FLAT read DTO of scalars Dapper can bind.
  **Rule: document supported property shapes; do not try to teach the library
  about converters.** Teaching it would mean depending on `IModel`, which is
  hard rule 2 in the bin. The honest fix is a docs statement plus fixtures that
  pin what actually happens for an enum, a struct ID, and a value object —
  none of which exist in the test suite today (every fixture is `Guid` + `string`).

- **`orderBy` and `where` are concatenated into the SQL unvalidated**
  (`DapperProjectionExtensions.BuildSql<T>`). Attribute values are compile-time
  constants so the attribute surface is low-risk, but `orderBy` is exactly the
  parameter applications wire to user input, and nothing here guards it. It also
  has no test coverage of any kind. Treat both as caller-trusted SQL and say so
  in the docs, or guard it — but do not leave it undocumented and untested.

## Heritage

This project is the modernization of `DapperDynamicQueryGenerator` (Erik
Cheatham, 2016), a SQL Server-era library that combined EF entity
metadata with on-the-fly Dapper INSERT/UPDATE/DELETE statement
generation. That library was the right tool for its era; the modern
shape of the problem has shifted to read-side projection complexity
(Postgres jsonb, multi-table JOINs, COALESCE chains), which is what
DynamicQuery targets.

The 2016 codebase is preserved in `Other Projects/DapDynamicQueryGenerator`
in the operator's local tree as a historical artifact, not a dependency.
