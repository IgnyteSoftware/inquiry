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
    [InquiryKey]
    [InquiryConverter(typeof(CustomerIDConverter))]
    public CustomerID CustomerID { get; set; }

    [InquiryColumn]
    public string CompanyName { get; set; } = "";
}
```

## The generator emits

The binder calls `new CustomerIDConverter().ToProvider(_e.CustomerID)`; the materializer calls `new CustomerIDConverter().FromProvider(reader.GetString(0))`. Allocation is one converter struct per call (or zero if you make the converter a `readonly struct`).

## Enum-as-string

For the common "enum stored as text" case, there's a shortcut — no converter needed:

```csharp
[InquiryColumn(EnumAsString = true)]
public OrderStatus Status { get; set; }
```

The generator emits `Enum.Parse<OrderStatus>(reader.GetString(i))` and `value.ToString()` directly, no converter type to declare.
