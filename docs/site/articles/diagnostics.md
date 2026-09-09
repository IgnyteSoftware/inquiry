# Diagnostic repair index

Start with the first INQ diagnostic on your declaration. CS8795 often follows because a rejected
partial method has no generated implementation; adding a method body hides the cause.
The IDE help link for each active shared or provider-owned ID points to its section below.
Entries describe small failing shapes and corrections, not complete standalone projects.

## First errors in a starter project

- Missing entity mapping: [INQ008](#inq008), then [INQ001](#inq001) if the table has no key.
- Unsupported method result: [INQ005](#inq005). Procedure list returns are not supported.
- Predicate binding mismatch: [INQ019](#inq019), or [INQ006](#inq006) for the broader operation signature.
- Ambiguous or unknown dialect: [INQ014](#inq014) and [INQ043](#inq043).

With no provider package, no provider generator may run at all. Reference a released provider package
such as Ignyte.Inquiry.Sqlite. Do not wait for an INQ diagnostic from an analyzer that is not installed.

## Severity and suppression

Fix errors rather than suppressing them. Suppression does not turn an invalid mapping into supported
SQL and does not guarantee that a partial implementation is emitted. Keep generated SQL hover help
for inspecting valid methods; ordinary declaration repairs should start here.

INQ039 is an explicit exception: lowering it to warning or none project-wide opts unsupported methods
into synchronous NotSupportedException stubs. That is a deliberate unavailable-operation policy,
not a repair. Local pragma suppression is not a substitute for that project-wide choice.

INQ048 is a SQL-injection warning. Suppress it locally only after verifying that dynamic SQL cannot
contain untrusted values or identifiers. Bind values with InquirySql.Sql or the FormattableString APIs.
DDL lints INQ061, INQ062, INQ064, INQ066, and INQ067 are disabled by default. Enable them deliberately:

```ini
[*.cs]
dotnet_diagnostic.INQ061.severity = warning
```

For an accepted warning, use a narrowly scoped configuration or pragma and document the reason.
Each entry below lists the descriptor's default severity and whether it is enabled. Provider SQL limits
still apply even when a warning is suppressed. IDs INQ003, INQ013, INQ015, and INQ027 are retired and are
not reused. INQ076 belongs to the SQL Server analyzer; the other active IDs are shared.

## Worked first-error repairs

### Missing mapping

An otherwise valid ProductStore cannot use an undecorated Product. Keep the store declaration and
add the mapping to the entity:

```csharp
using Inquiry.Entities;

[InquiryTable("Products")]
public sealed class Product
{
    [InquiryKey] public int ProductID { get; set; }
    [InquiryColumn] public string ProductName { get; set; } = "";
}
```

### Unsupported procedure list

Failing declaration inside a partial InquiryStore&lt;Product&gt;:

```csharp
[InquiryStoredProcedure("GetProducts")]
public partial Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct = default);
```

Corrected declaration for procedure SELECT rows:

```csharp
[InquiryStoredProcedure("GetProducts")]
public partial IAsyncEnumerable<Product> GetProductsAsync(CancellationToken ct = default);
```

Consume and, if needed, buffer that stream inside its service scope. Task&lt;Product?&gt; is the supported
zero-or-one-row alternative, not a list. Task&lt;int&gt; without read-back options means provider affected
rows, not a row list or universal success value. Use the separate OUTPUT/RETURN/INOUT declarations
only for their intended channels. See [Stored procedures](features/stored-procedures.md); do not invoke
a side-effecting procedure again merely to retrieve another channel.

### Predicate mismatch

For a non-nullable integer ProductID, this fails:

```csharp
[InquirySelectAllByPredicate]
[InquiryWhere(nameof(Product.ProductID))]
public partial Task<IReadOnlyList<Product>> FindAsync(string productID, CancellationToken ct = default);
```

Change only string productID to int productID. Keep parameters in criterion order. A method parameter
name does not bind it to a property by itself.

### Dialect selection

Use one provider package for an inferred dialect. If multiple references are intentional, add this
assembly attribute in a file with no top-level statements:

```csharp
[assembly: Inquiry.InquiryDialect("Sqlite")]
```

Use a name supplied by a referenced provider, and configure the matching runtime provider. This does
not allow one generated store assembly to switch SQL dialect at runtime.

## All active IDs

## INQ001

Entity must have at least one InquiryKey property. Default: error, enabled.

Trigger: A table entity has no key.

Failing shape: [InquiryTable("Products")] class Product { public int Id { get; set; } }

Smallest correction: Mark its identifier [InquiryKey]; keep the deployed primary key consistent.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L63).

## INQ002

Entity contains duplicate mapped column names. Default: error, enabled.

Trigger: Two properties map the same physical column.

Failing shape: Id and LegacyId both map to "Id".

Smallest correction: Give the properties distinct column names, or remove the unintended mapping.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L72).

## INQ004

Store class must be partial. Default: error, enabled.

Trigger: A generated store is not partial.

Failing shape: public class ProductStore : InquiryStore&lt;Product&gt;

Smallest correction: public partial class ProductStore : InquiryStore&lt;Product&gt;

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L81).

## INQ005

Query method return type is not supported. Default: error, enabled.

Trigger: The method return shape is not supported for its operation.

Failing shape: A procedure returns Task&lt;IReadOnlyList&lt;Product&gt;&gt;.

Smallest correction: Use IAsyncEnumerable&lt;Product&gt; for procedure rows, or another supported procedure result channel. See the [worked procedure repair](#unsupported-procedure-list).

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L90).

## INQ006

Query method parameter list is invalid. Default: error, enabled.

Trigger: The operation's parameters or entity capabilities do not meet its required shape.

Failing shape: A select-all method omits the trailing CancellationToken.

Smallest correction: Add CancellationToken ct = default; for keyed/mutation forms match entity/key parameters. Token upserts and unsupported token/provider combinations can also report this ID.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L99).

