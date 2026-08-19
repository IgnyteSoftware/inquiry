# Support

## Getting help

- **Documentation:** <https://ignytesoftware.github.io/inquiry/>
- **Bug reports and feature requests:** [GitHub Issues](https://github.com/IgnyteSoftware/inquiry/issues)
- **Discussions:** [GitHub Discussions](https://github.com/IgnyteSoftware/inquiry/discussions)

## Supported versions

Inquiry follows [Semantic Versioning 2.0.0](https://semver.org/). Once a line is released, only its latest patch receives bug fixes and security patches.

No stable release has shipped yet; `1.0.0-preview` packages are published on [nuget.org](https://www.nuget.org/packages/Ignyte.Inquiry) and receive fixes only in the latest preview.

| Version         | Status                          | .NET TFMs               |
|-----------------|---------------------------------|-------------------------|
| 1.0.0-preview.x | Published previews              | net8.0, net9.0, net10.0 |
| 1.0.x           | Upcoming first stable release   | net8.0, net9.0, net10.0 |

## Target framework policy

Inquiry tracks the .NET support lifecycle. The floor is always the oldest LTS version that is still in active support. STS releases that were active at ship time are included as a convenience and may be dropped in a minor release after Microsoft ends support. Dropping a supported LTS target framework is a breaking change and ships only in a major release.

## Compatibility and breaking changes

The public API surface is tracked with `Microsoft.CodeAnalysis.PublicApiAnalyzers`. Additions ship in minor releases; removals and signature changes ship only in major releases with at least one minor release of obsolescence warnings.

A **breaking change** is any modification that causes existing code that compiled and ran correctly against the prior version to fail to compile, throw at runtime, or produce different data. This includes:

- Removing or renaming a public type, method, property, or constant.
- Changing the signature of a public method (parameter types, return type, generic constraints).
- Changing the SQL emitted by the source generator for an unchanged store declaration.
- Changing the wire format of a runtime artifact (TVP descriptors, bulk-insert streams).
- Removing a supported target framework.

Behavioral bug fixes that correct documented-but-wrong behavior are not considered breaking, but are called out in the changelog.

## Provider support matrix

| Provider       | Package              | ADO.NET driver                |
|----------------|----------------------|-------------------------------|
| SQLite         | `Inquiry.Sqlite`     | `Microsoft.Data.Sqlite`       |
| SQL Server     | `Inquiry.SqlServer`  | `Microsoft.Data.SqlClient`    |
| PostgreSQL     | `Inquiry.PostgreSql` | `Npgsql`                      |
| MySQL          | `Inquiry.MySql`      | `MySqlConnector`              |
| MariaDB        | `Inquiry.MariaDb`    | `MySqlConnector`              |
| Oracle         | `Inquiry.Oracle`     | `Oracle.ManagedDataAccess.Core` |

## Companion packages

| Package              | Purpose                                                        |
|----------------------|----------------------------------------------------------------|
| `Inquiry.Interceptors` | Slow-query logging, sqlcommenter trace-context tagging, N+1 detection |
| `Inquiry.Testing`      | SQLite fixture, recording interceptor, entity factory, transaction sandbox, Respawn reset |
