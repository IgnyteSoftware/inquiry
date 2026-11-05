using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inquiry.Benchmarks.Ef;

/// <summary>
/// Lets <c>dotnet ef dbcontext optimize</c> build the context that generates <c>Ef/CompiledModels</c>.
/// The tool only reads the model, so it never opens this connection.
/// </summary>
public sealed class NorthwindDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NorthwindDbContext>
{
    public NorthwindDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<NorthwindDbContext>().UseSqlite("Data Source=:memory:").Options);
}
