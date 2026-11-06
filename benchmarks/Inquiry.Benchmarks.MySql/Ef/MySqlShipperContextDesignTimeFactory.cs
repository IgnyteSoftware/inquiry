using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inquiry.Benchmarks.MySql.Ef;

/// <summary>
/// Lets <c>dotnet ef dbcontext optimize</c> build the context that generates <c>Ef/CompiledModels</c>.
/// The tool only reads the model, so it never opens this connection. The server version is explicit,
/// because AutoDetect would connect.
/// </summary>
public sealed class MySqlShipperContextDesignTimeFactory : IDesignTimeDbContextFactory<MySqlShipperContext>
{
    public MySqlShipperContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<MySqlShipperContext>().UseMySql("Server=localhost;Database=northwind", new MySqlServerVersion(new Version(8, 0, 0))).Options);
}
