# Architecture deep-dive

This page is the long-form complement to [How it works](concepts.md). It documents the *implementation* — how the generator framework is laid out, how the request pipeline is structured, and the design constraints that shaped both.

For the canonical architecture write-up — including the full project layout, the SqlBuilder hierarchy, and the rationale for compile-time `const string` SQL — see the repository's [`README.md`](https://github.com/JakeOverstreet/inquiry/blob/main/README.md).

For project status, supported dialect matrix, and the workstream roadmap, see [`docs/STATUS.md`](https://github.com/JakeOverstreet/inquiry/blob/main/docs/STATUS.md).

## Quick reference

| Concern | Where it lives |
|---|---|
| Public runtime API (`IInquiry`, attributes, pipeline) | `src/Inquiry/` |
| Per-dialect Roslyn generator | `src/Inquiry.<Dialect>.Analyzer/` |
| Shared generator framework | `src/Inquiry.Generators.Shared/` |
| Per-dialect runtime provider package | `src/Inquiry.<Dialect>/` |
| SQL builder per dialect | `Inquiry.Generators.Shared/SqlBuilder` + dialect-specific subclasses |
| Materializer emission | `Inquiry.Generators.Shared/MaterializerEmitter` |
| Store-method emission | `Inquiry.Generators.Shared/StoreOperationEmitter` |
| Request pipeline (default) | `src/Inquiry/Pipeline/InquiryRequestPipeline.cs` |
| Request pipeline (transacted) | `src/Inquiry/Pipeline/TransactedInquiryRequestPipeline.cs` |
| Generated DDL emission | `Inquiry.Generators.Shared/SchemaEmitter.cs` |
| DI registration emission | `Inquiry.Generators.Shared/RegistrationEmitter.cs` |

## Key design constraints

1. **Compile-time SQL is non-negotiable.** Every SQL statement is a `const string`. The runtime never builds, formats, or interpolates SQL.
2. **One dialect per assembly.** `[InquiryDialect]` is `AllowMultiple = false`. Multi-dialect = multi-assembly.
3. **The runtime ships zero SQL.** `src/Inquiry/` has no `SELECT`, no `INSERT`, nothing. All SQL lives in the generated partials.
4. **Materializers are struct-specialized.** Generated stores call the struct-materializer overloads on the pipeline; the JIT emits a separate body per concrete struct so the per-row `materializer.Materialize(reader)` call inlines (no interface dispatch).
5. **Read streaming.** Generated stores pass `CommandBehavior.SequentialAccess`. Generated materializers read every column exactly once in ascending ordinal order, so this is safe and roughly halves allocation on large/wide reads.
6. **Diagnostics at compile time.** Any condition the generator can detect (unknown column, missing key, unsupported return shape, conflicting attributes) produces an `INQxxx` diagnostic at the source location.
