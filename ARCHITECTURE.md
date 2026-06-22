# Architecture

## Problem statement

Production read-side SQL projections accumulate mass. A non-trivial DTO
backed by 5+ tables, multi-source `COALESCE` chains, Postgres jsonb
extractors, and careful column aliasing for Dapper auto-bind grows to
multiple hundreds of lines of `@"..."` verbatim string. This is
edit-fragile in three distinct ways:

1. **Verbatim-string quote-escape failures.** A SQL comment containing
   unescaped `"` characters terminates the C# string mid-body and cascades
   into Unicode-token errors at lines far from the actual root cause.
2. **FUSE-mount / sandbox-tool truncation at scale.** Large files have
   proportionally more surface area for accidental corruption during
   AI-mediated editing.
3. **Refactoring tools can't see into raw SQL.** Renaming a property doesn't
   touch the `AS "OldName"` aliases inside the verbatim string; typos
   compile-pass and surface as runtime "column not found" errors.

EF Core LINQ projections solve part of (3) but expand the LINQ surface in
ways that don't always translate to the SQL the operator actually wants
(jsonb path access, custom `COALESCE` ordering, dialect-specific functions).

DynamicQuery's bet: **keep the SQL expression layer explicit, generate the
boilerplate composition from attributes on the DTO.** The operator owns
the SQL fragments (`m.title`, `r.standalone_title`, `r.content_json::jsonb
-> 0 ->> 'platform'`); the library owns the `SELECT col1, col2, ... FROM
table alias LEFT JOIN ...` skeleton.

## Layers

### Layer 1: Attributes (declarative surface)

Class-level:

- `[Projection(table, alias)]` — base table + alias. Required.
- `[LeftJoin(table, alias, onCondition)]` — repeatable join.

Property-level:

- `[Column(sqlExpression)]` — single-source expression.
- `[Coalesce(expr1, expr2, ...)]` — `COALESCE(e1, e2, ...)` chain.
- `[JsonbPath(column, index, key)]` — Postgres jsonb path shortcut.

The attributes carry intent, not the final SQL. The runtime layer
composes the final SQL from the intent.

### Layer 2: ProjectionDescriptor (built result)

Immutable record carrying:

- `SelectColumns: string` — the column projection (no leading `SELECT`).
- `FromClause: string` — the `table alias LEFT JOIN ...` block (no leading `FROM`).

Returned by `ProjectionRegistry.GetDescriptor<T>()`. Cached per type for the
process lifetime.

### Layer 3: ProjectionRegistry (cached reflection entry point)

`ProjectionRegistry.GetSelectColumns<T>()` and `GetFromClause<T>()` are the
canonical accessors. Internally:

1. First call per `T`: reflection scans `T` for class-level + property-
   level attributes, builds the `ProjectionDescriptor`, caches it in a
   `ConcurrentDictionary<Type, ProjectionDescriptor>`.
2. Subsequent calls return the cached instance.
3. Build-time validation throws `InvalidOperationException` for
   structural errors (missing `[Projection]`, multiple precedence
   attributes on one property, etc.). These errors surface on first use,
   not at app startup — for production usage, consumers should exercise
   each DTO at startup via a warm-up call to catch issues at deploy time.

### Layer 4: Dapper extension methods (optional convenience)

`IDbConnection.QueryProjectionAsync<T>(where, parameters, orderBy, limit)`
composes `SELECT {SC} FROM {FC} WHERE {where} ORDER BY {orderBy} LIMIT {limit}`
and dispatches via Dapper. Useful for the common case; consumers needing
more control (CTEs, UNION, complex WHERE expressions) drop down to the
`GetSelectColumns` / `GetFromClause` strings and compose by hand.

## Emission rules

### SELECT projection

Each annotated property contributes one comma-separated entry. Attribute
precedence (highest first):

