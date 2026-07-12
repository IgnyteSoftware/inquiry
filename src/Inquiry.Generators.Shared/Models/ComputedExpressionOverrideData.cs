namespace Inquiry.Generators.Models;

internal sealed record ComputedExpressionOverrideData(
    string ProviderId,
    string Expression,
    LocationData? ProviderIdLocation,
    LocationData? ExpressionLocation);
