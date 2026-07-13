using System;

namespace Inquiry.SqlServer.Tests.Fixtures;

internal static class SqlServerFullTextPolicy
{
    public static bool ShouldSkip(bool isRequired, bool isInstalled)
    {
        if (isInstalled) return false;

        if (isRequired)
        {
            throw new InvalidOperationException(
                "The required SQL Server test image does not provide Full-Text Search " +
                "(FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') != 1).");
        }

        return true;
    }
}
