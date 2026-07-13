using System.Collections.Generic;

namespace Inquiry.Generators.Models;

internal sealed record SchemaManifestData(string ProviderId, IReadOnlyList<SchemaManifestTableData> Tables, IReadOnlyList<SchemaManifestArtifactData> ProviderArtifacts);
internal sealed record SchemaManifestTableData(string? Schema, string Name, IReadOnlyList<SchemaManifestColumnData> Columns,
    IReadOnlyList<string>? PrimaryKey, IReadOnlyList<SchemaManifestIndexData> Indexes, IReadOnlyList<SchemaManifestCheckData> Checks, IReadOnlyList<SchemaManifestForeignKeyData> ForeignKeys);
internal sealed record SchemaManifestColumnData(string Name, string? StoreType, string TypeInference, string TypeClass, bool Nullable,
    int? PrimaryKeyOrdinal, string Generation, string? DefaultExpression, string? ComputedExpression, string Concurrency);
internal sealed record SchemaManifestIndexData(string Name, bool Unique, IReadOnlyList<string> KeyColumns, IReadOnlyList<string> IncludeColumns);
internal sealed record SchemaManifestCheckData(string Name, string Expression);
internal sealed record SchemaManifestForeignKeyData(string? Name, IReadOnlyList<string> LocalColumns, string? ReferencedSchema,
    string ReferencedTable, IReadOnlyList<string> ReferencedColumns, string OnDelete, string OnUpdate);
internal sealed record SchemaManifestArtifactData(string Schema, string Name, string Kind, string Signature);
