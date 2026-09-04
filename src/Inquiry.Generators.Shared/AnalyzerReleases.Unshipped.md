; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
INQ001 | Inquiry | Error | Entity must have at least one InquiryKey property
INQ002 | Inquiry | Error | Entity contains duplicate mapped column names
INQ004 | Inquiry | Error | Store class must be partial
INQ005 | Inquiry | Error | Query method return type is not supported
INQ006 | Inquiry | Error | Query method parameter list is invalid
INQ007 | Inquiry | Error | SelectByField references an unmapped property or column
INQ008 | Inquiry | Error | Store entity type is not mapped with InquiryTable
INQ009 | Inquiry | Error | Mapped entity property must have an accessible setter
INQ010 | Inquiry | Error | Query method must be a partial declaration
INQ011 | Inquiry | Error | Composite primary key cannot contain database-generated columns
INQ012 | Inquiry | Error | Eager loading is not supported on a composite-key parent entity
INQ014 | Inquiry | Error | Multiple Inquiry SQL dialects are referenced
INQ016 | Inquiry | Error | Store class cannot be nested inside another type
INQ017 | Inquiry | Error | Store class cannot be abstract
INQ018 | Inquiry | Error | InquiryWhere In operator requires a collection parameter of the column type
INQ019 | Inquiry | Error | InquiryWhere criteria do not match the method parameters
INQ020 | Inquiry | Error | Paged query requires an ORDER BY and matching paging parameters
INQ021 | Inquiry | Error | ORDER BY or keyset references an unmapped property or column
INQ022 | Inquiry | Error | Batch mutation is not supported for optimistic-concurrency entities
INQ023 | Inquiry | Error | Set-based mutation requires at least one InquiryWhere criterion
INQ024 | Inquiry | Error | Projection declares no mapped columns
INQ025 | Inquiry | Error | Query method result type is not the entity or a known projection
INQ026 | Inquiry | Error | Projection targets a different entity than the store
INQ028 | Inquiry | Error | Entity declares more than one InquiryConcurrencyToken column
INQ029 | Inquiry | Error | InquiryConcurrencyToken cannot also be the primary key
INQ030 | Inquiry | Error | Database-generated key must be an integer type
INQ031 | Inquiry | Error | String key column requires a bounded Length for this dialect
INQ032 | Inquiry | Warning | Indexed string column requires a bounded Length for this dialect
INQ033 | Inquiry | Error | InquirySoftDelete column type is not supported
INQ034 | Inquiry | Error | Entity declares more than one InquirySoftDelete column
INQ035 | Inquiry | Error | Full-text search is not supported by the target dialect
INQ036 | Inquiry | Error | InquiryEnumAsString applied to a non-enum property
INQ037 | Inquiry | Error | Converter type is invalid
INQ038 | Inquiry | Error | Converter provider type is not supported
INQ039 | Inquiry | Error | Operation is not supported by the target dialect
INQ040 | Inquiry | Error | InquiryRelation references an unmapped foreign-key property
INQ041 | Inquiry | Error | InquiryRelation child entity has an unsupported composite primary key
INQ042 | Inquiry | Error | OrderBy term has an invalid direction token
INQ043 | Inquiry | Error | Unknown Inquiry SQL dialect
INQ044 | Inquiry | Error | InquiryUpdate SET field is not an updatable column
INQ045 | Inquiry | Error | Ad-hoc DTO declares no mappable properties
INQ046 | Inquiry | Error | Ad-hoc DTO must be constructible
INQ047 | Inquiry | Error | SequentialGuid requires a client-supplied Guid key
INQ048 | Inquiry | Warning | Non-constant SQL passed to InquiryCommand
INQ049 | Inquiry | Error | Auditing timestamp column is invalid
INQ050 | Inquiry | Error | Duplicate auditing timestamp
INQ051 | Inquiry | Error | Stored-procedure scalar output is misconfigured
INQ052 | Inquiry | Error | View-mapped entity is read-only
INQ053 | Inquiry | Error | Operation requires a key the entity does not declare
INQ054 | Inquiry | Error | Cannot derive query fields from the method name
INQ055 | Inquiry | Error | Auditing user column is invalid
INQ056 | Inquiry | Error | Duplicate auditing user column
INQ057 | Inquiry | Error | Server-computed column is misconfigured
INQ058 | Inquiry | Error | InquiryRelation foreign key is on the wrong entity
INQ059 | Inquiry | Error | InquiryGlobalFilter column is invalid
INQ060 | Inquiry | Error | InquiryWhere JSON-path criterion is invalid
INQ061 | Inquiry | Disabled | Foreign-key column has no index
INQ062 | Inquiry | Disabled | Decimal column relies on the default precision and scale
INQ063 | Inquiry | Error | InquiryManyToMany declaration is unusable
INQ064 | Inquiry | Disabled | Filtered column has no index
INQ065 | Inquiry | Error | Column Length Precision or Scale is out of range
INQ066 | Inquiry | Disabled | Nullable column has a default value
INQ067 | Inquiry | Disabled | String column has no explicit length
INQ068 | Inquiry | Error | Database-generated concurrency token is invalid
INQ069 | Inquiry | Error | Provider cannot emit cyclic foreign keys
INQ070 | Inquiry | Error | Duplicate physical schema mapping
INQ071 | Inquiry | Error | Schema primitive is invalid for the provider
INQ072 | Inquiry | Error | Computed expression is invalid for the provider
INQ073 | Inquiry | Error | Schema manifest exceeds metadata transport limit
INQ074 | Inquiry | Error | Generated key schema facets conflict
INQ075 | Inquiry | Error | Schema manifest assembly metadata key is already declared
INQ077 | Inquiry | Error | Oracle computed string column requires a bounded length
INQ078 | Inquiry | Error | Converter model type does not match the property type
INQ079 | Inquiry | Error | Converter type cannot be abstract
INQ080 | Inquiry | Error | Converter type must be closed
INQ081 | Inquiry | Error | Converter type is inaccessible
INQ082 | Inquiry | Error | Converter type needs a public parameterless constructor
INQ083 | Inquiry | Error | InquiryPagedResult cannot be combined with Distinct
INQ084 | Inquiry | Error | Typed foreign key target lacks [InquiryTable]
INQ085 | Inquiry | Error | Typed foreign key target has no [InquiryKey]
INQ086 | Inquiry | Error | Stored-procedure collection parameter TVP binding is invalid
INQ087 | Inquiry | Error | InquiryManyToMany junction or related type is not a mapped entity
INQ088 | Inquiry | Error | InquiryManyToMany names a junction property that is not a mapped column
INQ089 | Inquiry | Error | InquiryManyToMany child foreign keys do not pair with the related entity's key
INQ090 | Inquiry | Error | InquiryManyToMany cannot synthesize an auto-managed junction
INQ091 | Inquiry | Error | InquiryIgnoreFilter cannot bypass the named filter
INQ092 | Inquiry | Error | InquiryGlobalFilter Name is invalid
INQ093 | Inquiry | Error | InquiryGlobalFilter ContextKey configuration is invalid
INQ094 | Inquiry | Error | InquiryIndex references an unmapped property
INQ095 | Inquiry | Error | Operation cannot honour a write-enforced InquiryGlobalFilter
INQ096 | Inquiry | Error | Mutation target is invalid
INQ097 | Inquiry | Error | InquiryWhere groups are unbalanced
INQ098 | Inquiry | Error | Optional InquiryWhere criterion is invalid
INQ099 | Inquiry | Error | InquirySet expression is invalid