## INQ007

SelectByField references an unmapped property or column. Default: error, enabled.

Trigger: A query field does not resolve to a mapped property or column.

Failing shape: [InquirySelectAllByField("CategroyID")]

Smallest correction: [InquirySelectAllByField(nameof(Product.CategoryID))]

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L108).

## INQ008

Store entity type is not mapped with InquiryTable. Default: error, enabled.

Trigger: The store's entity is not a valid mapped entity.

Failing shape: InquiryStore&lt;Product&gt; with an undecorated Product.

Smallest correction: Add [InquiryTable("Products")] and valid key/column mappings; repair earlier entity diagnostics first.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L117).

## INQ009

Mapped entity property must have an accessible setter. Default: error, enabled.

Trigger: A mapped property cannot be assigned by generated code.

Failing shape: public string Name { get; private set; }

Smallest correction: Use a public or internal setter, or a supported init setter.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L126).

## INQ010

Query method must be a partial declaration. Default: error, enabled.

Trigger: An attributed query method is not a partial declaration.

Failing shape: [InquirySelectAll] public Task&lt;IReadOnlyList&lt;Product&gt;&gt; AllAsync(...) { ... }

Smallest correction: Use public partial Task&lt;IReadOnlyList&lt;Product&gt;&gt; AllAsync(CancellationToken ct = default); without a body.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L135).

## INQ011

Composite primary key cannot contain database-generated columns. Default: error, enabled.

Trigger: A composite key includes a database-generated component.

Failing shape: Two [InquiryKey] properties, one with IsGenerated = true.

Smallest correction: Use client-supplied components, or redesign the database key as one generated column.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L144).

## INQ012

Eager loading is not supported on a composite-key parent entity. Default: error, enabled.

Trigger: An eager-loading parent has a composite key.

Failing shape: InquirySelectAllEager on a parent with two keys.

Smallest correction: Use separate non-eager reads or a single-key parent mapping; do not silently discard a real key component.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L153).

## INQ014

Multiple Inquiry SQL dialects are referenced. Default: error, enabled.

Trigger: More than one referenced provider supplies a dialect.

Failing shape: SQLite and SQL Server provider references without an override.

Smallest correction: Keep one provider reference, or set [assembly: Inquiry.InquiryDialect("Sqlite")] deliberately and use that runtime dialect.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L162).

## INQ016

Store class cannot be nested inside another type. Default: error, enabled.

Trigger: A store is nested inside another type.

Failing shape: class Host { partial class ProductStore : InquiryStore&lt;Product&gt; { } }

Smallest correction: Move ProductStore to namespace scope.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L180).

## INQ017

Store class cannot be abstract. Default: error, enabled.

Trigger: A store is abstract and cannot be instantiated by generated registration.

Failing shape: public abstract partial class ProductStore : InquiryStore&lt;Product&gt;

