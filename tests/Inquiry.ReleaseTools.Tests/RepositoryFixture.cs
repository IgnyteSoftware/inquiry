namespace Inquiry.ReleaseTools.Tests;

internal static class RepositoryFixture
{
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inquiry.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
        }
    }
}
