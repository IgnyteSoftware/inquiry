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

## Recorded exceptions

Each entry names the rule, the location, and the reason. Entries marked "owner to confirm" were kept from before the template adoption and need an owner and a review date.

| Rule | Location | Reason | Owner / review |
|---|---|---|---|
| All analyzer diagnostics (`generated_code = true`, `dotnet_analyzer_diagnostic.severity = none`) | `.editorconfig` section `[benchmarks/Inquiry.Benchmarks.DLG/*.cs]` | The project is third-party DLG generator output kept unchanged as a benchmark comparison target. Hand edits would make it stop matching what DLG emits. Sonar does not read `generated_code`, so the section also turns analyzer severities off. Compiler diagnostics still apply. | Owner decision |
| `CS8669`, `CS1570`, `CS1572`, `CS1573`, `CS1587` | `benchmarks/Inquiry.Benchmarks.DLG/Inquiry.Benchmarks.DLG.csproj` | Generated files need an explicit `#nullable` directive for `?` annotations (`CS8669`), and the DLG XML comments are malformed. Both come from the generator output above. | Owner decision |
| `CS8600`-`CS8625`, `CS0168`, `CS0219`, `CS0649` | `benchmarks/Inquiry.Benchmarks.DLG/Inquiry.Benchmarks.DLG.csproj` | Kept from before the template. Since the folder is marked generated, only `CS0219` still fires; the other IDs are now redundant. | Owner to confirm |
