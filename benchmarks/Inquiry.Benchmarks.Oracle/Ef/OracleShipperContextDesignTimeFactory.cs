using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inquiry.Benchmarks.Oracle.Ef;

/// <summary>
/// Lets <c>dotnet ef dbcontext optimize</c> build the context that generates <c>Ef/CompiledModels</c>.
/// The tool only reads the model, so it never opens this connection.
/// </summary>
public sealed class OracleShipperContextDesignTimeFactory : IDesignTimeDbContextFactory<OracleShipperContext>
{
    public OracleShipperContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<OracleShipperContext>().UseOracle("Data Source=localhost/XEPDB1").Options);
}
