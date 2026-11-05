using DbUp;
using DbUp.Engine;

namespace Catalog.Deployment;

public static class Migrations
{
    // Script names are DbUp's journal keys. Never rename or edit a script after any database has run it.
    public static readonly SqlScript Initial = Load("0001-initial-schema.sql");
    public static readonly SqlScript WidenCode = Load("0002-widen-code.sql");
    public static readonly SqlScript RetireV1Type = Load("0003-retire-v1-type.sql");

    // Each release ships the scripts it knows about: release 2 is [Initial, WidenCode].
    public static UpgradeEngine Upgrader(string connectionString, params SqlScript[] scripts) =>
        DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScripts(scripts)
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

    private static SqlScript Load(string name)
    {
        using var stream = typeof(Migrations).Assembly.GetManifestResourceStream("Scripts." + name)
            ?? throw new InvalidOperationException($"Embedded script {name} is missing.");
        using var reader = new StreamReader(stream);
        return new SqlScript(name, reader.ReadToEnd());
    }
}
