using System.Collections.Immutable;
using System.Globalization;
using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;

namespace Inquiry.Generators;

/// <summary>
/// Selects and validates each computed column's expression for the active dialect, one entity at a time,
/// so an edit to one entity re-resolves only that entity.
/// </summary>
internal static class ComputedExpressionResolver
{
    public static ResolvedEntity Resolve(EntityData entity, SqlBuilder builder)
    {
        var valid = entity.IsMapped;
        var computedInvalid = false;
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticData>();
        var columns = ImmutableArray.CreateBuilder<ColumnData>(entity.Columns.Count);
        foreach (var column in entity.Columns)
        {
            var fallback = column.ComputedExpression;
            var overrides = column.ComputedExpressionOverrides.AsImmutableArray();
            var selected = fallback;
            var selectedLocation = column.ComputedExpressionLocation ?? column.Location;
            var reasons = new List<(LocationData? Location, string Reason)>();
            foreach (var item in overrides)
            {
                if (!IsValidProviderId(item.ProviderId))
                {
                    reasons.Add((item.ProviderIdLocation, "provider id is invalid; use lowercase ASCII [a-z][a-z0-9.-]{0,63}"));
                }

                if (string.IsNullOrWhiteSpace(item.Expression))
                {
                    reasons.Add((item.ExpressionLocation, "override expression is empty or whitespace"));
                }
            }
            foreach (var duplicate in overrides.Where(item => IsValidProviderId(item.ProviderId)).GroupBy(static item => item.ProviderId, System.StringComparer.Ordinal).Where(static group => group.Count() > 1))
            {
                reasons.Add((duplicate.Skip(1).First().ExpressionLocation, "more than one override declares provider '" + duplicate.Key + "'"));
            }

            if (overrides.Length > 0 && string.IsNullOrWhiteSpace(fallback))
            {
                reasons.Add((overrides[0].ExpressionLocation, "a provider override requires a non-empty InquiryColumn.Computed fallback"));
            }

            var providerOverride = overrides.FirstOrDefault(item => item.ProviderId == builder.ProviderId);
            if (providerOverride is not null)
            { selected = providerOverride.Expression; selectedLocation = providerOverride.ExpressionLocation; }
            if (fallback is not null || overrides.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(selected))
                {
                    if (providerOverride is null)
                    {
                        reasons.Add((selectedLocation, "expression is empty or whitespace"));
                    }
                }
                else
                {
                    var failures = builder.ValidateComputedExpression(selected!);
                    if (failures.Count > 0)
                    {
                        reasons.Add((selectedLocation, string.Join("; ", failures)));
                    }

                    if (builder.RequiresBoundedComputedStrings && column.TypeClass == DbTypeClass.String)
                    {
                        var maxLength = builder.MaxBoundedStringLength(column.IsUnicode);
                        if (column.Length <= 0 || column.Length > maxLength)
                        {
                            diagnostics.Add(Create(
                                InquiryDiagnosticDescriptors.ComputedStringRequiresBoundedLength,
                                selectedLocation,
                                entity.FullyQualifiedName + "." + column.PropertyName,
                                maxLength.ToString(CultureInfo.InvariantCulture)));
                            valid = false;
                            computedInvalid = true;
                            selected = null;
                        }
                    }
                }
            }
            foreach (var reason in reasons)
            {
                diagnostics.Add(Create(InquiryDiagnosticDescriptors.ComputedExpressionInvalid,
                    reason.Location, entity.FullyQualifiedName + "." + column.PropertyName, builder.ProviderId, reason.Reason));
            }

            if (reasons.Count > 0)
            { valid = false; computedInvalid = true; }
            columns.Add(column with { ComputedExpression = reasons.Count == 0 && selected is not null ? builder.RenderComputedExpression(selected) : selected });
        }
        var finalColumns = columns.ToImmutable();
        ColumnData? Find(ColumnData? original) => original is null ? null : finalColumns.FirstOrDefault(column => column.PropertyName == original.PropertyName);
        var resolved = entity with
        {
            Columns = new EquatableArray<ColumnData>(finalColumns),
            Keys = new EquatableArray<ColumnData>(entity.Keys.AsImmutableArray().Select(key => finalColumns.First(column => column.PropertyName == key.PropertyName)).ToImmutableArray()),
            SoftDeleteColumn = Find(entity.SoftDeleteColumn),
            ConcurrencyToken = Find(entity.ConcurrencyToken),
            IsMapped = valid,
        };
        return new ResolvedEntity(resolved, new EquatableArray<DiagnosticData>(diagnostics.ToImmutable()), computedInvalid);
    }

    // DiagnosticData.Create drops locations that have no syntax tree, so build it from the stored location.
    private static DiagnosticData Create(Microsoft.CodeAnalysis.DiagnosticDescriptor descriptor, LocationData? location, params string[] messageArgs)
        => new(descriptor, location, new EquatableArray<string>(messageArgs.ToImmutableArray()));

    private static bool IsValidProviderId(string value)
    {
        if (value.Length is < 1 or > 64 || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!(value[i] is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
