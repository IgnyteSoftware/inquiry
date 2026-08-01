namespace Inquiry.Generators.Models;

/// <summary>
/// DI registration facts for one generated store. Produced during the emit stage and consumed by
/// <c>RegistrationEmitter</c>. <see cref="InterfaceFullyQualifiedName"/> is the generated
/// <c>I{StoreName}</c> interface for an <c>[InquiryGenerateInterface]</c> store (registered as a
/// scoped forward to the concrete store), or null when the store did not opt in.
/// </summary>
internal sealed record StoreRegistration(string StoreFullyQualifiedName, string? InterfaceFullyQualifiedName);
