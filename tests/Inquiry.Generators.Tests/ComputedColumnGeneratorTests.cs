using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Server-computed columns: <c>[InquiryColumn(Computed = "expr")]</c> renders the dialect's
/// computed-column DDL, is excluded from generated INSERT/UPDATE, and is still selected/materialized.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ComputedSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Person")]
        public sealed class Person
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("FirstName", Length = 50)]
            public string FirstName { get; set; } = string.Empty;

            [InquiryColumn("LastName", Length = 50)]
            public string LastName { get; set; } = string.Empty;

            [InquiryColumn("FullName", Length = 101, Computed = "FirstName || ' ' || LastName")]
            [InquiryComputedExpression("postgresql", "\"FirstName\" || ' ' || \"LastName\"")]
            [InquiryComputedExpression("mysql", "CONCAT(FirstName, ' ', LastName)")]
            [InquiryComputedExpression("mariadb", "CONCAT(FirstName, ' ', LastName)")]
            public string FullName { get; set; } = string.Empty;
        }

        public partial class PersonStore : InquiryStore<Demo.Person>
        {
            [InquiryInsert]
            public partial Task<int> InsertAsync(Person person, CancellationToken cancellationToken = default);

            [InquiryUpdate]
            public partial Task<bool> UpdateAsync(Person person, CancellationToken cancellationToken = default);

            [InquirySelectAll]
            public partial Task<IReadOnlyList<Person>> AllAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ComputedColumnExcludedFromInsertAndUpdateButSelected()
    {
        var result = RunGenerator(ComputedSource);
        AssertNoErrors(result);

        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("PersonStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        // INSERT and UPDATE omit FullName; SELECT includes it.
        Assert.Contains("_sqlInsert = \"INSERT INTO \\\"Person\\\" (\\\"Id\\\", \\\"FirstName\\\", \\\"LastName\\\") VALUES (@Id, @FirstName, @LastName)\";", text);
        Assert.Contains("_sqlUpdate = \"UPDATE \\\"Person\\\" SET \\\"FirstName\\\" = @FirstName, \\\"LastName\\\" = @LastName WHERE \\\"Id\\\" = @Id\";", text);
        Assert.Contains("_sqlSelectAll = \"SELECT \\\"Id\\\", \\\"FirstName\\\", \\\"LastName\\\", \\\"FullName\\\" FROM \\\"Person\\\"\";", text);
        // FullName is not bound anywhere.
        Assert.DoesNotContain("@FullName", text);
    }

    [Fact]
    public void ComputedColumnDdlExpressionFormDialectsUseAsExpr()
    {
        // SQLite and SQL Server use the base expression form. Oracle casts computed strings to a
        // bounded scalar type because virtual columns cannot have a LOB result.
        foreach (var dialect in new[] { "Sqlite", "SqlServer", "Oracle" })
        {
            var result = RunGenerator(ComputedSource, dialect: dialect);
            AssertNoErrors(result);
            var ddl = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
            // The standard expression form, with no type / NOT NULL on the computed column.
            Assert.Contains(dialect switch
            {
                "SqlServer" => "AS (FirstName + ' ' + LastName)",
                "Oracle" => "AS (CAST(FirstName || ' ' || LastName AS NVARCHAR2(101)))",
                _ => "AS (FirstName || ' ' || LastName)",
            }, ddl);
            Assert.DoesNotContain("GENERATED ALWAYS", ddl);
        }
    }

    [Fact]
    public void ComputedColumnDdlPostgreSqlAndMySqlUseStoredGeneratedForm()
    {
        var pg = RunGenerator(ComputedSource, dialect: "PostgreSql");
        AssertNoErrors(pg);
        var pgDdl = Assert.Single(pg.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("GENERATED ALWAYS AS (\"\"FirstName\"\" || ' ' || \"\"LastName\"\") STORED", pgDdl);

        var mysql = RunGenerator(ComputedSource, dialect: "MySql");
        AssertNoErrors(mysql);
        var mysqlDdl = Assert.Single(mysql.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("GENERATED ALWAYS AS (CONCAT(FirstName, ' ', LastName)) STORED", mysqlDdl);

        var mariadb = RunGenerator(ComputedSource, dialect: "MariaDb");
        AssertNoErrors(mariadb);
        var mariadbDdl = Assert.Single(mariadb.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("GENERATED ALWAYS AS (CONCAT(FirstName, ' ', LastName)) STORED", mariadbDdl);
    }

    [Fact]
    public void SqlServerComputedConcatenationTranslationPreservesQuotedAndCommentedPipes()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("Lexical")]
            public sealed class Lexical
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Computed = "FirstName || 'a||b''c' || [odd||name]]] || \"odd\"\"||name\" /* keep || */ || LastName -- keep ||\n || FirstName")]
                public string Value { get; set; } = string.Empty;
            }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("AS (FirstName + 'a||b''c' + [odd||name]]] + \"odd\"\"||name\" /* keep || */ + LastName -- keep ||", ddl);
        Assert.Contains(" + FirstName)", ddl);
    }

    [Fact]
    public void ComputedKeyColumnReportsINQ057()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("X")]
            public sealed class X
            {
                [InquiryKey("Id", Computed = "1")]
                public long Id { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ057");
    }

    [Theory]
    [InlineData("Value; DROP TABLE X", "statement separator")]
    [InlineData("(Value + 1", "unmatched parentheses")]
    [InlineData("(SELECT 1)", "subquery token")]
    [InlineData("SUM(Value) OVER ()", "window-function token")]
    [InlineData("'unterminated", "unterminated")]
    [InlineData("Value /* unterminated", "unterminated block")]
    [InlineData("Value -- comment", "consume generated wrapper")]
    [InlineData("Value /* outer /* nested */", "nested block")]
    public void CommonLexerRejectsProvenUnsafeShapes(string expression, string reason)
    {
        var analysis = Inquiry.Generators.Abstractions.SqlExpressionLexer.Analyze(
            expression, Inquiry.Generators.Abstractions.SqlExpressionCommentPolicy.Standard, false);
        Assert.Contains(analysis.Failures, failure => failure.Contains(reason, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("'SELECT; OVER || --'", false)]
    [InlineData("\"SELECT; OVER ||\"", false)]
    [InlineData("`SELECT; OVER ||`", false)]
    [InlineData("[SELECT; OVER ||]", false)]
    [InlineData("Value /* SELECT; OVER || */ + 1", false)]
    [InlineData("Value -- SELECT; OVER ||\n + 1", false)]
    [InlineData("Value || Other", true)]
    public void LexerIgnoresQuotedAndCommentedTokens(string expression, bool pipes)
    {
        var analysis = Inquiry.Generators.Abstractions.SqlExpressionLexer.Analyze(
            expression, Inquiry.Generators.Abstractions.SqlExpressionCommentPolicy.Standard, false);
        Assert.Empty(analysis.Failures);
        Assert.Equal(pipes, analysis.HasConcatenationOperator);
    }

    [Fact]
    public void MySqlCommentRulesAndAmbiguousPipesAreProviderAware()
    {
        Assert.Empty(Inquiry.Generators.Abstractions.SqlExpressionLexer.Analyze("a--b", Inquiry.Generators.Abstractions.SqlExpressionCommentPolicy.MySql, false).Failures);
        Assert.Contains(Inquiry.Generators.Abstractions.SqlExpressionLexer.Analyze("a-- b", Inquiry.Generators.Abstractions.SqlExpressionCommentPolicy.MySql, false).Failures, f => f.Contains("line comment", StringComparison.Ordinal));
        Assert.Contains(Inquiry.Generators.Abstractions.SqlExpressionLexer.Analyze("a # b", Inquiry.Generators.Abstractions.SqlExpressionCommentPolicy.MySql, false).Failures, f => f.Contains("line comment", StringComparison.Ordinal));

        var mysql = RunGenerator("using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryColumn(Computed=\"Id || 1\")] public int C {get;set;} }", dialect: "MySql");
        Assert.Contains(mysql.RunResult.Diagnostics, d => d.Id == "INQ072" && d.GetMessage().Contains("ambiguous", StringComparison.Ordinal));
        Assert.DoesNotContain(mysql.RunResult.GeneratedTrees, tree => tree.GetText().ToString().Contains("Id || 1", StringComparison.Ordinal));
    }

    [Fact]
    public void OverridesValidateAndSelectByStableProviderId()
    {
        const string source = """
            using Inquiry.Entities; namespace Demo;
            [InquiryTable("T")] public sealed class T { [InquiryKey] public int Id {get;set;}
            [InquiryColumn(Computed="Id + 1")]
            [InquiryComputedExpression("future.provider", "Id + 2")]
            [InquiryComputedExpression("mysql", "Id + 3")]
            public int C {get;set;} }
            """;
        var mysql = RunGenerator(source, dialect: "MySql");
        AssertNoErrors(mysql);
        Assert.Contains("Id + 3", ExtractSchemaDdl(mysql));
        var sqlite = RunGenerator(source, dialect: "Sqlite");
        AssertNoErrors(sqlite);
        Assert.Contains("Id + 1", ExtractSchemaDdl(sqlite));
    }

    [Theory]
    [InlineData("Bad", "Id + 1", "provider id is invalid")]
    [InlineData("mysql", "", "non-empty")]
    public void InvalidOverrideMetadataReportsINQ072(string provider, string fallback, string reason)
    {
        var source = $"using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T {{ [InquiryKey] public int Id {{get;set;}} [InquiryColumn(Computed=\"{fallback}\")] [InquiryComputedExpression(\"{provider}\", \"Id + 2\")] public int C {{get;set;}} }}";
        var result = RunGenerator(source, dialect: "MySql");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ072" && d.GetMessage().Contains(reason, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), d => d.Id == "CS8795");
    }

    [Fact]
    public void DuplicateOverrideReportsOneLocatedINQ072AndSuppressesEntitySql()
    {
        const string source = "using System.Threading.Tasks; using Inquiry.Entities; using Inquiry.Stores; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryColumn(Computed=\"Id+1\")] [InquiryComputedExpression(\"mysql\",\"Id+2\")] [InquiryComputedExpression(\"mysql\",\"Id+3\")] public int C {get;set;} } public partial class Store : InquiryStore<T> { [InquirySelectAll] public partial Task<System.Collections.Generic.IReadOnlyList<T>> All(); }";
        var result = RunGenerator(source, dialect: "MySql");
        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(d => d.Id == "INQ072"));
        Assert.NotEqual(Microsoft.CodeAnalysis.Location.None, diagnostic.Location);
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), d => d.Id == "CS8795");
        Assert.DoesNotContain(result.RunResult.GeneratedTrees, tree => tree.GetText().ToString().Contains("CREATE TABLE `T`", StringComparison.Ordinal));
    }

    [Fact]
    public void INQ072IsLocatedOnSelectedExpressionArgument()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryColumn(Computed=\"Id + 1\")] [InquiryComputedExpression(\"mysql\", \"Id || 1\")] public int C {get;set;} }";
        var result = RunGenerator(source, dialect: "MySql");
        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(d => d.Id == "INQ072"));
        Assert.Equal("\"Id || 1\"", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [Fact]
    public void ProviderOverrideDeclarationOrderDoesNotChangeSelection()
    {
        const string prefix = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryColumn(Computed=\"Id+1\")] ";
        const string suffix = " public int C {get;set;} }";
        var left = RunGenerator(prefix + "[InquiryComputedExpression(\"future.provider\",\"Id+2\")] [InquiryComputedExpression(\"mysql\",\"Id+3\")]" + suffix, dialect: "MySql");
        var right = RunGenerator(prefix + "[InquiryComputedExpression(\"mysql\",\"Id+3\")] [InquiryComputedExpression(\"future.provider\",\"Id+2\")]" + suffix, dialect: "MySql");
        Assert.Equal(ExtractSchemaDdl(left), ExtractSchemaDdl(right));
    }

    [Fact]
    public void UnknownProviderOverrideStillRequiresNonemptyExpression()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryColumn(Computed=\"Id+1\")] [InquiryComputedExpression(\"future.provider\", \"   \")] public int C {get;set;} }";
        var result = RunGenerator(source, dialect: "Sqlite");
        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(d => d.Id == "INQ072"));
        Assert.Contains("override expression is empty", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("\"   \"", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [Fact]
    public void ForeignAttributeWithSameSimpleNameIsIgnored()
    {
        const string source = "using System; using Inquiry.Entities; namespace Foreign { [AttributeUsage(AttributeTargets.Property, AllowMultiple=true)] public sealed class InquiryComputedExpressionAttribute : Attribute { public InquiryComputedExpressionAttribute(string p, string e) {} } } namespace Demo { [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryColumn(Computed=\"Id+1\")] [Foreign.InquiryComputedExpression(\"mysql\", \"Id || 1\")] public int C {get;set;} } }";
        var result = RunGenerator(source, dialect: "MySql");
        AssertNoErrors(result);
        Assert.Contains("Id+1", ExtractSchemaDdl(result));
    }

    [Fact]
    public void InvalidManyToManyJunctionProducesCompileSafeStoreStub()
    {
        const string source = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks; using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("Orders")] public sealed class Order { [InquiryKey] public long Id {get;set;} [InquiryManyToMany(typeof(OrderProduct), nameof(OrderProduct.OrderId), nameof(OrderProduct.ProductId))] public List<Product> Products {get;set;} = new(); }
            [InquiryTable("Products")] public sealed class Product { [InquiryKey] public long Id {get;set;} }
            [InquiryTable("OrderProduct")] public sealed class OrderProduct { [InquiryKey] public long OrderId {get;set;} [InquiryKey] public long ProductId {get;set;} [InquiryColumn(Computed="OrderId || ProductId")] public long Invalid {get;set;} }
            public partial class OrderStore : InquiryStore<Order> { [InquirySelectOneByKeyEager] public partial Task<Order?> Get(long id); }
            """;
        var result = RunGenerator(source, dialect: "MySql");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ072");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ008" && d.GetMessage().Contains("relation dependency", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), d => d.Id == "CS8795");
        var store = Assert.Single(result.RunResult.GeneratedTrees, tree => tree.FilePath.EndsWith("OrderStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("NotSupportedException", store);
        Assert.DoesNotContain("JOIN", store);
    }

    [Fact]
    public void SelectedEmptyOverrideReportsExactlyOneINQ072AtExpression()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryColumn(Computed=\"Id+1\")] [InquiryComputedExpression(\"mysql\", \"   \")] public int C {get;set;} }";
        var result = RunGenerator(source, dialect: "MySql");
        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(d => d.Id == "INQ072"));
        Assert.Contains("override expression is empty", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("\"   \"", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [Fact]
    public void NonComputedUnmappedJunctionRetainsExistingRelationDiagnostic()
    {
        const string source = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks; using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("Orders")] public sealed class Order { [InquiryKey] public long Id {get;set;} [InquiryManyToMany(typeof(OrderProduct), nameof(OrderProduct.OrderId), nameof(OrderProduct.ProductId))] public List<Product> Products {get;set;} = new(); }
            [InquiryTable("Products")] public sealed class Product { [InquiryKey] public long Id {get;set;} }
            [InquiryTable("OrderProduct")] public sealed class OrderProduct { [InquiryColumn] public long OrderId {get;set;} [InquiryColumn] public long ProductId {get;set;} }
            public partial class OrderStore : InquiryStore<Order> { [InquirySelectOneByKeyEager] public partial Task<Order?> Get(long id); }
            """;
        var result = RunGenerator(source, dialect: "MySql");
        // The junction declares no key, so it fails its own validation and never reaches mappedEntities —
        // which is INQ087 ("not a mapped entity"), the reason split out of INQ063's old catch-all.
        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(d => d.Id == "INQ087"));
        Assert.Contains("is not a mapped Inquiry entity", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("OrderProduct", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ008" && d.GetMessage().Contains("relation dependency", StringComparison.Ordinal));
    }

    [Fact]
    public void OracleComputedPhysicalIdentifiersAlignWithGeneratedSelect()
    {
        const string source = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks; using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("Computed")]
            public sealed class Entity
            {
                [InquiryKey] public int Id {get;set;}
                [InquiryColumn("Base Value")] public int BaseValue {get;set;}
                [InquiryColumn("MixedCaseValue")] public int MixedCaseValue {get;set;}
                [InquiryColumn("Computed Total", Computed="\"Base Value\" + \"MixedCaseValue\"")]
                [InquiryComputedExpression("oracle", "\"Base Value\" + MixedCaseValue")]
                public int Total {get;set;}
            }
            public partial class Store : InquiryStore<Entity> { [InquirySelectAll] public partial Task<IReadOnlyList<Entity>> All(CancellationToken cancellationToken = default); }
            """;
        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);
        Assert.Contains("MixedCaseValue NUMBER(10) NOT NULL", ddl);
        Assert.Contains("\"Computed Total\" AS (\"Base Value\" + MixedCaseValue)", ddl);
        Assert.DoesNotContain("CAST(\"Base Value\" + MixedCaseValue", ddl);
        Assert.DoesNotContain("\"MixedCaseValue\"", ddl);
        var store = Assert.Single(result.RunResult.GeneratedTrees, tree => tree.FilePath.EndsWith("Store.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("SELECT Id, \\\"Base Value\\\", MixedCaseValue, \\\"Computed Total\\\" FROM Computed", store);
    }

    [Fact]
    public void OracleComputedStringIsCastToDeclaredBoundedScalarType()
    {
        var result = RunGenerator(ComputedSource, dialect: "Oracle");
        AssertNoErrors(result);

        Assert.Contains("FullName AS (CAST(FirstName || ' ' || LastName AS NVARCHAR2(101)))", ExtractSchemaDdl(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2001)]
    public void OracleComputedStringWithoutSupportedBoundedLengthReportsINQ077(int length)
    {
        var lengthArgument = length == 0 ? string.Empty : ", Length = 2001";
        var source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryColumn(Computed=\"TO_CHAR(Id)\"" + lengthArgument + ")] public string Display {get;set;} = \"\"; }";

        var result = RunGenerator(source, dialect: "Oracle");

        Assert.Contains(result.RunResult.Diagnostics, diagnostic => diagnostic.Id == "INQ077");
    }

    [Fact]
    public void NamedConstructorArgumentsOutOfOrderRetainSemanticLocations()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryColumn(Computed=\"Id+1\")] [InquiryComputedExpression(expression: \"   \", providerId: \"Bad\")] public int C {get;set;} }";
        var result = RunGenerator(source, dialect: "MySql");
        var diagnostics = result.RunResult.Diagnostics.Where(d => d.Id == "INQ072").ToArray();
        Assert.Equal(2, diagnostics.Length);
        var provider = Assert.Single(diagnostics, d => d.GetMessage().Contains("provider id is invalid", StringComparison.Ordinal));
        var expression = Assert.Single(diagnostics, d => d.GetMessage().Contains("override expression is empty", StringComparison.Ordinal));
        Assert.Equal("\"Bad\"", source.Substring(provider.Location.SourceSpan.Start, provider.Location.SourceSpan.Length));
        Assert.Equal("\"   \"", source.Substring(expression.Location.SourceSpan.Start, expression.Location.SourceSpan.Length));
    }
}
