# Support

## Getting help

- **Documentation:** <https://ignytesoftware.github.io/inquiry/>
- **Bug reports and feature requests:** [GitHub Issues](https://github.com/IgnyteSoftware/inquiry/issues)
- **Discussions:** [GitHub Discussions](https://github.com/IgnyteSoftware/inquiry/discussions)

## Supported versions

Inquiry follows [Semantic Versioning 2.0.0](https://semver.org/). Once a line is released, only its latest patch receives bug fixes and security patches.

No stable release has shipped yet. Previews and `1.0.0-rc.N` release candidates publish to [nuget.org](https://www.nuget.org/packages/Ignyte.Inquiry). Once a release candidate is published, only the latest one receives fixes.

| Version         | Status                          | .NET TFMs               |
|-----------------|---------------------------------|-------------------------|
| 1.0.0-preview.x | Earlier previews, no fixes      | net8.0, net9.0, net10.0 |
| 1.0.0-rc.x      | Release candidates              | net10.0, net11.0        |
| 1.0.x           | Upcoming first stable release   | net10.0, net11.0        |

To upgrade from a preview, follow [Upgrade to 1.0](https://ignytesoftware.github.io/inquiry/articles/upgrading.html).

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

| Provider       | Package                     | ADO.NET driver                | Aspire integration |
|----------------|-----------------------------|-------------------------------|--------------------|
| SQLite         | `Ignyte.Inquiry.Sqlite`     | `Microsoft.Data.Sqlite`       | `Ignyte.Inquiry.Aspire.Sqlite` |
| SQL Server     | `Ignyte.Inquiry.SqlServer`  | `Microsoft.Data.SqlClient`    | `Ignyte.Inquiry.Aspire.SqlServer` |
| PostgreSQL     | `Ignyte.Inquiry.PostgreSql` | `Npgsql`                      | `Ignyte.Inquiry.Aspire.PostgreSql` |
| MySQL          | `Ignyte.Inquiry.MySql`      | `MySqlConnector`              | `Ignyte.Inquiry.Aspire.MySql` |
| MariaDB        | `Ignyte.Inquiry.MariaDb`    | `MySqlConnector`              | `Ignyte.Inquiry.Aspire.MariaDb` |
| Oracle         | `Ignyte.Inquiry.Oracle`     | `Oracle.ManagedDataAccess.Core` | `Ignyte.Inquiry.Aspire.Oracle` |

## Companion packages

| Package                       | Purpose                                                        |
|-------------------------------|----------------------------------------------------------------|
| `Ignyte.Inquiry.Aspire` | Provider-neutral Aspire telemetry and health-check defaults |
| `Ignyte.Inquiry.Aspire.SqlServer` / `.PostgreSql` / `.MySql` / `.MariaDb` / `.Oracle` / `.Sqlite` | Aspire resource-name registration for one provider |
| `Ignyte.Inquiry.AspNetCore`   | ASP.NET Core audit-context middleware (`UseInquiryAuditContext`) |
| `Ignyte.Inquiry.Interceptors` | Slow-query logging, sqlcommenter trace-context tagging, N+1 detection |
| `Ignyte.Inquiry.Testing`      | Provider-neutral recording, entity factories, transaction sandboxes, and reset helpers |
| `Ignyte.Inquiry.Sqlite.Testing` | In-memory SQLite fixture |