Smallest correction: Remove abstract from the store; query methods remain partial.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L189).

## INQ018

InquiryWhere In operator requires a collection parameter of the column type. Default: error, enabled.

Trigger: An IN/NOT IN criterion lacks a compatible collection parameter.

Failing shape: Compare.In on integer Id with int id.

Smallest correction: Use IEnumerable&lt;int&gt; ids, or another supported collection with matching element type.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L198).

## INQ019

InquiryWhere criteria do not match the method parameters. Default: error, enabled.

Trigger: Predicate parameter arity, order, or types disagree with the criteria.

Failing shape: Integer CategoryID criterion consumes string categoryId.

Smallest correction: Use the column's compatible type in criterion order; Between consumes two parameters and IsNull none. See the [worked predicate repair](#predicate-mismatch).

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L207).

## INQ020

Paged query requires an ORDER BY and matching offset/limit (or pageSize) parameters. Default: error, enabled.

Trigger: Paging lacks valid ordering or the expected parameters/result.

Failing shape: Paged = true with no OrderBy.

Smallest correction: Supply OrderBy and int offset, int limit before CancellationToken; keyset uses a nullable cursor and int pageSize.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L216).

## INQ021

ORDER BY / keyset references an unmapped property or column. Default: error, enabled.

Trigger: An ordering field is unmapped.

Failing shape: OrderBy = "Missing ASC"

Smallest correction: Use nameof(Product.ProductID) + " ASC" for a mapped property.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L225).

## INQ022

Batch mutation is not supported for optimistic-concurrency entities. Default: error, enabled.

Trigger: A batch or predicate mutation targets a concurrency-token entity.

Failing shape: InquiryUpdate with IEnumerable&lt;TokenEntity&gt;.

Smallest correction: Use a single-entity update/delete that checks its token; do not remove concurrency protection just to make the batch compile.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L234).

## INQ023

Set-based mutation requires at least one InquiryWhere criterion. Default: error, enabled.

Trigger: A set-based mutation has no predicate.

Failing shape: A partial scalar update without InquiryWhere.

Smallest correction: Add [InquiryWhere(nameof(Product.ProductID))] and its matching parameter; table-wide deletion has an explicit attribute.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L246).

## INQ024

Projection declares no mapped columns. Default: error, enabled.

Trigger: A projection has no mapped columns.

Failing shape: [InquiryProjection(typeof(Product))] class Summary with no InquiryColumn properties.

Smallest correction: Mark the intended source-matching properties [InquiryColumn].

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L309).

## INQ025

Query method result type is not the entity or a known projection. Default: error, enabled.

Trigger: A select returns neither the store entity nor a registered projection.

Failing shape: SelectAll returns `Task<IReadOnlyList<Summary>>` without projection mapping.

Smallest correction: Map Summary with [InquiryProjection(typeof(Product))] and columns, or return Product.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L318).

## INQ026

Projection targets a different entity than the store. Default: error, enabled.

Trigger: A projection targets a different entity from its store.

Failing shape: ProductStore returns a projection of Customer.

Smallest correction: Use a projection of Product or move the query to the appropriate store.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L327).

## INQ028

Entity declares more than one InquiryConcurrencyToken column. Default: error, enabled.

Trigger: An entity has multiple concurrency tokens.

Failing shape: Both Version and Stamp carry InquiryConcurrencyToken.

Smallest correction: Keep one token and remove the duplicate token annotation.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L264).

## INQ029

InquiryConcurrencyToken cannot also be the primary key. Default: error, enabled.

Trigger: A concurrency token is also a primary key.

Failing shape: [InquiryKey, InquiryConcurrencyToken] int Version.

Smallest correction: Use a separate non-key token column.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L273).

## INQ030

Database-generated key must be an integer type. Default: error, enabled.

Trigger: Identity-style generation is applied to a non-integer key.

Failing shape: [InquiryKey(IsGenerated = true)] Guid Id.

Smallest correction: Use a client Guid key, SequentialGuid, or a provider-supported database default; IsGenerated is for integer identities.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L340).

## INQ031

String key column requires a bounded Length for this dialect. Default: error, enabled.

Trigger: A string primary key exceeds the provider's bounded-key rules.

Failing shape: A SQL Server string key with no bounded Length.

Smallest correction: Declare a positive bounded Length within the provider's key limits and align the schema.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L349).

## INQ032

