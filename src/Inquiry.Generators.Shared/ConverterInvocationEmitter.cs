using Inquiry.Generators.Models;

namespace Inquiry.Generators;

internal static class ConverterInvocationEmitter
{
    public static string ToProvider(ConverterData converter, string value)
        => InvocationTarget(converter) + ".ToProvider(" + value + ")";

    public static string FromProvider(ConverterData converter, string value)
        => InvocationTarget(converter) + ".FromProvider(" + value + ")";

    private static string InvocationTarget(ConverterData converter)
        => converter.RequiresInterfaceDispatch
            ? "global::Inquiry.Entities.InquiryConverterDispatcher<" + converter.ConverterTypeDisplay + ", " +
                converter.ModelTypeDisplay + ", " + converter.ProviderTypeDisplay + ">"
            : "global::Inquiry.Entities.InquiryConverterCache<" + converter.ConverterTypeDisplay + ">.Instance";
}
