using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Stores;
using Npgsql;

namespace Inquiry.PostgreSql.Tests;

internal static class EncryptionKeyHolder
{
    internal static byte[] Key { get; set; } = new byte[32];
}

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

    [InquiryColumn("Secret", Converter = typeof(EncryptedStringConverter))]
    public string Secret { get; set; } = string.Empty;
}

public partial class SecretRecordStore : InquiryStore<SecretRecord>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<SecretRecord?> InsertAsync(SecretRecord record, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<SecretRecord?> ByIdAsync(long id, CancellationToken cancellationToken = default);
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ColumnEncryptionIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    private const string Ddl =
        """CREATE TABLE "SecretRecord" ("Id" BIGSERIAL PRIMARY KEY, "Label" TEXT NOT NULL, "Secret" TEXT NOT NULL);""";

    public ColumnEncryptionIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
        EncryptionKeyHolder.Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    }

    [SkippableFact]
    public async Task SecretRoundTripsButIsCiphertextAtRest()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "encrypt");
        var store = harness.GetRequiredService<SecretRecordStore>();

        var inserted = await store.InsertAsync(new SecretRecord { Label = "card", Secret = "4111-1111-1111-1111" });

        var loaded = await store.ByIdAsync(inserted!.Id);
        Assert.Equal("4111-1111-1111-1111", loaded!.Secret);

        await using var conn = new NpgsqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Secret\" FROM \"SecretRecord\" WHERE \"Id\" = " + inserted.Id;
        var raw = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.NotEqual("4111-1111-1111-1111", raw);
        Assert.Equal("4111-1111-1111-1111", new EncryptedStringConverter().FromProvider(raw));
    }
}
