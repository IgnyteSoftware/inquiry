using System.Data;

namespace Inquiry;

public sealed class InquiryParameter
{
    private InquiryParameter(
        string name,
        object? value,
        ParameterDirection direction,
        DbType? dbType,
        int? size,
        bool? isNullable)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Parameter name cannot be empty.", nameof(name))
            : name;
        Value = value;
        Direction = direction;
        DbType = dbType;
        Size = size;
        IsNullable = isNullable;
    }

    public string Name { get; }

    public object? Value { get; set; }

    public ParameterDirection Direction { get; }

    public DbType? DbType { get; }

    public int? Size { get; }

    public bool? IsNullable { get; }

    public static InquiryParameter Input(string name, object? value, DbType? dbType = null, bool? isNullable = null)
    {
        return new InquiryParameter(name, value, ParameterDirection.Input, dbType, null, isNullable);
    }

    public static InquiryParameter Output(string name, DbType dbType, int? size = null, bool? isNullable = null)
    {
        return new InquiryParameter(name, null, ParameterDirection.Output, dbType, size, isNullable);
    }

    public static InquiryParameter InputOutput(string name, object? value, DbType? dbType = null, int? size = null, bool? isNullable = null)
    {
        return new InquiryParameter(name, value, ParameterDirection.InputOutput, dbType, size, isNullable);
    }

    public static InquiryParameter ReturnValue(string name = "ReturnValue", DbType? dbType = null)
    {
        return new InquiryParameter(name, null, ParameterDirection.ReturnValue, dbType, null, null);
    }
}
