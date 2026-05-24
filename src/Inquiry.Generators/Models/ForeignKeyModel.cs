namespace Inquiry.Generators.Models;

internal sealed class ForeignKeyModel
{
    public ForeignKeyModel(string referencedTable, string referencedColumn)
    {
        ReferencedTable = referencedTable;
        ReferencedColumn = referencedColumn;
    }

    public string ReferencedTable { get; }

    public string ReferencedColumn { get; }
}
