# View-mapped (keyless, read-only) entities

Map a class to a database **view** — or any keyless, read-only projection — with `[InquiryView]`. It's the EF keyless-entity / TypeORM `@ViewEntity` analog: a normal entity for reading, but with no key requirement, no mutations, and no generated DDL.

## You write

```csharp
using Inquiry.Entities;

[InquiryView("v_CategoryTotals")]
public sealed class CategoryTotal
{
    [InquiryColumn("Category")]    public string Category { get; set; } = "";
    [InquiryColumn("SaleCount")]   public int SaleCount { get; set; }
    [InquiryColumn("TotalAmount")] public decimal TotalAmount { get; set; }
}

public partial class CategoryTotalStore : InquiryStore<CategoryTotal>
{
    [InquirySelectAll]
    public partial Task<IReadOnlyList<CategoryTotal>> AllAsync(CancellationToken ct = default);

    [InquirySelectAllByField(nameof(CategoryTotal.Category))]
    public partial Task<IReadOnlyList<CategoryTotal>> ByCategoryAsync(string category, CancellationToken ct = default);
}
```

Columns map exactly as on an `[InquiryTable]` entity, and the store materializes rows the same way — the only difference is what the store is *allowed* to do.

## Semantics

- **Read-only.** A store over a view may declare only SELECT, [aggregate](aggregates.md), and [count](aggregates.md) operations. Any mutation — insert, update, upsert, delete, batch, or [set-based](set-based-mutations.md) — is a build error (`INQ052`).
- **Keyless by default.** No `[InquiryKey]` is required. (You *may* declare one if the view exposes a unique id and you want `[InquirySelectOneByKey]`; without it, filter with `[InquirySelectAllByField]` / [predicates](crud.md).)
- **No DDL.** The schema generator (`InquiryGeneratedSchema.Ddl`) skips views — the view is defined in the database, not created by Inquiry — and no foreign-key constraints are generated for it.
- **`Schema`** qualifies the view name: `[InquiryView("v_Totals", Schema = "reporting")]`.

## When to use which

| Need | Use |
|---|---|
| Read + write a table | [`[InquiryTable]`](crud.md) |
| A column subset of one table, as a DTO | [`[InquiryProjection]`](projections.md) |
| Read a database view / keyless aggregate | **`[InquiryView]`** |
| Map arbitrary hand-written SQL into a POCO | [`[InquiryAdHoc]`](ad-hoc-dtos.md) |

## See also

- [Projections](projections.md) — generated column-subset selects over a *table*.
- [Ad-hoc DTOs](ad-hoc-dtos.md) — map any reporting SQL into a POCO.
- [Aggregates](aggregates.md) — single scalar results.
