namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string TypedFkSource = """
        using Inquiry.Entities;

        namespace Demo;

        [InquiryTable("TAuthor")]
        public sealed class Author
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn]
            public string Email { get; set; } = string.Empty;
        }

        [InquiryTable("TBook")]
        public sealed class Book
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn]
            public string Title { get; set; } = string.Empty;

            [InquiryForeignKey(typeof(Author))]
            public long AuthorId { get; set; }
        }
        """;

    [Fact]
    public void TypedForeignKeyResolvesTableAndKeyColumn()
    {
        var result = RunGenerator(TypedFkSource);
        AssertNoErrors(result);

        var schema = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal));
        var ddl = schema.GetText().ToString();

        Assert.Contains("REFERENCES", ddl);
        Assert.Contains("TAuthor", ddl);
    }

    [Fact]
    public void TypedForeignKeyWithColumnOverrideResolvesNamedProperty()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TUser")]
            public sealed class User
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("email_address")]
                public string Email { get; set; } = string.Empty;
            }

            [InquiryTable("TSession")]
            public sealed class Session
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey(typeof(User), nameof(User.Email))]
                public string UserEmail { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var schema = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal));
        var ddl = schema.GetText().ToString();

        Assert.Contains("TUser", ddl);
        Assert.Contains("email_address", ddl);
    }

    [Fact]
    public void TypedForeignKeyTargetWithoutTableReportsInq084()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            public sealed class Unmapped
            {
                public long Id { get; set; }
            }

            [InquiryTable("TChild")]
            public sealed class Child
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey(typeof(Unmapped))]
                public long ParentId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ084");
    }

    [Fact]
    public void TypedForeignKeyTargetWithoutKeyReportsInq085()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TParent")]
            public sealed class Parent
            {
                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            [InquiryTable("TChild")]
            public sealed class Child
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey(typeof(Parent))]
                public long ParentId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ085");
    }

    [Fact]
    public void TypedForeignKeyProducesSameDdlAsStringForm()
    {
        const string stringFormSource = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TAuthor")]
            public sealed class Author
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn]
                public string Email { get; set; } = string.Empty;
            }

            [InquiryTable("TBook")]
            public sealed class Book
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn]
                public string Title { get; set; } = string.Empty;

                [InquiryForeignKey("TAuthor", "Id")]
                public long AuthorId { get; set; }
            }
            """;

        var typedResult = RunGenerator(TypedFkSource);
        var stringResult = RunGenerator(stringFormSource);
        AssertNoErrors(typedResult);
        AssertNoErrors(stringResult);

        var typedDdl = ExtractDdlFragment(typedResult, "TBook");
        var stringDdl = ExtractDdlFragment(stringResult, "TBook");

        Assert.Equal(stringDdl, typedDdl);
    }

    [Fact]
    public void TypedForeignKeyResolvesTargetKeyWithForeignKeyAttributeOnIt()
    {
        // The target's key property itself carries a 2-arg [InquiryForeignKey].
        // Before the fix, GetConstructorString would read arg[0] (the referenced table name)
        // instead of defaulting to the property name for the local column.
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TGrandparent")]
            public sealed class Grandparent
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }
            }

            [InquiryTable("TParent")]
            public sealed class Parent
            {
                [InquiryKey]
                [InquiryForeignKey("TGrandparent", "Id")]
                public long GrandparentId { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            [InquiryTable("TChild")]
            public sealed class Child
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey(typeof(Parent))]
                public long ParentId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var ddl = ExtractDdlFragment(result, "TChild");

        Assert.Contains("TParent", ddl);
        Assert.Contains("GrandparentId", ddl);
    }

    [Fact]
    public void TypedForeignKeyResolvesExplicitKeyColumnOverForeignKeyAttribute()
    {
        // The target's key has an explicit column name via [InquiryKey("parent_pk")] AND
        // also carries a 2-arg [InquiryForeignKey]. The key column name must win.
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TGrandparent")]
            public sealed class Grandparent
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }
            }

            [InquiryTable("TParent")]
            public sealed class Parent
            {
                [InquiryKey("parent_pk")]
                [InquiryForeignKey("TGrandparent", "Id")]
                public long GrandparentId { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            [InquiryTable("TChild")]
            public sealed class Child
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey(typeof(Parent))]
                public long ParentId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var ddl = ExtractDdlFragment(result, "TChild");

        // FK must reference the mapped column name "parent_pk", not the property name.
        Assert.Contains("TParent", ddl);
        Assert.Contains("parent_pk", ddl);
    }

    [Fact]
    public void TypedForeignKeyCompositeKeyTargetReportsInq085()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TParent")]
            public sealed class Parent
            {
                [InquiryKey]
                public long TenantId { get; set; }

                [InquiryKey]
                public long ItemId { get; set; }
            }

            [InquiryTable("TChild")]
            public sealed class Child
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey(typeof(Parent))]
                public long ParentId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ085");
    }

    // INQ116 (#546): the second argument of the typed form is a property name on the target.
    private const string TypedFkPropertyTarget = """
        using Inquiry.Entities;

        namespace Demo;

        [InquiryTable("TCategory")]
        public sealed class Category
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("category_code")]
            public string Code { get; set; } = string.Empty;
        }
        """;

    private static string TypedFkPropertyChild(string referencedName, string tableOptions = "") => $$"""
        using Inquiry.Entities;

        namespace Demo;

        [InquiryTable("TProduct"{{tableOptions}})]
        public sealed class Product
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryForeignKey(typeof(Category), {{referencedName}})]
            public string CategoryCode { get; set; } = string.Empty;
        }
        """;

    private static string WithoutUsing(string source)
        => source.Replace("using Inquiry.Entities;", string.Empty, StringComparison.Ordinal)
            .Replace("namespace Demo;", string.Empty, StringComparison.Ordinal);

    [Theory]
    [InlineData("")]
    [InlineData(", GenerateDdl = false")]
    [InlineData(", GenerateForeignKeys = false")]
    public void TypedForeignKeyNamingAMissingPropertyReportsInq116(string tableOptions)
    {
        var source = TypedFkPropertyTarget + WithoutUsing(TypedFkPropertyChild("\"Cod\"", tableOptions));

        var result = RunGenerator(source);

        // Every provider generator in the harness reports the entity diagnostics, so the ID repeats.
        var diagnostic = result.RunResult.Diagnostics.First(static d => d.Id == "INQ116");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic.Severity);
        var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("typeof(Category), \"Cod\"", message);
        Assert.Contains("nameof(Category.Property)", message);
    }

    [Fact]
    public void TypedForeignKeyNamingAColumnInsteadOfAPropertyReportsInq116()
    {
        // "category_code" is the mapped column of Category.Code. The typed form takes the property name.
        var source = TypedFkPropertyTarget + WithoutUsing(TypedFkPropertyChild("\"category_code\""));

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ116");
    }

    [Theory]
    [InlineData("nameof(Category.Code)", false)]
    [InlineData("\"Cod\"", true)]
    public void TypedForeignKeyChecksAPropertyOfATargetInAnotherAssembly(string referencedName, bool expectError)
    {
        var result = RunGenerator(TypedFkPropertyChild(referencedName), referencedAssemblySource: TypedFkPropertyTarget);

        if (expectError)
        {
            Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ116");
            return;
        }

        AssertNoErrors(result);
        Assert.Contains("category_code", ExtractDdlFragment(result, "TProduct"));
    }

    [Fact]
    public void TypedForeignKeyResolvesAPropertyTheTargetInherits()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            public abstract class CategoryBase
            {
                [InquiryColumn("category_code")]
                public string Code { get; set; } = string.Empty;
            }

            [InquiryTable("TCategory", GenerateDdl = false)]
            public sealed class Category : CategoryBase
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }
            }

            [InquiryTable("TProduct")]
            public sealed class Product
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey(typeof(Category), nameof(Category.Code))]
                public string CategoryCode { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);

        AssertNoErrors(result);
        Assert.Contains("category_code", ExtractDdlFragment(result, "TProduct"));
    }

    private static string ExtractDdlFragment(GeneratorTestResult result, string tableName)
    {
        var schema = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal));
        var text = schema.GetText().ToString();
        var start = text.IndexOf("CREATE TABLE", StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }
        // Extract from the first CREATE TABLE containing our table name to the end of the DDL const.
        var tableStart = text.IndexOf(tableName, start, StringComparison.Ordinal);
        if (tableStart < 0)
        {
            return string.Empty;
        }

        var createStart = text.LastIndexOf("CREATE TABLE", tableStart, StringComparison.Ordinal);
        var end = text.IndexOf(';', tableStart);
        return end > createStart ? text.Substring(createStart, end - createStart + 1) : string.Empty;
    }
}
