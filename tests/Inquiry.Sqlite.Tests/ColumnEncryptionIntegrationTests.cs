using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// A test holder for the column-encryption key. A converter must be stateless with a parameterless
/// constructor (Inquiry instantiates/caches it), so the key is sourced from a static holder set once at
/// startup rather than injected through the constructor.
/// </summary>
internal static class EncryptionKeyHolder
{
    // 32 bytes = AES-256. In production this comes from a key vault / KMS, not a literal.
    internal static byte[] Key { get; set; } = new byte[32];
}

/// <summary>
/// A stateless value converter that AES-GCM-encrypts a string column at rest. The provider value is the
/// base64 of <c>nonce | tag | ciphertext</c>; a fresh random nonce per write means equal plaintexts
/// encrypt to different ciphertexts.
/// </summary>
internal sealed class EncryptedStringConverter : IInquiryValueConverter<string, string>
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string ToProvider(string model)
    {
        var plaintext = Encoding.UTF8.GetBytes(model);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(EncryptionKeyHolder.Key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

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
        using (var aes = new AesGcm(EncryptionKeyHolder.Key, TagSize))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return Encoding.UTF8.GetString(plaintext);
    }
}

[InquiryTable("SecretRecord")]
public sealed class SecretRecord
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Label")]
    public string Label { get; set; } = string.Empty;

    // Stored encrypted: the converter maps string ↔ base64 ciphertext (a TEXT column).
    [InquiryColumn("Secret", Converter = typeof(EncryptedStringConverter))]
    public string Secret { get; set; } = string.Empty;
}

public partial class SecretRecordStore : InquiryStore<SecretRecord>
{
    [InquiryInsert]
    public partial Task<SecretRecord?> InsertAsync(SecretRecord record, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<SecretRecord?> ByIdAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Demonstrates and verifies application-side column encryption over the value-converter seam (the
/// pattern documented in column-encryption.md): the property round-trips through the store, but the
/// value persisted in the column is ciphertext, not the plaintext.
/// </summary>
public sealed class ColumnEncryptionIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE SecretRecord (Id INTEGER PRIMARY KEY AUTOINCREMENT, Label TEXT NOT NULL, Secret TEXT NOT NULL);";

    public ColumnEncryptionIntegrationTests()
    {
        // Deterministic key for the test; production sources this from a vault.
        EncryptionKeyHolder.Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    }

    [Fact]
    public async Task SecretRoundTripsButIsCiphertextAtRest()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "Encrypt");
        await using var _ = harness;
        var store = harness.GetRequiredService<SecretRecordStore>();

        var inserted = await store.InsertAsync(new SecretRecord { Label = "card", Secret = "4111-1111-1111-1111" });

        // The property round-trips (decrypted on read).
        var loaded = await store.ByIdAsync(inserted!.Id);
        Assert.Equal("4111-1111-1111-1111", loaded!.Secret);

        // The raw column holds ciphertext, never the plaintext.
        var raw = (string)(await harness.ExecuteScalarAsync("SELECT Secret FROM SecretRecord WHERE Id = " + inserted.Id))!;
        Assert.NotEqual("4111-1111-1111-1111", raw);
        // It decrypts back to the original through the converter (deterministic proof it's the ciphertext).
        Assert.Equal("4111-1111-1111-1111", new EncryptedStringConverter().FromProvider(raw));
    }
}
