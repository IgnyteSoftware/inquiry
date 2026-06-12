using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable model for an <c>[InquiryAdHoc]</c> DTO — a free-standing result type for ad-hoc
/// <c>IInquiry.Query*</c> SQL, tied to no entity or table. Only the materializer-relevant facts are
/// carried: every publicly settable property, in declaration order, read by SELECT-list ordinal.
/// </summary>
internal sealed record AdHocData(
    string FullyQualifiedName,
    string Name,
    string? Namespace,
    EquatableArray<ColumnData> Columns,
    string ClassMaterializerName,
    string StructMaterializerName,
    string ClassMaterializerFullName,
    bool IsMapped,
    EquatableArray<DiagnosticData> Diagnostics);
