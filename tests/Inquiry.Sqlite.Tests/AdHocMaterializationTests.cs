using System.Collections.Generic;
using System.Threading.Tasks;
using Inquiry;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// An ad-hoc reporting DTO: no entity, no store — just <c>[InquiryAdHoc]</c>. Properties map to
/// SELECT-list ordinals in declaration order, so the GROUP BY query below must select
/// (Category, SUM, COUNT, MIN) in exactly this order.
/// </summary>
[InquiryAdHoc]
public sealed class CategorySalesRow
{
    public string Category { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    public int SaleCount { get; set; }

    public string? FirstNote { get; set; }
}

/// <summary>Ad-hoc record DTO with init-only properties (the nominal-record form).</summary>
[InquiryAdHoc]
public sealed record SaleNote
{
    public long Id { get; init; }

    public string? Note { get; init; }
}

/// <summary>
/// Ad-hoc DTO materialization end-to-end against SQLite: the generated materializer is resolved
/// from DI by the ad-hoc <c>IInquiry.Query*</c> methods and maps hand-written reporting SQL —
/// aggregates over a table that has no Inquiry entity at all — into the DTO by ordinal.
/// </summary>
public sealed class AdHocMaterializationTests
{
    private const string Ddl = """
        CREATE TABLE Sale (Id INTEGER PRIMARY KEY AUTOINCREMENT, Category TEXT NOT NULL, Amount NUMERIC NOT NULL, Note TEXT NULL);
        INSERT INTO Sale (Category, Amount, Note) VALUES
            ('Coffee', 12.50, 'morning'),
            ('Coffee', 7.25, NULL),
            ('Tea', 4.00, 'green');
        """;

    [Fact]
    public async Task QueryListMapsGroupByReportIntoAdHocDto()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "AdHoc");
        var inquiry = harness.GetRequiredService<IInquiry>();

        IReadOnlyList<CategorySalesRow> rows = await inquiry.QueryListAsync<CategorySalesRow>(
            $"SELECT Category, SUM(Amount), COUNT(*), MIN(Note) FROM Sale GROUP BY Category ORDER BY Category");

        Assert.Equal(2, rows.Count);

        Assert.Equal("Coffee", rows[0].Category);
        Assert.Equal(19.75m, rows[0].TotalAmount);
        Assert.Equal(2, rows[0].SaleCount);
        Assert.Equal("morning", rows[0].FirstNote);

        Assert.Equal("Tea", rows[1].Category);
        Assert.Equal(4.00m, rows[1].TotalAmount);
        Assert.Equal(1, rows[1].SaleCount);
        Assert.Equal("green", rows[1].FirstNote);
    }

    [Fact]
    public async Task QuerySingleOrDefaultBindsParameterAndMapsNullWhenNoRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "AdHoc");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var category = "Tea";
        var tea = await inquiry.QuerySingleOrDefaultAsync<CategorySalesRow>(
            $"SELECT Category, SUM(Amount), COUNT(*), MIN(Note) FROM Sale WHERE Category = {category} GROUP BY Category");

        Assert.NotNull(tea);
        Assert.Equal(4.00m, tea!.TotalAmount);
        Assert.Equal(1, tea.SaleCount);

        category = "Juice";
        var missing = await inquiry.QuerySingleOrDefaultAsync<CategorySalesRow>(
            $"SELECT Category, SUM(Amount), COUNT(*), MIN(Note) FROM Sale WHERE Category = {category} GROUP BY Category");

        Assert.Null(missing);
    }

    [Fact]
    public async Task StreamingQueryMapsRecordDtoWithNullableColumn()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "AdHoc");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var notes = new List<SaleNote>();
        await foreach (var note in inquiry.QueryAsync<SaleNote>($"SELECT Id, Note FROM Sale ORDER BY Id"))
        {
            notes.Add(note);
        }

        Assert.Equal(3, notes.Count);
        Assert.Equal("morning", notes[0].Note);
        Assert.Null(notes[1].Note);
        Assert.Equal("green", notes[2].Note);
    }
}
