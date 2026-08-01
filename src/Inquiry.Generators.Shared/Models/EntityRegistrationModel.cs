namespace Inquiry.Generators.Models;

/// <summary>
/// DI registration facts for one entity's materializer. Produced during the emit stage (so it does
/// not need to be cacheable) and consumed by <c>RegistrationEmitter</c>.
/// </summary>
internal sealed record EntityRegistration(string EntityFullyQualifiedName, string MaterializerFullName);
