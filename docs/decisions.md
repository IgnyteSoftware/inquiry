# Ignyte .NET standards

This directory records the policy used by the .NET repository layer. Copy the files in `repos/dotnet/` into a repository root, then replace the project-specific values in `.github/workflows/ci.yml`.

## Project identity

Every buildable project, including tests and tools, uses the form:

```text
Ignyte.<Product>.<Component>
```

Archives and vendor code may use an exception, but the exception must be documented in the project README or an ADR.

The solution file is named `Ignyte.<Product>.slnx` and sits at the repository root, next to `global.json` and `Directory.Build.props`. Projects live under `src/` and `tests/`.

## SDK and language version

- `global.json` pins the SDK exactly. Patch updates require an intentional template update.
- `Directory.Build.props` pins the C# language version to the version supported by that SDK for C# projects. F# and Visual Basic use their SDK defaults.
- Visual Basic projects use explicit `Option Strict`, `Option Explicit`, `Option Infer`, and binary comparison settings.
- SDK changes are deliberate changes. Update `global.json`, `LangVersion`, `AnalysisLevel`, and analyzer package versions together.

## Build and analyzer policy

- Nullable reference types are enabled.
- Implicit usings are enabled.
- The SDK's built-in Roslyn analyzers are enabled at the pinned recommended analysis level.
- Code style runs during builds.
- `Directory.Build.targets` reasserts the required build properties after project files are evaluated.
- Compiler and code-analysis warnings are treated as errors.
- The free SonarAnalyzer.CSharp and SonarAnalyzer.VisualBasic packages are referenced centrally and marked private to the consuming project. They run in the ordinary build; no SonarScanner, SonarCloud, or SonarQube service is used.
- Test projects get the centrally pinned Coverlet collector. CI does not collect coverage; run `dotnet test --collect "XPlat Code Coverage"` locally when you need it.
- F# projects receive the common build policy but have no Sonar analyzer in this template. They require a documented analyzer exception or a separately approved F# analyzer.
- Do not add blanket `NoWarn`, `WarningsNotAsErrors`, or analyzer suppressions beyond the SDK's existing defaults. Suppress a rule only with a local explanation and an owner-approved exception.
- `Directory.Build.props` turns on `GenerateDocumentationFile` so the build enforces `IDE0005` (unused usings). It suppresses `CS1591` (missing XML comment) repository-wide. This is an approved central suppression. A project that ships a public API may opt back in to `CS1591` with exactly `<NoWarn>$(NoWarn.Replace('CS1591', ''))</NoWarn>` in its project file. This line removes a suppression, so it does not need an exception. An `.editorconfig` severity or `WarningsAsErrors` entry does not override the central `NoWarn`.
- Doc output also makes the compiler check existing XML comments. `CS1572`, `CS1573`, `CS1574`, `CS1587`, and related errors are not suppressed. Fix the comment, or change a stray `///` to `//`.
- Test and benchmark code does not report `CA1707`, so names such as `Method_Scenario_Expected` and `SelectAll_AdoNet` build. `Directory.Build.targets` suppresses it for projects with `IsTestProject` or `IsTestingPlatformApplication` and for projects whose name contains `.Benchmarks`, and `.editorconfig` turns it off for files under `tests/` and `benchmarks/`. Benchmark names appear in results and baselines, so renaming them breaks comparisons. This is an approved central suppression. Production projects keep `CA1707`.
- `CA1848` (use `LoggerMessage` delegates) and `CA1873` (avoid expensive logging arguments) are suggestions, not errors. Prefer `[LoggerMessage]` in hot paths; converting existing log calls is not required.
- Agent review flags project-level `NoWarn` (except the `CS1591` opt-in above), `WarningsNotAsErrors`, `NuGetAuditSuppress`, and package-reference suppression metadata. Rule-level `.editorconfig` exceptions and source suppressions require the same documented justification.

## Formatting and naming

- Files use UTF-8 without BOM, LF line endings, spaces, four-space indentation, trimmed trailing whitespace, and a final newline.
- Formatting is enforced through `IDE0055`.
- Braces are required on every control statement (`IDE0011`). `dotnet format` adds missing braces.
- Naming is enforced through `IDE1006`.
- Generated-file patterns are limited to known generated suffixes. Do not mark an entire source directory as generated.
- The root `.editorconfig` is authoritative. Nested files are exceptions only and should not use `root = true` unless they intentionally replace the repository baseline.

