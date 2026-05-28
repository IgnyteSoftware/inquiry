using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Inquiry.Commands;
using Inquiry.Northwind.Models;
using Inquiry.Parameters;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Inquiry.Benchmarks;

/// <summary>
/// Measures the cost of Inquiry's parameter-binding path in isolation — no SQL execution, no
/// reader loop, no connection open. Just the work that happens between "user passes an entity"
/// and "DbCommand has its parameters populated".
/// </summary>
/// <remarks>
/// Two shapes are exercised: a single-parameter bind-by-key call (matches SelectByKey / Delete),
/// and an 11-parameter bind-by-entity call (matches Insert / Update on the Northwind Customer
/// row). Each measurement covers exactly what a generated store does today — build an
/// <c>InquiryParameter[]</c>, wrap it in <c>InquiryCommand</c>, hand off to
/// <c>InquiryParameterBinder.Bind</c>. The DbCommand itself is reused (its Parameters cleared
/// between iterations) so we measure the binder work, not connection / command lifecycle.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ParameterBindingBenchmarks
{
    private SqliteConnection _connection = null!;
    private DbCommand _command = null!;
    private Customer _customer = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _command = _connection.CreateCommand();
        _customer = new Customer
        {
            CustomerID = "ACME1",
            CompanyName = "Acme Research",
            ContactName = "Alice",
            ContactTitle = "Owner",
            Address = "1 Acme Way",
            City = "Springfield",
            Region = "IL",
            PostalCode = "62701",
            Country = "USA",
            Phone = "555-0100",
            Fax = "555-0101",
        };
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _command.Dispose();
        _connection.Dispose();
    }

    [BenchmarkCategory("BindByKey"), Benchmark(Baseline = true)]
    public int BindByKey_Current()
    {
        _command.Parameters.Clear();
        var cmd = new InquiryCommand(
            "SELECT * FROM Customers WHERE CustomerID = @CustomerID",
            new InquiryParameter[] { new InquiryParameter("@CustomerID", _customer.CustomerID) });
        ApplyInquiryCommand(_command, cmd);
        return _command.Parameters.Count;
    }

    [BenchmarkCategory("BindByKey"), Benchmark]
    public int BindByKey_Fast()
    {
        _command.Parameters.Clear();
        _command.CommandText = "DELETE FROM Customers WHERE CustomerID = @CustomerID";
        BindKey(_command, _customer.CustomerID);
        return _command.Parameters.Count;
    }

    // Mirrors what the generator now emits for [InquiryDeleteOneByKey]: a static method
    // (no closure capture) that writes parameters straight into the DbCommand.
    private static void BindKey(System.Data.Common.DbCommand cmd, string key)
    {
        var p0 = cmd.CreateParameter();
        p0.ParameterName = "@CustomerID";
        p0.Value = (object?)key ?? System.DBNull.Value;
        cmd.Parameters.Add(p0);
    }

    [BenchmarkCategory("BindEntity"), Benchmark(Baseline = true)]
    public int BindEntity_Current()
    {
        _command.Parameters.Clear();
        var c = _customer;
        var cmd = new InquiryCommand(
            "INSERT INTO Customers (...) VALUES (...)",
            new InquiryParameter[]
            {
                new InquiryParameter("@CustomerID",   c.CustomerID),
                new InquiryParameter("@CompanyName",  c.CompanyName),
                new InquiryParameter("@ContactName",  c.ContactName),
                new InquiryParameter("@ContactTitle", c.ContactTitle),
                new InquiryParameter("@Address",      c.Address),
                new InquiryParameter("@City",         c.City),
                new InquiryParameter("@Region",       c.Region),
                new InquiryParameter("@PostalCode",   c.PostalCode),
                new InquiryParameter("@Country",      c.Country),
                new InquiryParameter("@Phone",        c.Phone),
                new InquiryParameter("@Fax",          c.Fax),
            });
        ApplyInquiryCommand(_command, cmd);
        return _command.Parameters.Count;
    }

    [BenchmarkCategory("BindEntity"), Benchmark]
    public int BindEntity_Fast()
    {
        _command.Parameters.Clear();
        _command.CommandText = "INSERT INTO Customers (...) VALUES (...)";
        BindCustomer(_command, _customer);
        return _command.Parameters.Count;
    }

    // Mirrors what the generator emits for [InquiryInsert] / [InquiryUpdate] non-returning:
    // static binder, no captured state, parameters written directly to the DbCommand.
    private static void BindCustomer(System.Data.Common.DbCommand cmd, Customer c)
    {
        var p0 = cmd.CreateParameter();  p0.ParameterName = "@CustomerID";   p0.Value = (object?)c.CustomerID   ?? System.DBNull.Value; cmd.Parameters.Add(p0);
        var p1 = cmd.CreateParameter();  p1.ParameterName = "@CompanyName";  p1.Value = (object?)c.CompanyName  ?? System.DBNull.Value; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter();  p2.ParameterName = "@ContactName";  p2.Value = (object?)c.ContactName  ?? System.DBNull.Value; cmd.Parameters.Add(p2);
        var p3 = cmd.CreateParameter();  p3.ParameterName = "@ContactTitle"; p3.Value = (object?)c.ContactTitle ?? System.DBNull.Value; cmd.Parameters.Add(p3);
        var p4 = cmd.CreateParameter();  p4.ParameterName = "@Address";      p4.Value = (object?)c.Address      ?? System.DBNull.Value; cmd.Parameters.Add(p4);
        var p5 = cmd.CreateParameter();  p5.ParameterName = "@City";         p5.Value = (object?)c.City         ?? System.DBNull.Value; cmd.Parameters.Add(p5);
        var p6 = cmd.CreateParameter();  p6.ParameterName = "@Region";       p6.Value = (object?)c.Region       ?? System.DBNull.Value; cmd.Parameters.Add(p6);
        var p7 = cmd.CreateParameter();  p7.ParameterName = "@PostalCode";   p7.Value = (object?)c.PostalCode   ?? System.DBNull.Value; cmd.Parameters.Add(p7);
        var p8 = cmd.CreateParameter();  p8.ParameterName = "@Country";      p8.Value = (object?)c.Country      ?? System.DBNull.Value; cmd.Parameters.Add(p8);
        var p9 = cmd.CreateParameter();  p9.ParameterName = "@Phone";        p9.Value = (object?)c.Phone        ?? System.DBNull.Value; cmd.Parameters.Add(p9);
        var p10 = cmd.CreateParameter(); p10.ParameterName = "@Fax";         p10.Value = (object?)c.Fax         ?? System.DBNull.Value; cmd.Parameters.Add(p10);
    }

    // Reproduces InquiryRequestPipeline.InitializeCommandSync without taking a dependency on the
    // internal InquiryParameterBinder — keeps the benchmark in-tree even if the binder API moves.
    private static void ApplyInquiryCommand(DbCommand target, InquiryCommand source)
    {
        target.CommandText = source.CommandText;
        foreach (var p in source.Parameters)
        {
            var dbParam = target.CreateParameter();
            dbParam.ParameterName = p.Name;
            dbParam.Value = p.Value ?? (object)System.DBNull.Value;
            target.Parameters.Add(dbParam);
        }
    }
}
