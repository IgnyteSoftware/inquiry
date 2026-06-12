# Resiliency (retry & failover)

Inquiry's connection factories support two layers of open-time resiliency. Statement and
transaction retry are deliberately out of scope — replays of partially applied work belong to the
application, not the ORM.

## Transient-fault retry (cloud compatibility)

For cloud engines where throttling/failover faults are expected at connection open, the SQL Server
and PostgreSQL providers retry the open with exponential backoff plus jitter, classifying faults
with an engine-specific transient-error detector:

```csharp
services.AddInquirySqlServer(connectionString, o =>
{
    o.Compatibility = SqlServerCompatibility.AzureSql; // enables open-time retry
    o.MaxAttempts = 5;                                 // default
    o.RetryBaseDelay = TimeSpan.FromMilliseconds(200); // default
    o.RetryMaxDelay = TimeSpan.FromSeconds(30);        // default
});
```

PostgreSQL exposes the same knobs via `PostgreSqlCompatibility` (`CockroachDb`,
`AuroraPostgreSql`).

## Backup-server failover

Every server-based provider (SQL Server, PostgreSQL, MySQL/MariaDB, Oracle) accepts an optional
**failover connection string**. When the primary fails to open — after any configured retry — the
factory opens against the backup server instead:

```csharp
services.AddInquiryPostgreSql(primaryConnectionString, o =>
{
    o.FailoverConnectionString = backupConnectionString;
});
```

Semantics:

- Failover applies **per open**: every open tries the primary first, so traffic returns to the
  primary automatically once it recovers.
- A configured retry policy applies to **each** server in turn.
- Cancellation is never treated as a server fault — it propagates immediately.
- If both servers fail, an `AggregateException` carrying both faults is thrown.

The backup server must be able to serve the workload (e.g. a read replica promoted for reads, a
standby with replication). Inquiry does not replicate data or verify the failover target's role.

Driver-level alternatives remain available when preferred: SQL Server `Failover Partner=`, Npgsql
multi-host connection strings (`Host=a,b` + `Target Session Attributes`), MySqlConnector
`Server=primary,backup`, and Oracle TNS `ADDRESS_LIST`/`FAILOVER=on`. SQLite is an embedded file
database, so server failover does not apply.
