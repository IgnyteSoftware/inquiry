using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inquiry.Benchmarks.PostgreSql.Ef;

/// <summary>
/// Lets <c>dotnet ef dbcontext optimize</c> build the context that generates <c>Ef/CompiledModels</c>.
/// The tool only reads the model, so it never opens this connection.
/// </summary>
public sealed class PgShipperContextDesignTimeFactory : IDesignTimeDbContextFactory<PgShipperContext>
{
    public PgShipperContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<PgShipperContext>().UseNpgsql("Host=localhost;Database=northwind").Options);
}
