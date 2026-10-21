using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Inquiry.Sqlite.Tests;

internal sealed class RecordingDbDataSource : DbDataSource
{
    public bool Opened { get; private set; }

    public override string ConnectionString => "recording";

    protected override DbConnection CreateDbConnection() => new RecordingDbConnection();

    protected override ValueTask<DbConnection> OpenDbConnectionAsync(CancellationToken cancellationToken = default)
    {
        Opened = true;
        return ValueTask.FromResult<DbConnection>(new RecordingDbConnection());
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