Indexed string column requires a bounded Length for this dialect. Default: warning, enabled.

Trigger: An indexed string maps to an unindexable unbounded type.

Failing shape: [InquiryColumn(IsIndexed = true)] string Code without Length.

Smallest correction: Set a provider-valid bounded Length so the generated index is not skipped.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L358).

## INQ033

InquirySoftDelete column type is not supported. Default: error, enabled.

Trigger: The soft-delete property has an unsupported type.

Failing shape: [InquirySoftDelete] int Deleted.

Smallest correction: Use bool, DateTime?, or DateTimeOffset? according to the schema.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L282).

## INQ034

Entity declares more than one InquirySoftDelete column. Default: error, enabled.

Trigger: Multiple soft-delete indicators are declared.

Failing shape: Both Deleted and DeletedAt carry InquirySoftDelete.

Smallest correction: Choose one soft-delete representation and remove the duplicate annotation.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L291).

## INQ035

Full-text search is not supported by the target dialect. Default: error, enabled.

Trigger: The chosen dialect does not support the full-text operation.

Failing shape: InquiryFullTextSearch under an unsupported dialect.

Smallest correction: Use a supported provider or a separately reviewed parameterized search query; changing the provider is a runtime/schema decision too.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L300).

## INQ036

InquiryEnumAsString applied to a non-enum property. Default: error, enabled.

Trigger: Enum-as-string is applied to a non-enum property.

Failing shape: [InquiryEnumAsString] string State.

Smallest correction: Remove the annotation or declare a supported enum property.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L656).

## INQ037

Converter type is invalid. Default: error, enabled.

Trigger: The converter has no single matching converter contract.

Failing shape: Converter = typeof(NoConverterContract).

Smallest correction: Implement exactly one IInquiryValueConverter&lt;TModel,TProvider&gt; for the property's non-null model type.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L566).

## INQ038

Converter provider type is not supported. Default: error, enabled.

Trigger: The converter's provider representation is unsupported.

Failing shape: A converter uses a custom class or nullable scalar as TProvider.

Smallest correction: Use a supported non-null scalar provider type; model nullability controls SQL NULL.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L575).

## INQ039

Operation is not supported by the target dialect. Default: error, enabled.

Trigger: The selected dialect cannot emit this operation.

Failing shape: A returning mutation form rejected for the active dialect.

Smallest correction: Use a supported mutation/result shape or explicit provider SQL. Do not obtain an identity with an unsafe MAX(id) query. Lowering severity deliberately emits throwing stubs, not working SQL.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L668).

## INQ040

InquiryRelation references an unmapped foreign-key property. Default: error, enabled.

Trigger: A relation names an unmapped foreign-key property.

Failing shape: InquiryRelation references "MissingForeignKey".

Smallest correction: Name the actual mapped FK on the correct side, preferably with nameof.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L679).

## INQ041

InquiryRelation child entity has a composite primary key, which is not supported. Default: error, enabled.

Trigger: An InquiryRelation target has an unsupported composite key.

Failing shape: A reference relation targets a child with two key columns.

Smallest correction: Use explicit reads or a supported relation shape; do not misrepresent the database key.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L688).

## INQ042

OrderBy term has an invalid direction token. Default: error, enabled.

Trigger: OrderBy has an invalid direction token.

Failing shape: OrderBy = "ProductID DOWN"

Smallest correction: OrderBy = "ProductID DESC"

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L712).

## INQ043

Unknown Inquiry SQL dialect. Default: error, enabled.

Trigger: An explicit dialect name is not supplied by a referenced provider.

Failing shape: [assembly: Inquiry.InquiryDialect("SQLiteTypo")]

Smallest correction: Use an available dialect name such as "Sqlite" and reference its provider package.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L171).

## INQ044

InquiryUpdate SET field is not an updatable column. Default: error, enabled.

Trigger: A set-based update targets a protected column.

Failing shape: A partial update assigns ProductID or a concurrency token.

Smallest correction: Update a mutable column; use token-aware single-entity updates and delete/restore APIs for their owned columns.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L884).

## INQ045

Ad-hoc DTO declares no mappable properties. Default: error, enabled.

Trigger: An ad-hoc DTO has no assignable instance properties.

Failing shape: [InquiryAdHoc] class Row with only getter-only properties.

Smallest correction: Add a public/internal setter or init property for each SELECT ordinal.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L724).

## INQ046

