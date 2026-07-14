using System.Text.Json;

namespace Inquiry.ReleaseTools;

public static class ReleaseTool
{
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0)
            {
                return Task.FromResult(Fail(error, "Usage: verify-manifest <repository-root> <manifest> | verify-ci <repository-root> <contract> <workflow> | verify-bundle <repository-root> <manifest> <bundle-directory> <commit> [tag]"));
            }

            switch (args[0])
            {
                case "verify-manifest" when args.Length == 3:
                    PackageVerifier.VerifyManifest(args[1], args[2]);
                    output.WriteLine("Release manifest is valid.");
                    return Task.FromResult(0);
                case "verify-ci" when args.Length == 4:
                    CiContractVerifier.Verify(args[1], args[2], args[3]);
                    output.WriteLine("Required CI contract is valid.");
                    return Task.FromResult(0);
                case "verify-bundle" when args.Length is 5 or 6:
                    PackageVerifier.VerifyBundle(args[1], args[2], args[3], args[4], args.Length == 6 ? args[5] : null);
                    output.WriteLine("Release bundle is valid.");
                    return Task.FromResult(0);
                default:
                    return Task.FromResult(Fail(error, "Invalid command or arguments."));
            }
        }
        catch (ReleaseVerificationException exception)
        {
            return Task.FromResult(Fail(error, exception.Message));
        }
    }

    internal static ReleaseManifest ReadManifest(string manifestPath)
    {
        try
        {
            using var stream = File.OpenRead(manifestPath);
            return JsonSerializer.Deserialize(stream, ReleaseJsonContext.Default.ReleaseManifest)
                ?? throw new ReleaseVerificationException("The release manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new ReleaseVerificationException($"The release manifest is invalid JSON: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ReleaseVerificationException($"Could not read release manifest '{manifestPath}': {exception.Message}");
        }
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine($"release-verification-error: {message}");
        return 1;
    }
}

public sealed class ReleaseVerificationException(string message) : Exception(message);
