# Optimistic concurrency

Mark a column with `[InquiryConcurrencyToken]` and every `UPDATE` / `DELETE` will add a `WHERE RowVersion = @RowVersion` check. If another transaction has bumped the version since you read the row, the affected-row count is 0 and the operation returns `false` (or `null` for a `ReturnEntity = true` update). Set `InquiryOptions.ThrowOnConcurrencyConflict = true` to throw `InquiryConcurrencyException` instead.

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

- **SQL Server** can use the native `ROWVERSION` (`TIMESTAMP`) type — declare it as `byte[]` with `[InquiryConcurrencyToken(DatabaseGenerated = true)]` and the generator omits the manual `+1` (SQL Server bumps it).
- **PostgreSQL** typically uses an integer column with explicit increment (as above), or `xmin` for system-managed.
- **MySQL** uses an integer column with explicit increment.
- **Oracle** uses an integer or `TIMESTAMP` column with explicit increment.

## See also

- [CRUD](crud.md) — baseline update/delete.
- [Soft delete](soft-delete.md) — composes with concurrency.
