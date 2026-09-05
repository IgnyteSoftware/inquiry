# Optimistic concurrency

Mark a column with `[InquiryConcurrencyToken]` and every `UPDATE` / `DELETE` will add a `WHERE RowVersion = @RowVersion` check. If another transaction has bumped the version since you read the row, the affected-row count is 0 and the operation returns `false` (or `null` for an update returning `Task<TEntity?>`). Set `InquiryOptions.ThrowOnConcurrencyConflict = true` to throw `InquiryConcurrencyException` instead.

## You write

```csharp
using Inquiry.Entities;

[InquiryTable("Orders")]
public sealed class Order
{
    [InquiryKey] public int OrderID { get; set; }
    [InquiryColumn] public string Status { get; set; } = "";

    [InquiryConcurrencyToken]
    public int RowVersion { get; set; }
}

public partial class OrderStore : InquiryStore<Order>
{
    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Order order, CancellationToken ct = default);
}
```

## The generator emits

The row-version column is included in the `WHERE`, and the new value is the old one plus one:

```csharp
private const string _sqlUpdate =
    "UPDATE \"Orders\" SET \"Status\" = @Status, \"RowVersion\" = \"RowVersion\" + 1 " +
    "WHERE \"OrderID\" = @OrderID AND \"RowVersion\" = @RowVersion";
```

If you call `UpdateAsync` with a stale `order.RowVersion`, zero rows match and the call returns `false`. Compare-and-swap, baked at compile time.

## Provider-specific notes

- **SQL Server** can use native `ROWVERSION` — declare a non-nullable `byte[]` with `[InquiryConcurrencyToken(DatabaseGenerated = true)]`. Generated schema DDL emits `ROWVERSION NOT NULL`; inserts and bulk inserts omit the column; updates match the original eight-byte token and return the database-generated replacement. `SqlType`, length/precision/scale, defaults, computed expressions, converters, keys, and nullable token shapes are rejected at build time (`INQ068`) because they would weaken that contract.
- Other providers reject `DatabaseGenerated = true`; use an ORM-managed numeric token there.
- **PostgreSQL** typically uses an integer column with explicit increment (as above), or `xmin` for system-managed.
- **MySQL** uses an integer column with explicit increment.
- **Oracle** uses an integer or `TIMESTAMP` column with explicit increment.

## See also

- [CRUD](crud.md) — baseline update/delete.
- [Soft delete](soft-delete.md) — composes with concurrency.