1. `[JsonbPath]` → `(<column>::jsonb -> <index> ->> '<key>') AS "<PropertyName>"`
2. `[Coalesce]` → `COALESCE(<expr1>, <expr2>, ...) AS "<PropertyName>"`
3. `[Column]` → `<expr> AS "<PropertyName>"`

Multiple attributes on one property throw `InvalidOperationException` at
build time. Properties with no DynamicQuery attribute are SKIPPED (opt-in
by default — DTOs frequently carry transient fields that shouldn't
participate in the projection, e.g. `[JsonIgnore]` setters that parse
serialized state).

Column aliasing uses the C# property name verbatim, wrapped in double
quotes for Postgres compatibility (`AS "MovieTitle"`). Dapper auto-binds
on the column name; case-insensitive matching makes the quoting
cosmetic for the runtime path but the case-correct form documents the
intent.

### FROM block

The `[Projection(table, alias)]` attribute emits `<table> <alias>` as the
base. `[LeftJoin]` attributes append `LEFT JOIN <table> <alias> ON <on>`
in declaration order.

Future v0.2 attributes (`[InnerJoin]`, `[CrossJoin]`) follow the same
pattern. The order-of-emission is class-attribute order; consumers needing
specific JOIN ordering can re-arrange the attribute order on the class.

## Performance characteristics

- **First call per `T`**: reflection scan over the type's
  `GetCustomAttributes` and `GetProperties` — typically <1ms for a DTO
  with 30+ properties.
- **Subsequent calls**: dictionary lookup, ~100ns.
- **Memory**: one `ProjectionDescriptor` per registered `T` (two strings:
  `SelectColumns` + `FromClause`, typically ~2KB each for a realistic DTO).
- **Thread safety**: `ConcurrentDictionary` makes concurrent reads + first-
  write safe. The descriptor itself is immutable.

## Source generator (shipped 0.2.0, 2026-06-21)

The compile-time emission path. A Roslyn `IIncrementalGenerator`
(`DynamicQuery.SourceGenerator`) reads the same attribute shapes from the
consumer's DTO definitions at compile time and emits, per `[Projection]` DTO,
a registrar in the `DynamicQuery.Generated` namespace:

```csharp
internal static class MyApp_Dtos_ReviewDTO_DynamicQueryProjection
{
    public const string SelectColumns = "r.id AS \"Id\",\n    COALESCE(...) AS \"Title\"";
    public const string FromClause = "reviews r\n    LEFT JOIN media m ON ...";

    [ModuleInitializer]
    internal static void Register()
        => ProjectionRegistry.RegisterGenerated(typeof(ReviewDTO), SelectColumns, FromClause);
}
```

The `[ModuleInitializer]` runs on assembly load and pre-populates the same
`ConcurrentDictionary` the reflection path uses, so a consumer's
`GetSelectColumns<T>()` returns the const with **zero runtime reflection** —
no first-call scan, AOT-friendly, the const visible to "Find All References."

**Byte-identity is the contract.** The generator's string-building mirrors
`ProjectionRegistry.Build` exactly (fragment join `",\n    "`; per-join
`"\n    LEFT JOIN t a ON on"`; the three per-attribute fragment formats). A
test (`GeneratorByteIdentityTests`) runs the generator on a DTO's source AND
compiles + loads the *same* source to get the runtime value, then asserts the
generator's emitted literal equals `SymbolDisplay.FormatLiteral` of the runtime
output. One source of truth → the two paths cannot drift. This is why the
generator is "a fast-path, never a behavior change."

