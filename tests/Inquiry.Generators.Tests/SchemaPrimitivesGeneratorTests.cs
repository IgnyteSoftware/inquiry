using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Inquiry.Generators.Models;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators;
using Inquiry.Generators.Abstractions;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string PrimitiveSource = """
        using Inquiry.Entities;
        namespace Demo;

        [InquiryTable("Parent")]
        public sealed class Parent { [InquiryKey] public long Id { get; set; } }

        [InquiryTable("Primitive")]
        [InquiryIndex(nameof(Code), nameof(Kind), Name = "IX_Primitive_CodeKind", IsUnique = true, Include = new[] { nameof(Payload) })]
        [InquiryCheck("[Kind] > 0", Name = "CK_Primitive_Kind")]
        public sealed class Primitive
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn(Length = 32)] public string Code { get; set; } = "";
            [InquiryColumn] public int Kind { get; set; }
            [InquiryColumn] public string? Payload { get; set; }
            [InquiryForeignKey("ParentId", "Parent", "Id", ConstraintName = "FK_Primitive_Parent", OnDelete = InquiryReferentialAction.Cascade, OnUpdate = InquiryReferentialAction.SetNull)]
            public long? ParentId { get; set; }
        }
        """;

    [Fact]
    public void SqlServerRendersCompositeUniqueIncludeCheckAndNamedActions()
    {
        var result = RunGenerator(PrimitiveSource, dialect: "SqlServer");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);
        Assert.Contains("CONSTRAINT [CK_Primitive_Kind] CHECK ([Kind] > 0)", ddl);
        Assert.Contains("CONSTRAINT [FK_Primitive_Parent] FOREIGN KEY ([ParentId]) REFERENCES [Parent]([Id]) ON DELETE CASCADE ON UPDATE SET NULL", ddl);
        Assert.Contains("CREATE UNIQUE INDEX [IX_Primitive_CodeKind] ON [Primitive] ([Code], [Kind]) INCLUDE ([Payload])", ddl);
    }

    [Fact]
    public void PostgreSqlRendersCoveringIndex()
    {
        var result = RunGenerator(PrimitiveSource, dialect: "PostgreSql");
        AssertNoErrors(result);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Primitive_CodeKind\" ON \"Primitive\" (\"Code\", \"Kind\") INCLUDE (\"Payload\")", ExtractSchemaDdl(result));
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void UnsupportedIncludeReportsInq071(string dialect)
    {
        var result = RunGenerator(PrimitiveSource, dialect: dialect);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ071" && d.GetMessage().Contains("INCLUDE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void UnsupportedSetDefaultReportsInq071(string dialect)
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("P")] public sealed class P { [InquiryKey] public int Id { get; set; } }
            [InquiryTable("C")] public sealed class C
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryForeignKey("PId", "P", "Id", DefaultExpression = "0", OnDelete = InquiryReferentialAction.SetDefault)] public int PId { get; set; }
            }
            """;
        var result = RunGenerator(source, dialect: dialect);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ071" && d.GetMessage().Contains("ON DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public void SetDefaultRequiresDefaultExpressionExactly()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("P")] public sealed class P { [InquiryKey] public int Id { get; set; } }
            [InquiryTable("C")] public sealed class C
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryForeignKey("PId", "P", "Id", UseDatabaseDefault = true, OnDelete = InquiryReferentialAction.SetDefault)] public int PId { get; set; }
            }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ071" && d.GetMessage().Contains("DefaultExpression", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyIndexAndUnnamedForeignKeyRemainStable()
    {
        var result = RunGenerator(AuthorBookSource, dialect: "SqlServer");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);
        Assert.Contains("FOREIGN KEY ([AuthorId]) REFERENCES [Author]([Id])", ddl);
        Assert.DoesNotContain("CONSTRAINT [FK_Book", ddl);
    }

    [Fact]
    public void LegacyMultipleIndexesPreserveColumnOrder()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("T")] public sealed class T
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(IsIndexed = true)] public int Z { get; set; }
                [InquiryColumn(IsIndexed = true)] public int A { get; set; }
            }
            """;
        var ddl = ExtractSchemaDdl(RunGenerator(source, dialect: "SqlServer"));
        Assert.True(ddl.IndexOf("[IX_T_Z]", StringComparison.Ordinal) < ddl.IndexOf("[IX_T_A]", StringComparison.Ordinal));
    }

    [Fact]
    public void ForeignKeyExplicitNameScopeIsProviderCorrect()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("P")] public sealed class P { [InquiryKey] public int Id { get; set; } }
            [InquiryTable("A")] public sealed class A { [InquiryKey] public int Id { get; set; } [InquiryForeignKey("PId", "P", "Id", ConstraintName = "FK_Same")] public int PId { get; set; } }
            [InquiryTable("B")] public sealed class B { [InquiryKey] public int Id { get; set; } [InquiryForeignKey("PId", "P", "Id", ConstraintName = "FK_Same")] public int PId { get; set; } }
            """;
        var pg = RunGenerator(source, dialect: "PostgreSql");
        Assert.DoesNotContain(pg.RunResult.Diagnostics, d => d.Id == "INQ070");
        var sqlServer = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(sqlServer.RunResult.Diagnostics, d => d.Id == "INQ070");
    }

    [Fact]
    public void SafeFallbackDiagnosesCheckIncludeAndAction()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("P")] public sealed class P { [InquiryKey] public int Id { get; set; } }
            [InquiryTable("C"), InquiryIndex(nameof(A), Include = new[] { nameof(B) }), InquiryCheck("A > 0")]
            public sealed class C
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn] public int A { get; set; }
                [InquiryColumn] public int B { get; set; }
                [InquiryForeignKey("PId", "P", "Id", OnDelete = InquiryReferentialAction.Cascade)] public int PId { get; set; }
            }
            """;
        var result = RunGenerator(source, dialect: "Fallback", includeFallbackGenerator: true);
        Assert.Equal(3, result.RunResult.Diagnostics.Count(d => d.Id == "INQ071"));
        var ddl = ExtractSchemaDdl(result);
        Assert.DoesNotContain("CHECK (", ddl);
        Assert.DoesNotContain("INCLUDE (", ddl);
        Assert.DoesNotContain("FOREIGN KEY (\"PId\")", ddl);
    }

    [Fact]
    public void InvalidAndDuplicateClassDeclarationsAreExcluded()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("T")]
            [InquiryIndex(nameof(A), nameof(A))]
            [InquiryIndex(nameof(A), Include = new[] { nameof(A) })]
            [InquiryIndex("Missing")]
            [InquiryCheck(" ")]
            [InquiryCheck("A > 0", Name = "bad\u0001name")]
            public sealed class T { [InquiryKey] public int Id { get; set; } [InquiryColumn] public int A { get; set; } }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        // The two malformed indexes and the two malformed checks are rejected by the schema
        // primitive validator; the index over an unmapped property is caught earlier, during entity
        // discovery, which names the offending property instead of reporting a generic blank key.
        Assert.True(result.RunResult.Diagnostics.Count(d => d.Id == "INQ071") >= 4);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ094" && d.GetMessage().Contains("Missing", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassIndexDuplicatingLegacyFlagIsRejected()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("T"), InquiryIndex(nameof(A))]
            public sealed class T { [InquiryKey] public int Id { get; set; } [InquiryColumn(IsIndexed = true)] public int A { get; set; } }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ071" && d.GetMessage().Contains("duplicate index", StringComparison.Ordinal));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(ExtractSchemaDdl(result), "CREATE INDEX").Cast<System.Text.RegularExpressions.Match>());
    }

    [Theory]
    [InlineData("Sqlite", "Restrict")]
    [InlineData("Sqlite", "Cascade")]
    [InlineData("Sqlite", "SetNull")]
    [InlineData("Sqlite", "SetDefault")]
    [InlineData("SqlServer", "Cascade")]
    [InlineData("SqlServer", "SetNull")]
    [InlineData("SqlServer", "SetDefault")]
    [InlineData("PostgreSql", "Restrict")]
    [InlineData("PostgreSql", "Cascade")]
    [InlineData("PostgreSql", "SetNull")]
    [InlineData("PostgreSql", "SetDefault")]
    [InlineData("MySql", "Restrict")]
    [InlineData("MySql", "Cascade")]
    [InlineData("MySql", "SetNull")]
    [InlineData("MariaDb", "Restrict")]
    [InlineData("MariaDb", "Cascade")]
    [InlineData("MariaDb", "SetNull")]
    [InlineData("Oracle", "Cascade")]
    [InlineData("Oracle", "SetNull")]
    public void SupportedDeleteActionsRender(string dialect, string action)
    {
        var result = RunGenerator(ActionSource(action, onUpdate: false), dialect: dialect);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ071");
        Assert.Contains("ON DELETE " + ActionToken(action), ExtractSchemaDdl(result));
    }

    [Theory]
    [InlineData("Sqlite", "Restrict")]
    [InlineData("Sqlite", "Cascade")]
    [InlineData("Sqlite", "SetNull")]
    [InlineData("Sqlite", "SetDefault")]
    [InlineData("SqlServer", "Cascade")]
    [InlineData("SqlServer", "SetNull")]
    [InlineData("SqlServer", "SetDefault")]
    [InlineData("PostgreSql", "Restrict")]
    [InlineData("PostgreSql", "Cascade")]
    [InlineData("PostgreSql", "SetNull")]
    [InlineData("PostgreSql", "SetDefault")]
    [InlineData("MySql", "Restrict")]
    [InlineData("MySql", "Cascade")]
    [InlineData("MySql", "SetNull")]
    [InlineData("MariaDb", "Restrict")]
    [InlineData("MariaDb", "Cascade")]
    [InlineData("MariaDb", "SetNull")]
    public void SupportedUpdateActionsRender(string dialect, string action)
    {
        var result = RunGenerator(ActionSource(action, onUpdate: true), dialect: dialect);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ071");
        Assert.Contains("ON UPDATE " + ActionToken(action), ExtractSchemaDdl(result));
    }

    [Theory]
    [InlineData("SqlServer", "Restrict", false)]
    [InlineData("SqlServer", "Restrict", true)]
    [InlineData("MySql", "SetDefault", false)]
    [InlineData("MySql", "SetDefault", true)]
    [InlineData("MariaDb", "SetDefault", false)]
    [InlineData("MariaDb", "SetDefault", true)]
    [InlineData("Oracle", "Restrict", false)]
    [InlineData("Oracle", "SetDefault", false)]
    [InlineData("Oracle", "Restrict", true)]
    [InlineData("Oracle", "Cascade", true)]
    [InlineData("Oracle", "SetNull", true)]
    [InlineData("Oracle", "SetDefault", true)]
    public void UnsupportedActionsReport(string dialect, string action, bool onUpdate)
    {
        var result = RunGenerator(ActionSource(action, onUpdate), dialect: dialect);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ071" && d.GetMessage().Contains(onUpdate ? "ON UPDATE" : "ON DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public void SetNullRequiresNullableProperty()
    {
        var result = RunGenerator(ActionSource("SetNull", onUpdate: false).Replace("int? PId", "int PId"), dialect: "PostgreSql");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ071" && d.GetMessage().Contains("nullable", StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateForeignKeysFalseSuppressesActionWithoutDiagnostic()
    {
        var source = ActionSource("Cascade", onUpdate: false).Replace("[InquiryTable(\"C\")]", "[InquiryTable(\"C\", GenerateForeignKeys = false)]");
        var result = RunGenerator(source, dialect: "Fallback", includeFallbackGenerator: true);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ071");
        Assert.DoesNotContain("FOREIGN KEY", ExtractSchemaDdl(result));
    }

    [Fact]
    public void CyclicExplicitNameAndActionsUseDeferredMetadataAndSql()
    {
        var source = CyclicSchemaSource.Replace("[InquiryForeignKey(\"BId\", \"CycleB\", \"Id\")]", "[InquiryForeignKey(\"BId\", \"CycleB\", \"Id\", ConstraintName = \"FK_Custom_AB\", OnDelete = InquiryReferentialAction.Cascade)]");
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        Assert.Contains("ADD CONSTRAINT [FK_Custom_AB] FOREIGN KEY ([BId]) REFERENCES [CycleB]([Id]) ON DELETE CASCADE", ExtractSchemaDdl(result));
    }

    [Fact]
    public void NormalizedMetadataDistinguishesRequestedGeneratedAndEmittedNames()
    {
        var legacy = new IndexData(null, "T", default, default, false, null, null) { EmittedName = "IX_T_A", Origin = IndexOrigin.ColumnFlag };
        Assert.Null(legacy.RequestedName);
        Assert.Equal("IX_T_A", legacy.EmittedName);

        var fk = new ForeignKeyConstraintData(null, "A", "BId", null, "B", "Id", null, "canonical", "FK_generated")
        { LocalProperty = "ParentId", GeneratedNameCandidate = "FK_generated" };
        var inline = SchemaEmitter.ApplyForeignKeyEmissionMetadata(fk, false, CyclicForeignKeyStrategy.AlterTable);
        Assert.Equal(ForeignKeyEmissionMode.Inline, inline.EmissionMode);
        Assert.Null(inline.EmittedName);
        var deferred = SchemaEmitter.ApplyForeignKeyEmissionMetadata(fk, true, CyclicForeignKeyStrategy.AlterTable);
        Assert.Equal(ForeignKeyEmissionMode.Deferred, deferred.EmissionMode);
        Assert.Equal("FK_generated", deferred.EmittedName);
        var suppressed = SchemaEmitter.ApplyForeignKeyEmissionMetadata(fk, true, CyclicForeignKeyStrategy.ReportDiagnostic);
        Assert.Equal(ForeignKeyEmissionMode.Suppressed, suppressed.EmissionMode);
        Assert.Null(suppressed.EmittedName);
    }

    [Fact]
    public void ReorderedClassPrimitiveDeclarationsProduceStableNamesAndRelativeDdl()
    {
        const string prelude = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("T")]
            """;
        const string first = """
            [InquiryIndex(nameof(T.A), nameof(T.B))]
            [InquiryIndex(nameof(T.B), IsUnique = true)]
            [InquiryCheck("A > 0")]
            [InquiryCheck("B > 0")]
            """;
        const string second = """
            [InquiryCheck("B > 0")]
            [InquiryIndex(nameof(T.B), IsUnique = true)]
            [InquiryCheck("A > 0")]
            [InquiryIndex(nameof(T.A), nameof(T.B))]
            """;
        const string entity = """
            public sealed class T { [InquiryKey] public int Id { get; set; } [InquiryColumn] public int A { get; set; } [InquiryColumn] public int B { get; set; } }
            """;
        var left = ExtractSchemaDdl(RunGenerator(prelude + first + entity, dialect: "SqlServer"));
        var right = ExtractSchemaDdl(RunGenerator(prelude + second + entity, dialect: "SqlServer"));
        static string Primitives(string ddl) => string.Join("\n", ddl.Split('\n').Where(line => line.Contains("CONSTRAINT [CK_", StringComparison.Ordinal) || line.Contains("CREATE ", StringComparison.Ordinal) && line.Contains("INDEX [", StringComparison.Ordinal)));
        Assert.Equal(Primitives(left), Primitives(right));
    }

    [Theory]
    [InlineData("SqlServer", true)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("Oracle", false)]
    [InlineData("Sqlite", false)]
    public void CrossTableIndexNameScopeMatchesProvider(string dialect, bool tableLocal)
    {
        var result = RunGenerator(CrossTableNameSource, dialect: dialect);
        var diagnostics = result.RunResult.Diagnostics.Where(d => d.Id == "INQ071" && d.GetMessage().Contains("duplicate index name", StringComparison.Ordinal)).ToArray();
        Assert.Equal(tableLocal ? 0 : 1, diagnostics.Length);
        Assert.Equal(tableLocal ? 2 : 1, CountPrimitiveOccurrences(ExtractSchemaDdl(result), "CREATE INDEX"));
    }

    [Theory]
    [InlineData("PostgreSql", true)]
    [InlineData("Sqlite", true)]
    [InlineData("SqlServer", false)]
    [InlineData("MySql", false)]
    [InlineData("MariaDb", false)]
    [InlineData("Oracle", false)]
    public void CrossTableCheckNameScopeMatchesProvider(string dialect, bool tableLocal)
    {
        var result = RunGenerator(CrossTableNameSource, dialect: dialect);
        var diagnostics = result.RunResult.Diagnostics.Where(d => d.Id == "INQ071" && d.GetMessage().Contains("duplicate check constraint name", StringComparison.Ordinal)).ToArray();
        Assert.Equal(tableLocal ? 0 : 1, diagnostics.Length);
        Assert.Equal(tableLocal ? 2 : 1, CountPrimitiveOccurrences(ExtractSchemaDdl(result), "CK_Shared"));
    }

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Oracle")]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void SchemaWideObjectNamesMayRepeatAcrossSchemas(string dialect)
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("T", Schema = "one"), InquiryIndex(nameof(A), Name = "IX_Shared"), InquiryCheck("A > 0", Name = "CK_Shared")]
            public sealed class One { [InquiryKey] public int Id { get; set; } [InquiryColumn] public int A { get; set; } }
            [InquiryTable("T", Schema = "two"), InquiryIndex(nameof(A), Name = "IX_Shared"), InquiryCheck("A > 0", Name = "CK_Shared")]
            public sealed class Two { [InquiryKey] public int Id { get; set; } [InquiryColumn] public int A { get; set; } }
            """;
        var result = RunGenerator(source, dialect: dialect);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ071" && d.GetMessage().Contains("duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void IdenticalPhysicalTableMappingsEmitOnce()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("Shared"), InquiryIndex(nameof(Value), Name = "IX_Value"), InquiryCheck("Value > 0", Name = "CK_Value")]
            public sealed class AEntity { [InquiryKey] public int Id { get; set; } [InquiryColumn] public int Value { get; set; } }
            [InquiryTable("Shared"), InquiryIndex(nameof(Value), Name = "IX_Value"), InquiryCheck("Value > 0", Name = "CK_Value")]
            public sealed class BEntity { [InquiryKey] public int Id { get; set; } [InquiryColumn] public int Value { get; set; } }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        var ddl = ExtractSchemaDdl(result);
        Assert.Equal(1, CountPrimitiveOccurrences(ddl, "CREATE TABLE [Shared]"));
        Assert.Equal(1, CountPrimitiveOccurrences(ddl, "CONSTRAINT [CK_Value] CHECK (Value > 0)"));
        Assert.Equal(1, CountPrimitiveOccurrences(ddl, "CREATE INDEX [IX_Value] ON [Shared] ([Value])"));
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ070");
        var oracleDdl = ExtractSchemaDdl(RunGenerator(source, dialect: "Oracle"));
        Assert.Equal(1, CountPrimitiveOccurrences(oracleDdl, "CREATE TABLE Shared"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncompatiblePhysicalTableMappingsDiagnoseAndSuppressNonOwner(bool ownerFirst)
    {
        var source = "using Inquiry.Entities; namespace Demo;" + (ownerFirst
            ? "[InquiryTable(\"Shared\")] public sealed class A { [InquiryKey] public int Id {get;set;} [InquiryColumn] public int Value {get;set;} } [InquiryTable(\"Shared\"), InquiryIndex(nameof(Other), Name=\"IX_Other\")] public sealed class B { [InquiryKey] public int Id {get;set;} [InquiryColumn] public string Other {get;set;} = string.Empty; }"
            : "[InquiryTable(\"Shared\"), InquiryIndex(nameof(Other), Name=\"IX_Other\")] public sealed class B { [InquiryKey] public int Id {get;set;} [InquiryColumn] public string Other {get;set;} = string.Empty; } [InquiryTable(\"Shared\")] public sealed class A { [InquiryKey] public int Id {get;set;} [InquiryColumn] public int Value {get;set;} }");
        var result = RunGenerator(source, dialect: "SqlServer");
        var ddl = ExtractSchemaDdl(result);
        Assert.Equal(1, CountPrimitiveOccurrences(ddl, "CREATE TABLE [Shared]"));
        Assert.Contains("[Value]", ddl);
        Assert.DoesNotContain("[Other]", ddl);
        Assert.DoesNotContain("IX_Other", ddl);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ070" && d.GetMessage().Contains("canonical mapping", StringComparison.Ordinal));
    }

    [Fact]
    public void SuppressedDuplicateLengthCannotMakeForeignKeyIndexBounded()
    {
        const string source = """
            using Inquiry.Entities; namespace Demo;
            [InquiryTable("Target")] public sealed class ATarget { [InquiryKey] public int Id {get;set;} [InquiryColumn] public string Code { get; set; } = ""; }
            [InquiryTable("Target")] public sealed class ZTarget { [InquiryKey] public int Id {get;set;} [InquiryColumn(Length=32)] public string Code { get; set; } = ""; }
            [InquiryTable("Child")] public sealed class Child { [InquiryKey] public int Id {get;set;} [InquiryForeignKey("TargetCode", "Target", "Code", IsIndexed=true)] public string TargetCode {get;set;} = ""; }
            """;
        var result = RunGenerator(source, dialect: "MySql");
        var ddl = ExtractSchemaDdl(result);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ032" && d.GetMessage().Contains("index is skipped", StringComparison.Ordinal));
        Assert.DoesNotContain("CREATE INDEX", ddl);
        Assert.DoesNotContain("VARCHAR(32)", ddl);
    }

    [Fact]
    public void ForeignKeyToColumnOnlyOnSuppressedMappingIsRejected()
    {
        const string source = """
            using Inquiry.Entities; namespace Demo;
            [InquiryTable("Target")] public sealed class ATarget { [InquiryKey] public int Id {get;set;} }
            [InquiryTable("Target")] public sealed class ZTarget { [InquiryKey] public int Id {get;set;} [InquiryColumn] public int AlternateKey {get;set;} }
            [InquiryTable("Child")] public sealed class Child { [InquiryKey] public int Id {get;set;} [InquiryForeignKey("TargetKey", "Target", "AlternateKey")] public int TargetKey {get;set;} }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        var ddl = ExtractSchemaDdl(result);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ070" && d.GetMessage().Contains("absent from the canonical", StringComparison.Ordinal));
        Assert.DoesNotContain("FOREIGN KEY ([TargetKey])", ddl);
        Assert.DoesNotContain("ALTER TABLE", ddl);
    }

    [Fact]
    public void PhysicallyEquivalentEnumAndIntegerMappingsDeduplicate()
    {
        const string source = """
            using Inquiry.Entities; namespace Demo;
            public enum State : int { None }
            [InquiryTable("Shared")] public sealed class A { [InquiryKey] public int Id {get;set;} [InquiryColumn] public int Value {get;set;} }
            [InquiryTable("Shared")] public sealed class B { [InquiryKey] public int Id {get;set;} [InquiryColumn] public State Value {get;set;} }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ070");
        Assert.Equal(1, CountPrimitiveOccurrences(ExtractSchemaDdl(result), "CREATE TABLE [Shared]"));
    }

    [Fact]
    public void GenerateDdlFalseExcludesEntityFromEntireSchemaPipeline()
    {
        const string source = """
            using Inquiry.Entities; namespace Demo;
            [InquiryTable("Target")] public sealed class AOwner { [InquiryKey] public int Id {get;set;} [InquiryColumn] public string Code {get;set;} = ""; }
            [InquiryTable("Target", GenerateDdl=false), InquiryIndex(nameof(OnlySuppressed), Name="IX_Collision")]
            public sealed class ZSuppressed { [InquiryKey] public int Id {get;set;} [InquiryColumn(Length=32)] public string Code {get;set;} = ""; [InquiryColumn] public string OnlySuppressed {get;set;} = ""; }
            [InquiryTable("Other"), InquiryIndex(nameof(Value), Name="IX_Collision")] public sealed class Other { [InquiryKey] public int Id {get;set;} [InquiryColumn] public int Value {get;set;} }
            [InquiryTable("Child")] public sealed class Child { [InquiryKey] public int Id {get;set;} [InquiryForeignKey("Code", "Target", "Code", IsIndexed=true)] public string Code {get;set;} = ""; }
            """;
        var result = RunGenerator(source, dialect: "MySql");
        var ddl = ExtractSchemaDdl(result);
        Assert.DoesNotContain("OnlySuppressed", ddl);
        Assert.DoesNotContain("VARCHAR(32)", ddl);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ070");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ032");
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ071" && d.GetMessage().Contains("duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateDdlDefaultsToTrue()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"DefaultTable\")] public sealed class Entity { [InquiryKey] public int Id {get;set;} }";
        Assert.Contains("DefaultTable", ExtractSchemaDdl(RunGenerator(source, dialect: "SqlServer")));
    }

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Oracle")]
    [InlineData("Sqlite")]
    public void CrossEntityIndexCollisionWinnerIsStableAcrossSourceOrder(string dialect)
    {
        var left = RunGenerator(CrossEntityCollisionSource(aFirst: true), dialect: dialect);
        var right = RunGenerator(CrossEntityCollisionSource(aFirst: false), dialect: dialect);
        Assert.Equal(CollisionObjects(ExtractSchemaDdl(left)), CollisionObjects(ExtractSchemaDdl(right)));
        Assert.Contains("ON " + QuoteForDialect(dialect, "A"), CollisionObjects(ExtractSchemaDdl(left)));
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void CrossEntityCheckCollisionWinnerIsStableAcrossSourceOrder(string dialect)
    {
        var left = RunGenerator(CrossEntityCollisionSource(aFirst: true), dialect: dialect);
        var right = RunGenerator(CrossEntityCollisionSource(aFirst: false), dialect: dialect);
        Assert.Equal(CollisionObjects(ExtractSchemaDdl(left)), CollisionObjects(ExtractSchemaDdl(right)));
    }

    [Theory]
    [InlineData("PostgreSql", false, false, false)]
    [InlineData("Oracle", true, true, true)]
    [InlineData("SQLite", true, false, false)]
    [InlineData("SqlServer", true, true, true)]
    [InlineData("MySql", true, false, true)]
    [InlineData("MariaDb", true, true, true)]
    public void CaseOnlyNameCollisionsMatchProviderMatrix(string dialect, bool indexCollision, bool checkCollision, bool foreignKeyCollision)
    {
        var actualDialect = dialect == "SQLite" ? "Sqlite" : dialect;
        var result = RunGenerator(CaseCollisionSource, dialect: actualDialect);
        Assert.Equal(indexCollision, result.RunResult.Diagnostics.Any(d => d.Id == "INQ071" && d.GetMessage().Contains("duplicate physical index name", StringComparison.Ordinal)));
        Assert.Equal(checkCollision, result.RunResult.Diagnostics.Any(d => d.Id == "INQ071" && d.GetMessage().Contains("duplicate physical check name", StringComparison.Ordinal)));
        Assert.Equal(foreignKeyCollision, result.RunResult.Diagnostics.Any(d => d.Id == "INQ070" && d.GetMessage().Contains("explicit constraint name", StringComparison.Ordinal)));
    }

    private static string CrossEntityCollisionSource(bool aFirst)
    {
        const string prelude = "using Inquiry.Entities; namespace Demo;";
        const string a = "[InquiryTable(\"A\"), InquiryIndex(nameof(Value), Name = \"IX_Same\"), InquiryCheck(\"Value > 0\", Name = \"CK_Same\")] public sealed class A { [InquiryKey] public int Id { get; set; } [InquiryColumn] public int Value { get; set; } }";
        const string b = "[InquiryTable(\"B\"), InquiryIndex(nameof(Value), Name = \"IX_Same\"), InquiryCheck(\"Value > 0\", Name = \"CK_Same\")] public sealed class B { [InquiryKey] public int Id { get; set; } [InquiryColumn] public int Value { get; set; } }";
        return prelude + (aFirst ? a + b : b + a);
    }

    private static string CollisionObjects(string ddl)
        => string.Join("\n", ddl.Split('\n').Where(line => line.Contains("IX_Same", StringComparison.Ordinal) || line.Contains("CK_Same", StringComparison.Ordinal)));

    private static string QuoteForDialect(string dialect, string identifier) => dialect switch
    {
        "PostgreSql" => "\"" + identifier + "\"",
        "Sqlite" => "\"" + identifier + "\"",
        _ => identifier,
    };

    private const string CaseCollisionSource = """
        using Inquiry.Entities;
        namespace Demo;
        [InquiryTable("P")] public sealed class P { [InquiryKey] public int Id { get; set; } }
        [InquiryTable("C")]
        [InquiryIndex(nameof(A), Name = "IX_Case")]
        [InquiryIndex(nameof(B), Name = "ix_case")]
        [InquiryCheck("A > 0", Name = "CK_Case")]
        [InquiryCheck("B > 0", Name = "ck_case")]
        public sealed class C
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public int A { get; set; }
            [InquiryColumn] public int B { get; set; }
            [InquiryForeignKey("P1", "P", "Id", ConstraintName = "FK_Case")] public int P1 { get; set; }
            [InquiryForeignKey("P2", "P", "Id", ConstraintName = "fk_case")] public int P2 { get; set; }
        }
        """;

    private const string CrossTableNameSource = """
        using Inquiry.Entities;
        namespace Demo;
        [InquiryTable("A"), InquiryIndex(nameof(Value), Name = "IX_Shared"), InquiryCheck("Value > 0", Name = "CK_Shared")]
        public sealed class A { [InquiryKey] public int Id { get; set; } [InquiryColumn] public int Value { get; set; } }
        [InquiryTable("B"), InquiryIndex(nameof(Value), Name = "IX_Shared"), InquiryCheck("Value > 0", Name = "CK_Shared")]
        public sealed class B { [InquiryKey] public int Id { get; set; } [InquiryColumn] public int Value { get; set; } }
        """;

    private static int CountPrimitiveOccurrences(string value, string token)
    {
        var count = 0;
        for (var offset = 0; (offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0; offset += token.Length) count++;
        return count;
    }

    private static string ActionSource(string action, bool onUpdate) => $$"""
        using Inquiry.Entities;
        namespace Demo;
        [InquiryTable("P")] public sealed class P { [InquiryKey] public int Id { get; set; } }
        [InquiryTable("C")] public sealed class C
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryForeignKey("PId", "P", "Id", DefaultExpression = "0", {{(onUpdate ? "OnUpdate" : "OnDelete")}} = InquiryReferentialAction.{{action}})] public int? PId { get; set; }
        }
        """;

    private static string ActionToken(string action) => action switch
    {
        "SetNull" => "SET NULL",
        "SetDefault" => "SET DEFAULT",
        _ => action.ToUpperInvariant(),
    };
}
