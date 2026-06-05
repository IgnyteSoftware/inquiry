using Inquiry.Commands;
using Inquiry.Parameters;

namespace Inquiry.Tests;

public sealed class InquiryCommandTests
{
    [Fact]
    public void ConstructorUsesParameterArrayWithoutCopying()
    {
        var parameters = new[]
        {
            new InquiryParameter("Id", 1),
        };

        var command = new InquiryCommand("SELECT 1 WHERE Id = @Id", parameters);

        Assert.Same(parameters, command.Parameters);
    }

    [Fact]
    public void ConstructorCopiesNonArrayParameterLists()
    {
        var parameters = new List<InquiryParameter>
        {
            new("Id", 1),
        };

        var command = new InquiryCommand("SELECT 1 WHERE Id = @Id", parameters);

        parameters[0] = new InquiryParameter("Id", 2);
        Assert.Equal(1, command.Parameters[0].Value);
    }

    [Fact]
    public void SqlFactoryParameterizesFormattableString()
    {
        var id = 42;
        var name = "Ada";

        var command = InquirySql.Sql($"SELECT * FROM People WHERE Id = {id} AND Name = {name}");

        Assert.Equal("SELECT * FROM People WHERE Id = @p0 AND Name = @p1", command.CommandText);
        Assert.Equal(new InquiryParameter("@p0", 42), command.Parameters[0]);
        Assert.Equal(new InquiryParameter("@p1", "Ada"), command.Parameters[1]);
    }
}
