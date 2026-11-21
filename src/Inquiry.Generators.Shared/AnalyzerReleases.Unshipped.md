; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
INQ118 | Inquiry | Error | StampModified update also assigns a stamped column
INQ119 | Inquiry | Error | InquiryDateTimeKind cannot apply to the column
INQ120 | Inquiry | Warning | Handwritten SQL result columns do not match the result type
INQ121 | Inquiry | Error | Upsert MatchOn is invalid
INQ122 | Inquiry | Error | Select Exclude names a column the read cannot omit
INQ123 | Inquiry | Error | InquiryQuery method is invalid
