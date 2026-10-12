# Partial updates and JSON field presence

Use a generated partial-update method when its columns are fixed at compile time; see
[set-based mutations](set-based-mutations.md). Every declared update argument is supplied, including
null for a nullable column. A nullable value alone cannot mean both “leave unchanged” and “clear.”

For a variable JSON patch, the application can preserve field presence and call the existing
`IInquiry.ExecuteAsync(FormattableString, CancellationToken)` API. This recipe uses one guarded SQL
statement, without loading and merging the row first. Inquiry does not provide a generated PATCH
binding or a `FieldChange<T>` API.

## Three distinct inputs

Starting with phone `old`, email `mail`, and version 0, these calls were executed sequentially on SQLite:

| JSON | Expected version | Persisted phone | Persisted email | New version | Result |
|---|---:|---|---|---:|---|
| `{}` | 0 | `old` | `mail` | 0 | No command |
| `{"phone": null}` | 0 | SQL NULL | `mail` | 1 | 1 row |
| `{"phone": "555-0100"}` | 1 | `555-0100` | `mail` | 2 | 1 row |

An omitted field keeps its stored value. An explicit null clears it. A supplied string replaces it,
even when the string is empty. Do not infer omission from null, an empty string, or `default(T)`.

## Complete SQLite recipe

The example table and starting row are:

<!-- recipe: partial-updates/schema.sql -->
```sql
CREATE TABLE Contacts (
    Id INTEGER PRIMARY KEY,
    Phone TEXT NULL,
    Email TEXT NULL,
    Version INTEGER NOT NULL,
    TenantId INTEGER NOT NULL,
    IsDeleted INTEGER NOT NULL
);
INSERT INTO Contacts VALUES (1, 'old', 'mail', 0, 7, 0);
```

Configure SQLite with `AddInquirySqlite(connectionString)` and register Inquiry with
`AddInquiry(typeof(ContactPatches).Assembly)`. Pass the authorized tenant from application context,
the target key, and the version the caller last read. The request body may contain only `phone` and
`email`; it cannot set the key, tenant, deletion marker, or version.

<!-- recipe: partial-updates/ContactPatches.cs -->
```csharp
using Inquiry;
using System.Text.Json;

public static class ContactPatches
{
    public static async Task<(bool Submitted, int Rows)> ApplyAsync(
        IInquiry inquiry, long id, long tenantId, int expectedVersion, string json,
        CancellationToken ct = default)
    {
        using var patch = JsonDocument.Parse(json);
        var root = patch.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The patch must be a JSON object.", nameof(json));

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("phone" or "email") ||
                property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new ArgumentException("Only phone and email strings or null are allowed.", nameof(json));
        }

        var hasPhone = root.TryGetProperty("phone", out var phoneNode);
        var hasEmail = root.TryGetProperty("email", out var emailNode);
        if (!hasPhone && !hasEmail)
            return (false, 0);

        var phone = hasPhone ? phoneNode.GetString() : null;
        var email = hasEmail ? emailNode.GetString() : null;
        var rows = await inquiry.ExecuteAsync($"""
            UPDATE Contacts
            SET Phone = CASE WHEN {hasPhone} THEN {phone} ELSE Phone END,
                Email = CASE WHEN {hasEmail} THEN {email} ELSE Email END,
                Version = Version + 1
            WHERE Id = {id} AND TenantId = {tenantId}
              AND IsDeleted = 0 AND Version = {expectedVersion}
            """, ct);
        return (true, rows);
    }
}
```

`TryGetProperty` supplies the presence flag independently of the nullable value. The interpolated
expression goes directly to `IInquiry`; each value becomes a parameter. Keep the table, columns,
and SQL structure fixed. Do not turn the interpolated expression into a string first.

The command has this parameterized shape (parameter names shown schematically):

```sql
UPDATE Contacts
SET Phone = CASE WHEN @hasPhone THEN @phone ELSE Phone END,
    Email = CASE WHEN @hasEmail THEN @email ELSE Email END,
    Version = Version + 1
WHERE Id = @id AND TenantId = @tenantId
  AND IsDeleted = 0 AND Version = @expectedVersion;
```

This is handwritten SQL bound by the runtime, not SQL emitted by a store-method generator. The
SQLite example uses a numeric version and SQLite boolean expressions. Other providers require
their own SQL and binding checks; SQL Server, for example, requires an explicit comparison in the
CASE condition. This recipe does not implement string/Guid rotation or database-generated rowversion.

## Result and policy contract

- An empty object returns `(false, 0)` without issuing SQL or advancing the version. It does not
  establish that the row exists or that the supplied version is current. Authorize the request first.
- A submitted patch returns `(true, 1)` only when the key, tenant, active state, and expected version
  all match. The field changes and numeric version increment occur in that same atomic statement.
  Supplying the current value still counts as a submitted patch and advances the version.
- `(true, 0)` means no row matched. It does not distinguish a missing row, stale version, wrong
  tenant, or archived row. Choose the HTTP response in the application without exposing other tenants.
- The expected version is a separate argument, never a SET value from the JSON body. After success,
  reload before another write if the caller needs the current full row. The input JSON is unchanged.
- Only `phone` and `email` are writable here. Unknown or protected fields are rejected before SQL.
  Extend this allowlist and the fixed statement together when adding an editable field.
- Raw SQL does not inherit generated global filters or soft-delete rules. The application owns the
  explicit tenant and active-row predicates and must obtain `tenantId` from trusted authorization context.

The executable example also verified that stale-version and wrong-tenant requests affect zero rows,
that `{"email": null}` preserves the phone, and that an archived row cannot be changed even with its
current version. No preliminary read is needed to apply a patch.

## Supported combinations

| Operation | Contract |
|---|---|
| Generated static partial update | Fixed columns; supplied null writes null where allowed. Predicate mutations reject concurrency-token entities (`INQ022`). |
| This single-row SQL recipe | Variable presence for phone/email, numeric expected version, explicit tenant/active guards. |
| Generated variable PATCH | Unsupported; no presence wrapper or generated JSON binding is available. |
| Batch or multi-row presence patch | Not supplied by this recipe. Do not remove the key or reuse one expected version for several rows. |
| Upsert presence patch | Unsupported. Omitted fields and conflict handling need a separate explicit contract. |

Do not map a second edit entity onto the same table to simulate PATCH; conflicting schemas can
produce `INQ070`. For more complex fixed predicates and assignments, see
[query composition](query-composition.md).