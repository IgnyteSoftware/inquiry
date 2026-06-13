# Column encryption

Inquiry has no bespoke encryption API — it doesn't need one. A [value converter](value-converters.md) already sits on every column's read/write path, so **application-side encryption is just a converter**: encrypt in `ToProvider`, decrypt in `FromProvider`. The column stores ciphertext; your property is plaintext.

## The pattern

```csharp
[InquiryTable("PaymentMethods")]
public sealed class PaymentMethod
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }

    // Stored encrypted: the converter maps string ↔ base64 ciphertext (a TEXT column).
    [InquiryColumn(Converter = typeof(EncryptedStringConverter))]
    public string CardNumber { get; set; } = "";
}
```

A converter must be **stateless with a parameterless constructor** (Inquiry instantiates and caches it), so the key can't come through the constructor — source it from a **static holder** set once at startup (from your key vault / KMS):

```csharp
public static class EncryptionKeyHolder
{
    public static byte[] Key { get; set; } = LoadFromVault(); // 32 bytes for AES-256
}

public sealed class EncryptedStringConverter : IInquiryValueConverter<string, string>
{
    private const int NonceSize = 12, TagSize = 16;

    public string ToProvider(string model)
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes(model);
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new System.Security.Cryptography.AesGcm(EncryptionKeyHolder.Key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Persist nonce | tag | ciphertext (a fresh nonce per write → equal plaintexts differ at rest).
        var packed = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, packed, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, packed, NonceSize + TagSize, ciphertext.Length);
        return Convert.ToBase64String(packed);
    }

    public string FromProvider(string provider)
    {
        var packed = Convert.FromBase64String(provider);
        var nonce = packed.AsSpan(0, NonceSize);
        var tag = packed.AsSpan(NonceSize, TagSize);
        var ciphertext = packed.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new System.Security.Cryptography.AesGcm(EncryptionKeyHolder.Key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}
```

`AesGcm` gives you **authenticated** encryption (tamper detection). Use a `string` (base64) provider for a `TEXT` column, or a `byte[]` provider for a `BLOB`/`VARBINARY` column. This pattern is exercised end-to-end in the test suite (`ColumnEncryptionIntegrationTests`): the property round-trips through the store while the persisted column holds ciphertext, never the plaintext.

## Trade-off: you can't query the value

Because each row's ciphertext is opaque (and randomized by the nonce), the database **can't filter, sort, or index on the plaintext**. `[InquiryWhere("CardNumber", …)]` would match the ciphertext, not the card number. If you need to *look up* by an encrypted value, store a separate **deterministic** keyed hash (HMAC) of it in an indexed column and query that, accepting the equality-only, no-range trade-off.

## Database-native alternatives

Application-side conversion is portable across all five providers and keeps keys out of the database, but two engines offer native options worth knowing:

- **SQL Server — Always Encrypted.** Encryption happens *client-side in the SQL Server driver*, transparently, keyed by a Column Master Key in your key store. Map the column as the plaintext CLR type and enable Always Encrypted on the connection (`Column Encryption Setting=Enabled`) — no converter needed. Deterministic encryption supports equality lookups; randomized does not.
- **PostgreSQL — pgcrypto.** Encryption happens *in the database* via `pgp_sym_encrypt` / `pgp_sym_decrypt`. Store the column as `bytea` and wrap reads/writes in those functions — most naturally through an [ad-hoc query](ad-hoc-dtos.md) or a server-computed expression, since the key travels with the SQL.

For most apps the converter pattern above is the simplest correct choice: it's provider-agnostic, the key never reaches the database, and it's plain .NET cryptography you can audit.

## See also

- [Value converters](value-converters.md) — the seam encryption rides on.
- [Ad-hoc DTOs](ad-hoc-dtos.md) — for the pgcrypto / HMAC-lookup escape hatches.