Ad-hoc DTO must be a concrete type with an accessible parameterless constructor. Default: error, enabled.

Trigger: An ad-hoc DTO cannot be constructed.

Failing shape: An abstract DTO or only a parameterized constructor.

Smallest correction: Use a concrete DTO with an accessible parameterless constructor and assignable properties.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L736).

## INQ047

SequentialGuid requires a client-supplied Guid key. Default: error, enabled.

Trigger: SequentialGuid is combined with an incompatible key type or generator.

Failing shape: [InquiryKey(SequentialGuid = true, IsGenerated = true)] Guid Id.

Smallest correction: Keep SequentialGuid on a client-supplied Guid/Guid? key without IsGenerated or UseDatabaseDefault.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L748).

## INQ048

Non-constant SQL passed to InquiryCommand. Default: warning, enabled.

Trigger: InquiryCommand receives non-constant SQL text.

Failing shape: new InquiryCommand("SELECT ... WHERE Id=" + userId).

Smallest correction: Use InquirySql.Sql($"SELECT ... WHERE Id={userId}") so values become parameters. Review trusted dynamic identifiers separately before a local suppression.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L872).

## INQ049

Auditing timestamp column is invalid. Default: error, enabled.

Trigger: An audit timestamp has an incompatible type or another ownership role.

Failing shape: [InquiryCreatedAt] string Created.

Smallest correction: Use a plain DateTime/DateTimeOffset column, nullable if appropriate, without key/default/token roles.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L760).

## INQ050

Entity declares more than one auditing timestamp of the same kind. Default: error, enabled.

Trigger: An audit timestamp kind is duplicated.

Failing shape: Two InquiryCreatedAt properties.

Smallest correction: Keep one CreatedAt and at most one ModifiedAt annotation.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L769).

## INQ051

Stored-procedure scalar output is misconfigured. Default: error, enabled.

Trigger: Stored-procedure output/return settings conflict or have a wrong result type.

Failing shape: OutputParameter and ReturnsValue both set.

Smallest correction: Choose one read-back channel; ReturnsValue requires Task&lt;int&gt;. Match INOUT type and return type.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L860).

## INQ052

View-mapped entity is read-only. Default: error, enabled.

Trigger: A write or procedure is declared against a read-only view entity.

Failing shape: InquiryInsert on InquiryStore&lt;ViewRow&gt;.

Smallest correction: Use read-only operations for the view, or target the correctly mapped writable table.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L781).

## INQ053

Operation requires a key the entity does not declare. Default: error, enabled.

Trigger: A key-dependent operation targets a keyless view.

Failing shape: InquirySelectOneByKey on a view without InquiryKey.

Smallest correction: Use a field/predicate query, or map a real unique key if the view has one.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L847).

## INQ054

Cannot derive query fields from the method name. Default: error, enabled.

Trigger: A field-less ByField attribute cannot derive fields from the method name.

Failing shape: [InquirySelectAllByField] FindAsync(...).

Smallest correction: Supply [InquirySelectAllByField(nameof(Product.CategoryID))]; method names then remain cosmetic.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L835).

## INQ055

Auditing user column is invalid. Default: error, enabled.

Trigger: An audit user column is not a plain compatible string column.

Failing shape: [InquiryCreatedBy] int UserId.

Smallest correction: Use a string column without key/generated/default/token/soft-delete roles.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L814).

## INQ056

Entity declares more than one auditing user column of the same kind. Default: error, enabled.

Trigger: An audit-user kind is duplicated.

Failing shape: Two InquiryModifiedBy properties.

Smallest correction: Keep at most one property per audit-user kind.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L823).

## INQ057

Server-computed column is misconfigured. Default: error, enabled.

Trigger: A computed column also declares another value owner.

Failing shape: Computed plus UseDatabaseDefault or InquiryKey.

Smallest correction: Remove the conflicting role; the database expression alone owns the computed value.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L793).

## INQ058

InquiryRelation foreign key is on the wrong entity. Default: error, enabled.

Trigger: A relation's FK is declared on the wrong side.

Failing shape: A collection relation names an FK on its parent instead of child.

Smallest correction: For collections put/name the FK on the child; for reference navigation use the parent's FK.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L700).

## INQ059

InquiryGlobalFilter column is invalid. Default: error, enabled.

Trigger: A static global visibility filter has an invalid shape.

Failing shape: [InquiryGlobalFilter] int Visible.

