using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Tests.UniqueViolations;

[InquiryTable("UvParent")]
public sealed class UvParent
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 20)]
    public string Code { get; set; } = string.Empty;
}

[InquiryTable("UvChild")]
public sealed class UvChild
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn]
    public int ParentId { get; set; }
}

public partial class UvParentStore : InquiryStore<UvParent>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(UvParent parent, CancellationToken cancellationToken = default);
}

public partial class UvChildStore : InquiryStore<UvChild>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(UvChild child, CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared live checks for #612. Each provider's DDL gives UvParent a primary key on Id, a unique
/// index on Code, and gives UvChild a foreign key from ParentId to UvParent.Id. Assert.ThrowsAsync
/// requires the exact exception type, so each scenario also proves that the provider exception
/// reaches the caller unchanged.
/// </summary>
internal static class UniqueViolationScenarios
{
    public static async Task<TException> DuplicatePrimaryKeyAsync<TException>(UvParentStore store)
        where TException : Exception
    {
        await store.InsertAsync(new UvParent { Id = 1, Code = "alpha" });

        var exception = await Assert.ThrowsAsync<TException>(
            () => store.InsertAsync(new UvParent { Id = 1, Code = "beta" }));

        AssertUniqueViolation(exception);
        return exception;
    }

    public static async Task<TException> DuplicateUniqueIndexAsync<TException>(UvParentStore store)
        where TException : Exception
    {
        await store.InsertAsync(new UvParent { Id = 1, Code = "alpha" });

        var exception = await Assert.ThrowsAsync<TException>(
            () => store.InsertAsync(new UvParent { Id = 2, Code = "alpha" }));

        AssertUniqueViolation(exception);
        return exception;
    }

    public static async Task<TException> ForeignKeyViolationAsync<TException>(UvChildStore store)
        where TException : Exception
    {
        var exception = await Assert.ThrowsAsync<TException>(
            () => store.InsertAsync(new UvChild { Id = 1, ParentId = 99 }));

        Assert.False(exception.IsUniqueViolation());
        Assert.False(new AggregateException("failed", exception).IsUniqueViolation());
        Assert.False(new OperationCanceledException("cancelled", exception).IsUniqueViolation());
        return exception;
    }

    public static async Task<TException> NotNullViolationAsync<TException>(UvParentStore store)
        where TException : Exception
    {
        var exception = await Assert.ThrowsAsync<TException>(
            () => store.InsertAsync(new UvParent { Id = 1, Code = null! }));

        Assert.False(exception.IsUniqueViolation());
        return exception;
    }

    private static void AssertUniqueViolation(Exception exception)
    {
        Assert.True(exception.IsUniqueViolation());

        // The error contract allows these two wrappers around a provider exception.
        Assert.True(new OperationCanceledException("cancelled", exception).IsUniqueViolation());
        Assert.True(new AggregateException("failed", new InvalidOperationException("cleanup"), exception).IsUniqueViolation());
        Assert.True(new AggregateException("failed", new OperationCanceledException("cancelled", exception)).IsUniqueViolation());
    }
}
