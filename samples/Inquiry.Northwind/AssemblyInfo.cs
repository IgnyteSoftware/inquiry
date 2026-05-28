using Inquiry;

// Inquiry now bakes provider-specific SQL into the generated stores at compile time, so this
// shared model library has to pick one dialect at its compilation. Sqlite matches what the
// benchmark and integration tests run against. The multi-provider sample (which references
// SqlServer/PostgreSql as well) needs a separate refactor to either pick a single provider or
// split Northwind into per-provider variants.
[assembly: InquiryDialect("Sqlite")]
