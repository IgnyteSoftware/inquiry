using System.Data.Common;

namespace Inquiry.Testing;

/// <summary>
/// The ADO.NET rules on which SqlClient and Microsoft.Data.Sqlite differ. The scripted connection
/// applies the provider's rules, so code that the provider rejects fails in a scripted test too.
/// </summary>
internal sealed record ScriptedProviderRules(
    bool CommandNeedsTransaction,
    StringComparison SavepointNames,
    bool ReleaseOfUnknownSavepointFails,
    bool ParameterHasOneCollection,
    bool EnforcesSequentialAccess)
{
    private static readonly ScriptedProviderRules _sqlite = new(
        CommandNeedsTransaction: false,
        SavepointNames: StringComparison.OrdinalIgnoreCase,
        ReleaseOfUnknownSavepointFails: true,
        ParameterHasOneCollection: false,
        EnforcesSequentialAccess: false);

    // SqlClient's rules are the default: they are the stricter set.
    private static readonly ScriptedProviderRules _sqlClient = new(
        CommandNeedsTransaction: true,
        SavepointNames: StringComparison.Ordinal,
        ReleaseOfUnknownSavepointFails: false,
        ParameterHasOneCollection: true,
        EnforcesSequentialAccess: true);

    public static ScriptedProviderRules For(DbProviderFactory factory)
        => factory.GetType().FullName == "Microsoft.Data.Sqlite.SqliteFactory" ? _sqlite : _sqlClient;
}
