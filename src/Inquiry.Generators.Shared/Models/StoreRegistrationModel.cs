namespace Inquiry.Generators.Models;

/// <summary>
/// DI registration facts for one generated store. Produced during the emit stage and consumed by
/// <c>RegistrationEmitter</c>.
/// </summary>
internal sealed record StoreRegistration(string StoreFullyQualifiedName);
