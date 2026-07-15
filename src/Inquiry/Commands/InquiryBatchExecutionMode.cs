using System.ComponentModel;

namespace Inquiry.Commands;

/// <summary>Provider-selected execution strategy for generated batch commands.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum InquiryBatchExecutionMode
{
    /// <summary>Use bounded ADO.NET <see cref="System.Data.Common.DbBatch"/> instances when available.</summary>
    DbBatch = 0,

    /// <summary>Reuse one command and parameter set while updating values for each item.</summary>
    ReusedCommand = 1,

    /// <summary>Use a provider command configured to bind one array per parameter.</summary>
    ArrayBinding = 2,
}
