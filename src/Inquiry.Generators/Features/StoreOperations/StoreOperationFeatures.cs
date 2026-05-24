using System.Collections.Immutable;

namespace Inquiry.Generators.Features.StoreOperations;

internal static class StoreOperationFeatures
{
    public static readonly ImmutableArray<IStoreOperationFeature> All = ImmutableArray.Create<IStoreOperationFeature>(
        new SelectAllFeature(),
        new SelectOneByKeyFeature(),
        new SelectAllByFieldFeature(),
        new InsertFeature(),
        new UpdateFeature(),
        new DeleteOneByKeyFeature());
}
