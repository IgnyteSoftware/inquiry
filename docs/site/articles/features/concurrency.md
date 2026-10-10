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

    [InquiryUpdate]
    public partial Task<Order?> UpdateReturningAsync(Order order, CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<Order?> SelectByKeyAsync(int orderID, CancellationToken ct = default);
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

## Refresh before the next edit

The bool-returning update binds the token on the input and does not refresh that object. With no
competing writer, the first update can succeed, but a second update of the same instance still binds
the old token. It returns false (or throws when configured), because the database token has advanced.

Use update-returning when the caller will keep editing:

```csharp
var original = await store.SelectByKeyAsync(orderID, ct)
    ?? throw new InvalidOperationException("Order not found.");
original.Status = "Packed";
var saved = await store.UpdateReturningAsync(original, ct);
if (saved is null)
    return; // Handle the conflict or missing row before continuing.

saved.Status = "Shipped";
var savedAgain = await store.UpdateReturningAsync(saved, ct);
if (savedAgain is null)
    return; // The second write can also conflict.

// Keep savedAgain for the next edit; original and saved retain their old tokens.
```

These are two distinct successful writes when no conflict occurs. Each uses the entity returned by
the preceding read/write. If using the bool form, reread after success before another edit.
Do not increment tokens in application code: numeric and database-generated token strategies differ.
When ThrowOnConcurrencyConflict is enabled, catch InquiryConcurrencyException instead of relying only
on the null branches. See [Errors and partial outcomes](../error-contracts.md) for failure timing.

## String and GUID stamps

Existing stamp schemas can opt into client-generated replacement values:

```csharp
[InquiryConcurrencyToken(RotateOnUpdate = true, Length = 36)]
public string ConcurrencyStamp { get; init; } = Guid.NewGuid().ToString("D");
```

A non-nullable `Guid` property also works. Each single-row update generates a fresh GUID,
compares the original stamp and key, and assigns the replacement only to the matching row.
Strings use the 36-character GUID `D` format. Existing string values need not be GUIDs.
Initialize the stamp before insertion; inserts preserve the supplied value. Existing nullable database
columns work when stored stamps are populated. Backfill NULL stamps (and empty strings on Oracle)
before using managed concurrency; equality predicates cannot match SQL NULL.

Like numeric tokens, the input remains unchanged. Use the entity returned by update-returning,
or reload after a bool-returning update. A stale update returns false/null or throws when configured.
Hard deletes compare the original stamp and do not generate a replacement. Soft deletion currently
compares the stamp without rotating it; its archive/restore contract is documented separately.

The numeric default accepts non-nullable integral types. Other types require a supported strategy;
`DatabaseGenerated` and `RotateOnUpdate` cannot be combined. Nullable tokens, incompatible SqlType overrides, converters, computed
columns, and conflicting generation settings produce `INQ101`. String length, when specified,
must hold all 36 characters. Explicit string SqlType supports text/CLOB families and character types
with capacity of at least 36; Guid rotation uses the default provider mapping. SQL Server TEXT/NTEXT
and Oracle CLOB/NCLOB cannot compare stamps with equality and produce INQ101. On Oracle, specify
a supported bounded Length (for example 36) to avoid the default LOB mapping. Oracle fixed CHAR/NCHAR
stamps are rejected because padding can change equality against string parameters; use VARCHAR2/NVARCHAR2. For unsupported representations, use an explicit guarded update that
matches the original value and checks the affected-row count.

## Provider-specific notes

- **SQL Server** can use native `ROWVERSION` — declare a non-nullable `byte[]` with `[InquiryConcurrencyToken(DatabaseGenerated = true)]`. Generated schema DDL emits `ROWVERSION NOT NULL`; inserts and bulk inserts omit the column; updates match the original eight-byte token. The returning form materializes the database-generated replacement; the bool form does not refresh the input. `SqlType`, length/precision/scale, defaults, computed expressions, converters, keys, and nullable token shapes are rejected at build time (`INQ068`) because they would weaken that contract.
- Other providers reject `DatabaseGenerated = true`; use an ORM-managed numeric token there.
- **PostgreSQL** typically uses an integer column with explicit increment (as above), or `xmin` for system-managed.
- **MySQL** uses an integer column with explicit increment.
- **Oracle** uses an integer column with explicit increment.

## See also

- [CRUD](crud.md) — baseline update/delete.
- [Soft delete](soft-delete.md) — composes with concurrency.