Smallest correction: Use a non-null bool without conflicting ownership roles; tenant ContextKey rules are separate.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L896).

## INQ060

InquiryWhere JSON-path criterion is invalid. Default: error, enabled.

Trigger: A JSON predicate uses an unsupported path or column.

Failing shape: JsonPath = "$.items[0]" or a converted JSON model column.

Smallest correction: Use a plain string JSON column and a dotted identifier path such as "$.address.city".

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L909).

## INQ061

Foreign-key column has no index. Default: info, disabled.

Trigger: An FK lacks an index on a provider that does not add one.

Failing shape: A frequently joined FK has no index.

Smallest correction: Add IsIndexed = true to its column/FK mapping after checking the access pattern.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L372).

## INQ062

Decimal column relies on the default precision/scale. Default: info, disabled.

Trigger: Decimal precision and scale are implicit.

Failing shape: [InquiryColumn] decimal Amount.

Smallest correction: Declare Precision and Scale, for example Precision = 18, Scale = 2, to match the intended schema.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L504).

## INQ063

InquiryManyToMany declaration is unusable. Default: error, enabled.

Trigger: A many-to-many declaration has an invalid collection/junction shape.

Failing shape: InquiryManyToMany on a scalar navigation.

Smallest correction: Use a supported collection navigation and a valid explicit junction/FK mapping, or the supported auto-junction form.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L387).

## INQ064

Filtered column has no index. Default: info, disabled.

Trigger: A query filter column has no index.

Failing shape: A frequently filtered CategoryID has no index.

Smallest correction: Add IsIndexed = true or a suitable explicit index after checking the query workload.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L516).

## INQ065

Column Length/Precision/Scale is out of range. Default: error, enabled.

Trigger: A column facet is outside its accepted range.

Failing shape: Length = -1 or Scale greater than Precision.

Smallest correction: Remove the invalid facet or set a supported non-negative/ordered combination reported by the diagnostic.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L531).

## INQ066

Nullable column has a default value. Default: info, disabled.

Trigger: A nullable column also declares a default.

Failing shape: A nullable value with DefaultExpression.

Smallest correction: Choose nullability/default intentionally. Remove an unintended default or make the column non-nullable when valid. Explicit NULL can still be stored in a nullable column; DEFAULT does not forbid NULL.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L544).

## INQ067

String column has no explicit length. Default: info, disabled.

Trigger: A string uses an implicit unbounded SQL type.

Failing shape: [InquiryColumn] string Code with no Length or SqlType.

Smallest correction: Set an appropriate bounded Length, or retain the unbounded type deliberately and suppress this optional lint.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L557).

## INQ068

Database-generated concurrency token is invalid. Default: error, enabled.

Trigger: A database-generated concurrency token has an invalid representation.

Failing shape: DatabaseGenerated = true on an int token.

Smallest correction: On SQL Server use non-null byte[] with no conflicting facets; elsewhere use a supported managed numeric token.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L918).

## INQ069

Provider cannot emit cyclic foreign keys. Default: error, enabled.

Trigger: A schema FK cycle cannot be emitted by this provider.

Failing shape: Two generated tables reference each other on a provider lacking the required deferred constraint creation.

Smallest correction: Break the cycle or manage those constraints outside generated DDL; do not remove constraints from the deployed schema without a migration decision.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L927).

## INQ070

Duplicate physical schema mapping. Default: error, enabled.

Trigger: Physical schema mappings collide.

Failing shape: Two entities map the same schema/table incompatibly.

Smallest correction: Keep one unambiguous physical mapping and fix colliding constraint names.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L936).

## INQ071

Schema primitive is invalid for the provider. Default: error, enabled.

Trigger: A schema primitive has a provider-specific validation error.

Failing shape: An index/constraint/default option rejected by the selected dialect.

Smallest correction: Use the accepted shape stated in the diagnostic's reason, or manage that provider-specific object outside generated schema.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L945).

## INQ072

Computed expression is invalid for the provider. Default: error, enabled.

Trigger: A computed expression is not supported by the provider.

Failing shape: A computed SQL expression uses an unsupported provider construct.

Smallest correction: Replace it with an expression accepted by that provider, or manage the computed object outside generated DDL.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L954).

## INQ073

Schema manifest exceeds metadata transport limit. Default: error, enabled.

Trigger: The schema manifest exceeds its metadata chunk limit.

