using System.Collections.Immutable;
using Inquiry.Generators.Abstractions;

namespace Inquiry.Generators.Models;

internal sealed record StoreEmissionResult(
    StoreRegistration Registration,
    ImmutableArray<CollectionParameterArtifact> Artifacts);
