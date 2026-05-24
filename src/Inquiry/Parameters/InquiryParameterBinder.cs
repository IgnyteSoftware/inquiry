using System.Data.Common;

namespace Inquiry.Parameters;

internal static class InquiryParameterBinder
{
    public static void Bind(DbCommand command, IEnumerable<InquiryParameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = NormalizeName(parameter.Name);
            dbParameter.Value = parameter.Value ?? DBNull.Value;

            if (parameter.DbType is not null)
            {
                dbParameter.DbType = parameter.DbType.Value;
            }

            if (parameter.Direction is not null)
            {
                dbParameter.Direction = parameter.Direction.Value;
            }

            if (parameter.Size is not null)
            {
                dbParameter.Size = parameter.Size.Value;
            }

            if (parameter.Precision is not null)
            {
                dbParameter.Precision = parameter.Precision.Value;
            }

            if (parameter.Scale is not null)
            {
                dbParameter.Scale = parameter.Scale.Value;
            }

            command.Parameters.Add(dbParameter);
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
        }

        return name[0] is '@' or ':' or '$' or '?'
            ? name
            : "@" + name;
    }
}
