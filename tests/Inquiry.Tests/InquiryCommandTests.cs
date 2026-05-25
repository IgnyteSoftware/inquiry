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
}
