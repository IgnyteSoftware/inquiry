using System.Security.Cryptography;
using System.Text;
using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

/// <summary>AES-GCM with a fresh nonce for each call, packed as <c>nonce | tag | ciphertext</c>.</summary>
internal sealed class EncryptedBlobConverter : IInquiryValueConverter<string, byte[]>
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public byte[] ToProvider(string model)
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
        return packed;
    }

    public string FromProvider(byte[] provider)
    {
        var nonce = provider.AsSpan(0, NonceSize);
        var tag = provider.AsSpan(NonceSize, TagSize);
        var ciphertext = provider.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(EncryptionKeyHolder.Key, TagSize))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return Encoding.UTF8.GetString(plaintext);
    }
}

[InquiryTable("SecretBlob")]
public sealed class SecretBlob
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn("Payload", Converter = typeof(EncryptedBlobConverter))]
    public string Payload { get; set; } = string.Empty;
}

public partial class SecretBlobStore : InquiryStore<SecretBlob>
{
    [InquiryBulkInsert]
    public partial Task<long> BulkInsertAsync(IEnumerable<SecretBlob> blobs, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<SecretBlob?> GetAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Bulk insert of a converted <c>VARBINARY(MAX)</c> value that is larger than one 4 KB read. The
/// converter returns different bytes on every call, so the stored value is readable only if the
/// bulk copy takes the whole value from one call.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BulkEncryptedBlobIntegrationTests
{
    private const string Ddl = "CREATE TABLE SecretBlob (Id INT NOT NULL PRIMARY KEY, Payload VARBINARY(MAX) NOT NULL);";

    private readonly SqlServerContainerFixture _fixture;

    public BulkEncryptedBlobIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task BulkInsertStoresALargeEncryptedValueThatDecrypts()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "bulkblob");
        var store = harness.GetRequiredService<SecretBlobStore>();
        var small = new string('s', 100);
        var large = new string('L', 10_000);

        Assert.Equal(2, await store.BulkInsertAsync([new SecretBlob { Id = 1, Payload = small }, new SecretBlob { Id = 2, Payload = large }]));

        Assert.Equal(small, (await store.GetAsync(1))!.Payload);
        Assert.Equal(large, (await store.GetAsync(2))!.Payload);
    }
}
