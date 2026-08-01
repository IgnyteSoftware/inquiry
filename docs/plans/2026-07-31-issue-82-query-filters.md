# #82 — Parameterized and named query filters, write-side enforcement, Postgres RLS helpers

Working plan, one gated commit per phase. Ground truth for current behavior:
`[InquiryGlobalFilter]` marks a non-nullable bool column; `SqlBuildContext` composes the constant
predicate into `ActiveRowPredicate` (entity contexts detect filter columns from their column list,
projection contexts receive them via `globalFilterPredicateColumns`); there is no per-method opt-out.
Per-method context selection happens in `StoreProcessor.CtxFor` (line ~1384), today a two-way switch
between `ctx` and `ctxIncludeDeleted` (`suppressSoftDelete: true`).

## Phase A — named filters + `[InquiryIgnoreFilter]` + diagnostics

- `InquiryGlobalFilterAttribute.Name` (string, optional). Unnamed filters remain non-bypassable.
- New `[InquiryIgnoreFilter("name")]`, `AllowMultiple = true`, on store *methods*; valid only on
  select-shaped operations (the same set that accepts `IncludeDeleted`). Soft delete is not a named
  filter and stays bypassable only via `IncludeDeleted`.
- Generator threading: parse names into `StoreMethodData.IgnoredFilterNames`; generalize `CtxFor` to
  build (and cache per distinct (includeDeleted, name-set)) a `SqlBuildContext` with a new
  `ignoredGlobalFilterNames` input that drops matching columns from the active-row composition.
  `ColumnData` gains `GlobalFilterName`.
- Diagnostics (next free id INQ091+): unknown / unnamed-filter name on `[InquiryIgnoreFilter]`;
  the attribute on an operation kind that never composes filters. Names are consts — resolved fully
  at generation time; result stays a const string.
- Eager-loading note: relation consts compose the CHILD entity's filters from the child context.
  Phase A scopes `[InquiryIgnoreFilter]` to the DECLARING entity's own filters; bypassing a related
  entity's filter through an eager method is out of scope (and arguably should stay impossible).
- Tests: generator snapshots per dialect (filter dropped from exactly the annotated method's consts,
  other methods untouched, unknown name fails the build), live SQLite round-trip; docs in
  `global-filters.md`.

## Phase B — runtime-parameterized filters — DONE

Landed: `ContextKey` on `[InquiryGlobalFilter]` (separate from `Name`; both Codex consults converged
on that split), static `InquiryFilterContext` mirroring `InquiryAuditContext` (xhigh consult's
simpler surface chosen over a DI provider interface), `@__gf_<property>` const terms with values
bound inside the generated static binders at execute time, `InquiryFilterValueMissingException`
before execution on any missing/mistyped ambient value, INQ093 (blank key, KeepWhen conflict,
nullable column, role overlaps, eager rejection), key participation allowed. Binding coverage is by
construction: every read-binder emission seam (EmptyGeneratedCommand, AppendBinderLambda,
single-param commands, predicate/paged/keyset/FTS binders, set-based UPDATE and soft predicate
DELETE) takes the per-method binder name computed from one shared function. Eager methods reject
parameterized filters anywhere in the relation tree rather than emit unbindable SQL.

## Phase C — opt-in write-side enforcement

- Attribute opt-in (e.g. `EnforceOnWrites = true`) AND-composes the filter predicate onto key-based
  UPDATE / DELETE / soft-delete WHERE clauses. rows-affected == 0 then means "not found OR filtered";
  document that it is indistinguishable by design (same as concurrency-token misses today).

## Phase D — Postgres RLS session helpers

- `SET LOCAL` helpers in `Inquiry.PostgreSql` (Drizzle-style). `SET LOCAL` is transaction-scoped, so
  the seam must interact with the transacted pipeline; design after B (shares the ambient-value
  plumbing).

## Order rationale

A is fully specified and self-contained. B introduces the ambient seam D reuses. C is independent of
B but shares A's attribute surface. A → B → C → D.
