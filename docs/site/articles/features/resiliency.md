# Resiliency (retry & failover)

Inquiry's connection factories support two layers of open-time resiliency. Statement and
transaction retry are deliberately out of scope — replays of partially applied work belong to the
application, not the ORM.

## Provider capability matrix

Not every provider exposes both layers. The table below shows what each provider supports:

| Capability | SQL Server | PostgreSQL | MySQL / MariaDB | Oracle |
|---|---|---|---|---|
| Transient-fault retry | Yes (`AzureSql`) | Yes (`CockroachDb`, `AuroraPostgreSql`) | No | No |
| `MaxAttempts` / `RetryBaseDelay` / `RetryMaxDelay` | Yes | Yes | — | — |
| Backup-server failover (`FailoverConnectionString`) | Yes | Yes | Yes | Yes |

MySQL and Oracle support failover only — they have no built-in transient-fault retry at the Inquiry
level. If you need open-time retry on those engines, implement it in the application or use a
driver-level mechanism (e.g. MySqlConnector `Server=primary,backup`, Oracle TNS
`ADDRESS_LIST`/`FAILOVER=on`).

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

MySQL and Oracle do not expose retry options — see the capability matrix above.

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
- On providers with retry enabled, the retry policy applies to **each** server in turn. On
  providers without retry (MySQL, Oracle), each server gets a single open attempt.
- Cancellation is never treated as a server fault — it propagates immediately.
- If both servers fail, an `AggregateException` carrying both faults is thrown.

The backup server must be able to serve the workload (e.g. a read replica promoted for reads, a
standby with replication). Inquiry does not replicate data or verify the failover target's role.

Driver-level alternatives remain available when preferred: SQL Server `Failover Partner=`, Npgsql
multi-host connection strings (`Host=a,b` + `Target Session Attributes`), MySqlConnector
`Server=primary,backup`, and Oracle TNS `ADDRESS_LIST`/`FAILOVER=on`. SQLite is an embedded file
database, so server failover does not apply.
