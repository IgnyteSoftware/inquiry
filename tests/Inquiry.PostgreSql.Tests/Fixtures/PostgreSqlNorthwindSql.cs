using System;

namespace Inquiry.PostgreSql.Tests.Fixtures;

internal static class PostgreSqlNorthwindSql
{
    public static FormattableString InsertCustomer(string customerId, string companyName, string country)
        => $"INSERT INTO \"Customers\" (\"CustomerID\", \"CompanyName\", \"Country\") VALUES ({customerId}, {companyName}, {country})";

    public static FormattableString UpdateCustomerCountry(string customerId, string country)
        => $"UPDATE \"Customers\" SET \"Country\" = {country} WHERE \"CustomerID\" = {customerId}";

    public static FormattableString CountCustomer(string customerId)
        => $"SELECT COUNT(*) FROM \"Customers\" WHERE \"CustomerID\" = {customerId}";

    public static FormattableString SelectCustomers()
        => $"SELECT \"CustomerID\", \"CompanyName\", \"ContactName\", \"ContactTitle\", \"Address\", \"City\", \"Region\", \"PostalCode\", \"Country\", \"Phone\", \"Fax\" FROM \"Customers\"";

    public static FormattableString SelectCustomer(string customerId)
        => $"SELECT \"CustomerID\", \"CompanyName\", \"ContactName\", \"ContactTitle\", \"Address\", \"City\", \"Region\", \"PostalCode\", \"Country\", \"Phone\", \"Fax\" FROM \"Customers\" WHERE \"CustomerID\" = {customerId}";
}
