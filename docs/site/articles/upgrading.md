# Upgrade to 1.0

This guide covers the move from `1.0.0-preview.12`, the last published preview, to `1.0.0-rc.1` and the
1.0 releases that follow it. The changes that need action are listed here. The
[changelog](https://github.com/IgnyteSoftware/inquiry/blob/main/CHANGELOG.md) has the full entry for each
one. `1.0.0-preview.13` was prepared but never published, so its changes are part of `1.0.0-rc.1`.

From 1.0 on, the public API and the diagnostic IDs are frozen. Later 1.x releases add to them but do not
remove or change them. See [Support](https://github.com/IgnyteSoftware/inquiry/blob/main/SUPPORT.md).

## 1. Move to .NET 10 and align the package versions

- Every package targets `net10.0` and `net11.0`. The `net8.0` and `net9.0` targets are removed. Move the
  consuming project to .NET 10 or later. A project that must stay on .NET 8 or .NET 9 can keep
  `1.0.0-preview.12`, which receives no further fixes.
- Every `Ignyte.Inquiry*` package now depends on the other Inquiry packages at exactly its own version.
  Set every `Ignyte.Inquiry*` reference to the same version and update them together. A mix fails restore
  with `NU1107` or `NU1605`, or warns with `NU1608`.
- `Ignyte.Inquiry.MySql`, `Ignyte.Inquiry.MariaDb`, `Ignyte.Inquiry.Aspire.MySql` and
  `Ignyte.Inquiry.Aspire.MariaDb` depend on MySqlConnector 3.0.0. If your own code uses MySqlConnector,
  check its 3.0 release notes.
- The Aspire registrations moved from `Ignyte.Inquiry.Aspire` to one package per provider, such as
  `Ignyte.Inquiry.Aspire.SqlServer` for `AddInquirySqlServer`. Replace the `Ignyte.Inquiry.Aspire`
  reference with the package for your provider. Calls such as `builder.AddInquirySqlServer("name")`
  compile unchanged. A call through the class name, such as
  `InquiryAspireExtensions.AddInquirySqlServer(builder, "name")`, must use the new class, such as
  `InquiryAspireSqlServerExtensions`. See [Aspire integration](features/aspire.md).
- On .NET 11, `Ignyte.Inquiry.Aspire` and the `Ignyte.Inquiry.Aspire.*` provider packages depend on the
  Microsoft.Extensions 11.0.0 packages, which OpenTelemetry 1.21.0 needs there. On .NET 10, they and the
  other packages depend on Microsoft.Extensions 10.0.15. `1.0.0-rc.1` used 11.0.0 on both targets.
- If you upgrade from `1.0.0-preview.10` or earlier: `SqliteInquiryFixture` moved to the
  `Ignyte.Inquiry.Sqlite.Testing` package and the `Inquiry.Sqlite.Testing` namespace in
  `1.0.0-preview.11`. Add that package and change the `using` directive.

## 2. Fix the new build errors

Declarations that compiled before and then failed or did the wrong thing at run time now fail the build.
The [diagnostic repair index](diagnostics.md) has the fix for each ID.

| Diagnostic | What it reports | Fix |
|---|---|---|
| `INQ106` | Two properties of an `[InquiryAdHoc]` DTO or `[InquiryProjection]` claim the same column name, without regard to case. | Rename one `[InquiryAdHoc]` property. For a projection, give one property a distinct `[InquiryColumn]` name, or rename it if its column name comes from the property name. |
| `INQ114` | An `[InquiryStoredProcedure]` method that the target database cannot run. Procedures run on SQL Server and Oracle only. | Remove the method, or call the SQL through `IInquiry`. On Oracle, see [stored procedures](features/stored-procedures.md). |
| `INQ115` | `[InquiryWhere]` criteria on an operation that ignores them. | Use `[InquirySelectAllByPredicate]` or `[InquiryCount]`, or remove the criteria. |
| `INQ116` | `[InquiryForeignKey(typeof(T), "Name")]` where `T` has no property `Name`. | Use `nameof`, or `[InquiryForeignKey("Table", "Column")]` for physical names. |
| `INQ039` | A locking select that the database always rejects: `Distinct` with a lock on PostgreSQL or Oracle, or offset paging with a lock on Oracle. | Remove `Distinct`, the paging or the lock. |
| `CS8795` | A store method that a suppressed or lowered Inquiry error rejected. Before, it compiled into a stub that threw `NotSupportedException`. | Fix the declaration that the suppressed error reports. |

Lowering `INQ039` project-wide still generates a throwing stub for the methods that report `INQ039`.

New warnings fail builds that treat warnings as errors:

- `INQ112`: positional criteria bind two or more parameters of the same type. Set `Parameter = nameof(x)`
  on each criterion.
- `INQ117`: a non-nullable column sets `UseDatabaseDefault = true` with no `DefaultExpression` on an entity
  that generates DDL. Add a `DefaultExpression`, or set `GenerateDdl = false`.
- `INQ120`, added in `1.0.0-rc.2`: constant handwritten SQL returns no column, or two columns, for a
  property of its result type. See the name-mapping change below.

## 3. Check the mapping changes

- **Handwritten SQL and stored procedures map by column name.** Before, results mapped by `SELECT`-list
  position. For `[InquiryAdHoc]` DTOs, alias each result column to its CLR property name, for example
  `SUM(Amount) AS TotalAmount`. For entities and projections, including entity results of stored
  procedures, return every mapped column under the column name the entity maps it to, such as a name set by `[InquiryColumn]`, `[InquiryKey]` or `[InquiryForeignKey]`.
  Extra columns are ignored. A missing or duplicate match throws `InvalidOperationException` before the
  first row. Build warning [`INQ120`](diagnostics.md#inq120) reports these reads at build time when the SQL
  text is constant, including an unaliased expression such as `COUNT(*)`, `COALESCE(...)`, `CASE ... END`,
  a scalar subquery or an `OUTPUT` expression, and a column whose name differs from the expected name. It
  cannot see SQL built at run time, `SELECT *` or stored procedures, so review those reads too. Code that caught `InvalidCastException`,
  `FormatException` or `OverflowException` around these reads must catch `InvalidOperationException` and
  read `InnerException`. Generated store queries are unchanged.
- **Attributed base-class properties are mapped.** An entity that derives from a class with Inquiry
  attributes gains those columns and relations, before its own columns. Add the columns to deployed tables,
  or remove the attribute from the base property to keep it unmapped.
- `InquiryForeignKeyAttribute.ReferencedTable` and `ReferencedColumn` are now `string?`. Code that reads them
  through reflection gets a nullable warning.

## 4. Check the runtime behavior changes

- **Paged results use one command.** A method that returns `InquiryPagedResult<T>` sends the page and the
  count as one command with two result sets. Interceptors see one command and telemetry records one
  `QUERY_MULTIPLE` operation. If a custom `IInquiry` implementation runs generated paged methods,
  implement or forward `QueryMultipleAsync`. Its default implementation throws `NotSupportedException`.
- **Transactions report the database error.** A failed rollback during cleanup no longer replaces the
  original error. Code that read the provider error from an `AggregateException` around `CommitAsync` or
  `RollbackAsync` must also catch the provider exception.
- **Grid read failures come first.** When a grid read fails and its disposal also fails, `DisposeAsync`
  throws `AggregateException` with the read failure first. A cancelled read can surface as an
  `AggregateException` whose first inner exception is `OperationCanceledException`.
- **N+1 scopes dispose in order.** Disposing an `InquiryNPlusOneScope` while a nested scope is active throws
  `InvalidOperationException`.
- **SQL Server bulk inserts follow `INSERT` rules.** They check constraints, keep `NULL` values and fire
  insert triggers. Rows that break a constraint now fail with error 547. Constraints that an earlier bulk
  insert left untrusted stay untrusted until you run `ALTER TABLE ... WITH CHECK CHECK CONSTRAINT ...`.
- **SQLite `IN` lists match stored values.** A `Compare.In` list over `Guid`, `DateTime`, `DateTimeOffset`,
  `TimeOnly`, `byte[]` or `float` now matches rows. A `byte[]` list needs SQLite 3.41 or later. A value
  that another writer stored in a different form, such as a lower-case GUID, no longer matches.

## 5. Register stores through each library's own method

This change comes in `1.0.0-rc.2`. The generated `AddInquiryGeneratedStores()` extension and its class,
`Inquiry.DependencyInjection.InquiryGeneratedServiceCollectionExtensions`, are now `internal`. Before, every
assembly that generates stores exposed them as public, so a library that generates stores and references
another such library failed to build with `CS0436` or `CS0121` when it called `AddInquiryGeneratedStores()`.

- A call to `AddInquiryGeneratedStores()` in the assembly that generates the stores is unchanged.
- A call to a referenced library's generated method now fails with `CS1061`. Add a public registration
  method with a unique name to that library, and call it instead. See
  [Registering stores from multiple libraries](library-registration.md).
- A library that generates stores and references another one no longer needs to call
  `Inquiry.Generated.InquiryGeneratedServiceRegistration` directly. Call `AddInquiryGeneratedStores()` from
  its own registration method.
- The fix needs every library that generates stores to be built with the new version. A library built with
  `1.0.0-rc.1`, such as a package published with it, still exposes its public class, and a library that
  references it still fails with `CS0121`. Rebuild that library with the new version.