## Package management and restore

- Package versions belong in `Directory.Packages.props`.
- Project `PackageReference` items omit `Version`.
- `VersionOverride` is disabled by default.
- `nuget.config` clears inherited sources and maps all packages to an approved source.
- Package signatures are required. The template pins the SonarSource signing certificate and all three NuGet.org repository certificates. Packages keep the certificate from the year they were published, so the older ones stay. The 2024 certificate expires on 2027-05-18. Add the next fingerprint when NuGet announces the rotation, or restore rejects newly signed packages. Add the approved certificate and source mapping when an internal feed is introduced.
- Never commit feed credentials. Use a credential provider, environment-backed secret, or the CI platform's secret store.
- Restore creates `packages.lock.json`. Commit the lock file and use locked mode in CI.
- Dependabot does not always update every lock file. A project that gets a package through a project reference or a central transitive version can keep the old version, and locked restore then fails with `NU1004`. Fix the Dependabot branch with `dotnet restore --force-evaluate` and push the lock files.
- NuGet audit covers direct and transitive packages at low severity and above.
- The NuGet audit source is explicit and does not inherit machine- or user-level audit sources.

## Exceptions

An exception must state:

1. The affected project or path.
2. The rule being relaxed.
3. Why the default cannot be used.
4. The owner and a review date.

Exceptions should be as narrow as possible. Prefer a rule-specific diagnostic entry over a category-wide suppression.

## Repository-specific CI order

The template runs `dotnet format` before `dotnet build`. Here the provider source generators attach to consuming projects from the analyzer projects' build output, so a workspace loaded before the build sees no generated code and reports `using Inquiry.Generated;` and similar usings as unnecessary (`IDE0005`). The `build-and-unit` job therefore builds first and runs `Configuration=Release dotnet format ... --no-restore --verify-no-changes --severity error` afterwards, so the format workspace loads the same Release analyzer output. `IDE0005` stays an error. `eng/Inquiry.ReleaseTools/CiContractVerifier.cs` pins this order.

## Recorded exceptions

Each entry names the rule, the location, and the reason. Entries marked "owner to confirm" were kept from before the template adoption and need an owner and a review date.

