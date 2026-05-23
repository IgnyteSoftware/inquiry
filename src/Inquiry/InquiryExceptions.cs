namespace Inquiry;

public class InquiryException : Exception
{
    public InquiryException(string message)
        : base(message)
    {
    }

    public InquiryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class InquiryMappingException : InquiryException
{
    public InquiryMappingException(string message)
        : base(message)
    {
    }

    public InquiryMappingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class InquiryProviderException : InquiryException
{
    public InquiryProviderException(string message)
        : base(message)
    {
    }
}

public sealed class InquiryCommandException : InquiryException
{
    public InquiryCommandException(string message)
        : base(message)
    {
    }

    public InquiryCommandException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class InquiryConcurrencyException : InquiryException
{
    public InquiryConcurrencyException(string message)
        : base(message)
    {
    }
}

public sealed class InquiryValidationException : InquiryException
{
    public InquiryValidationException(string message)
        : base(message)
    {
    }
}

public sealed class InquirySourceGenerationException : InquiryException
{
    public InquirySourceGenerationException(string message)
        : base(message)
    {
    }
}
