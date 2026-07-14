# Batch operations

`[InquiryInsertAll]`, `[InquiryUpdateAll]`, and `[InquiryDeleteAll]` treat a collection as one
**logical batch operation**. That does not mean every provider uses one SQL statement or one network
round trip. Inquiry selects a bounded execution shape for the provider and operation: a set-based
statement, `DbBatch`, a reused row command, or native array binding.

## You write

```csharp
public partial class ShipperStore : InquiryStore<Shipper>
{
    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(IReadOnlyList<Shipper> shippers, CancellationToken ct = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IReadOnlyList<Shipper> shippers, CancellationToken ct = default);

    [InquiryDeleteAll]
    public partial Task<int> DeleteAllByKeyAsync(IReadOnlyList<int> shipperIDs, CancellationToken ct = default);
}
```

Inquiry enumerates the input once, buffers at most one bounded chunk, and returns the affected-row
total across all physical commands. An empty collection is a zero-row no-op and does not open a
connection.

## Atomicity and transactions

A non-empty batch called outside an active Inquiry transaction opens one connection and an implicit
`ReadCommitted` transaction for the entire logical operation. Inquiry commits only after every chunk
succeeds. Failure or cancellation rolls that transaction back, so earlier chunks are not left committed.

Inside an ambient Inquiry transaction, the batch reuses that transaction's connection and
`DbTransaction`. It neither commits nor rolls back the caller-owned transaction and does not create a
savepoint. The outer `ExecuteInTransactionAsync` callback or `IInquiryTransaction` handle remains
responsible for the final outcome. See [Transactions](transactions.md) for the ambient Inquiry model and
its distinction from `TransactionScope`.

## Bounded chunking

Inquiry automatically splits a batch by the smallest applicable bound:

- `InquiryOptions.MaxBatchSize` (default `1000`)
- `InquiryOptions.MaxParametersPerCommand` (default `2000`) for a generated whole-chunk or adaptive
  descriptor that records a per-item parameter cost
- a generated provider or SQL-shape limit, such as SQL Server's 1,000-row `VALUES` ceiling

The configured parameter bound is not multiplied across fixed-row or array-binding descriptors. Those
paths are bounded by `MaxBatchSize` and any generated provider limit; each fixed command already has its
own legal parameter shape. For example, a SQL Server `DbBatch` may contain many one-row commands because
the parameter limit applies to each child, not to the aggregate batch. The one-item fail-fast check
applies only when a generated whole-chunk or adaptive descriptor records a per-item parameter cost and
that one item exceeds the configured command limit.

Lower the configured bounds for stricter deployments or memory budgets. Raise them only after checking
the database, driver, packet, and statement limits that apply to your schema.

## Provider strategies

For entities with at least one bound insert column, the normal generated paths are:

| Provider | `InsertAll` | `UpdateAll` | `DeleteAll` |
|---|---|---|---|
| SQLite | Fixed single-row command with reused parameters; under `PrepareStatements.Auto`, Inquiry prefers one preparation for the batch | Reused single-row command | One bounded `json_each` key-set delete per chunk |
| SQL Server | Multi-row `VALUES` below 250 rows when the statement fits; `DbBatch` of fixed one-row inserts at or above 250 | `DbBatch` of fixed one-row updates | One bounded TVP key-set delete per chunk |
| PostgreSQL | One bounded multi-row `VALUES` insert per chunk | `DbBatch` of fixed one-row updates | One bounded native-array (`ANY`) key-set delete per chunk |
| MySQL / MariaDB | One bounded multi-row `VALUES` insert per chunk | Set-based derived-table update for eligible chunks; fixed row commands otherwise | One bounded `JSON_TABLE` key-set delete per chunk |
| Oracle | Native array binding over one fixed insert statement per chunk | Native array binding over one fixed update statement per chunk | Native array binding over one fixed delete statement per chunk |

An entity with no bound insert columns uses its provider's fixed, default-only insert once per row
instead of a multi-row or array-bound shape. SQLite reuses that command with its normal preparation
preference. SQL Server, PostgreSQL, MySQL, and MariaDB use `DbBatch` when available and otherwise reuse
the command. Oracle reuses the command because this shape has no array binder.

The SQL Server insert threshold is based on diagnostic measurements, not a universal claim about every
machine or schema. The set-based path is also subject to the configured and generated parameter limits.
If `DbBatch` is unavailable or its child commands cannot create parameters, Inquiry falls back to one
reused row command.

MySQL and MariaDB use their set-based update only when the generated key shape is safe and keys within
the chunk are unique. Other chunks use `DbBatch` when available, then the reused row-command fallback.
PostgreSQL row updates follow the same `DbBatch`-then-reuse fallback.

Oracle array binding is used for generated mutation shapes that supply an array binder. Shapes without
one, such as `DEFAULT VALUES` inserts, reuse the fixed row command. A provider error during array binding
is surfaced; Inquiry does not retry writes through a different transport after execution has started.

For the measurement scope and limitations behind the current SQLite and SQL Server insert choices, see
the [batch insert strategy diagnostic decision](https://github.com/JakeOverstreet/inquiry/blob/7b691883e3da6a36514249cbbaed63a3620c491f/docs/plans/2026-07-14-batch-insert-strategy-decision.md).

## Interceptors and fallbacks

Active command interceptors must receive a real `DbCommand` lifecycle, so Inquiry does not use
`DbBatch` or Oracle array binding for an intercepted chunk. A selected set-based command still produces
one interceptor lifecycle for that physical chunk; a selected row or Oracle path produces one lifecycle
per row. The SQLite descriptor's `Auto` prepare-once preference is not applied on this interceptor path.
Explicit preparation settings may still prepare individual physical commands.

These rules can increase the number of commands and round trips relative to the normal path. The batch
still uses one transaction and the same chunk bounds.

## Generated SQL and parameter binding

Whole-chunk SQL is assembled at run time because the row count is not known at compile time, but the
generator bakes the column list, parameter metadata, binders, and provider limits into a cached immutable
descriptor. Fixed-row and array-binding paths keep their SQL text constant across batch sizes.

For example, a set-based insert may produce:

```csharp
// INSERT INTO Shippers (CompanyName, Phone)
// VALUES (@p0_0, @p0_1), (@p1_0, @p1_1), ...
```

`[InquiryDeleteAll]` takes keys in the entity's model type, including strongly typed IDs. Inquiry passes
each non-null key through its configured value converter exactly once, then uses the provider's generated
collection transport (for example JSON table, native array, or TVP). A null key element is not converted
and cannot match a non-null primary key.

PostgreSQL arrays and SQL Server TVPs normalize unsigned and `sbyte` elements to provider-supported
numeric partners with unchecked, bit-preserving casts. SQL Server uses `byte`/`TINYINT` for `sbyte`;
PostgreSQL uses `short[]` with values `0..255` because Npgsql reserves `byte[]` for scalar `bytea`.
Both use `short`, `int`, and `long` for `ushort`, `uint`, and `ulong`.

`IInquiry.ExecuteBatchAsync` is also public for repeated-statement workloads. The built-in
`DefaultInquiry` routes that overload through its bounded, implicit-transaction row-command pipeline.
Passing a null item collection directly to this overload throws `ArgumentNullException`.
The interface's default implementation instead calls `ExecuteAsync` once per item for compatibility with
custom and test implementations; those implementations must provide their own batching and transaction
semantics if they need the built-in guarantees.
