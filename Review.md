# Pre-Publication Code Review — Inquiry NuGet Package

---

## Security Issues

### 1. Index-out-of-bounds crash on empty parameter name
[`InquiryParameterBinder.cs:46`](src/Inquiry/Parameters/InquiryParameterBinder.cs)

```csharp
return name[0] is '@' or ':' or '$' or '?'
```

`name[0]` is accessed with no guard. An empty string throws `IndexOutOfRangeException`; a null reference throws `NullReferenceException`. This is a public-facing code path — callers using `InquiryParameter` directly can trigger it.

```csharp
// Fix:
if (string.IsNullOrEmpty(name))
    throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
return name[0] is '@' or ':' or '$' or '?'
    ? name : "@" + name;
```

---

### 2. Assembly scanning is unbounded and uses `nonPublic: true`
[`InquiryServiceCollectionExtensions.cs:30`](src/Inquiry/DependencyInjection/InquiryServiceCollectionExtensions.cs)

```csharp
foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
...
var registration = (IInquiryServiceRegistration?)Activator.CreateInstance(registrationType, nonPublic: true);
```

Two problems:
- Every loaded assembly is scanned, not just assemblies the user explicitly opted in. Any assembly in the process that happens to implement `IInquiryServiceRegistration` will have code executed.
- `nonPublic: true` bypasses access modifiers. The generated registration type is `internal sealed` with no public constructor, so this is necessary for the current design — but it means any `internal` implementation anywhere in the process is also reachable.

The right fix before publishing is to require the caller to pass their assembly (or use a `[assembly: InquiryGeneratedServicesAssembly]` marker) rather than scanning everything.

---

### 3. Interceptors expose raw `DbCommand` and parameter values
[`InquiryRequestPipeline.cs:128`](src/Inquiry/Pipeline/InquiryRequestPipeline.cs)

`InquiryCommandContext` hands the live `DbCommand` to every registered interceptor. This is by design, but it's undocumented that interceptors therefore have access to unredacted parameter values (PII, tokens, etc.) and can mutate the command text. The public docs should call this out explicitly before anyone ships to production.

---

## Correctness Bugs

### 4. `TrimStart` is used incorrectly — type validation works by accident
[`InquiryGenerator.cs:569`](src/Inquiry.Generators/InquiryGenerator.cs)

```csharp
named.ConstructedFrom.ToDisplayString(...).TrimStart("global::".ToCharArray()) == metadataName
```

`TrimStart(char[])` strips *any character in the array from the front*, not the literal prefix `"global::"`. The char array is `['g','l','o','b','a',':']`. For `"global::System.Threading.Tasks.Task<TResult>"` this accidentally works because `S` isn't in the set, but it is fragile. Any type whose namespace starts with those letters (e.g., a hypothetical `global::ballistic.Foo`) would have leading characters silently stripped.

```csharp
// Fix:
var display = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
var normalized = display.StartsWith("global::", StringComparison.Ordinal) ? display[8..] : display;
normalized == metadataName
```

---

### 5. `INSERT` always includes the primary key column
[`InquirySqlStatementBuilder.cs:30-31`](src/Inquiry/Sql/InquirySqlStatementBuilder.cs)

```csharp
var selectColumns = string.Join(", ", columns.Select(c => _dialect.QuoteIdentifier(c.ColumnName)));
var insertColumns = string.Join(", ", columns.Select(c => _dialect.QuoteIdentifier(c.ColumnName)));
```

`selectColumns` and `insertColumns` are **identical** — same LINQ expression. The insert column list should exclude the key when it is auto-generated (IDENTITY / AUTOINCREMENT). As written, any entity with a database-generated primary key will fail at runtime. There is no `[DatabaseGenerated]` attribute or any mechanism to opt out.

This is the most impactful correctness gap for real users. Before shipping, either:
- Add a `IsGenerated` flag to `InquirySqlColumn` and a corresponding attribute, or
- At minimum, document prominently that auto-increment keys are not supported.

---

### 6. Wrong diagnostic reported for properties without a public setter
[`InquiryGenerator.cs:128-136`](src/Inquiry.Generators/InquiryGenerator.cs)

```csharp
if (property.SetMethod is null || property.SetMethod.DeclaredAccessibility == Accessibility.Private)
{
    context.ReportDiagnostic(Diagnostic.Create(
        InquiryDiagnosticDescriptors.UnsupportedPropertyType, ...));
```

`UnsupportedPropertyType` (INQ003) is also the diagnostic for an unrecognized CLR type. When the actual problem is a missing or private setter, the error message misleads the developer. These should be separate diagnostics with distinct IDs and messages.

---

### 7. `GetOperation` does not check attribute namespace
[`InquiryGenerator.cs:519`](src/Inquiry.Generators/InquiryGenerator.cs)

```csharp
var name = candidate.AttributeClass?.Name;
switch (name)
{
    case "InquirySelectAttribute": ...
```