Failing shape: More than 10000 generated manifest chunks.

Smallest correction: Split the mapped schema into separate assemblies.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L963).

## INQ074

Generated key schema facets conflict. Default: error, enabled.

Trigger: An identity key declares conflicting physical facets.

Failing shape: An IsGenerated key also supplies a default expression.

Smallest correction: Remove the conflicting facet identified in the message; identity generation owns it.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L972).

## INQ075

Schema manifest assembly metadata key is already declared. Default: error, enabled.

Trigger: User assembly metadata occupies an Inquiry-reserved transport key.

Failing shape: AssemblyMetadata uses an Inquiry schema-manifest key.

Smallest correction: Remove or rename the user metadata entry; preserve generated metadata.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L978).

## INQ076

SQL Server TVP collection mapping is invalid. Default: error, enabled.

Trigger: SQL Server cannot map a collection element to the required TVP storage.

Failing shape: A TVP element column has an unsupported physical mapping.

Smallest correction: Correct the type/facets identified in the reason or use a supported scalar collection mapping. The SQL Server resolver owns this ID.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.SqlServer.Analyzer/SqlServerTvpResolver.cs#L12).

## INQ077

Oracle computed string column requires a bounded length. Default: error, enabled.

Trigger: An Oracle computed string lacks a valid scalar bound.

Failing shape: Computed string with no Length.

Smallest correction: Set a positive Length no greater than the limit in the diagnostic.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L802).

## INQ078

Converter model type does not match the property type. Default: error, enabled.

Trigger: The converter's model type differs from the property.

Failing shape: A converter for Money is attached to an int property.

Smallest correction: Use a converter whose TModel is the property's non-null model type.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L584).

## INQ079

Converter type cannot be abstract. Default: error, enabled.

Trigger: The converter class is abstract.

Failing shape: Converter = typeof(AbstractMoneyConverter).

Smallest correction: Use a concrete converter class with the required contract and constructor.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L593).

## INQ080

Converter type must be closed. Default: error, enabled.

Trigger: The converter type is open generic.

Failing shape: Converter = typeof(Converter&lt;&gt;).

Smallest correction: Supply a closed converter type, such as a concrete MoneyConverter.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L602).

## INQ081

Converter type is inaccessible. Default: error, enabled.

Trigger: Generated code cannot access the converter type.

Failing shape: A private nested converter type.

Smallest correction: Make the converter accessible from the generated assembly code.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L611).

## INQ082

Converter type needs a public parameterless constructor. Default: error, enabled.

Trigger: The converter has no public parameterless constructor.

Failing shape: Only MoneyConverter(string setting) exists.

Smallest correction: Provide a public parameterless constructor for the stateless converter.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L620).

## INQ083

InquiryPagedResult cannot be combined with Distinct. Default: error, enabled.

Trigger: A total-count page also requests DISTINCT.

Failing shape: Distinct = true with InquiryPagedResult&lt;Summary&gt;.

Smallest correction: Remove Distinct or use a result without the paired total, such as a supported list query.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L629).

## INQ084

Typed foreign key target lacks [InquiryTable]. Default: error, enabled.

Trigger: A typed FK target lacks a table mapping.

Failing shape: InquiryForeignKey(typeof(Customer)) on an unmapped Customer.

Smallest correction: Map Customer with InquiryTable, or use a valid explicit string table/column mapping.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L638).

## INQ085

Typed foreign key target has no single [InquiryKey]. Default: error, enabled.

Trigger: A typed FK target has no single inferable key.

Failing shape: Typed FK points to a composite-key target without explicit column selection.

Smallest correction: Name the target column explicitly or use an explicit string mapping; retain the real composite key.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L647).

## INQ086

Stored-procedure collection parameter TVP binding is invalid. Default: error, enabled.

Trigger: A procedure collection parameter lacks a supported TVP binding.

Failing shape: A SQL Server procedure IEnumerable&lt;int&gt; parameter has no TvpTypeName.

Smallest correction: Apply `[InquiryParameter(TvpTypeName = "[dbo].[IntList]")]` to the collection parameter, matching a deployed type. Other providers need a supported non-TVP parameter design.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L984).

## INQ087

InquiryManyToMany junction or related type is not a mapped entity. Default: error, enabled.

Trigger: An explicit many-to-many junction or related type is not mapped.

Failing shape: The junction class lacks InquiryTable.

