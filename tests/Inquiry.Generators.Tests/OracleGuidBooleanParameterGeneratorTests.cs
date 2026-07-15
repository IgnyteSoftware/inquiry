namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string OracleGuidBooleanParameterSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Paging;
        using Inquiry.Stores;
        namespace Demo;

        public readonly record struct ExternalId(Guid Value);
        public sealed class ExternalIdConverter : IInquiryValueConverter<ExternalId, Guid>
        {
            public Guid ToProvider(ExternalId value) => value.Value;
            public ExternalId FromProvider(Guid value) => new(value);
        }

        public readonly record struct Toggle(bool Value);
        public sealed class ToggleConverter : IInquiryValueConverter<Toggle, bool>
        {
            public bool ToProvider(Toggle value) => value.Value;
            public Toggle FromProvider(bool value) => new(value);
        }

        [InquiryTable("BindingItem")]
        public sealed class BindingItem
        {
            [InquiryKey] public Guid Id { get; set; }
            [InquiryColumn] public bool Enabled { get; set; }
            [InquiryColumn] public Guid? OptionalToken { get; set; }
            [InquiryColumn] public bool? OptionalEnabled { get; set; }
            [InquiryColumn(Converter = typeof(ExternalIdConverter))] public ExternalId ConvertedToken { get; set; }
            [InquiryColumn(Converter = typeof(ToggleConverter))] public Toggle ConvertedEnabled { get; set; }
            [InquiryColumn(Converter = typeof(ExternalIdConverter))] public ExternalId? OptionalConvertedToken { get; set; }
            [InquiryColumn(Converter = typeof(ToggleConverter))] public Toggle? OptionalConvertedEnabled { get; set; }
        }

        public partial class BindingStore : InquiryStore<BindingItem>
        {
            [InquiryInsert(ReturnEntity = true)] public partial Task<BindingItem?> InsertAsync(BindingItem item, CancellationToken ct = default);
            [InquiryUpdate(ReturnEntity = true)] public partial Task<BindingItem?> UpdateAsync(BindingItem item, CancellationToken ct = default);
            [InquiryUpsert(ReturnEntity = true)] public partial Task<BindingItem?> UpsertAsync(BindingItem item, CancellationToken ct = default);
            [InquirySelectOneByKey] public partial Task<BindingItem?> GetAsync(Guid id, CancellationToken ct = default);
            [InquiryDeleteOneByKey] public partial Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
            [InquirySelectAllByField("OptionalToken", OrderBy = "Id", Paged = true)]
            public partial Task<IReadOnlyList<BindingItem>> PageAsync(Guid? token, int offset, int limit, CancellationToken ct = default);
            [InquirySelectAllByPredicate]
            [InquiryWhere("Enabled")]
            [InquiryWhere("ConvertedToken")]
            public partial Task<IReadOnlyList<BindingItem>> SearchAsync(bool enabled, ExternalId token, CancellationToken ct = default);
            [InquiryExists]
            [InquiryWhere("ConvertedEnabled")]
            public partial Task<bool> ExistsAsync(Toggle enabled, CancellationToken ct = default);
            [InquiryUpdateWhere("Enabled")]
            [InquiryWhere("OptionalToken")]
            public partial Task<int> SetEnabledAsync(bool enabled, Guid? token, CancellationToken ct = default);
            [InquiryDeleteWhere]
            [InquiryWhere("OptionalConvertedEnabled")]
            public partial Task<int> DeleteFlagAsync(Toggle? enabled, CancellationToken ct = default);
            [InquiryKeysetPage("Id")]
            public partial Task<InquiryPage<BindingItem, Guid>> SeekAsync(Guid? after, int pageSize, CancellationToken ct = default);
            [InquirySelectAllByPredicate]
            [InquiryWhere("Id", Compare.In)]
            [InquiryWhere("Enabled", Compare.In)]
            public partial Task<IReadOnlyList<BindingItem>> InDirectAsync(IReadOnlyList<Guid> ids, IReadOnlyList<bool> enabled, CancellationToken ct = default);
            [InquirySelectAllByPredicate]
            [InquiryWhere("ConvertedToken", Compare.In)]
            [InquiryWhere("ConvertedEnabled", Compare.In)]
            public partial Task<IReadOnlyList<BindingItem>> InConvertedAsync(IReadOnlyList<ExternalId> tokens, IReadOnlyList<Toggle> enabled, CancellationToken ct = default);
            [InquirySelectAllByPredicate]
            [InquiryWhere("Id", Compare.NotIn)]
            [InquiryWhere("Enabled", Compare.NotIn)]
            public partial Task<IReadOnlyList<BindingItem>> NotInDirectAsync(IReadOnlyList<Guid> ids, IReadOnlyList<bool> enabled, CancellationToken ct = default);
            [InquirySelectAllByPredicate]
            [InquiryWhere("ConvertedToken", Compare.NotIn)]
            [InquiryWhere("ConvertedEnabled", Compare.NotIn)]
            public partial Task<IReadOnlyList<BindingItem>> NotInConvertedAsync(IReadOnlyList<ExternalId> tokens, IReadOnlyList<Toggle> enabled, CancellationToken ct = default);
            [InquiryInsertAll] public partial Task<int> InsertAllAsync(IEnumerable<BindingItem> items, CancellationToken ct = default);
            [InquiryUpdateAll] public partial Task<int> UpdateAllAsync(IEnumerable<BindingItem> items, CancellationToken ct = default);
            [InquiryBulkInsert] public partial Task<long> BulkWriteAsync(IEnumerable<BindingItem> items, CancellationToken ct = default);
        }

        [InquiryTable("GuidParent")]
        public sealed class GuidParent
        {
            [InquiryKey] public Guid Id { get; set; }
            [InquiryRelation(nameof(GuidChild.ParentId))]
            public IReadOnlyList<GuidChild> Children { get; set; } = new List<GuidChild>();
        }

        [InquiryTable("GuidChild")]
        public sealed class GuidChild
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public Guid ParentId { get; set; }
        }

        public partial class GuidParentStore : InquiryStore<GuidParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<GuidParent?> GetAsync(Guid id, CancellationToken ct = default);
        }

        [InquiryTable("ConvertedGuidParent")]
        public sealed class ConvertedGuidParent
        {
            [InquiryKey(Converter = typeof(ExternalIdConverter))] public ExternalId Id { get; set; }
            [InquiryRelation(nameof(ConvertedGuidChild.ParentId))]
            public IReadOnlyList<ConvertedGuidChild> Children { get; set; } = new List<ConvertedGuidChild>();
        }

        [InquiryTable("ConvertedGuidChild")]
        public sealed class ConvertedGuidChild
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn(Converter = typeof(ExternalIdConverter))] public ExternalId ParentId { get; set; }
        }

        public partial class ConvertedGuidParentStore : InquiryStore<ConvertedGuidParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<ConvertedGuidParent?> GetAsync(ExternalId id, CancellationToken ct = default);
        }

        [InquiryTable("BoolGridParent")]
        public sealed class BoolGridParent
        {
            [InquiryKey] public bool Id { get; set; }
            [InquiryRelation(nameof(BoolGridChild.ParentId))]
            public IReadOnlyList<BoolGridChild> Children { get; set; } = new List<BoolGridChild>();
        }

        [InquiryTable("BoolGridChild")]
        public sealed class BoolGridChild
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public bool ParentId { get; set; }
        }

        public partial class BoolGridParentStore : InquiryStore<BoolGridParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<BoolGridParent?> GetAsync(bool id, CancellationToken ct = default);
        }

        [InquiryTable("ConvertedBoolGridParent")]
        public sealed class ConvertedBoolGridParent
        {
            [InquiryKey(Converter = typeof(ToggleConverter))] public Toggle Id { get; set; }
            [InquiryRelation(nameof(ConvertedBoolGridChild.ParentId))]
            public IReadOnlyList<ConvertedBoolGridChild> Children { get; set; } = new List<ConvertedBoolGridChild>();
        }

        [InquiryTable("ConvertedBoolGridChild")]
        public sealed class ConvertedBoolGridChild
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn(Converter = typeof(ToggleConverter))] public Toggle ParentId { get; set; }
        }

        public partial class ConvertedBoolGridParentStore : InquiryStore<ConvertedBoolGridParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<ConvertedBoolGridParent?> GetAsync(Toggle id, CancellationToken ct = default);
        }

        [InquiryTable("BoolParent")]
        public sealed class BoolParent
        {
            [InquiryKey] public bool Id { get; set; }
            [InquiryColumn(Converter = typeof(ToggleConverter))] public Toggle OwnerId { get; set; }
            [InquiryRelation(nameof(OwnerId))] public ToggleOwner? Owner { get; set; }
        }

        [InquiryTable("ToggleOwner")]
        public sealed class ToggleOwner
        {
            [InquiryKey(Converter = typeof(ToggleConverter))] public Toggle Id { get; set; }
        }

        public partial class BoolParentStore : InquiryStore<BoolParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<BoolParent?> GetAsync(bool id, CancellationToken ct = default);
        }

        [InquiryTable("ConvertedBoolParent")]
        public sealed class ConvertedBoolParent
        {
            [InquiryKey(Converter = typeof(ToggleConverter))] public Toggle Id { get; set; }
            [InquiryColumn] public bool OwnerId { get; set; }
            [InquiryRelation(nameof(OwnerId))] public BoolOwner? Owner { get; set; }
        }

        [InquiryTable("BoolOwner")]
        public sealed class BoolOwner
        {
            [InquiryKey] public bool Id { get; set; }
        }

        public partial class ConvertedBoolParentStore : InquiryStore<ConvertedBoolParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<ConvertedBoolParent?> GetAsync(Toggle id, CancellationToken ct = default);
        }

        [InquiryTable("GuidReferenceParent")]
        public sealed class GuidReferenceParent
        {
            [InquiryKey] public Guid Id { get; set; }
            [InquiryColumn] public Guid OwnerId { get; set; }
            [InquiryRelation(nameof(OwnerId))] public GuidOwner? Owner { get; set; }
        }

        [InquiryTable("GuidOwner")]
        public sealed class GuidOwner
        {
            [InquiryKey] public Guid Id { get; set; }
        }

        public partial class GuidReferenceParentStore : InquiryStore<GuidReferenceParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<GuidReferenceParent?> GetAsync(Guid id, CancellationToken ct = default);
        }

        [InquiryTable("ConvertedGuidReferenceParent")]
        public sealed class ConvertedGuidReferenceParent
        {
            [InquiryKey(Converter = typeof(ExternalIdConverter))] public ExternalId Id { get; set; }
            [InquiryColumn(Converter = typeof(ExternalIdConverter))] public ExternalId OwnerId { get; set; }
            [InquiryRelation(nameof(OwnerId))] public ConvertedGuidOwner? Owner { get; set; }
        }

        [InquiryTable("ConvertedGuidOwner")]
        public sealed class ConvertedGuidOwner
        {
            [InquiryKey(Converter = typeof(ExternalIdConverter))] public ExternalId Id { get; set; }
        }

        public partial class ConvertedGuidReferenceParentStore : InquiryStore<ConvertedGuidReferenceParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<ConvertedGuidReferenceParent?> GetAsync(ExternalId id, CancellationToken ct = default);
        }
        """;

    [Fact]
    public void OracleGuidAndBooleanMetadataCoversEveryColumnBackedScalarBinder()
    {
        var result = RunGenerator(OracleGuidBooleanParameterSource, dialect: "Oracle");
        AssertNoErrors(result);
        var text = OracleBindingStoreText(result, "BindingStore");

        foreach (var methodName in new[]
        {
            "InsertAsync", "UpdateAsync", "UpsertAsync", "GetAsync", "DeleteAsync", "PageAsync",
            "SearchAsync", "ExistsAsync", "SetEnabledAsync", "DeleteFlagAsync", "SeekAsync",
        })
        {
            var method = Method(text, methodName);
            Assert.DoesNotContain("DbType.Guid", method);
            Assert.DoesNotContain("DbType.Boolean", method);
            AssertOracleScalarMetadataPrecedesValue(method);
        }

        foreach (var methodName in new[] { "InsertAllAsync", "UpdateAllAsync", "BulkWriteAsync" })
        {
            var descriptor = BatchDescriptor(text, methodName);
            Assert.DoesNotContain("DbType.Guid", descriptor);
            Assert.DoesNotContain("DbType.Boolean", descriptor);
            AssertOracleScalarMetadataPrecedesValue(descriptor);
        }

        Assert.Contains("DbType.Binary", Method(text, "InsertAsync"));
        Assert.Contains("DbType.Int32", Method(text, "InsertAsync"));
        Assert.Contains("DbType.Binary", Method(text, "UpdateAsync"));
        Assert.Contains("DbType.Int32", Method(text, "UpdateAsync"));
        Assert.Contains("DbType.Binary", Method(text, "UpsertAsync"));
        Assert.Contains("DbType.Int32", Method(text, "UpsertAsync"));
        Assert.Contains("DbType.Binary", Method(text, "GetAsync"));
        Assert.Contains("DbType.Binary", Method(text, "DeleteAsync"));
        Assert.Contains("DbType.Binary", Method(text, "PageAsync"));
        Assert.Contains("DbType.Binary", Method(text, "SearchAsync"));
        Assert.Contains("DbType.Int32", Method(text, "SearchAsync"));
        Assert.Contains("DbType.Int32", Method(text, "ExistsAsync"));
        Assert.Contains("DbType.Binary", Method(text, "SetEnabledAsync"));
        Assert.Contains("DbType.Int32", Method(text, "SetEnabledAsync"));
        Assert.Contains("DbType.Int32", Method(text, "DeleteFlagAsync"));
        Assert.Contains("DbType.Binary", Method(text, "SeekAsync"));
        Assert.Contains("DbType.Binary", BatchDescriptor(text, "InsertAllAsync"));
        Assert.Contains("DbType.Int32", BatchDescriptor(text, "InsertAllAsync"));
        Assert.Contains("DbType.Binary", BatchDescriptor(text, "UpdateAllAsync"));
        Assert.Contains("DbType.Int32", BatchDescriptor(text, "UpdateAllAsync"));
        Assert.Contains("DbType.Binary", BatchDescriptor(text, "BulkWriteAsync"));
        Assert.Contains("DbType.Int32", BatchDescriptor(text, "BulkWriteAsync"));

        var insert = Method(text, "InsertAsync");
        Assert.Contains("_e.Id", insert);
        Assert.Contains("_e.Enabled", insert);
        Assert.Contains("_e.OptionalToken", insert);
        Assert.Contains("_e.OptionalEnabled", insert);
        Assert.Contains("ExternalIdConverter>.Instance.ToProvider(_e.ConvertedToken)", insert);
        Assert.Contains("ToggleConverter>.Instance.ToProvider(_e.ConvertedEnabled)", insert);
        Assert.Contains("_e.OptionalConvertedToken is null ? global::System.DBNull.Value", insert);
        Assert.Contains("_e.OptionalConvertedEnabled is null ? global::System.DBNull.Value", insert);
        Assert.DoesNotContain(".ToByteArray()", text);
        Assert.DoesNotContain("? 1 : 0", text);
    }

    [Fact]
    public void OracleCollectionsPreserveJsonInAndTypeExpandedNotInElements()
    {
        var result = RunGenerator(OracleGuidBooleanParameterSource, dialect: "Oracle");
        AssertNoErrors(result);
        var text = OracleBindingStoreText(result, "BindingStore");
        const string guidFromJson = "HEXTORAW(SUBSTR(jt.val, 7, 2) || SUBSTR(jt.val, 5, 2) || SUBSTR(jt.val, 3, 2) || SUBSTR(jt.val, 1, 2) || SUBSTR(jt.val, 12, 2) || SUBSTR(jt.val, 10, 2) || SUBSTR(jt.val, 17, 2) || SUBSTR(jt.val, 15, 2) || SUBSTR(jt.val, 20, 4) || SUBSTR(jt.val, 25, 12))";

        var directIn = Method(text, "InDirectAsync");
        var directGuidSql = $"Id IN (SELECT {guidFromJson} FROM JSON_TABLE(:iq1$Idxxxx$30d4cf864d6e68, '$[*]' COLUMNS(val VARCHAR2(36) PATH '$')) jt)";
        var directInSql = SqlConstantContaining(text, directGuidSql);
        Assert.Contains(directInSql.Name, directIn);
        Assert.Contains("Enabled IN (SELECT CASE jt.val WHEN 'true' THEN 1 WHEN 'false' THEN 0 END FROM JSON_TABLE(:iq1$Enable$f77dd1731be3f8, '$[*]' COLUMNS(val VARCHAR2(5) PATH '$')) jt)", directInSql.Declaration);
        Assert.Contains("InquiryJsonArrayParameter.Bind(_c, \":iq1$Idxxxx$30d4cf864d6e68\", ids);", directIn);
        Assert.Contains("InquiryJsonArrayParameter.Bind(_c, \":iq1$Enable$f77dd1731be3f8\", enabled);", directIn);
        Assert.DoesNotContain("InquiryInExpansion", directIn);
        Assert.DoesNotContain("DbType.Binary", directIn);
        Assert.DoesNotContain("DbType.Int32", directIn);

        var convertedIn = Method(text, "InConvertedAsync");
        var convertedGuidSql = $"ConvertedToken IN (SELECT {guidFromJson} FROM JSON_TABLE(:iq1$Conver$a66c43bcfcaa30, '$[*]' COLUMNS(val VARCHAR2(36) PATH '$')) jt)";
        var convertedInSql = SqlConstantContaining(text, convertedGuidSql);
        Assert.Contains(convertedInSql.Name, convertedIn);
        Assert.Contains("ConvertedEnabled IN (SELECT CASE jt.val WHEN 'true' THEN 1 WHEN 'false' THEN 0 END FROM JSON_TABLE(:iq1$Conver$59f8aef3bdc996, '$[*]' COLUMNS(val VARCHAR2(5) PATH '$')) jt)", convertedInSql.Declaration);
        Assert.Contains("InquiryJsonArrayParameter.Bind(_c, \":iq1$Conver$a66c43bcfcaa30\", tokens is null ? null : global::System.Linq.Enumerable.Select(tokens, static _e => global::Inquiry.Entities.InquiryConverterCache<global::Demo.ExternalIdConverter>.Instance.ToProvider(_e)));", convertedIn);
        Assert.Contains("InquiryJsonArrayParameter.Bind(_c, \":iq1$Conver$59f8aef3bdc996\", enabled is null ? null : global::System.Linq.Enumerable.Select(enabled, static _e => global::Inquiry.Entities.InquiryConverterCache<global::Demo.ToggleConverter>.Instance.ToProvider(_e)));", convertedIn);
        Assert.DoesNotContain("InquiryInExpansion", convertedIn);

        var directNotIn = Method(text, "NotInDirectAsync");
        Assert.Contains("InquiryInExpansion.ExpandNotIn(_c, \":iq1$Idxxxx$30d4cf864d6e68\", ids, _args.MaxParameters, dbType: global::System.Data.DbType.Binary);", directNotIn);
        Assert.Contains("InquiryInExpansion.ExpandNotIn(_c, \":iq1$Enable$f77dd1731be3f8\", enabled, _args.MaxParameters, dbType: global::System.Data.DbType.Int32);", directNotIn);

        var convertedNotIn = Method(text, "NotInConvertedAsync");
        Assert.Contains("InquiryInExpansion.ExpandNotIn(_c, \":iq1$Conver$a66c43bcfcaa30\", tokens is null ? null : global::System.Linq.Enumerable.Select(tokens, static _e => global::Inquiry.Entities.InquiryConverterCache<global::Demo.ExternalIdConverter>.Instance.ToProvider(_e)), _args.MaxParameters, dbType: global::System.Data.DbType.Binary);", convertedNotIn);
        Assert.Contains("InquiryInExpansion.ExpandNotIn(_c, \":iq1$Conver$59f8aef3bdc996\", enabled is null ? null : global::System.Linq.Enumerable.Select(enabled, static _e => global::Inquiry.Entities.InquiryConverterCache<global::Demo.ToggleConverter>.Instance.ToProvider(_e)), _args.MaxParameters, dbType: global::System.Data.DbType.Int32);", convertedNotIn);

        Assert.DoesNotContain(".ToByteArray()", directNotIn + convertedNotIn);
        Assert.DoesNotContain("? 1 : 0", directNotIn + convertedNotIn);
    }

    [Fact]
    public void OracleEagerGridAndSeparateRelationKeysUseProviderMetadataWithoutChangingValues()
    {
        var result = RunGenerator(OracleGuidBooleanParameterSource, dialect: "Oracle");
        AssertNoErrors(result);
        var directGrid = OracleBindingStoreText(result, "GuidParentStore");
        var convertedGrid = OracleBindingStoreText(result, "ConvertedGuidParentStore");
        var directBoolGrid = OracleBindingStoreText(result, "BoolGridParentStore");
        var convertedBoolGrid = OracleBindingStoreText(result, "ConvertedBoolGridParentStore");
        var directSeparate = OracleBindingStoreText(result, "BoolParentStore");
        var convertedSeparate = OracleBindingStoreText(result, "ConvertedBoolParentStore");
        var directGuidSeparate = OracleBindingStoreText(result, "GuidReferenceParentStore");
        var convertedGuidSeparate = OracleBindingStoreText(result, "ConvertedGuidReferenceParentStore");

        Assert.Contains("_grid.ReadListAsync<", directGrid);
        Assert.Contains("new global::Inquiry.Commands.InquiryGeneratedCommand<global::System.Guid>(", directGrid);
        Assert.Contains("_p0.ParameterName = \"iq1$Idxxxx$30d4cf864d6e68\";", directGrid);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Binary;", directGrid);
        Assert.Contains("_p0.Value = (object?)_key ?? global::System.DBNull.Value;", directGrid);
        Assert.Contains("_p1.ParameterName = \"iq1$Parent$b4df331386b214\";", directGrid);
        Assert.Contains("_p1.DbType = global::System.Data.DbType.Binary;", directGrid);
        Assert.Contains("_p1.Value = (object?)_key ?? global::System.DBNull.Value;", directGrid);

        Assert.Contains("_grid.ReadListAsync<", convertedGrid);
        Assert.Contains("_p0.ParameterName = \"iq1$Idxxxx$30d4cf864d6e68\";", convertedGrid);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Binary;", convertedGrid);
        Assert.Contains("_p0.Value = (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.ExternalIdConverter>.Instance.ToProvider(_key);", convertedGrid);
        Assert.Contains("_p1.ParameterName = \"iq1$Parent$b4df331386b214\";", convertedGrid);
        Assert.Contains("_p1.DbType = global::System.Data.DbType.Binary;", convertedGrid);
        Assert.Contains("_p1.Value = (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.ExternalIdConverter>.Instance.ToProvider(_key);", convertedGrid);

        Assert.Contains("_grid.ReadListAsync<", directBoolGrid);
        Assert.Contains("_p0.ParameterName = \"iq1$Idxxxx$30d4cf864d6e68\";", directBoolGrid);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", directBoolGrid);
        Assert.Contains("_p0.Value = (object?)_key ?? global::System.DBNull.Value;", directBoolGrid);
        Assert.Contains("_p1.ParameterName = \"iq1$Parent$b4df331386b214\";", directBoolGrid);
        Assert.Contains("_p1.DbType = global::System.Data.DbType.Int32;", directBoolGrid);
        Assert.Contains("_p1.Value = (object?)_key ?? global::System.DBNull.Value;", directBoolGrid);

        Assert.Contains("_grid.ReadListAsync<", convertedBoolGrid);
        Assert.Contains("_p0.ParameterName = \"iq1$Idxxxx$30d4cf864d6e68\";", convertedBoolGrid);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", convertedBoolGrid);
        Assert.Contains("_p0.Value = (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.ToggleConverter>.Instance.ToProvider(_key);", convertedBoolGrid);
        Assert.Contains("_p1.ParameterName = \"iq1$Parent$b4df331386b214\";", convertedBoolGrid);
        Assert.Contains("_p1.DbType = global::System.Data.DbType.Int32;", convertedBoolGrid);
        Assert.Contains("_p1.Value = (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.ToggleConverter>.Instance.ToProvider(_key);", convertedBoolGrid);

        Assert.DoesNotContain("_grid.ReadListAsync<", directSeparate);
        Assert.Contains("new global::Inquiry.Commands.InquiryGeneratedCommand<bool>(", directSeparate);
        Assert.Contains("_p.ParameterName = \"iq1$Idxxxx$30d4cf864d6e68\";", directSeparate);
        Assert.Contains("_p.DbType = global::System.Data.DbType.Int32;", directSeparate);
        Assert.Contains("_p.Value = (object?)_arg ?? global::System.DBNull.Value;", directSeparate);
        Assert.Contains("_p.Value = (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.ToggleConverter>.Instance.ToProvider(_arg);", directSeparate);
        Assert.Contains("_entity.OwnerId,", directSeparate);

        Assert.DoesNotContain("_grid.ReadListAsync<", convertedSeparate);
        Assert.Contains("_p.ParameterName = \"iq1$Idxxxx$30d4cf864d6e68\";", convertedSeparate);
        Assert.Contains("_p.DbType = global::System.Data.DbType.Int32;", convertedSeparate);
        Assert.Contains("_p.Value = (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.ToggleConverter>.Instance.ToProvider(_arg);", convertedSeparate);
        Assert.Contains("_p.Value = (object?)_arg ?? global::System.DBNull.Value;", convertedSeparate);
        Assert.Contains("_entity.OwnerId,", convertedSeparate);

        Assert.DoesNotContain("_grid.ReadListAsync<", directGuidSeparate);
        Assert.Contains("_p.ParameterName = \"iq1$Idxxxx$30d4cf864d6e68\";", directGuidSeparate);
        Assert.Contains("_p.DbType = global::System.Data.DbType.Binary;", directGuidSeparate);
        Assert.Contains("_p.Value = (object?)_arg ?? global::System.DBNull.Value;", directGuidSeparate);
        Assert.Contains("_entity.OwnerId,", directGuidSeparate);

        Assert.DoesNotContain("_grid.ReadListAsync<", convertedGuidSeparate);
        Assert.Contains("_p.ParameterName = \"iq1$Idxxxx$30d4cf864d6e68\";", convertedGuidSeparate);
        Assert.Contains("_p.DbType = global::System.Data.DbType.Binary;", convertedGuidSeparate);
        Assert.Contains("_p.Value = (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.ExternalIdConverter>.Instance.ToProvider(_arg);", convertedGuidSeparate);
        Assert.Contains("_entity.OwnerId,", convertedGuidSeparate);

        foreach (var text in new[]
        {
            directGrid, convertedGrid, directBoolGrid, convertedBoolGrid,
            directSeparate, convertedSeparate, directGuidSeparate, convertedGuidSeparate,
        })
        {
            Assert.DoesNotContain("DbType.Guid", text);
            Assert.DoesNotContain("DbType.Boolean", text);
            Assert.DoesNotContain(".ToByteArray()", text);
            Assert.DoesNotContain("? 1 : 0", text);
        }
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void NonOracleGuidAndBooleanMetadataAndValuesRemainPortable(string dialect)
    {
        var result = RunGenerator(OracleGuidBooleanParameterSource, dialect: dialect);
        AssertNoErrors(result);
        var text = OracleBindingStoreText(result, "BindingStore");

        Assert.Contains("DbType.Guid", Method(text, "InsertAsync"));
        Assert.Contains("DbType.Boolean", Method(text, "InsertAsync"));
        Assert.Contains("DbType.Guid", Method(text, "SearchAsync"));
        Assert.Contains("DbType.Boolean", Method(text, "SearchAsync"));
        Assert.Contains("DbType.Guid", Method(text, "SeekAsync"));
        Assert.DoesNotContain(".ToByteArray()", text);
        Assert.DoesNotContain("? 1 : 0", text);
    }

    private static string OracleBindingStoreText(GeneratorTestResult result, string storeName)
    {
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            t => string.Equals(
                global::System.IO.Path.GetFileName(t.FilePath),
                $"Demo.{storeName}.InquiryStore.g.cs",
                StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    private static (string Name, string Declaration) SqlConstantContaining(string generated, string sqlFragment)
    {
        var fragment = generated.IndexOf(sqlFragment, StringComparison.Ordinal);
        Assert.True(fragment >= 0, $"Generated SQL fragment '{sqlFragment}' was not found.");
        const string marker = "private const string ";
        var start = generated.LastIndexOf(marker, fragment, StringComparison.Ordinal);
        Assert.True(start >= 0, "Generated SQL constant declaration was not found.");
        var nameStart = start + marker.Length;
        var nameEnd = generated.IndexOf(' ', nameStart);
        Assert.True(nameEnd > nameStart, "Generated SQL constant name was not terminated.");
        var name = generated[nameStart..nameEnd];
        var end = generated.IndexOf(';', start);
        Assert.True(end > fragment, $"Generated SQL constant '{name}' was not terminated.");
        return (name, generated[start..(end + 1)]);
    }

    private static void AssertOracleScalarMetadataPrecedesValue(string method)
    {
        const string metadataPattern = @"(?<parameter>[A-Za-z_][A-Za-z0-9_]*)\.DbType = global::System\.Data\.DbType\.(?:Binary|Int32);";
        var metadata = global::System.Text.RegularExpressions.Regex.Matches(method, metadataPattern);
        Assert.True(metadata.Count > 0, "Expected at least one Oracle Guid/boolean metadata assignment.");
        var ordered = global::System.Text.RegularExpressions.Regex.Matches(
            method,
            metadataPattern + @"\r?\n\s+\k<parameter>\.Value =");
        Assert.Equal(metadata.Count, ordered.Count);
    }
}