The helper `GetAttribute()` at line 587 correctly validates both the short name *and* the `ContainingNamespace`. `GetOperation` only checks the short name. A user-defined `InquirySelectAttribute` in any namespace would silently trigger code generation against the wrong method.

---

### 8. Interceptors are not notified when enumeration is abandoned early
[`PipelineAsyncEnumerator.cs:76-95`](src/Inquiry/Pipeline/PipelineAsyncEnumerator.cs)

`DisposeAsync` cleans up ADO.NET resources but never calls `NotifyExecutedAsync` or `NotifyFailedAsync`. When a consumer breaks out of `await foreach` early, interceptors used for tracing, metrics, or span completion receive no signal that the operation ended. Add a call to `NotifyFailedAsync` (with a suitable exception or a dedicated cancelled context) inside `DisposeAsync` when `_initialized && !_completed`.

---

## API / Design Issues

### 9. `ISourceGenerator` is deprecated — use `IIncrementalGenerator`
[`InquiryGenerator.cs:14`](src/Inquiry.Generators/InquiryGenerator.cs)

```csharp
public sealed class InquiryGenerator : ISourceGenerator
```

The V1 `ISourceGenerator` API re-runs the generator on every keystroke in the IDE regardless of whether anything relevant changed. `IIncrementalGenerator` was introduced specifically to fix this; it is the current recommendation for all new generators. Shipping a NuGet generator package built on the V1 API will noticeably degrade IDE performance for every consumer project. This should be addressed before going live.

---

### 10. Target framework `net6.0` is end-of-life
[`Inquiry.csproj:3`](src/Inquiry/Inquiry.csproj)

```xml
<TargetFramework>net6.0</TargetFramework>
```

`net6.0` reached end of life in November 2024 and no longer receives security patches. A brand-new NuGet package targeting only an EOL framework will immediately trigger warnings in consuming projects. Target `net8.0` (current LTS) or multi-target `net6.0;net8.0` if you need to support older runtimes.

---

### 11. `AddInquirySqlLite` — public API typo, no `[Obsolete]`
[`SqliteInquiryServiceCollectionExtensions.cs:30`](src/Inquiry.Sqlite/SqliteInquiryServiceCollectionExtensions.cs)

```csharp
public static IServiceCollection AddInquirySqlLite(...)
```

The correct spelling is `AddInquirySqlite`. This alternate casing is either a mistake or an intentional alias. If intentional, it must be marked `[Obsolete]`. If accidental, remove it before publishing — adding `[Obsolete]` after publishing becomes a permanent API commitment.

---

### 12. `InquirySqlStatementBuilder.Build()` — unhelpful exception when `columns` has no key
[`InquirySqlStatementBuilder.cs:28`](src/Inquiry/Sql/InquirySqlStatementBuilder.cs)

```csharp
var key = columns.Single(c => c.IsKey);
```

If `columns` has no key, this throws `InvalidOperationException: Sequence contains no elements` with no context. If it has more than one key, the message is `Sequence contains more than one element`. Both messages are opaque. This path is reachable at runtime by anyone calling the public `Build()` method directly without the generator.

---

### 13. No validation on `CommandTimeout` — negative values crash at ADO.NET layer
[`InquiryCommandDefinition.cs:22`](src/Inquiry/Commands/InquiryCommandDefinition.cs)

`commandTimeout` is forwarded to `DbCommand.CommandTimeout` without any range check. A negative value throws at the provider layer with a provider-specific message. Fail fast with a clear message at construction time instead.

---

## Summary of Priority

| # | Severity | Where |
|---|----------|-------|
| 5 | **High** — INSERT always breaks identity/autoincrement PKs | `InquirySqlStatementBuilder.cs` |
| 9 | **High** — Deprecated generator API hurts IDE performance for all consumers | `InquiryGenerator.cs` |
| 4 | **High** — `TrimStart` bug in return-type validation | `InquiryGenerator.cs` |
| 1 | **Medium** — Empty parameter name crashes | `InquiryParameterBinder.cs` |
| 2 | **Medium** — Unbounded assembly scan with `nonPublic: true` | `InquiryServiceCollectionExtensions.cs` |
| 7 | **Medium** — `GetOperation` namespace not checked | `InquiryGenerator.cs` |
| 6 | **Medium** — Wrong diagnostic for missing setter | `InquiryGenerator.cs` |
| 8 | **Medium** — Interceptors not notified on early cancel | `PipelineAsyncEnumerator.cs` |
| 10 | **Medium** — EOL target framework | `Inquiry.csproj` |
| 11 | **Low** — Typo method in public API | `SqliteInquiryServiceCollectionExtensions.cs` |
| 3, 12, 13 | **Low** | Various |

Items 5, 9, and 4 are the ones I would not let past a PR review before publishing. The others are important but some could be addressed in a fast-follow patch release.
