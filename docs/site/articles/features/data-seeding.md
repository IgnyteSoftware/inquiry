# Data seeding

A thin, first-class hook for first-run data: implement `IInquiryDataSeeder`, register it with `AddInquirySeeder<T>()`, and run all seeders once at startup with `SeedInquiryAsync()`. The EF `UseSeeding` / `prisma db seed` analog — without Inquiry ever deciding *when* seeding happens.

## You write

```csharp
using Inquiry.Seeding;

public sealed class CatalogSeeder : IInquiryDataSeeder
{
    private readonly ProductStore _products;
    public CatalogSeeder(ProductStore products) => _products = products;   // scoped DI — stores inject directly

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if ((await _products.CountAsync(ct)) > 0) return;   // the conventional idempotency guard

        await _products.InsertAsync(new Product { ProductName = "Chai", UnitPrice = 18m }, ct);
        await _products.InsertAsync(new Product { ProductName = "Chang", UnitPrice = 19m }, ct);
    }
}
```

```csharp
builder.Services.AddInquirySeeder<CatalogSeeder>();      // repeatable; duplicates are no-ops

var app = builder.Build();
await app.Services.SeedInquiryAsync();                   // after the schema exists
```

## Semantics

- **Registration order is execution order.** Multiple seeders run sequentially inside **one DI scope**, so they share scope-local services and each can build on the previous one's data.
- **Scoped resolution.** Seeders constructor-inject generated stores or `IInquiry` like any scoped component.
- **Explicit invocation.** Inquiry never runs seeders implicitly — call `SeedInquiryAsync()` where your host wants it (typically right after applying `InquiryGeneratedSchema.Ddl` or your [migrations](migrations.md)).
- **Idempotency is yours.** The conventional guard is "return early when rows exist". For whole-environment resets in tests, prefer the `Inquiry.Testing` Respawn wrapper.
- Registering the same seeder type twice is a no-op (`TryAddEnumerable` semantics), so library-style registration helpers can call `AddInquirySeeder` safely.

The bundled Blazor sample's `DataSeeder` (13 Northwind tables) runs through exactly this hook — see `samples/Inquiry.Sample/Program.cs`.

## See also

- [Migrations recipe](migrations.md) — getting the schema in place before seeding.
- [Testing](testing.md) — per-test data setup and database resets.
