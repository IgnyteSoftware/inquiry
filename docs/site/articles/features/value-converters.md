# Value converters

A value converter maps a CLR type to a different database primitive. Inquiry calls the converter's `ToProvider` when binding parameters and `FromProvider` when materializing rows. Use it for: domain primitives ("strongly-typed IDs"), enums you'd rather store as strings, or types the provider doesn't natively handle.

## You write

```csharp
using Inquiry.Conversion;

public readonly record struct CustomerID(string Value)
{
    public static implicit operator string(CustomerID id) => id.Value;
}

public sealed class CustomerIDConverter : IInquiryValueConverter<CustomerID, string>
{
    public string ToProvider(CustomerID value) => value.Value;
    public CustomerID FromProvider(string value) => new(value);
}

[InquiryTable("Customers")]
public sealed class Customer
{
    [InquiryKey(Converter = typeof(CustomerIDConverter))]
    public CustomerID CustomerID { get; set; }

    [InquiryColumn]
    public string CompanyName { get; set; } = "";
}
```

## The generator emits

The binder writes through a cached singleton — `InquiryConverterCache<CustomerIDConverter>.Instance.ToProvider(_e.CustomerID)` — and the materializer reads via `InquiryConverterCache<CustomerIDConverter>.Instance.FromProvider(reader.GetString(0))`. The converter is allocated exactly once per converter type (`InquiryConverterCache<T>.Instance = new()`) and reused across every call and row — there is no per-call allocation, whether the converter is a `class` or a `struct` — which is safe because converters are stateless by contract.

`TProvider` must be a supported non-null scalar type. Database nullability belongs to the model property: use a nullable `TModel` property with a non-null `TProvider`. A nullable or otherwise unsupported provider type reports `INQ038`.

Collection predicates and `[InquiryDeleteAll]` accept model values and project each non-null element through the cached converter exactly once before binding. Null collections retain the operation's empty/no-op behavior. Null elements do not invoke `ToProvider`; they bind as NULL and cannot match a non-null key. The projection is deferred and uses a static selector, so it adds no captured closure or intermediate list.

## Enum-as-string

For the common "enum stored as text" case, there's a shortcut — no converter needed:

```csharp
[InquiryColumn]
[InquiryEnumAsString]
public OrderStatus Status { get; set; }
```

The generator emits `Enum.Parse<OrderStatus>(reader.GetString(i))` and `value.ToString()` directly, no converter type to declare.
