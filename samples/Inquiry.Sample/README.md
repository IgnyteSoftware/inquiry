# Inquiry.Sample

This sample shows the current end-user flow for Inquiry with SQLite and dependency injection.

It demonstrates:

- Entity mapping with `[InquiryTable]`, `[InquiryKey]`, and `[InquiryColumn]`.
- User-defined abstract store methods.
- Custom store queries through `_inquiry.QueryAsync<T>()`.
- Generated store registration through `AddInquiryStores()`.
- Provider registration through `AddInquirySqlite(connectionString)`.
- Insert, select by key, select by field, update, and delete.

Run it from the repository root:

```powershell
dotnet run --project samples\Inquiry.Sample\Inquiry.Sample.csproj
```

The sample creates a local SQLite database in the app output directory.
