using Inquiry.Commands;
using Inquiry.Parameters;
using Inquiry.Transactions;
using System.Data;
using System.Text.RegularExpressions;

namespace Inquiry.PostgreSql;

/// <summary>
/// PostgreSQL session-variable helpers for row-level security. Sets a transaction-scoped custom GUC
/// (the <c>SET LOCAL</c> equivalent) that an RLS policy can read back with
/// <c>current_setting('app.tenant_id', true)</c>, so the database enforces the tenant boundary
/// underneath the application's own <c>[InquiryGlobalFilter]</c> predicates.
/// </summary>
/// <remarks>
/// <para>
/// The extensions hang off <see cref="IInquiryTransaction"/> and nothing else, deliberately. A
/// transaction-scoped setting applied outside a transaction does not survive to the next statement:
/// PostgreSQL's <c>SET LOCAL</c> emits <c>WARNING: SET LOCAL can only be used in transaction
/// blocks</c> and has no effect, and <c>set_config(…, is_local => true)</c> under autocommit expires
/// with the implicit single-statement transaction that ran it. Either way the policy then reads an
/// unset parameter and the caller sees zero rows — fail-closed, but silently, and only at the point
/// some unrelated query returns nothing. Requiring a transaction handle turns that into a compile
/// error instead.
/// </para>
/// <para>
/// The handle is only half the story at runtime: generated stores join the transaction through the
/// ambient async-local slot, not through this object. Use the handle on the same async flow that
/// began the transaction — work started on a flow that did not inherit the slot runs outside the
/// transaction, so the setting does not apply to it and its reads come back empty.
/// </para>
/// <para>
/// Writing the RLS policies themselves is out of scope: they belong in your migration alongside
/// <c>ALTER TABLE … ENABLE ROW LEVEL SECURITY</c>. This API is only the session-variable primitive
/// those policies read.
/// </para>
/// </remarks>
public static class PostgreSqlInquiryTransactionExtensions
{
    /// <summary>
    /// Matches a custom GUC name: two or more dot-separated identifiers, e.g. <c>app.tenant_id</c> or
    /// <c>myapp.rls.tenant_id</c>. PostgreSQL requires at least one dot — a bare undotted name is
    /// rejected as an unrecognized configuration parameter — and constraining every component to a
    /// simple identifier means the name never needs quoting or escaping even though
    /// <c>set_config</c> takes it as a value.
    /// </summary>
    /// <remarks>
    /// Anchored with <c>\A</c>/<c>\z</c>, not <c>^</c>/<c>$</c>: in .NET <c>$</c> also matches
    /// immediately before a trailing newline, so <c>^…$</c> would accept <c>"app.tenant_id\n"</c> and
    /// pass it to the server — which rejects it and aborts the caller's transaction, breaking the
    /// promise that an invalid name throws before any statement runs.
    /// </remarks>
    private static readonly Regex SettingNamePattern = new(
        @"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+\z",
        RegexOptions.CultureInvariant);

    // set_config(name, value, is_local => true) is the function form of SET LOCAL, and unlike the
    // statement form it takes both name and value as ordinary parameters. That is the whole reason
    // this is a SELECT rather than a SET: the statement form would require interpolating a caller's
    // value into SQL text.
    private const string SetConfigSql = "SELECT set_config(@name, @value, true)";

    /// <summary>
    /// Sets a transaction-scoped PostgreSQL configuration parameter that RLS policies can read with
    /// <c>current_setting(name, true)</c>.
    /// </summary>
    /// <param name="transaction">The active transaction whose scope the setting is bound to.</param>
    /// <param name="settingName">
    /// The custom GUC name, which must be two dot-separated identifiers (<c>app.tenant_id</c>).
    /// </param>
    /// <param name="value">
    /// The value to bind. Passed as a command parameter, never interpolated, so it is safe for
    /// caller-supplied data.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="settingName"/> is not a valid dotted GUC name.</exception>
    /// <remarks>
    /// <para>
    /// The setting reverts automatically when the transaction commits or rolls back — that is what
    /// the <c>is_local</c> flag means — so a pooled connection never carries it into the next
    /// transaction. Do NOT add a compensating <c>RESET</c> or <c>DISCARD</c>: there is nothing to
    /// clean up, and issuing one would only cost a round trip.
    /// </para>
    /// <para>
    /// A policy written as <c>USING (tenant_id = current_setting('app.tenant_id', true))</c> is
    /// fail-closed when this is never called: the unset GUC reads as NULL, the comparison is NULL,
    /// and no row is visible.
    /// </para>
    /// </remarks>
    public static async Task SetLocalAsync(
        this IInquiryTransaction transaction,
        string settingName,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (transaction is null) throw new ArgumentNullException(nameof(transaction));
        if (value is null) throw new ArgumentNullException(nameof(value));
        ValidateSettingName(settingName);

        // Routed through the transaction's own execute surface rather than its raw Connection so the
        // configured pipeline (interceptors, logging, command timeout) sees this statement like any
        // other, and so it runs on the connection this handle owns.
        await transaction.ExecuteScalarAsync<string>(
            new InquiryCommand(
                SetConfigSql,
                new[]
                {
                    new InquiryParameter("name", settingName, DbType.String),
                    new InquiryParameter("value", value, DbType.String),
                }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets several transaction-scoped configuration parameters, one statement each. Convenience over
    /// repeated <see cref="SetLocalAsync(IInquiryTransaction, string, string, CancellationToken)"/>
    /// calls; the same rules and lifetime apply to every entry.
    /// </summary>
    /// <param name="transaction">The active transaction whose scope the settings are bound to.</param>
    /// <param name="settings">The GUC names and values to set. An empty collection is a no-op.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> or <paramref name="settings"/> is null, or an entry's value is null.</exception>
    /// <exception cref="ArgumentException">An entry's name is not a valid dotted GUC name.</exception>
    /// <remarks>
    /// <para>
    /// EVERY name is validated before the first statement runs, so an invalid entry throws with none
    /// of the batch applied. Without that, a caller who catches <see cref="ArgumentException"/> and
    /// carries on would be running against a nondeterministic subset — dictionary order decides which
    /// settings made it.
    /// </para>
    /// <para>
    /// The statements themselves are not atomic — each entry is its own — so a failure mid-way (a
    /// cancelled token, a dropped connection) can leave earlier entries applied. They are all scoped
    /// to <paramref name="transaction"/>, so nothing outlives it either way.
    /// </para>
    /// </remarks>
    public static async Task SetLocalAsync(
        this IInquiryTransaction transaction,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        if (transaction is null) throw new ArgumentNullException(nameof(transaction));
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        foreach (var setting in settings)
        {
            ValidateSettingName(setting.Key);
            if (setting.Value is null)
            {
                throw new ArgumentNullException(
                    nameof(settings), $"The value for setting '{setting.Key}' is null.");
            }
        }

        foreach (var setting in settings)
        {
            await transaction.SetLocalAsync(setting.Key, setting.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateSettingName(string settingName)
    {
        if (string.IsNullOrWhiteSpace(settingName))
        {
            throw new ArgumentException(
                "Setting name cannot be empty. PostgreSQL custom settings are named '<prefix>.<name>', e.g. \"app.tenant_id\".",
                nameof(settingName));
        }

        if (!SettingNamePattern.IsMatch(settingName))
        {
            throw new ArgumentException(
                $"Setting name '{settingName}' is not a valid PostgreSQL custom configuration parameter. " +
                "Use dot-separated identifiers with at least one dot, e.g. \"app.tenant_id\" or \"myapp.rls.tenant_id\".",
                nameof(settingName));
        }
    }
}
