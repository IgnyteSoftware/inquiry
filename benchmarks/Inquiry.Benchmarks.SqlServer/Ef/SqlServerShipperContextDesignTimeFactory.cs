using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inquiry.Benchmarks.SqlServer.Ef;

/// <summary>
/// Lets <c>dotnet ef dbcontext optimize</c> build the context that generates <c>Ef/CompiledModels</c>.
/// The tool only reads the model, so it never opens this connection.
/// </summary>
public sealed class SqlServerShipperContextDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerShipperContext>
{
    public SqlServerShipperContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<SqlServerShipperContext>().UseSqlServer("Server=localhost;Database=northwind").Options);
}
