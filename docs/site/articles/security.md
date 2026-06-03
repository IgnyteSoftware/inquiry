# Security model

Inquiry has one security-relevant boundary worth understanding up front: **generated store methods are injection-safe by construction; raw-SQL APIs execute the SQL string you give them verbatim and trust you to parameterize.**

This is the same trust model as Dapper and the raw ADO.NET layer underneath both.

## Generated stores — no injection surface

Every method the generator emits for an `InquiryStore<T>` is safe against SQL injection by design, because of three architectural constraints (see [Architecture](architecture.md#key-design-constraints)):

- **SQL is compile-time `const string`.** The runtime never builds, formats, or interpolates SQL. Each generated statement is baked into the assembly at build time; there is no code path that concatenates a string into a command at run time.
- **Identifiers come from compile-time metadata.** Table and column names are read from your entity attributes (`[InquiryTable]`, `[InquiryColumn]`, …) at generation time and quoted per dialect by the `SqlBuilder` (`"Customers"`, `[Customers]`, `` `Customers` ``). They are never sourced from runtime input.
- **Values are always bound as parameters.** A generated binder writes each value into a real `DbParameter` (`_p0.Value = entity.CompanyName`) and adds it to the command's parameter collection — it never appears in the SQL text. `OrderBy` directions, pagination offsets/limits, and keyset cursors are likewise compile-time-validated identifiers or bound parameters, not interpolated strings.

The upshot: **no user-supplied value can change the structure of a generated query.** Passing `'; DROP TABLE Customers; --` as a customer name inserts/queries that literal string; it cannot break out of the parameter.

`OrderBy` deserves a specific note because it controls SQL structure rather than a value: its argument is a compile-time attribute constant, and the generator validates every column name (`INQ021`) and direction token (`INQ042`) at build time. There is no runtime `OrderBy` injection vector because there is no runtime `OrderBy` input.

## Raw-SQL APIs — you own the SQL string

The ad-hoc `IInquiry` overloads that take a SQL string execute it **verbatim**. Inquiry does not parse, rewrite, or sanitize the `commandText`:

- `ExecuteAsync(string commandText, …)`
- `QueryAsync<T>(string commandText, …)` / `QueryListAsync<T>(string commandText, …)`
- `QuerySingleOrDefaultAsync<T>(string commandText, …)`
- `ExecuteScalarAsync<T>(string commandText, …)`
- anything you build by hand as an `InquiryCommand`

These exist for queries the generator doesn't cover. With them, **safe parameterization is your responsibility** — but the API makes the safe path the easy one. Pass dynamic values through the `object? parameters` argument (or an `InquiryParameter` / `InquiryCommand`); Inquiry turns each into a bound `DbParameter` via `InquiryParameterReader` + `InquiryParameterBinder`, exactly like the generated path:

```csharp
// SAFE — the value is bound, never concatenated.
await inquiry.QueryListAsync<Customer>(
    "SELECT * FROM Customers WHERE Country = @country",
    new { country = userInput });

// UNSAFE — never do this. userInput is concatenated into the SQL text.
await inquiry.QueryListAsync<Customer>(
    "SELECT * FROM Customers WHERE Country = '" + userInput + "'");
```

The rule is the ordinary one: **never string-concatenate untrusted input into a SQL command.** Bind it.

## Credentials and connection strings

Connection strings — and the credentials in them — are the host application's responsibility. Inquiry takes a connection string at DI-registration time (`AddInquirySqlServer(connectionString)`, …) and hands it to the provider's `DbConnection`; it never logs it. Keep real credentials out of source control: load them from environment variables, a secrets manager, or your platform's configuration provider. (The bundled sample demonstrates this with its `INQUIRY_SAMPLE_DB` environment-variable override — see [`samples/Inquiry.Sample/README.md`](https://github.com/JakeOverstreet/inquiry/blob/main/samples/Inquiry.Sample/README.md).)

## Reporting

Found a security issue in Inquiry itself (not in caller-supplied raw SQL)? Open a private report via the repository's GitHub security advisories rather than a public issue.