| Rule | Location | Reason | Owner / review |
|---|---|---|---|
| All analyzer diagnostics (`generated_code = true`, `dotnet_analyzer_diagnostic.severity = none`) | `.editorconfig` section `[benchmarks/Inquiry.Benchmarks.DLG/*.cs]` | The project is third-party DLG generator output kept unchanged as a benchmark comparison target. Hand edits would make it stop matching what DLG emits. Sonar does not read `generated_code`, so the section also turns analyzer severities off. Compiler diagnostics still apply. | Owner decision |
| `CS8669`, `CS1570`, `CS1572`, `CS1573`, `CS1587` | `benchmarks/Inquiry.Benchmarks.DLG/Inquiry.Benchmarks.DLG.csproj` | Generated files need an explicit `#nullable` directive for `?` annotations (`CS8669`), and the DLG XML comments are malformed. Both come from the generator output above. | Owner decision |
| `CS8600`-`CS8625`, `CS0168`, `CS0219`, `CS0649` | `benchmarks/Inquiry.Benchmarks.DLG/Inquiry.Benchmarks.DLG.csproj` | Kept from before the template. Since the folder is marked generated, only `CS0219` still fires; the other IDs are now redundant. | Owner to confirm |
| `RS0026`, `RS0027` | `src/Directory.Build.props` (packable projects) | Kept from before the template. The 1.0 API surface uses optional parameters on overloads on purpose; changing it now would break consumers. | Owner to confirm |
| `RS1032`, `RS1036` | `src/Inquiry.Generators.Shared` and the six `src/Inquiry.*.Analyzer` project files | Kept from before the template. Analyzer-authoring rules on diagnostic message format and banned-API configuration for the bundled generators. | Owner to confirm |
| `INQ039` | `benchmarks/Inquiry.Benchmarks.Oracle/Inquiry.Benchmarks.Oracle.csproj`, `tests/Inquiry.Oracle.UnsupportedFixtures/Inquiry.Oracle.UnsupportedFixtures.csproj` | Kept from before the template. Both compile shapes Oracle cannot implement on purpose: the benchmark link-compiles shared stores with three unsupported upserts, and the fixture assembly exists so `OracleGeneratorTests` can assert every `INQ039`. | Owner to confirm |
| `CA1068` | `IInquiry.QueryListAsync` (2 overloads), `IInquiryRequestPipeline.QueryListAsync` (2 overloads) | Published API. `capacityHint` follows the optional `CancellationToken`; moving it would break callers and implementers. The private pipeline cores were reordered instead. | Public API exception |
| `CA1000` | `InquiryConverterDispatcher<TConverter, TModel, TProvider>.ToProvider` / `.FromProvider` | Published API called by generated code; the type arguments select the converter at compile time. | Public API exception |
| `CA1010` | `InquiryBulkRowReader<TEntity>` | Published API. It derives from `DbDataReader`; adding `IEnumerable<T>` would change the public surface. | Public API exception |
| `S2326` | `InquiryStore<TEntity>` | Published API. Generated and user stores derive from it; removing the type parameter breaks them. | Public API exception |
| `S3246` | `IInquiryEntityMaterializer<TEntity>` | Published API. Adding `out` variance changes the shipped public contract. | Public API exception |
| `CA2201`, `S112` | `InquiryBulkRowReader.GetOrdinal` and ordinal validation, `InquiryParameterBinder.FindByLogicalName` | `DbDataReader` and `DbParameterCollection` document `IndexOutOfRangeException` for unknown names and ordinals; tests pin it. | Behaviour exception |
| `S3928`, `CA2208` | `InquiryBulkInsertOptions.Validate`, `InquiryBatchCommand<TItem>.Validate` / `.GetChunkCommandText`, `InquiryGeneratedCommand<TArgs>.Validate` | Validation is deferred, so `ParamName` names the caller's constructor argument or option property. Callers observe that name. | Behaviour exception |
| `S4456` | `InquiryRequestPipeline` and `TransactedInquiryRequestPipeline`: `QueryAsync` (2 overloads each) and `QueryGeneratedCore` | Argument errors surface on first enumeration today; splitting the iterator would make them throw eagerly. | Behaviour exception |
| `S1854` | `TransactedInquiryRequestPipeline`: the same three iterator methods | False positive. `primaryException` is read in the `finally` block, which Sonar does not track through async iterators. | False positive |
| `S3877` | `InquiryAuditContext` and `InquiryFilterContext` scope `Dispose` | Disposing nested ambient scopes out of order would corrupt the ambient stack, so misuse throws on purpose. | Behaviour exception |
| `S2743` | `InquiryJsonArrayParameter.ElementMetadata<T>`, `InquiryTvpParameter.TvpRecordEnumerable<T>._valueWriter` | One cache per closed generic type is intended. | False positive |
| `S3218` | `InquiryCommandResources.ResourceSet.Dispose` | Implements `IDisposable`; the outer `Dispose(DbCommand)` is a different public operation. | False positive |
| `S3398` | `InquiryJsonArrayParameter`: `AppendBoxedValue`, `AppendString`, `AppendNullable`, `AppendNullableSByte`, `AppendNullableUInt16`, `AppendNullableUInt32`, `AppendNullableUInt64`, `AppendNullableFinite(float?)` | Kept outside the generic `ElementWriter<T>` so each helper is not compiled once per closed element type (NativeAOT size). | Design exception |
| `S3267` | `InquiryArrayParameter.MapNonNull` / `ConvertEnumElements`, `GridCommandInterception.Select` | Runs per command; the loops avoid LINQ iterator and delegate allocations. | Performance exception |
| `IDE0060` | `TransactedInquiryRequestPipeline.ReleaseSavepointAsync` / `RollbackToSavepointAsync` (lease overloads) | The lease argument shows the caller already holds the exclusive operation and selects the overload. | Design exception |

### Source suppressions in tests, benchmarks, samples, tools, and generators

Each row is a narrow `[SuppressMessage]` or a `#pragma warning disable`/`restore` pair around one statement. None of this code ships as a consumer-facing API; the generator and analyzer rows ship inside the provider packages but do not change generated output.

