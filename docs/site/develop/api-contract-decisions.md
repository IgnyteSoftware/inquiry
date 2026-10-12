# 1.0 API contract decisions

This page records the 1.0 decisions for generated stores, predicate binding, result mapping,
stored procedures, optional filters, and user-selected sorting. It covers issues
[#422](https://github.com/IgnyteSoftware/inquiry/issues/422),
[#419](https://github.com/IgnyteSoftware/inquiry/issues/419),
[#420](https://github.com/IgnyteSoftware/inquiry/issues/420),
[#423](https://github.com/IgnyteSoftware/inquiry/issues/423),
[#425](https://github.com/IgnyteSoftware/inquiry/issues/425), and
[#426](https://github.com/IgnyteSoftware/inquiry/issues/426).
The grid reader callback rules from
[#272](https://github.com/IgnyteSoftware/inquiry/issues/272) are user documentation and live in
[Runtime interface guarantees](../articles/runtime-contract.md#grid-reader-callbacks).

Each section states the current behavior with source references, the options considered, the
decision, what it fixes in the 1.0 public contract, and the implementation work it implies.
Source references are to `main` at commit `2d4fc2e`. Code marked
**proposed** is a design sketch. It is not supported syntax, and the final names may change.

## Summary

| Issue | Decision | Changes the 1.0 contract? |
|---|---|---|
| #422 Stores | Keep `InquiryStore<TEntity>` as the only generated store form for 1.0. Handwritten classes that inject `IInquiry` are the supported form for query-only and procedure-only work. A non-generic `InquiryStore` base is the one accepted later addition. | No. The later base type is additive. |
| #419 Predicate binding | Keep positional binding as a permanent supported form. Add an optional named parameter reference on `InquiryWhere`. Do not infer bindings from names. | No. The named form is additive. |
| #420 Ad-hoc mapping | Map handwritten-SQL results by column name, resolved once per result set. Keep fixed ordinals for generator-owned SQL. Defer constructor binding. | Yes. This changes runtime behavior and must land before the freeze. |
| #423 Procedures | Correct the guide now. Support a buffered `Task<IReadOnlyList<T>>` result and read DTOs. Defer combined rows-plus-outputs and parameter-name overrides until a consumer needs them. | No breaking change. The additions are additive. |
| #425 Optional filters | Keep `Optional = true` unchanged. Publish the two-declaration pattern for all, unassigned, and one value. No presence type for 1.0. | No. |
| #426 Sorting | Keep compile-time `OrderBy`. Publish the declared-methods-plus-enum pattern. A bounded generated sort option stays a possible later addition. | No. |

## #422 Generated stores for entity and query-only workloads

### Current behavior

- `InquiryStore<TEntity>` is the only store base type. It is an abstract class with a protected
  constructor that takes `IInquiry` and a protected `Inquiry` property
  (`src/Inquiry/Stores/InquiryStore.cs:6-20`).
- The generator finds stores by their base type. A syntax filter matches any base type named
  `InquiryStore` (`src/Inquiry.Generators.Shared/InquiryGeneratorBase.cs:126-151`). The semantic
  check then requires the generic `Inquiry.Stores.InquiryStore<T>`
  (`src/Inquiry.Generators.Shared/Infrastructure/GeneratorHelpers.cs:115-131`). Any other class is
  not a store (`src/Inquiry.Generators.Shared/StoreProcessor.cs:31-36`).
- The store entity must be mapped, or the store reports `INQ008` and its methods become throwing
  stubs (`StoreProcessor.cs:1344-1352`).
- Each store gets a generated `public Store(IInquiry inquiry) : base(inquiry)` constructor
  (`StoreProcessor.cs:2195-2198`).
- Generated registration adds each store as scoped. A store with `[InquiryGenerateInterface]` also
  gets a scoped `I{Store}` registration that forwards to the same instance
  (`src/Inquiry.Generators.Shared/RegistrationEmitter.cs:51-65`).
- Procedure rows must be the store entity, or mapped entities in a tuple
  (`StoreProcessor.cs:1308-1326`, `StoreProcessor.cs:2249-2260`). A procedure-only class therefore
  needs some mapped entity today, such as `ReportStore : InquiryStore<Order>` in the procedure guide.
- Any class can inject the scoped `IInquiry` and run handwritten SQL into `[InquiryAdHoc]` DTOs.
  Every `DefaultInquiry` call uses the active ambient transaction pipeline
  (`src/Inquiry/DefaultInquiry.cs:84-106`). Such a class joins `ExecuteInTransactionAsync` like a
  generated store does.

### Options considered

1. **Keep generic-only stores.** Document handwritten `IInquiry` classes for query-only and
   procedure-only work. No API change. Handwritten procedure calls lose generated OUTPUT, INOUT,
   TVP, and Oracle cursor handling.
2. **Add a non-generic `InquiryStore` base** for generated methods that do not belong to one
   entity. It keeps the same discovery rule, generated constructor, DI lifetime, and transaction
   path as today's stores.
3. **Add an attributed partial class** (for example `[InquiryQueries] partial class SalesQueries`)
   with no base type. This adds a second discovery mechanism. The generator would also need to
   emit the `IInquiry` field and property that the base class supplies today.
4. **Generate implementations from user-declared interfaces.** This is a new declaration model and
   needs new discovery and registration rules. `[InquiryGenerateInterface]` already gives callers an
   interface without a rewrite.
5. **Replace stores with runtime fluent or LINQ queries.** This drops the compile-time SQL contract
   in the [roadmap](roadmap.md). No concrete workload requires it.

### Decision

Choose option 1 for 1.0. Accept option 2 as the only additional store form, and defer it.

- `InquiryStore<TEntity>` stays the generated form for table operations. Its shape stays as it is.
- For 1.0, write query-only and procedure-only code as a handwritten class that injects
  `IInquiry`. Use `[InquiryAdHoc]` result types. Do not invent `[InquiryTable]` or `[InquiryView]`
  mappings to make a store compile.
- The later non-generic `InquiryStore` should be a separate abstract class, not a new base of
  `InquiryStore<TEntity>`. That keeps the existing type unchanged. Its methods return read DTOs, so
  it depends on the result-mapping decision in #420 and the procedure result work in #423.
- Reject option 3. One base-type discovery rule is simpler than two.
- Reject options 4 and 5 for 1.0. Keep `[InquiryGenerateInterface]` optional.
- Applications name and group methods by use case. Inquiry does not require one store per table,
  generic repositories, or pass-through service wrappers. Table-wide deletion stays explicit.

The current supported shape for the use cases in #422:

```csharp
using System.Data;
using Inquiry;
using Inquiry.Commands;
using Inquiry.Entities;
using Inquiry.Parameters;
using Inquiry.Stores;

[InquiryTable("Products")]
public sealed class Product
{
    [InquiryKey] public int ProductID { get; set; }
    [InquiryColumn] public string ProductName { get; set; } = "";
    [InquiryColumn] public int? CategoryID { get; set; }
    [InquiryColumn] public decimal UnitPrice { get; set; }
}

// Entity operations: a generated store. The optional interface is registered as a scoped
// forward to the same store instance.
[InquiryGenerateInterface]
public partial class ProductStore : InquiryStore<Product>
{
    [InquirySelectOneByKey]
    public partial Task<Product?> FindAsync(int productId, CancellationToken ct = default);

    [InquiryUpdate]
    public partial Task<bool> SaveAsync(Product product, CancellationToken ct = default);
}

[InquiryAdHoc]
public sealed class CategorySales
{
    public string CategoryName { get; init; } = "";
    public decimal Total { get; init; }
}

// Cross-table report and procedure call: a handwritten class over the same scoped IInquiry.
public sealed class SalesReports(IInquiry inquiry)
{
    public Task<IReadOnlyList<CategorySales>> ByCategoryAsync(
        DateTime since, CancellationToken ct = default)
        => inquiry.QueryListAsync<CategorySales>($"""
            SELECT c.CategoryName, SUM(od.UnitPrice * od.Quantity)
            FROM Orders o
            JOIN OrderDetails od ON od.OrderID = o.OrderID
            JOIN Products p ON p.ProductID = od.ProductID
            JOIN Categories c ON c.CategoryID = p.CategoryID
            WHERE o.OrderDate >= {since}
            GROUP BY c.CategoryName
            """, ct);

    public Task<IReadOnlyList<CategorySales>> FromProcedureAsync(
        DateTime since, CancellationToken ct = default)
        => inquiry.QueryListAsync<CategorySales>(
            new InquiryCommand(
                "reporting.GetSales",
                [new InquiryParameter("@since", since)],
                CommandType.StoredProcedure),
            ct);
}
```

Registration and a transactional call:

```csharp
services.AddInquiry();
services.AddInquirySqlServer(connectionString);
services.AddInquiryGeneratedStores(); // Stores, IProductStore, and ad-hoc materializers.
services.AddScoped<SalesReports>();

// Inside one DI scope. inquiry, products (IProductStore), and reports come from that scope.
await inquiry.ExecuteInTransactionAsync(async (tx, token) =>
{
    var product = await products.FindAsync(productId, token)
        ?? throw new KeyNotFoundException();
    product.UnitPrice = newPrice;
    if (!await products.SaveAsync(product, token))
        throw new InvalidOperationException("The product changed or was removed.");
    await reports.ByCategoryAsync(since, token); // Same transaction.
}, cancellationToken: ct);
```

In a data library, call `AddInquiryGeneratedStores()` from a library-owned wrapper, as described in
[Registering data libraries](../articles/library-registration.md). `IProductStore` is optional.
An application can also define its own interface over a service. Neither is required.

The SQL report has known limits. It does not inherit global filters or soft-delete rules. Its
columns map by ordinal until #420 is implemented. The procedure call above runs a single result
set into a DTO. It cannot read OUTPUT parameters or use the Oracle cursor wrapper.

### 1.0 contract consequences

- `InquiryStore<TEntity>`, its protected constructor, its protected `Inquiry` property, and
  base-type discovery are frozen as they are.
- Scoped store registration and scoped interface forwarding are part of the contract.
- Adding a separate non-generic `InquiryStore` later adds a public type and widens discovery. It
  does not change existing declarations.

### Follow-up

- Open an issue for a non-generic `InquiryStore` with generated procedure and read-DTO methods.
  Sequence it after the #420 materializer work and the #423 DTO work. Cover discovery, the
  generated constructor, registration, and `[InquiryGenerateInterface]` on the new form.

### Confidence

Medium-high on the direction. Whether to build the non-generic base before 1.0 is a maintainer call.
This page recommends deferring it because it is additive.

## #419 Binding predicates to method parameters

### Current behavior

- `InquiryWhere` criteria bind to method parameters by position. The XML documentation says so
  (`src/Inquiry/Stores/InquiryWhereAttribute.cs:5-6`).
- The generator reads criteria in declaration order and expands specification attributes in place
  (`StoreProcessor.cs:877-935`).
- `TryResolvePredicates` consumes parameters left to right. Scalar operators take one, `Between`
  takes two, `In` takes one collection, and `IsNull` takes none (`StoreProcessor.cs:3368-3577`).
  Offset and keyset paging parameters are trailing parameters and are not predicate inputs.
- Each parameter type must equal the column type, including nullability. Optional criteria ignore
  nullability (`StoreProcessor.cs:3579-3583`). A count mismatch reports `INQ019`
  (`StoreProcessor.cs:3563-3569`).
- Two parameters of the same type can swap silently. The sample `ProductSearch` specification takes
  `decimal? minimum, decimal? maximum` (`samples/Inquiry.Sample.Data/WorkspaceStores.cs:6-21`).
  Swapping them compiles and reverses the price range.
- `InquirySet` expressions already bind by name with `@parameter`. The SET parameters must form a
  contiguous prefix, and the WHERE parameters that follow stay positional
  (`StoreProcessor.cs:3268-3318`, `StoreProcessor.cs:3347-3357`).

### Options considered

1. **Accept positional binding** and keep documenting the risk. The
   [query composition](../articles/features/query-composition.md) guide already does this.
2. **Put `InquiryWhere` on the parameter.** The SQL structure would then follow parameter order.
   `Or`, `OpenGroups`, and `CloseGroups` on parameters would still change meaning when parameters
   move. `IsNull` has no parameter. `Between` spans two parameters. Class-level specifications
   cannot reach parameters.
3. **Add an explicit parameter reference to each method-level criterion.** Attribute order keeps
   defining the Boolean structure. The reference defines the value. C# 11 and later allow
   `nameof(parameter)` in an attribute on the same method, and Inquiry consumers target .NET 8 or
   later, so the default language version supports it.
4. **Infer bindings from parameter names.** Rejected. Names are not intent. A rename would change
   SQL meaning, which #386 already ruled out for method names.

### Decision

Choose option 3 as an additive opt-in. Keep option 1 as the supported default.

Proposed syntax:

```csharp
// Proposed. Not supported by current packages.
[InquirySelectAllByPredicate]
[InquiryWhere(nameof(Product.UnitPrice), Compare.GreaterThanOrEqual, Parameter = nameof(minimum))]
[InquiryWhere(nameof(Product.UnitPrice), Compare.LessThanOrEqual, Parameter = nameof(maximum))]
public partial Task<IReadOnlyList<Product>> PriceRangeAsync(
    decimal? maximum, decimal? minimum, CancellationToken ct = default);
```

Binding rules for the named form:

- One method uses one form. If any value-consuming criterion names a parameter, all must. Mixing
  forms is an error.
- An unknown name is an error. A parameter that no criterion uses is an error, as it is today.
- One parameter bound by two criteria is an error in the first version. Allowing reuse later is
  additive.
- `Between` takes `Parameter` for the lower bound and a second property (proposed name
  `UpperParameter`) for the upper bound.
- `In` and `NotIn` name one collection parameter. `IsNull` and `IsNotNull` take no parameter, and
  setting one is an error.
- Type checks and the nullable rule for `Optional` stay the same.
- A specification criterion may name a parameter as a string. Each method that uses the
  specification must declare a parameter with that name.
- For a set-based update with named criteria, the contiguous SET-prefix rule no longer applies.
  Every value binds by name.
- Paging, keyset cursor, page size, and cancellation parameters stay trailing and positional.

Existing declarations need no migration. To adopt the named form, add `Parameter = nameof(...)` to
each value-consuming criterion of a method.

### 1.0 contract consequences

- Positional binding is a permanent supported contract. Removing it would be a major-version
  change.
- The `Parameter` properties are additive and can ship in 1.0 or later.
- A new warning after 1.0 can break builds that treat warnings as errors. If maintainers want a
  diagnostic that flags positional methods with two same-type value parameters, add it before the
  freeze or ship it at Info severity.

### Follow-up

- Open an issue to implement `Parameter` and `UpperParameter` on `InquiryWhereAttribute`, with
  diagnostics for the rules above and examples for `Between`, `In`, `IsNull`, specifications,
  paging, and set-based updates.
- Optionally add the same-type positional diagnostic. Use Info severity if it lands after 1.0.

### Confidence

Medium. The direction is clear. The timing, the property names, and the optional diagnostic need a
maintainer decision.

## #420 Safe defaults for ad-hoc result mapping

### Current behavior

- An `[InquiryAdHoc]` DTO maps every settable public or internal instance property, in declaration
  order, to one SELECT-list ordinal. Column names and aliases are ignored
  (`src/Inquiry.Generators.Shared/AdHocProcessor.cs:97-136`). The
  [ad-hoc guide](../articles/features/ad-hoc-dtos.md) documents this.
- The materializer reads ordinal `i` into property `i` (`MaterializerEmitter.cs:12-22`). It checks
  `IsDBNull` only for nullable properties. A database NULL in a non-nullable property fails inside
  the provider's typed getter (`MaterializerEmitter.cs:35-38`).
- Extra trailing columns are ignored. Too few columns make the provider's getter throw for the
  missing ordinal. Same-type columns in the wrong order are assigned silently.
- Generated materializers declare themselves safe for `SequentialAccess` because they read in
  ascending ordinal order (`AdHocProcessor.cs:74`, `EntityProcessor.cs:164`). #181 introduced that
  guarantee.
- The materializer interface is per row. It has no per-result-set setup step
  (`src/Inquiry/Materialization/IInquiryEntityMaterializer.cs:23-28`). The class materializer that
  handwritten SQL uses is a DI singleton (`RegistrationEmitter.cs:46-49`,
  `DefaultInquiry.cs:573-575`). It cannot hold per-reader state safely.
- Entity types read through handwritten SQL, such as `QuerySingleOrDefaultAsync<Customer>`, use the
  same ordinal materializer. Procedure rows also map by ordinal into the entity's property order
  (`src/Inquiry.Generators.Shared/StoreOperationEmitter.cs:1730-1737`).
- Positional records are rejected with `INQ046`, because the materializer uses an object
  initializer (`AdHocProcessor.cs:38-45`).

### Options considered

1. **Keep ordinal mapping** and improve the documentation. This is cheapest and fastest. It keeps
   the silent-misassignment risk that #428 hit.
2. **Map by name.** Resolve each property to a column ordinal once per result set. Then read each
   row through the resolved ordinals. This is safe under reordering. It needs a per-result-set step
   and care with `SequentialAccess`.
3. **Validate ordinals.** Keep positional reads, but check `GetName(i)` against each property name
   and check `FieldCount` once per result set. Mismatches fail with a clear error. This is safe
   against silent swaps, but the SELECT order must still match.
4. **Explicit ordinal attribute** such as `[InquiryOrdinal(2)]`. This documents intent but does not
   detect mistakes.

### Decision

Choose option 2 for all results of handwritten SQL. Keep fixed ordinals for SQL the generator owns.

- **Scope.** Name mapping applies to `[InquiryAdHoc]` DTOs and entity types read through the
  `IInquiry` handwritten-SQL overloads. It also applies to procedure result rows. Generated store
  queries keep their fixed column lists and fixed ordinals.
- **Matching.** For an entity, match each mapped property by its mapped column name, from
  `InquiryColumn` or `InquiryKey`, not by its property name. For an ad-hoc DTO, which has no column
  mapping, match by property name. Compare names without regard to case, using ordinal invariant
  comparison. A property whose mapped name is absent is a missing column even if a column with its
  property name is present.
- **Missing column.** Throw `InvalidOperationException` before the first row is returned. Name the
  DTO, the property, and the available columns.
- **Duplicate names.** If two result columns match one property, throw. Do not pick the first. If
  two properties share a name under the matching comparer, such as `Id` and `ID`, report a build
  error. No two properties may resolve to the same result column.
- **Extra columns.** Ignore them. Reports often return helper columns.
- **Database NULL.** A nullable property receives null. For a non-nullable property, throw an error
  that names the property.
- **Incompatible types.** Let the provider's typed getter fail, wrapped in an exception that names
  the property and column.
- **SequentialAccess.** The resolved order may not be ascending. The materializer must read values
  into locals in ascending resolved ordinal order, then construct the object. If that cannot be
  done for a shape, the materializer must report that it is not sequential-safe, and the pipeline
  then buffers. Do not mark a reordering reader safe without that.
- **Per-result-set step.** Add a hidden, additive capability that creates per-reader state from the
  reader schema. Shared singleton materializers must not store per-reader ordinals.
- **Escape hatch.** Do not add an ordinal opt-in now. Add one later only if wide-row benchmarks show
  a real cost.
- **Constructors.** Defer constructor and positional-record binding. It is additive and is not
  needed to fix silent misassignment.

If benchmarks show that option 2 costs too much on wide rows or early-disposed streams, fall back to
option 3. It gives the same protection against silent misassignment without reordering.

### 1.0 contract consequences

- This changes behavior. A preview DTO whose aliases do not match its property names will fail at
  run time after the change. So will handwritten entity SQL and procedures whose result columns do
  not use the entity's mapped column names. The migration note must cover all three callers. It must land before the freeze, or 1.0 must accept ordinal mapping
  permanently.
- The `SequentialAccess` guarantee from #181 stays for generated SQL. For handwritten SQL, it holds
  where the ascending-read rule above is met.
- Custom `IInquiryEntityMaterializer<T>` implementations keep working. The new capability is
  optional for them.

### Follow-up

- Implement name resolution for handwritten-SQL materialization, with the error contract above.
  Measure wide rows, large final columns, and early stream disposal against the current ordinal
  path. Update the ad-hoc guide and the "ordinal contract" notes in the query composition and
  procedure guides. Add a changelog migration note.
- Track constructor binding for positional records as a separate additive issue.

### Confidence

Medium. Name mapping is the safer default, but it is a behavior change with a measurable cost. The
maintainer should choose between options 2 and 3, or accept option 1 permanently.

## #423 Typed procedure results and parameters

### Current behavior

- Accepted row shapes are `IAsyncEnumerable<TEntity>`, `Task<TEntity?>`, and `Task<int>`
  (`StoreProcessor.cs:1308-1326`). A tuple of two or more `IReadOnlyList<T>` elements gives multiple
  result sets (`StoreProcessor.cs:1128-1162`). A single `Task<IReadOnlyList<TEntity>>` reports
  `INQ005` (`StoreProcessor.cs:1106-1116`, `StoreProcessor.cs:145`).
- The [procedure guide](../articles/features/stored-procedures.md) opened with that unsupported
  `Task<IReadOnlyList<Customer>>` example. It also said "abstract" methods, though stores require
  partial methods. Its limitations section was empty. This change corrects the guide.
- Single-set rows must be the store entity. Tuple elements must be mapped entities, or the method
  reports `INQ051` (`StoreProcessor.cs:2249-2260`). Ad-hoc DTOs are not accepted.
- One scalar read-back is allowed: `OutputParameter`, `ReturnsValue`, or one INOUT parameter. None
  of them combines with rows (`StoreProcessor.cs:284-328`, `StoreProcessor.cs:373-406`).
- Database parameter names come from C# parameter names (`SqlBuilder.cs:126-127`,
  `OracleSqlBuilder.cs:123-126`). `InquiryParameterAttribute` has no name property
  (`src/Inquiry/Stores/InquiryParameterAttribute.cs:24-74`). The Oracle cursor wrapper passes
  arguments by position (`OracleSqlBuilder.cs:138-164`).
- `Task<int>` is the provider's affected-row count. Generated help says so, and separates RETURN
  status, OUTPUT values, and rows (`StoreOperationEmitter.cs:3028-3036`).
- TVP parameters are SQL Server only (`StoreProcessor.cs:1586-1594`).
- Live procedure tests exist for SQL Server and Oracle only
  (`tests/Inquiry.SqlServer.Tests/StoredProcedure*IntegrationTests.cs`,
  `tests/Inquiry.Oracle.Tests/*StoredProcedure*IntegrationTests.cs`). The generator has no
  provider check for procedure support, so a SQLite store accepts the declaration.

### Options considered

For buffered lists:

1. Support `Task<IReadOnlyList<TEntity>>`, like ordinary selects.
2. Keep the restriction and document it.

For read DTOs:

1. Accept `[InquiryAdHoc]` types as procedure row types for single and multiple result sets.
2. Keep entity-only rows. Callers then invent table or view mappings.

For rows plus outputs:

1. A declared result class that the generator fills. Rows, OUTPUT values, and RETURN status are
   available when the awaited call completes.
2. More flags on `InquiryStoredProcedure`. Each flag pair needs its own exclusion rules.
3. Ordinary `out` or `ref` parameters. Async methods cannot have them.
4. Two calls to the same procedure. This runs side effects twice and is rejected.

For parameter names:

1. Add `Name` to `InquiryParameterAttribute`.
2. Keep the C# name as the database name.

### Decision

- **Correct the guide now.** Done in this change: partial methods, a streaming example in place of
  the unsupported list, the `public` modifier on the TVP example, and a filled limitations section.
- **Buffered lists: option 1.** Support `Task<IReadOnlyList<TEntity>>` for procedures. This matches
  ordinary selects, and the getting-started guide already teaches buffered lists first. It is
  additive.
  Implement it before 1.0 if possible, because the guide used it as the first example.
- **Read DTOs: option 1.** Accept `[InquiryAdHoc]` row types, mapped by name under #420. This
  removes the need for fake mappings, and the later non-generic store from #422 builds on it.
- **Rows plus outputs: option 1, deferred.** Build it only when a consumer needs it. Begin with a
  buffered result. OUTPUT and RETURN values are readable only after the reader closes, so a
  streaming form would need its own completion contract. Keep the existing scalar form.
- **Parameter names: option 1, deferred.** Add `Name` when a consumer needs a database name that
  is not a valid or wanted C# name. It does not change the Oracle wrapper's positional order.
- **Result meanings.** Keep the distinction between affected rows, RETURN status, OUTPUT values, and
  rows that the guide and generated help already state. Do not treat every `Task<int>` as a count.

Proposed shape for a combined result:

```csharp
// Proposed. Not supported by current packages.
[InquiryProcedureResult]
public sealed class SalesResult
{
    public IReadOnlyList<SalesRow> Rows { get; init; } = [];
    [InquiryOutput("Total")] public decimal Total { get; init; }
    [InquiryReturnValue] public int ReturnCode { get; init; }
}
```

### 1.0 contract consequences

- No existing declaration changes meaning. Every addition above is a new accepted shape or a new
  optional property.
- The guide must stop showing unsupported code before release. This change does that.
- The provider capability table in #423 needs live evidence. This repository has live procedure
  tests for SQL Server and Oracle only. A table for PostgreSQL, MySQL, and MariaDB must wait for
  those tests.

### Follow-up

- Implement `Task<IReadOnlyList<TEntity>>` for procedures, with a live SQL Server and Oracle check.
- Implement `[InquiryAdHoc]` procedure rows after #420.
- Decide whether procedure methods on providers without procedures (SQLite) should fail at build
  time with a provider diagnostic.
- Keep combined results and `InquiryParameter.Name` as demand-driven issues.
- Publish the provider capability table once live tests exist for each listed provider.

### Confidence

High for the corrections and the buffered list. Medium for read DTOs, because they depend on #420.
The deferrals follow the issue's own recommendation.

## #425 Absent, null, and value states for optional filters

### Current behavior

- `Optional = true` renders `(@p IS NULL OR column = @p)`. A null argument disables the criterion. It
  does not mean `column IS NULL` (`src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs:1319-1321`).
- `Optional` needs one nullable scalar parameter and rejects `In` and `NotIn` with `INQ098`
  (`StoreProcessor.cs:3438-3443`).
- `Compare.IsNull` consumes no parameter (`StoreProcessor.cs:3467-3470`). An unassigned search
  therefore needs its own declaration.
- `false` and `0` are values. Only null disables an optional criterion. An empty string is a value
  except on Oracle, which treats `""` as NULL, so it disables an optional string criterion there.
- The #428 sample needs a second declaration for the unassigned state of each sort
  (`samples/Inquiry.Sample.Data/WorkspaceStores.cs:17-71`). Its service selects one with a switch
  (`samples/Inquiry.Sample.Data/ProductWorkspace.cs:28-38`).

### Options considered

1. **Keep the current API.** Publish the pattern: one optional declaration for all and one value,
   one `IsNull` declaration for unassigned, and an application-owned filter type that selects one.
2. **A presence-aware argument type**, for example `InquiryFilter<int>` with `Any`, `IsNull`, and
   `EqualTo(42)`. It would render one constant SQL shape with a mode parameter. That shape is not
   sargable, like `Optional`.
3. **A flag that makes null mean `IS NULL`.** This gives two states, not three, and makes `Optional`
   harder to read.
4. **Reuse a presence type from #417.** #417 chose an application recipe and added no presence type
   ([partial updates](../articles/features/partial-updates.md)). There is nothing to reuse.

### Decision

Choose option 1 for 1.0. Keep `Optional = true` exactly as it is.

The pattern is published in
[Choosing a select declaration](../articles/features/select-declarations.md#all-unassigned-or-one-category).
Option 2 stays a possible additive feature. Revisit it if more applications show the same
duplication as #428, together with any sort convenience from #426. Duplication multiplies across
the two.

### 1.0 contract consequences

- None. `Optional` keeps its meaning. A later filter type would be a new parameter type in new
  declarations.

### Follow-up

- None required. If option 2 is pursued later, open a separate design issue that covers binding
  (#419), ordering, paging, and specifications.

### Confidence

High for 1.0. The maintainer may still prefer to schedule option 2, given the #428 evidence.

## #426 User-selected sorting

### Current behavior

- `OrderBy` is a constant attribute string. The generator resolves and quotes each field at build
  time. Unknown fields fail the build (`src/Inquiry/Stores/InquirySelectAllAttribute.cs:70-75`,
  `StoreProcessor.cs:347-354`).
- Only `ASC` and `DESC` are accepted. `NULLS FIRST` and `NULLS LAST` report `INQ042`
  (`StoreProcessor.cs:966-1013`). Null placement follows each provider's default, which differs.
- Keyset pages use one direction for all key fields. Their columns must be non-nullable
  (`src/Inquiry/Stores/InquiryKeysetPageAttribute.cs:17-60`, `StoreProcessor.cs:355-369`).
- The #428 sample declares four sort orders, each with a unique `Id` tie-breaker, and dispatches
  with an enum switch (`samples/Inquiry.Sample.Data/ProductWorkspace.cs:6-38`).

### Options considered

1. **Declared methods plus application dispatch.** One generated method per allowed order. An
   application enum selects the method. This is the current API.
2. **A bounded generated sort option.** Declare a fixed set of orders on one method. A generated
   enum parameter selects one of the pre-generated SQL constants. Code size grows with the number of
   declared orders.
3. **A runtime sort string checked against an allow-list.** This moves identifiers into run-time
   input and weakens the compile-time guarantee.
4. **Runtime expression translation.** Rejected. It is the LINQ provider the roadmap excludes.

### Decision

Choose option 1 for 1.0. The pattern is published in
[Choosing a select declaration](../articles/features/select-declarations.md#user-selected-sorting).
Option 2 is the only convenience worth considering later. It is additive. Reject options 3 and 4.

Rules the pattern states:

- Parse user input into an application enum. Reject undefined values before any database call.
  Never use input text as an identifier.
- End every order with a unique tie-breaker.
- When the order or a filter changes, restart offset paging at 0. Restart keyset paging with a null
  cursor. A cursor from one order is not valid for another.

### 1.0 contract consequences

- None. `OrderBy` stays compile-time. A later bounded option would be a new attribute or property.

### Follow-up

- None required. If option 2 is pursued, open a design issue that bounds generated code size and
  covers keyset cursors per order.

### Confidence

High.

## Decisions that most need maintainer input

1. **#420 default mapping.** Name mapping, validated ordinals, or permanent ordinal mapping. This
   is the only decision here that must land before the freeze to change behavior.
2. **#419 timing and names.** Whether the `Parameter` binding and the optional diagnostic land
   before 1.0, and what the properties are called.
3. **#422 timing.** Whether the non-generic `InquiryStore` ships in 1.0 or after.
4. **#423 scope for 1.0.** Whether the buffered procedure list and ad-hoc procedure rows are 1.0
   work or later additions.
5. **#425 and #426 together.** Whether the eight declarations in #428 justify a bounded sort
   option or a filter type before 1.0. This page says no, because both are additive later.
