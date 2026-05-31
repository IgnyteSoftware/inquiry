using System.Net.Sockets;
using Inquiry.Connections;
using Inquiry.PostgreSql;
using Npgsql;

namespace Inquiry.PostgreSql.Tests;

public sealed class PostgreSqlTransientErrorDetectorTests
{
    private static PostgresException Postgres(string sqlState) =>
        new(messageText: "boom", severity: "ERROR", invariantSeverity: "ERROR", sqlState: sqlState);

    public sealed class Cockroach
    {
        private readonly CockroachDbTransientErrorDetector _detector = new();

        [Theory]
        [InlineData("40001")] // serialization_failure
        [InlineData("08006")] // connection_failure
        [InlineData("57P01")] // admin_shutdown
        public void DocumentedSqlStatesAreTransient(string sqlState)
        {
            Assert.True(_detector.IsTransient(Postgres(sqlState)));
        }

        [Theory]
        [InlineData("23505")] // unique_violation
        [InlineData("42P01")] // undefined_table
        [InlineData("28P01")] // invalid_password
        public void OtherSqlStatesAreNotTransient(string sqlState)
        {
            Assert.False(_detector.IsTransient(Postgres(sqlState)));
        }

        [Fact]
        public void NonPostgresExceptionIsNotTransient()
        {
            Assert.False(_detector.IsTransient(new InvalidOperationException()));
        }
    }

    public sealed class Aurora
    {
        private readonly AuroraTransientErrorDetector _detector = new();

        [Theory]
        [InlineData("08006")] // connection_failure
        [InlineData("08001")] // sqlclient_unable_to_establish_sqlconnection
        [InlineData("08004")] // sqlserver_rejected_establishment_of_sqlconnection
        [InlineData("57P01")] // admin_shutdown
        public void ConnectionClassAndShutdownAreTransient(string sqlState)
        {
            Assert.True(_detector.IsTransient(Postgres(sqlState)));
        }

        [Theory]
        [InlineData("23505")] // unique_violation
        [InlineData("42P01")] // undefined_table
        public void TerminalSqlStatesAreNotTransient(string sqlState)
        {
            Assert.False(_detector.IsTransient(Postgres(sqlState)));
        }

        [Fact]
        public void TransportLevelNpgsqlExceptionIsTransient()
        {
            // Npgsql flags an NpgsqlException wrapping a socket fault as transient; this models a
            // dropped connection during Aurora failover.
            var transport = new NpgsqlException("socket reset", new SocketException());
            Assert.True(transport.IsTransient);
            Assert.True(_detector.IsTransient(transport));
        }

        [Fact]
        public void NonNpgsqlExceptionIsNotTransient()
        {
            Assert.False(_detector.IsTransient(new InvalidOperationException()));
        }
    }
}