Smallest correction: Map both types and repair their own validation errors before repairing the relation.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L467).

## INQ088

InquiryManyToMany names a junction property that is not a mapped column. Default: error, enabled.

Trigger: A many-to-many child FK name is not a mapped junction column.

Failing shape: ChildForeignKeys names an unannotated junction property.

Smallest correction: Name a junction property carrying InquiryColumn or InquiryKey, using nameof.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L479).

## INQ089

InquiryManyToMany child foreign keys do not pair with the related entity's key. Default: error, enabled.

Trigger: Junction child FKs do not pair with the related key.

Failing shape: One child FK for a two-part related key, or wrong order/types.

Smallest correction: List one distinct matching junction property per key component, in key-declaration order.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L492).

## INQ090

InquiryManyToMany cannot synthesize an auto-managed junction. Default: error, enabled.

Trigger: An auto-managed junction cannot be synthesized.

Failing shape: Auto-junction options conflict with its related/key mapping.

Smallest correction: Correct the specific reason in the message, or use an explicitly mapped junction with valid columns and keys.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L402).

## INQ091

InquiryIgnoreFilter cannot bypass the named filter. Default: error, enabled.

Trigger: A filter bypass names no eligible filter or is on an unsupported operation.

Failing shape: InquiryIgnoreFilter("Missing") on a store method.

Smallest correction: Use the exact declared filter Name on a supported non-eager read, or remove the bypass.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L456).

## INQ092

InquiryGlobalFilter Name is invalid. Default: error, enabled.

Trigger: A global-filter name is blank or duplicated.

Failing shape: Two global filters both use Name = "Tenant".

Smallest correction: Choose distinct nonblank names on the entity.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L414).

## INQ093

InquiryGlobalFilter ContextKey configuration is invalid. Default: error, enabled.

Trigger: A ContextKey filter cannot be bound for its shape or operation.

Failing shape: An eager method traverses an entity with a runtime ContextKey filter.

Smallest correction: Use non-eager reads that bind the context, or correct the named invalid option. Do not drop tenant isolation to silence the error.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L427).

## INQ094

InquiryIndex references an unmapped property. Default: error, enabled.

Trigger: An index key or Include references an unmapped property.

Failing shape: InquiryIndex("Missing").

Smallest correction: Use mapped property names, preferably nameof; check Include entries too.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L993).

## INQ095

Operation cannot honour a write-enforced InquiryGlobalFilter. Default: error, enabled.

Trigger: A write operation cannot honor an enforced filter.

Failing shape: InquiryUpsert with EnforceOnWrites = true.

Smallest correction: Use supported separate insert/update operations and preserve the enforcement policy.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L442).

## INQ096

Mutation target is invalid. Default: error, enabled.

Trigger: A mutation target is missing, conflicting, or has an unsupported return mode.

Failing shape: A parameterless InquiryDelete without InquiryWhere.

Smallest correction: For all rows use InquiryDeleteAll explicitly; otherwise provide key or predicate targeting with a supported result.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L255).

## INQ097

InquiryWhere groups are unbalanced. Default: error, enabled.

Trigger: Predicate groups are negative, unbalanced, or misnested.

Failing shape: OpenGroups = 1 without a matching CloseGroups.

Smallest correction: Balance each group in declaration order, or use handwritten parameterized SQL when nesting is hard to read.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L999).

## INQ098

Optional InquiryWhere criterion is invalid. Default: error, enabled.

Trigger: An optional criterion is not one nullable scalar parameter.

Failing shape: Optional = true with Compare.In.

Smallest correction: Use a supported nullable scalar criterion; choose a separate query for an absent collection filter.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L1005).

## INQ099

InquirySet expression is invalid. Default: error, enabled.

Trigger: An InquirySet assignment has an invalid target/expression/binding.

Failing shape: A SET expression targets a key or references a missing bound parameter.

Smallest correction: Use a mutable mapped target and the expression/parameter shape described in the diagnostic's reason.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L1011).

## INQ100

Keyset ordering field must be non-nullable. Default: error, enabled.

Trigger: A keyset ordering column is nullable in the database.

Failing shape: InquiryKeysetPage includes a nullable non-key date column.

Smallest correction: Choose non-nullable ordering fields or use offset paging. Null cursor remains the first-page sentinel.

[Source descriptor](https://github.com/IgnyteSoftware/inquiry/blob/main/src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs#L1017).
