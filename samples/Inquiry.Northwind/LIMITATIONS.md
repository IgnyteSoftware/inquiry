# Inquiry.Northwind — exposed limitations

Mapping classic Northwind onto Inquiry surfaced gaps in the current runtime/generator.
This file is the record of what the schema still can't express and why. Fixed items have
been removed.

## Composite primary keys are not supported

`InquirySqlDialect.CreateContext` validates that an entity has **exactly one**
`[InquiryKey]` column. Northwind has three tables whose primary key is a
composite of two columns:

| Table | Composite key |
| --- | --- |
| `Order Details` | (`OrderID`, `ProductID`) |
| `EmployeeTerritories` | (`EmployeeID`, `TerritoryID`) |
| `CustomerCustomerDemo` | (`CustomerID`, `CustomerTypeID`) |

These tables exist in `NorthwindSchema.SqliteDdl` for fidelity to the original
schema, but they have **no entity class and no store**. Consumers that need to
read or write them must do so via raw SQL through `IInquiry.QueryAsync` /
`IInquiry.ExecuteAsync`. The sample's transaction demo uses this pattern to
insert `Order Details` rows alongside an `Orders` insert.
