namespace Inquiry.Generators.Models;

/// <summary>One unresolved <c>[InquirySet]</c> assignment captured during store discovery.</summary>
internal sealed record SetExpressionData(string Field, string Expression);
