using Inquiry.Generators.Abstractions;
using System.Collections.Immutable;

namespace Inquiry.Generators.Models;

internal sealed record StoreEmissionResult(
    StoreRegistration Registration,
    ImmutableArray<CollectionParameterArtifact> Artifacts);