| Rule | Location | Reason | Scope |
|---|---|---|---|
| `BDN1302` | `benchmarks/Inquiry.Benchmarks.MariaDb/ShipperBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.MySql/ShipperBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.Oracle/ShipperBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.PostgreSql/BulkInsertBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.PostgreSql/InListBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.PostgreSql/PreparedStatementBenchmarks.cs:Rows` and 4 more | The single value keeps the parameter in the benchmark result names, so results stay comparable across runs. | Test, benchmark, sample, tool, or generator code |
| `CA1001` | `benchmarks/Inquiry.Benchmarks.Oracle/OracleCommandFinalizationBenchmarks.cs:OracleCommandFinalizationBenchmarks` | BenchmarkDotNet disposes the command in the [GlobalCleanup] method. | Test, benchmark, sample, tool, or generator code |
| `CA1001` | `benchmarks/Inquiry.Benchmarks/CommandResourceLookupBenchmarks.cs:CommandResourceLookupBenchmarks` | BenchmarkDotNet disposes the commands in the [GlobalCleanup] method; the no-op resource holds nothing. | Test, benchmark, sample, tool, or generator code |
| `CA1001` | `benchmarks/Inquiry.Benchmarks/ActiveTelemetryBenchmarks.cs:ActiveTelemetryBenchmarks`, `benchmarks/Inquiry.Benchmarks/GeneratedCommandPipelineBenchmarks.cs:GeneratedCommandPipelineBenchmarks`, `benchmarks/Inquiry.Benchmarks/ParameterBindingBenchmarks.cs:ParameterBindingBenchmarks`, `benchmarks/Inquiry.Benchmarks/ProductCrudBenchmarks.cs:ProductCrudBenchmarks` | BenchmarkDotNet disposes these fields in the [GlobalCleanup] method. | Test, benchmark, sample, tool, or generator code |
| `CA1051` | `benchmarks/Inquiry.Benchmarks.MariaDb/BatchMutationStrategyBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.MySql/BatchMutationStrategyBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.Oracle/BatchMutationStrategyBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.PostgreSql/BatchMutationStrategyBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.SqlServer/BatchMutationStrategyBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks/BatchMutationStrategyBenchmarks.cs:Rows` | SelectedBatchStrategyContracts reads Rows by reflection as a public field. | Test, benchmark, sample, tool, or generator code |
| `CA1711` | `tests/Inquiry.MariaDb.Tests/Fixtures/MariaDbCollection.cs:MariaDbCollection`, `tests/Inquiry.MySql.Tests/Fixtures/MySqlCollection.cs:MySqlCollection`, `tests/Inquiry.Oracle.Tests/Fixtures/OracleCollection.cs:OracleCollection`, `tests/Inquiry.PostgreSql.Tests/Fixtures/PostgreSqlCollection.cs:PostgreSqlCollection`, `tests/Inquiry.SqlServer.Tests/Dlg/DlgCollection.cs:DlgCollection`, `tests/Inquiry.SqlServer.Tests/Fixtures/SqlServerCollection.cs:SqlServerCollection` | xUnit collection definitions use the <Name>Collection convention; this type is not a collection. | Test, benchmark, sample, tool, or generator code |
| `CA1805` | `benchmarks/Inquiry.Benchmarks.SqlServer/GeneratedAdHocSequentialAccessBenchmarks.cs:_structMaterializer` | The stateless struct materializer is used as its default value; the explicit initializer shows that this is intended. | Test, benchmark, sample, tool, or generator code |
| `CA1822` | `benchmarks/Inquiry.Benchmarks.SqlServer/EagerLoadingBenchmarks.cs:Eager_Dlg`, `benchmarks/Inquiry.Benchmarks.SqlServer/ProductReadBenchmarks.cs:Count_Dlg`, `benchmarks/Inquiry.Benchmarks.SqlServer/ProductReadBenchmarks.cs:OffsetPage_Dlg`, `benchmarks/Inquiry.Benchmarks.SqlServer/ProductReadBenchmarks.cs:Search_Dlg`, `benchmarks/Inquiry.Benchmarks.SqlServer/ShipperBenchmarks.cs:Insert_Dlg`, `benchmarks/Inquiry.Benchmarks.SqlServer/ShipperBenchmarks.cs:SelectAll_Dlg` and 5 more | BenchmarkDotNet requires benchmark methods to be instance methods. | Test, benchmark, sample, tool, or generator code |
| `CA1822` | `benchmarks/Inquiry.Benchmarks.MariaDb/MariaDbBenchmarkDatabase.cs:BatchMutations`, `benchmarks/Inquiry.Benchmarks.MariaDb/MariaDbBenchmarkDatabase.cs:ConnectionString`, `benchmarks/Inquiry.Benchmarks.MariaDb/MariaDbBenchmarkDatabase.cs:DbContextFactory`, `benchmarks/Inquiry.Benchmarks.MariaDb/MariaDbBenchmarkDatabase.cs:Shippers`, `benchmarks/Inquiry.Benchmarks.MySql/MySqlBenchmarkDatabase.cs:BatchMutations`, `benchmarks/Inquiry.Benchmarks.MySql/MySqlBenchmarkDatabase.cs:ConnectionString` and 19 more | Benchmarks reach the process-wide database through the handle that CreateAsync returns, so this member stays on the instance. | Test, benchmark, sample, tool, or generator code |
| `CA1822` | `tests/Inquiry.Benchmarks.Contracts.Tests/PrecomputedTransportBenchmarkContractTests.cs:Generated_PrecomputedSqlFloor`, `tests/Inquiry.Benchmarks.Contracts.Tests/PrecomputedTransportBenchmarkContractTests.cs:Production_PrecomputedSqlFloor`, `tests/Inquiry.Benchmarks.Contracts.Tests/PrecomputedTransportBenchmarkContractTests.cs:Raw_PreSerializedJsonFloor`, `tests/Inquiry.Benchmarks.Contracts.Tests/PrecomputedTransportBenchmarkContractTests.cs:Raw_PrecomputedSql`, `tests/Inquiry.Benchmarks.Contracts.Tests/PrecomputedTransportBenchmarkContractTests.cs:Raw_PrecomputedSqlFloor`, `tests/Inquiry.Benchmarks.Contracts.Tests/PrecomputedTransportBenchmarkContractTests.cs:Selected_PrecomputedSqlFloor` | The contract reads public instance methods, so the fixture methods stay on the instance. | Test, benchmark, sample, tool, or generator code |
| `CA1859` | `benchmarks/Inquiry.Benchmarks.SqlServer/GeneratedAdHocSequentialAccessBenchmarks.cs:_classMaterializer` | The benchmark measures interface dispatch, so the field keeps the interface type. | Test, benchmark, sample, tool, or generator code |
| `CA2201` | `tests/Inquiry.SqlServer.Tests/InquiryTvpParameterTests.cs` | The fake matches ADO.NET providers, which throw IndexOutOfRangeException for a missing parameter. | Test, benchmark, sample, tool, or generator code |
| `CA2215` | `tests/Inquiry.Tests/BatchExecutionProbeTests.cs:DisposeAsync` | The test double counts async disposal separately. base.DisposeAsync would call Dispose and count a synchronous disposal too. | Test, benchmark, sample, tool, or generator code |
| `CA2215` | `tests/Inquiry.Sqlite.Tests/TransactionStateMachineTests.cs:DisposeAsync` | The test double disposes its inner object asynchronously and then simulates a dispose failure. base.DisposeAsync would also run the synchronous Dispose path. | Test, benchmark, sample, tool, or generator code |
| `CA2215` | `tests/Inquiry.Tests/BatchCancellationRaceTests.cs:DisposeAsync`, `tests/Inquiry.Tests/SequentialAccessReadBehaviorTests.cs:DisposeAsync`, `tests/Shared/BatchExecutionProbe.cs:DisposeAsync` | The test double disposes its inner object asynchronously. base.DisposeAsync would also run the synchronous Dispose path, which the test does not expect. | Test, benchmark, sample, tool, or generator code |
| `S1104` | `benchmarks/Inquiry.Benchmarks.MariaDb/BatchMutationStrategyBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.MySql/BatchMutationStrategyBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.Oracle/BatchMutationStrategyBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.PostgreSql/BatchMutationStrategyBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks.SqlServer/BatchMutationStrategyBenchmarks.cs:Rows`, `benchmarks/Inquiry.Benchmarks/BatchMutationStrategyBenchmarks.cs:Rows` | SelectedBatchStrategyContracts reads Rows by reflection as a public field. | Test, benchmark, sample, tool, or generator code |
| `S1144` | `tests/Inquiry.Tests/DataSeedingTests.cs:FirstSeeder`, `tests/Inquiry.Tests/DataSeedingTests.cs:ScopedSeeder`, `tests/Inquiry.Tests/DataSeedingTests.cs:SecondSeeder` | Dependency injection calls this constructor through reflection. | Test, benchmark, sample, tool, or generator code |
| `S1244` | `tests/Inquiry.Sqlite.Tests/SchemaDdlIntegrationTests.cs:GeneratedSchemaExecutesAndRoundTripsCrud` | 2.5 is exact in binary floating point, and SQLite REAL stores it without loss. The test checks the exact round trip. | Test, benchmark, sample, tool, or generator code |
| `S1244` | `benchmarks/Inquiry.Benchmarks.Contracts/Evidence/EvidenceContracts.cs:Validate` | Evidence must match the recorded BenchmarkDotNet values bit for bit; a tolerance would accept altered evidence. | Test, benchmark, sample, tool, or generator code |
| `S1244` | `benchmarks/Inquiry.Benchmarks.Contracts/Evidence/EvidenceContracts.cs:Validate` | The baseline medians must equal the medians recomputed from the same measurements; a tolerance would accept altered baselines. | Test, benchmark, sample, tool, or generator code |
| `S1244` | `tests/Inquiry.Tests/InquiryTelemetryTests.cs:BulkInsertRecordsDurationMetric` | The fake copier records exactly 1 ms and 2 ms. The test checks that these values reach the metric unchanged. | Test, benchmark, sample, tool, or generator code |
| `S1244` | `benchmarks/Inquiry.Benchmarks.Contracts/Evidence/EvidenceContracts.cs:MatchesBenchmarkDotNetField` | The parsed field must equal the value rounded the same way BenchmarkDotNet rounds it; a tolerance would accept altered evidence. | Test, benchmark, sample, tool, or generator code |
| `S1244` | `benchmarks/Inquiry.Benchmarks.Contracts/Evidence/EvidenceContracts.cs:ValidateComputedStatistics` | The stored statistics must equal the values recomputed from the same measurements; a tolerance would accept altered evidence. | Test, benchmark, sample, tool, or generator code |
| `S1313` | `samples/Inquiry.Sample/Services/DataSeeder.cs` | "91.24.45.40" is a French phone number from Northwind, not an IP address. | Test, benchmark, sample, tool, or generator code |
| `S1751` | `benchmarks/Inquiry.Benchmarks.SqlServer/GeneratedAdHocSequentialAccessBenchmarks.cs:GeneratedFirstRowThenDispose` | The benchmark measures reading one row and then disposing the stream, so the loop returns on its first pass. | Test, benchmark, sample, tool, or generator code |
| `S1751` | `tests/Inquiry.SqlServer.Tests/TvpResourceLifetimeIntegrationTests.cs` | The test checks that leaving the stream after the first row disposes the source. | Test, benchmark, sample, tool, or generator code |
| `S1751` | `tests/Inquiry.MySql.Tests/GeneratedAdHocSequentialAccessIntegrationTests.cs`, `tests/Inquiry.PostgreSql.Tests/GeneratedAdHocSequentialAccessIntegrationTests.cs`, `tests/Inquiry.SqlServer.Tests/GeneratedAdHocSequentialAccessIntegrationTests.cs` | The test checks that leaving the stream after the first row is safe. | Test, benchmark, sample, tool, or generator code |
| `S2068` | `benchmarks/Inquiry.Benchmarks.Oracle/OracleCommandFinalizationBenchmarks.cs:_factory` | The benchmark never opens this connection; the password is a placeholder, not a credential. | Test, benchmark, sample, tool, or generator code |
| `S2077` | `benchmarks/Inquiry.Benchmarks.SqlServer/SqlServerCollectionBenchmarkDatabase.cs:ValidateFixtureAsync` | Table and column names come from the fixture schema in code; no part comes from input. | Test, benchmark, sample, tool, or generator code |
| `S2077` | `benchmarks/Inquiry.Benchmarks.PostgreSql/PostgreSqlBenchmarkDatabase.cs:CreateAsync`, `benchmarks/Inquiry.Benchmarks.SqlServer/SqlServerBenchmarkDatabase.cs:CreateAsync`, `benchmarks/Inquiry.Benchmarks/BenchmarkDatabase.cs:CreateAsync` | The DDL is built only from constants in code; no part comes from input. | Test, benchmark, sample, tool, or generator code |
| `S2077` | `benchmarks/Inquiry.Benchmarks/InCardinalityBenchmarks.cs:InList_AdoNet`, `benchmarks/Inquiry.Benchmarks/PredicateBenchmarks.cs:InList_AdoNet` | The IN list holds generated parameter names only; the values are bound as parameters. | Test, benchmark, sample, tool, or generator code |
| `S2077` | `benchmarks/Inquiry.Benchmarks.SqlServer/EagerLoadingBenchmarks.cs:LoadManyToManyGraphAdoAsync` | The SQL joins constant strings chosen in code; no part comes from input. | Test, benchmark, sample, tool, or generator code |
| `S2077` | `benchmarks/Inquiry.Benchmarks.SqlServer/SqlServerCollectionBenchmarkDatabase.cs:CreateAsync` | The database name is a fixed prefix plus a new GUID; no part comes from input. | Test, benchmark, sample, tool, or generator code |
| `S2077` | `benchmarks/Inquiry.Benchmarks/ActiveTelemetryBenchmarks.cs:Setup` | The seed SQL uses fixed integer IDs from code; no part comes from input. | Test, benchmark, sample, tool, or generator code |
| `S2077` | `benchmarks/Inquiry.Benchmarks/QueryPlanCapture.cs:RunAsync` | The statements come from a constant table in code; no part comes from input. | Test, benchmark, sample, tool, or generator code |
| `S2094` | `tests/Inquiry.Tests/DataSeedingTests.cs:ScopeProbe` | Dependency injection resolves this marker type. It needs no members. | Test, benchmark, sample, tool, or generator code |
| `S2094` | `tests/Inquiry.Oracle.UnsupportedFixtures/UnsupportedUpsertFixtures.cs:OracleUnsupportedFixtureMarker` | Empty marker type; the Oracle test harness uses typeof(...).Assembly to register the fixture stores. | Test, benchmark, sample, tool, or generator code |
| `S2094` | `tests/Inquiry.SqlServer.Tests/TvpResourceLifetimePipelineTests.cs:ProbeEntity` | Placeholder entity type for the generic pipeline calls; the tests never materialize a row. | Test, benchmark, sample, tool, or generator code |
| `S2094` | `tests/Inquiry.IntegrationTesting/IsExternalInit.cs:IsExternalInit` | The compiler needs this empty type to support init-only setters on netstandard2.0. | Test, benchmark, sample, tool, or generator code |
| `S2094` | `tests/Inquiry.Tests/AddInquiryAssemblyOverloadTests.cs:SentinelMarker` | The test counts service descriptors of this marker type. It needs no members. | Test, benchmark, sample, tool, or generator code |
| `S2325` | `benchmarks/Inquiry.Benchmarks.MariaDb/MariaDbBenchmarkDatabase.cs:BatchMutations`, `benchmarks/Inquiry.Benchmarks.MariaDb/MariaDbBenchmarkDatabase.cs:ConnectionString`, `benchmarks/Inquiry.Benchmarks.MariaDb/MariaDbBenchmarkDatabase.cs:DbContextFactory`, `benchmarks/Inquiry.Benchmarks.MariaDb/MariaDbBenchmarkDatabase.cs:Shippers`, `benchmarks/Inquiry.Benchmarks.MySql/MySqlBenchmarkDatabase.cs:BatchMutations`, `benchmarks/Inquiry.Benchmarks.MySql/MySqlBenchmarkDatabase.cs:ConnectionString` and 19 more | Benchmarks reach the process-wide database through the handle that CreateAsync returns, so this member stays on the instance. | Test, benchmark, sample, tool, or generator code |
| `S3265` | `benchmarks/Inquiry.Benchmarks.PostgreSql/PostgreSqlBatchMutationStrategyRunner.cs` | Npgsql combines Array with an element type by design, although the enum has no [Flags] attribute. | Test, benchmark, sample, tool, or generator code |
| `S3265` | `tests/Inquiry.PostgreSql.Tests/BatchChunkingIntegrationTests.cs` | Npgsql combines NpgsqlDbType.Array with an element type by design. | Test, benchmark, sample, tool, or generator code |
| `S3871` | `tests/Inquiry.SqlServer.Tests/InquiryTvpParameterTests.cs:ParameterAddException`, `tests/Inquiry.SqlServer.Tests/InquiryTvpParameterTests.cs:ProbeException`, `tests/Inquiry.SqlServer.Tests/TvpResourceLifetimePipelineTests.cs:BinderProbeException`, `tests/Inquiry.SqlServer.Tests/TvpResourceLifetimePipelineTests.cs:CleanupProbeException`, `tests/Inquiry.SqlServer.Tests/TvpResourceLifetimePipelineTests.cs:PrepareProbeException` | Test-only probe exception; it never leaves this test class. | Test, benchmark, sample, tool, or generator code |
| `S3871` | `tests/Inquiry.Tests/RetryingConnectionOpenerTests.cs:TerminalException`, `tests/Inquiry.Tests/RetryingConnectionOpenerTests.cs:TransientException` | The test throws and catches this exception inside the same test class. | Test, benchmark, sample, tool, or generator code |
| `S3877` | `tests/Inquiry.Benchmarks.SqlServer.Tests/CollectionTransportBenchmarkIntegrationTests.cs:Dispose` | The probe throws from Dispose on purpose to test cleanup failure handling. | Test, benchmark, sample, tool, or generator code |
| `S3877` | `tests/Inquiry.SqlServer.Tests/InquiryTvpParameterTests.cs` | The probe throws from Dispose to test that cleanup failures are aggregated. | Test, benchmark, sample, tool, or generator code |
| `S3877` | `tests/Inquiry.SqlServer.Tests/TvpResourceLifetimePipelineTests.cs` | The probe throws from Dispose to test that the pipeline reports cleanup failures. | Test, benchmark, sample, tool, or generator code |
| `S3877` | `tests/Inquiry.Tests/BatchRuntimeEvidenceTests.cs:Dispose`, `tests/Inquiry.Tests/ExecuteBatchTests.cs:Dispose`, `tests/Inquiry.Tests/GeneratedCommandRuntimeTests.cs:Dispose`, `tests/Inquiry.Tests/InquiryTestingTests.cs:Dispose` | The test double throws from Dispose on purpose to test how disposal failures are handled. | Test, benchmark, sample, tool, or generator code |
| `S3928` | `benchmarks/Inquiry.Benchmarks/CrossDialectReadBenchmarks.cs` | Dialect is the [Params] property that acts as this setup's input. | Test, benchmark, sample, tool, or generator code |
| `S3928` | `tests/Inquiry.ReleaseTools.Tests/PackageAdversarialTests.cs:Analyzer_symbol_bypass_attempts_are_rejected` | The lambda rejects the enclosing method's mutation parameter, which is the out-of-range value. | Test, benchmark, sample, tool, or generator code |
| `S4036` | `eng/Inquiry.ReleaseTools/PackageVerifier.cs:static` | The release tool runs the dotnet host that the build selected on PATH, which is the pinned SDK. | Test, benchmark, sample, tool, or generator code |
| `S4036` | `benchmarks/Inquiry.Benchmarks.Generators/ConsumerBuildEvidence.cs:RunDotnetAsync` | This developer tool runs the dotnet host found on PATH on purpose, so the evidence records the SDK that the developer uses. | Test, benchmark, sample, tool, or generator code |
| `S4144` | `benchmarks/Inquiry.Benchmarks.SqlServer/GeneratedAdHocSequentialAccessBenchmarks.cs:GeneratedFullStream` | Each benchmark category needs its own full-stream baseline method; the bodies match on purpose. | Test, benchmark, sample, tool, or generator code |
| `S5034` | `tests/Inquiry.Sqlite.Tests/TransactionStateMachineTests.cs:ConcurrentRootDisposeCleansProviderResourcesExactlyOnce`, `tests/Inquiry.Sqlite.Tests/TransactionStateMachineTests.cs:ConcurrentSavepointDisposeCallsShareCleanup` | Each DisposeAsync call returns its own ValueTask, consumed once through AsTask. The test races the calls on purpose. | Test, benchmark, sample, tool, or generator code |
| `S6444` | `benchmarks/Inquiry.Benchmarks.RegressionGate/Program.cs` | The pattern comes from an escaped glob, so it has only wildcards and cannot backtrack catastrophically. | Test, benchmark, sample, tool, or generator code |
| `S6966` | `benchmarks/Inquiry.Benchmarks.RegressionGate/Program.cs` | The entry point is synchronous, so there is no async context to await in. | Test, benchmark, sample, tool, or generator code |
| `S6966` | `tests/Inquiry.Tests/BatchExecutionProbeTests.cs:MixedDisposalDelegatesToInnerConnectionExactlyOnce`, `tests/Inquiry.Tests/BatchExecutionProbeTests.cs:MixedFactoryDisposalDelegatesToInnerFactoryExactlyOnce` | The test calls the synchronous Dispose after DisposeAsync on purpose. | Test, benchmark, sample, tool, or generator code |
