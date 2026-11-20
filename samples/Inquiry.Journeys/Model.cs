using System.Text.Json.Serialization;
using Inquiry.Entities;

[assembly: InquiryJsonContext(typeof(Inquiry.Journeys.JourneyJsonContext))]

namespace Inquiry.Journeys;

[InquiryTable]
public sealed class Customer
{
    [InquiryKey(IsGenerated = true)]
    public long? Id { get; set; }

    [InquiryColumn(Length = 100)]
    public string Name { get; set; } = "";

    [InquiryColumn]
    [InquiryJson]
    public ShippingPreferences? Preferences { get; set; }
}

public sealed class ShippingPreferences
{
    public string Carrier { get; set; } = "";

    public List<string> DeliveryWindows { get; set; } = [];

    public bool GiftWrap { get; set; }
}

// Source-generated metadata lets the JSON column serialize without reflection under NativeAOT.
[JsonSerializable(typeof(ShippingPreferences))]
internal sealed partial class JourneyJsonContext : JsonSerializerContext;

[InquiryTable]
public sealed class SalesOrder
{
    [InquiryKey(IsGenerated = true)]
    public long? Id { get; set; }

    [InquiryForeignKey(typeof(Customer), IsIndexed = true)]
    public long CustomerId { get; set; }

    [InquiryColumn(Length = 20)]
    public string Reference { get; set; } = "";

    [InquiryRelation(nameof(CustomerId))]
    public Customer? Customer { get; set; }

    [InquiryRelation(nameof(OrderLine.OrderId))]
    public List<OrderLine> Lines { get; set; } = [];
}

[InquiryTable]
[InquiryCheck("Quantity > 0")]
public sealed class OrderLine
{
    [InquiryKey(IsGenerated = true)]
    public long? Id { get; set; }

    [InquiryForeignKey(typeof(SalesOrder), IsIndexed = true)]
    public long OrderId { get; set; }

    [InquiryColumn(Length = 40)]
    public string Sku { get; set; } = "";

    [InquiryColumn]
    public int Quantity { get; set; }

    [InquiryColumn]
    public long UnitPriceCents { get; set; }
}

[InquiryTable]
public sealed class User
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 50)]
    public string Name { get; set; } = "";

    [InquiryManyToMany(typeof(UserRole), nameof(UserRole.UserId), nameof(UserRole.RoleId))]
    public List<Role> Roles { get; set; } = [];
}

[InquiryTable]
public sealed class Role
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 50)]
    public string Name { get; set; } = "";
}

[InquiryTable]
public sealed class UserRole
{
    [InquiryKey]
    [InquiryForeignKey(typeof(User))]
    public int UserId { get; set; }

    [InquiryKey]
    [InquiryForeignKey(typeof(Role))]
    public int RoleId { get; set; }
}

[InquiryTable]
public sealed class Article
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 200)]
    public string Title { get; set; } = "";

    [InquiryConcurrencyToken]
    public int Version { get; set; }
}

[InquiryTable]
public sealed class Document
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 200)]
    public string Title { get; set; } = "";

    [InquiryColumn]
    [InquiryGlobalFilter(ContextKey = "TenantId", EnforceOnWrites = true)]
    public int TenantId { get; set; }
}

// Maps a table that handwritten DDL created. Its physical names differ from the CLR names.
[InquiryTable("legacy_supplier", GenerateDdl = false)]
public sealed class Supplier
{
    [InquiryKey("supplier_no", IsGenerated = true)]
    public int? Id { get; set; }

    [InquiryColumn("company_nm")]
    public string Name { get; set; } = "";

    [InquiryColumn("active_flag")]
    public bool IsActive { get; set; }
}

// A report row for handwritten SQL. Each property reads the result column with the same name.
[InquiryAdHoc]
public sealed class CustomerSales
{
    public string Customer { get; set; } = "";

    public long OrderCount { get; set; }

    public long Units { get; set; }

    public long TotalCents { get; set; }
}