**Fail-safe to the runtime.** The generator skips any DTO it cannot emit
identically — a property carrying more than one of
`[Column]`/`[Coalesce]`/`[JsonbPath]`, a DTO that would yield zero columns, or
(today's known limitation) a DTO that annotates members inherited from a base
class (the generator scans declared members; the runtime walks the full
`GetProperties` set). Skipped DTOs fall through to `RegisterGenerated` never
being called for them, so `GetDescriptor` runs the reflection `Build`, which
throws the precise diagnostic for the malformed cases. The generator never
masks an error and never silently emits wrong SQL.

**Packaged as an analyzer inside Core.** `DynamicQuery.Core.csproj` references
the generator with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`
(build-time only, no runtime dependency) and packs the built generator DLL into
the nupkg's `analyzers/dotnet/cs` path. A consumer referencing the
`DynamicQuery.Core` *package* therefore gets the attributes AND the generator
with a single reference — no separate analyzer package to add.

**v1.0 (API-lock) still pending.** The generator substrate shipped early; the
v1.0 milestone is the attribute-contract freeze plus the remaining exit
criteria (incremental-perf validation per Microsoft's sub-100ms target,
production-consumer-under-load). The incremental model is currently keyed on a
three-string value record (fully-qualified name + rendered SELECT + rendered
FROM), which gives correct structural caching; tightening it further (e.g. an
`EquatableArray` over the pre-render shape) is a v1.0 perf-tuning item.

## What v0.1 does NOT do

- **Source generator shipped in 0.2.0** (see the section above). The runtime
  reflection path remains load-bearing as the fallback for types the generator
  can't statically see (runtime-dynamic composition, design-time tooling,
  base-class-inherited annotations).
- **No write support.** EF Core handles writes via change tracking
  (better tooling, better dialect support, automatic concurrency
  detection). DynamicQuery is read-side only.
- **No query plan analysis.** This is a SQL-emission library, not a
  query optimizer. Consumers are responsible for ensuring the joins
  + columns they declare are indexed appropriately at the database.
- **No multi-database dialect abstraction.** v0.1 emits Postgres-leaning
  SQL (jsonb extractor syntax) plus dialect-portable basics
  (LEFT JOIN, COALESCE, `AS "alias"`). Other dialects work for the
  basics but `[JsonbPath]` is Postgres-only. SQL Server `JsonValue`
  equivalent is banked for v0.2+.

## Design tensions banked

### Why attributes vs. fluent builder?

The fluent builder API (`ProjectionDescriptor<T>.Build().Column(...)
.Coalesce(...)`) was the alternative considered. Trade-offs:

- **Attributes**: declarative, lives ON the DTO, refactor-tool-friendly,
  but verbose for large DTOs (one attribute per property).
- **Builder**: less attribute noise, easier dynamic composition, but
  separates the projection definition from the DTO definition — readers
  have to look in two places.

Decision: attributes for v0.1 because the DTO IS the canonical place
to declare what columns the projection produces. The builder pattern is
banked for v0.2 as a complementary API for cases where the projection
is genuinely dynamic (admin UIs, ad-hoc reporting tools where columns
are operator-selected at runtime).

### Why no `[InnerJoin]` in v0.1?

The read-side projection problem is dominated by LEFT JOINs (nullable
FK relationships, optional related rows). INNER JOINs imply the
related row MUST exist, which is rare in user-facing read paths —
typically the inner-join shape lives in admin / reporting queries that
also have other dynamic dimensions a `[LeftJoin]` can't capture
cleanly. Banked for v0.2 alongside the fluent builder.

### Why precedence (`[JsonbPath]` > `[Coalesce]` > `[Column]`) instead of
allowing combinations?

Allowing combinations (e.g. `[Coalesce]` containing a `[JsonbPath]`
reference) was considered. Trade-offs:

- **Allowed combinations** — more expressive, but the combinatorics
  explode quickly and emission becomes ambiguous (which attribute
  contributes to the COALESCE? in what position?).
- **Precedence** — pick one, throw on multiple. Simple to reason about.
  The expressive case is covered by writing the COALESCE expression
  manually inside `[Coalesce]`: `[Coalesce("(r.content_json::jsonb -> 0
  ->> 'title')", "m.title", "r.standalone_title")]` works fine.

Decision: precedence for v0.1. If a real consumer hits the limit, v0.2
revisits.
