namespace Inquiry.Stores;

/// <summary>
/// Generates a provider-native bulk insert — the 100k+-row tier above <see cref="InquiryInsertAllAttribute"/>.
/// On SQL Server this rides <c>SqlBulkCopy</c>, on PostgreSQL binary <c>COPY</c>, and on MySQL
/// <c>MySqlBulkCopy</c>; on dialects without a bulk-copy API (SQLite, Oracle) the generator falls
/// back to the multi-row batch <c>INSERT</c> at compile time. The method takes an
/// <see cref="System.Collections.Generic.IEnumerable{T}"/> (or <c>IReadOnlyList&lt;T&gt;</c>) of the
/// entity plus a <see cref="System.Threading.CancellationToken"/> and returns <c>Task&lt;long&gt;</c>
/// (rows written). An empty collection is a no-op returning 0.
/// </summary>
/// <remarks>
/// Rows stream to the server — no parameter cap applies on bulk-copy dialects (the batch-SQL
/// fallback keeps its cap). Database-generated, database-default, and database-generated
/// concurrency-token columns are omitted; sequential-GUID keys and auditing timestamps are stamped
/// per row as the stream is enumerated. Bulk insert opens a dedicated connection and does
/// <b>not</b> join an ambient Inquiry transaction; interceptors and telemetry do not observe it.
/// On MySQL, <c>MySqlBulkCopy</c> requires <c>AllowLoadLocalInfile=true</c> on the connection
/// string and <c>local_infile=1</c> on the server.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryBulkInsertAttribute : Attribute
{
}
