# Security model

Inquiry has one security-relevant boundary worth understanding up front: **generated store methods are injection-safe by construction, and ad-hoc facade SQL parameterizes interpolated values before it reaches ADO.NET.**

## Current scan status

A formal Codex Security repository scan was completed during pre-release hardening. The validated findings
were fixed on `main` in `318ee5f` (`Fix security scan findings`): lazy `IEnumerable<T>` batch operations now
check the configured parameter cap during materialization, MySQL update-returning on optimistic-concurrency
entities returns `null` for stale updates instead of re-reading the current row, and Oracle generated bind
names preserve leading-underscore parameters without collisions.

## Generated stores - no injection surface

Every method the generator emits for an `InquiryStore<T>` is safe against SQL injection by design, because of three architectural constraints (see [Architecture](architecture.md#key-design-constraints)):

- **SQL is compile-time `const string`.** The runtime never builds, formats, or interpolates SQL. Each generated statement is baked into the assembly at build time; there is no code path that concatenates a string into a command at run time.
- **Identifiers come from compile-time metadata.** Table and column names are read from your entity attributes (`[InquiryTable]`, `[InquiryColumn]`, ...) at generation time and quoted per dialect by the `SqlBuilder` (`"Customers"`, `[Customers]`, `` `Customers` ``). They are never sourced from runtime input.
- **Values are always bound as parameters.** A generated binder writes each value into a real `DbParameter` (`_p0.Value = entity.CompanyName`) and adds it to the command's parameter collection. It never appears in the SQL text. `OrderBy` directions, pagination offsets/limits, and keyset cursors are likewise compile-time-validated identifiers or bound parameters, not interpolated strings.

The upshot: **no user-supplied value can change the structure of a generated query.** Passing `'; DROP TABLE Customers; --` as a customer name inserts/queries that literal string; it cannot break out of the parameter.

`OrderBy` deserves a specific note because it controls SQL structure rather than a value: its argument is a compile-time attribute constant, and the generator validates every column name (`INQ021`) and direction token (`INQ042`) at build time. There is no runtime `OrderBy` injection vector because there is no runtime `OrderBy` input.

## Ad-hoc SQL - interpolation is parameterized

The ad-hoc `IInquiry` and `IInquiryTransaction` overloads take `FormattableString`, not `string`. Each interpolation hole becomes a bound parameter before the command reaches the provider:

```csharp
var customers = await inquiry.QueryListAsync<Customer>(
    $"SELECT * FROM Customers WHERE Country = {userInput}");
```

That call sends SQL shaped like `SELECT * FROM Customers WHERE Country = @p0` and binds `userInput` as the value of `@p0`. The same safe path is also available explicitly through `InquirySql.Sql(FormattableString)` when you need an `InquiryCommand` value:

```csharp
var command = InquirySql.Sql($"SELECT * FROM Customers WHERE Country = {userInput}");
var customers = await inquiry.QueryListAsync<Customer>(command);
```

Plain string facade overloads such as `ExecuteAsync(string)` and `QueryListAsync<T>(string)` are intentionally not available. To run hand-authored raw SQL or stored-procedure commands, build an `InquiryCommand` explicitly. Treat that as the advanced escape hatch: **never string-concatenate untrusted input into an `InquiryCommand`'s command text.** Bind values through generated stores, interpolated facade calls, `InquirySql.Sql(...)`, or explicit `InquiryParameter` values.

The escape hatch is linted: passing a **non-constant** string as `InquiryCommand`'s command text raises analyzer warning **`INQ048`** at the call site (concatenations, interpolated strings, and variables all trigger it; literals, `const` fields, `nameof`, and constant concatenation stay silent). Dynamic SQL composed from trusted fragments is sometimes legitimate — review the call site and suppress the warning there (`#pragma warning disable INQ048` or `.editorconfig`) once you're satisfied no user input can reach the text. Generated code is excluded from the lint.

## MySQL user-variables caveat

Inquiry enables `AllowUserVariables=true` on every **MySQL** connection (the emulated insert-returning
path needs it for the collision-safe `@'__inquiry.generated-key'` capture used by non-auto
database-default keys). This has one important consequence for **ad-hoc SQL**: if you misspell a
`@parameter` name in hand-written SQL, MySqlConnector treats the unrecognized name as a MySQL user
variable and evaluates it as `NULL` — silently, with no error.

Generated store methods are unaffected (their SQL and parameters are compile-time constants), and the `FormattableString` facade path auto-names its parameters (`@p0`, `@p1`, …), so the risk is limited to explicitly constructed `InquiryCommand` instances where you author parameter names by hand. If a query unexpectedly returns no rows or null columns on MySQL, verify that every `@name` in the command text matches a parameter in the collection.

**MariaDB** is not affected — the MariaDB provider uses native `INSERT…RETURNING` and does not force `AllowUserVariables`.

## Credentials and connection strings

Connection strings, and the credentials in them, are the host application's responsibility. Inquiry takes a connection string at DI-registration time (`AddInquirySqlServer(connectionString)`, ...) and hands it to the provider's `DbConnection`; it never logs it. Keep real credentials out of source control: load them from environment variables, a secrets manager, or your platform's configuration provider. The bundled sample demonstrates this with its `INQUIRY_SAMPLE_DB` environment-variable override; see [`samples/Inquiry.Sample/README.md`](https://github.com/IgnyteSoftware/inquiry/blob/main/samples/Inquiry.Sample/README.md).

## Telemetry and data exposure

The opt-in telemetry layer (`AddInquiryTelemetry()`) never records parameter values — Inquiry SQL is compile-time-constant and data flows only through bound parameters. Spans and debug logs carry the SQL text (table/column names) by default; set `RecordCommandText = false` to redact it. See [Observability](features/observability.md).

## Reporting

Found a security issue in Inquiry itself? Open a private report via [GitHub private vulnerability reporting](https://github.com/IgnyteSoftware/inquiry/security/advisories/new) rather than a public issue — see [SECURITY.md](https://github.com/IgnyteSoftware/inquiry/blob/main/SECURITY.md) for the disclosure policy.
