namespace Inquiry;

public static class InquiryOpenTelemetryOptionsExtensions
{
    public static InquiryOptions UseOpenTelemetry(this InquiryOptions options)
    {
        return options.UseOpenTelemetry(_ => { });
    }

    public static InquiryOptions UseOpenTelemetry(
        this InquiryOptions options,
        Action<InquiryOpenTelemetryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);

        var telemetryOptions = new InquiryOpenTelemetryOptions();
        configure(telemetryOptions);
        options.UseMiddleware(new OpenTelemetryInquiryMiddleware(telemetryOptions));
        return options;
    }
}
