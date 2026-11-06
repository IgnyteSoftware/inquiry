using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inquiry.Benchmarks.MariaDb.Ef;

/// <summary>
/// Lets <c>dotnet ef dbcontext optimize</c> build the context that generates <c>Ef/CompiledModels</c>.
/// The tool only reads the model, so it never opens this connection. The server version is explicit,
/// because AutoDetect would connect.
/// </summary>
public sealed class MariaDbShipperContextDesignTimeFactory : IDesignTimeDbContextFactory<MariaDbShipperContext>
{
    public MariaDbShipperContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<MariaDbShipperContext>().UseMySql("Server=localhost;Database=northwind", new MariaDbServerVersion(new Version(11, 4, 0))).Options);
}
