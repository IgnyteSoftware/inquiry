using Inquiry.Generators.Models;

namespace Inquiry.Generators;

internal static class ConverterInvocationEmitter
{
    public static string ToProvider(ConverterData converter, string value)
        => InvocationTarget(converter) + ".ToProvider(" + value + ContextArgument(converter) + ")";

    public static string FromProvider(ConverterData converter, string value)
        => InvocationTarget(converter) + ".FromProvider(" + value + ContextArgument(converter) + ")";

    private static string ContextArgument(ConverterData converter)
        => converter.JsonContextDisplay is null ? string.Empty : ", " + converter.JsonContextDisplay + ".Default";

    private static string InvocationTarget(ConverterData converter)
    {
        // The JSON context converter is a static class, so no cached instance is needed.
        if (converter.JsonContextDisplay is not null)
        {
            return converter.ConverterTypeDisplay;
        }

        return converter.RequiresInterfaceDispatch
            ? "global::Inquiry.Entities.InquiryConverterDispatcher<" + converter.ConverterTypeDisplay + ", " +
                converter.ModelTypeDisplay + ", " + converter.ProviderTypeDisplay + ">"
            : "global::Inquiry.Entities.InquiryConverterCache<" + converter.ConverterTypeDisplay + ">.Instance";
    }
}
