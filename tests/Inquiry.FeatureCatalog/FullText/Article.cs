using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog.FullText;

/// <summary>
/// Full-text-search fixture. Linked ONLY into the FTS-capable dialect test projects (PostgreSQL,
/// SQL Server, MySQL, and provisionally Oracle) — never into a SQLite-compiled project, which rejects
/// <c>[InquiryFullTextSearch]</c> with INQ035.
/// </summary>
[InquiryTable("Article")]
public sealed class Article
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    [InquiryColumn("Body")]
    public string Body { get; set; } = string.Empty;
}

public partial class ArticleStore : InquiryStore<Article>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<Article?> InsertAsync(Article article, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<Article>> AllAsync(CancellationToken cancellationToken = default);

    [InquiryFullTextSearch("Title", "Body")]
    public partial Task<IReadOnlyList<Article>> SearchAsync(string term, CancellationToken cancellationToken = default);
}
