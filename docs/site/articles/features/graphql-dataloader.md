# GraphQL DataLoader recipe (Hot Chocolate)

GraphQL resolvers fan out — a page of 50 orders resolving `order.customer` naively issues 50 point reads. The standard fix is a **DataLoader**, which collects the keys requested during one execution tick and fetches them in a single batch. Inquiry's `Compare.In` batch selects are exactly that fetch: one round trip, compile-time SQL (and on PostgreSQL a constant `= ANY(@ids)` statement — see [Prepared statements](prepared-statements.md)).

## The store method

One predicate method per parent/child association is all Inquiry needs:

```csharp
public partial class CustomerStore : InquiryStore<Customer>
{
    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(Customer.CustomerID), Compare.In)]
    public partial Task<IReadOnlyList<Customer>> ByIdsAsync(
        IReadOnlyList<string> ids, CancellationToken ct = default);
}
```

## The DataLoader

Hot Chocolate's `BatchDataLoader<TKey, TValue>` maps a key batch to a dictionary:

```csharp
public sealed class CustomerByIdDataLoader : BatchDataLoader<string, Customer>
{
    private readonly IServiceProvider _services;

    public CustomerByIdDataLoader(
        IBatchScheduler scheduler, DataLoaderOptions options, IServiceProvider services)
        : base(scheduler, options) => _services = services;

    protected override async Task<IReadOnlyDictionary<string, Customer>> LoadBatchAsync(
        IReadOnlyList<string> keys, CancellationToken ct)
    {
        // Stores are scoped; a DataLoader is request-singleton — resolve per batch.
        await using var scope = _services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<CustomerStore>();

        var customers = await store.ByIdsAsync(keys, ct);
        return customers.ToDictionary(c => c.CustomerID!);
    }
}
```

Resolver usage:

```csharp
public static Task<Customer?> GetCustomerAsync(
    [Parent] Order order, CustomerByIdDataLoader loader, CancellationToken ct)
    => order.CustomerID is null ? Task.FromResult<Customer?>(null) : loader.LoadAsync(order.CustomerID, ct)!;
```

Register with `services.AddGraphQLServer()...AddDataLoader<CustomerByIdDataLoader>()`.

## For one-to-many (child collections)

Use `GroupedDataLoader<TKey, TValue>` over the same `Compare.In` shape, keyed by the foreign key:

```csharp
protected override async Task<ILookup<string, Order>> LoadGroupedBatchAsync(
    IReadOnlyList<string> keys, CancellationToken ct)
{
    await using var scope = _services.CreateAsyncScope();
    var orders = await scope.ServiceProvider.GetRequiredService<OrderStore>().ByCustomerIdsAsync(keys, ct);
    return orders.ToLookup(o => o.CustomerID!);
}
```

## Notes

- **Parameter caps:** very large key batches hit the configured `MaxParametersPerCommand` on expansion dialects; cap the DataLoader batch size (`DataLoaderOptions.MaxBatchSize`) accordingly. On PostgreSQL the array parameter lifts this limit.
- **Eager loading vs DataLoader:** inside one store call, prefer Inquiry's [eager loading](eager-loading.md). DataLoader earns its keep *across* resolvers, where the keys aren't known up front.
- This is a recipe, not a package — Inquiry has no GraphQL dependency.

## See also

- [Eager loading](eager-loading.md) — batch fetching within a single store call.
- [CRUD](crud.md) — predicate selects and `Compare.In`.
