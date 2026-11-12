# Entity mapping

An entity is a class marked `[InquiryTable]`. Inquiry maps only the properties you mark, and it
decides at build time which columns each generated statement writes, which it leaves to the
database, and which it reads back. This page shows those rules and a complete example that runs on
[SQLite](providers/sqlite.md). The rules are the same on every provider. Only the SQL that reads
values back differs, as described in [Provider notes](#provider-notes).

## What gets mapped

- **Table name.** `[InquiryTable]` uses the class name exactly, with no pluralization.
  `[InquiryTable("Products")]` names the table, and `Schema = "sales"` puts it in a schema.
- **Columns are opt-in.** A property is a column only when it has `[InquiryColumn]`, `[InquiryKey]` or
  `[InquiryForeignKey]`. Inquiry ignores other properties: it does not write, read or create them.
- **Base classes.** A property with one of these attributes on a base class is a column of the entity,
  and so is a relation declared there. This also works on a generic base such as `Entity<TKey>` that
  holds the key. Inherited columns come first, starting with the outermost base class. When a derived
  class overrides a property, the override keeps the base mapping unless it has an Inquiry attribute of
  its own. A property that hides a base property with `new` replaces it. A base property that generated
  code cannot reach, such as a protected one, is not mapped. Each entity class still maps
  to its own table with all of its columns: there is no shared-table or joined-table inheritance.
- **Column name.** A mapped property uses its own name unless the attribute gives one, as in
  `[InquiryColumn("BODY")]` or `[InquiryKey("ShipperID")]`.
- **Nullability.** A column allows `NULL` when its property type is a nullable value type (`int?`) or a
  nullable reference type (`string?`). Otherwise generated DDL declares it `NOT NULL`. Key columns are
  always `NOT NULL`. On a database-generated key, `int?` only means that C# has no value yet. A
  computed column gets no `NOT NULL`: its expression decides, so make the property nullable when the
  expression can return `NULL`.
- **Types.** Inquiry infers the column type from the property type. `Length`, `Precision`, `Scale` and
  `IsUnicode` adjust it on providers whose types take them. SQLite ignores them and declares a storage
  type such as `TEXT` or `NUMERIC`. `SqlType = "NVARCHAR(64)"` replaces the type with text that Inquiry copies into
  the DDL unchanged, so a `SqlType` is specific to one provider.

## Who supplies each value

Each mapping decides whether the generated `INSERT`, `UPDATE` and upsert send the property value, or
leave the column to the database. Every entity-returning method reads all mapped columns back.

| Mapping | Generated DDL | INSERT | UPDATE | Upsert | Read back |
|---|---|---|---|---|---|
| `[InquiryColumn]`, with or without a CLR initializer | The column | Sends the property value | Sets it | Sends it, and sets it on conflict | Yes |
| `[InquiryColumn(DefaultExpression = "0")]` | Adds `DEFAULT 0` | Sends the property value | Sets it | Sends it, and sets it on conflict | Yes |
| `[InquiryColumn(DefaultExpression = "0", UseDatabaseDefault = true)]` | Adds `DEFAULT 0` | **Omits the column**, even when you assign a value | Sets it | Omits it on insert, sets it on conflict | Yes |
| `[InquiryColumn(UseDatabaseDefault = true)]` | No default: the deployed table must have one | **Omits the column** | Sets it | Omits it on insert, sets it on conflict | Yes |
| `[InquiryKey(IsGenerated = true)]` | Identity column | **Omits the column**, even when you assign a value | Uses it in `WHERE`, never sets it | See [Generated keys](#generated-keys) | Yes |
| `[InquiryColumn(Computed = "...")]` | Computed column | **Omits the column** | **Omits the column** | **Omits the column** | Yes |
| `[InquiryTable(GenerateDdl = false)]` | No DDL for the table | Each column follows its own row | Same | Same | Yes |
| A property with no mapping attribute | Nothing | Nothing | Nothing | Nothing | No |

- `DefaultExpression = "0"` and `DefaultExpression = "0", UseDatabaseDefault = true` are alternatives,
  not two attributes to apply together. Choose the first when callers supply the value and the
  default only serves other SQL. Choose the second when the database always decides the value on
  insert.
- Omission does not depend on the value. Inquiry does not treat `0`, `""` or `null` as "not set".
- Batch and bulk inserts use the same INSERT column list.
- A returning method gives you a new object read from the database. Inquiry does not copy
  database-supplied values into the object you passed in. Sequential GUID keys and auditing columns
  are the exception: Inquiry assigns them on your object before the statement runs (see
  [CRUD](features/crud.md#return-values-and-input-mutation)).
- Auditing, soft-delete, concurrency-token and global-filter columns add their own rules on top of
  this table. Their pages describe them.

## Example

The example has three entities. `Project` and `Ticket` are owned by this application, so their tables
come from the generated DDL. `LegacyNote` maps a table that a migration created, and keeps its
physical names. `Ticket` has one property for each row of the table above.

<!-- recipe: entity-mapping/Models.cs -->
```csharp
using Inquiry.Entities;

// The table is named "Project" and the key column "Id", after the class and property.
[InquiryTable]
public sealed class Project
{
    [InquiryKey(IsGenerated = true)]
    public int? Id { get; set; }

    [InquiryColumn(Length = 100)]
    public string Name { get; set; } = "";
}

[InquiryTable]
public sealed class Ticket
{
    [InquiryKey(IsGenerated = true)]
    public int? Id { get; set; }

    [InquiryForeignKey(typeof(Project), IsIndexed = true)]
    public int ProjectId { get; set; }

    [InquiryColumn(Length = 200)]
    public string Title { get; set; } = "";

    [InquiryColumn]
    public int Priority { get; set; } = 3;

    [InquiryColumn(DefaultExpression = "0")]
    public int Attempts { get; set; }

    [InquiryColumn(DefaultExpression = "0")]
    public int? Retries { get; set; }

    [InquiryColumn(Length = 20, DefaultExpression = "'New'", UseDatabaseDefault = true)]
    public string Status { get; set; } = "";

    [InquiryColumn(Computed = "Priority * 10 + Attempts")]
    public int Score { get; set; }

    public string Notes { get; set; } = "";
}

// A table that a migration created. Inquiry maps it but generates no DDL for it.
[InquiryTable("TBL_NOTES", GenerateDdl = false)]
public sealed class LegacyNote
{
    [InquiryKey("NOTE_ID", IsGenerated = true)]
    public int? Id { get; set; }

    [InquiryForeignKey("PROJECT_ID", "Project", "Id")]
    public int ProjectId { get; set; }

    [InquiryColumn("BODY")]
    public string Body { get; set; } = "";

    [InquiryColumn("SOURCE", UseDatabaseDefault = true)]
    public string Source { get; set; } = "";
}
```

The stores use `nameof` for property references, so a rename changes the generated SQL instead of
breaking it:

<!-- recipe: entity-mapping/Stores.cs -->
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Stores;

public partial class ProjectStore : InquiryStore<Project>
{
    [InquiryInsert]
    public partial Task<Project?> InsertReturningAsync(
        Project project, CancellationToken ct = default);
}

public partial class TicketStore : InquiryStore<Ticket>
{
    [InquiryInsert]
    public partial Task<Ticket?> InsertReturningAsync(
        Ticket ticket, CancellationToken ct = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(
        Ticket ticket, CancellationToken ct = default);

    [InquiryUpdate]
    public partial Task<Ticket?> UpdateReturningAsync(
        Ticket ticket, CancellationToken ct = default);

    [InquiryUpsert]
    public partial Task<Ticket?> UpsertReturningAsync(
        Ticket ticket, CancellationToken ct = default);

    [InquirySelectAllByField(nameof(Ticket.ProjectId), OrderBy = nameof(Ticket.Id))]
    public partial Task<IReadOnlyList<Ticket>> SelectByProjectAsync(
        int projectId, CancellationToken ct = default);
}

public partial class LegacyNoteStore : InquiryStore<LegacyNote>
{
    [InquiryInsert]
    public partial Task<LegacyNote?> InsertReturningAsync(
        LegacyNote note, CancellationToken ct = default);
}
```

A command interceptor prints each statement just before it runs, so the output shows the real column
lists:

<!-- recipe: entity-mapping/SqlTrace.cs -->
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Commands;
using Inquiry.Interceptors;

// Prints each command just before Inquiry sends it.
public sealed class SqlTrace : IInquiryCommandInterceptor
{
    public ValueTask CommandExecutingAsync(
        InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("SQL: " + context.Command.CommandText);
        return ValueTask.CompletedTask;
    }
}
```

The program sets every value the database owns, to show which assignments the statements ignore:

<!-- recipe: entity-mapping/Program.cs -->
```csharp
using System;
using Inquiry.DependencyInjection;
using Inquiry.Generated;
using Inquiry.Interceptors;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

const string connectionString = "Data Source=inquiry-entity-mapping.db;Foreign Keys=True";

await using (var connection = new SqliteConnection(connectionString))
{
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();

    // Inquiry owns Project and Ticket, so their CREATE TABLE comes from the generated DDL.
    Console.WriteLine(InquiryGeneratedSchema.Ddl);
    command.CommandText = InquiryGeneratedSchema.Ddl;
    await command.ExecuteNonQueryAsync();

    // TBL_NOTES stands for a table that a migration created. It carries its own default.
    command.CommandText = """
        CREATE TABLE TBL_NOTES (
            NOTE_ID INTEGER PRIMARY KEY AUTOINCREMENT,
            PROJECT_ID INTEGER NOT NULL REFERENCES Project (Id),
            BODY TEXT NOT NULL,
            SOURCE TEXT NOT NULL DEFAULT 'web');
        """;
    await command.ExecuteNonQueryAsync();
}

var services = new ServiceCollection();
services.AddInquiry();
services.AddInquiryGeneratedStores();
services.AddInquirySqlite(connectionString);
services.AddSingleton<IInquiryCommandInterceptor, SqlTrace>();

await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var projects = scope.ServiceProvider.GetRequiredService<ProjectStore>();
var tickets = scope.ServiceProvider.GetRequiredService<TicketStore>();
var notes = scope.ServiceProvider.GetRequiredService<LegacyNoteStore>();

var project = await projects.InsertReturningAsync(new Project { Name = "Website" })
    ?? throw new InvalidOperationException("The insert did not return a project.");
var projectId = project.Id!.Value;

// Every value the database owns is set here, to show that INSERT ignores it.
var input = new Ticket
{
    Id = 900,
    ProjectId = projectId,
    Title = "Fix login",
    Attempts = 2,
    Retries = null,
    Status = "Ignored",
    Score = 999,
    Notes = "Not a column",
};
var saved = await tickets.InsertReturningAsync(input)
    ?? throw new InvalidOperationException("The insert did not return a ticket.");
Console.WriteLine($"Inserted: {Describe(saved)}");
Console.WriteLine($"Input:    {Describe(input)}");

saved.Status = "Open";
saved.Attempts = 5;
saved.Score = 999;
var updated = await tickets.UpdateReturningAsync(saved)
    ?? throw new InvalidOperationException("The update did not return a ticket.");
Console.WriteLine($"Updated:  {Describe(updated)}");

var candidate = new Ticket { Id = 500, ProjectId = projectId, Title = "Add search", Status = "Ignored" };
var first = await tickets.UpsertReturningAsync(candidate)
    ?? throw new InvalidOperationException("The upsert did not return a ticket.");
Console.WriteLine($"Upsert 1: {Describe(first)}");
candidate.Status = "Closed";
var second = await tickets.UpsertReturningAsync(candidate)
    ?? throw new InvalidOperationException("The upsert did not return a ticket.");
Console.WriteLine($"Upsert 2: {Describe(second)}");

var draft = new Ticket { ProjectId = projectId, Title = "Write docs", Status = "Ignored" };
var affected = await tickets.InsertAsync(draft);
Console.WriteLine($"Inserted {affected} row. Input: {Describe(draft)}");

foreach (var ticket in await tickets.SelectByProjectAsync(projectId))
{
    Console.WriteLine($"Stored:   {Describe(ticket)}");
}

var note = await notes.InsertReturningAsync(
    new LegacyNote { ProjectId = projectId, Body = "Imported", Source = "Ignored" })
    ?? throw new InvalidOperationException("The insert did not return a note.");
Console.WriteLine($"Note:     Id={note.Id} Source={note.Source}");

static string Describe(Ticket t) =>
    $"Id={t.Id?.ToString() ?? "null"} Priority={t.Priority} Attempts={t.Attempts} " +
    $"Retries={t.Retries?.ToString() ?? "null"} Status={t.Status} Score={t.Score} Notes={t.Notes}";
```

Run it against a new database file, as in [Getting started](getting-started.md#replace-programcs).
It prints:

<!-- recipe: entity-mapping/output.txt -->
```text
CREATE TABLE IF NOT EXISTS "Project" (
    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "Ticket" (
    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
    "ProjectId" INTEGER NOT NULL,
    "Title" TEXT NOT NULL,
    "Priority" INTEGER NOT NULL,
    "Attempts" INTEGER NOT NULL DEFAULT 0,
    "Retries" INTEGER DEFAULT 0,
    "Status" TEXT NOT NULL DEFAULT 'New',
    "Score" AS (Priority * 10 + Attempts),
    FOREIGN KEY ("ProjectId") REFERENCES "Project"("Id")
);

CREATE INDEX IF NOT EXISTS "IX_Ticket_ProjectId" ON "Ticket" ("ProjectId");
SQL: INSERT INTO "Project" ("Name") VALUES (@Name) RETURNING "Id", "Name"
SQL: INSERT INTO "Ticket" ("ProjectId", "Title", "Priority", "Attempts", "Retries") VALUES (@ProjectId, @Title, @Priority, @Attempts, @Retries) RETURNING "Id", "ProjectId", "Title", "Priority", "Attempts", "Retries", "Status", "Score"
Inserted: Id=1 Priority=3 Attempts=2 Retries=null Status=New Score=32 Notes=
Input:    Id=900 Priority=3 Attempts=2 Retries=null Status=Ignored Score=999 Notes=Not a column
SQL: UPDATE "Ticket" SET "ProjectId" = @ProjectId, "Title" = @Title, "Priority" = @Priority, "Attempts" = @Attempts, "Retries" = @Retries, "Status" = @Status WHERE "Id" = @Id RETURNING "Id", "ProjectId", "Title", "Priority", "Attempts", "Retries", "Status", "Score"
Updated:  Id=1 Priority=3 Attempts=5 Retries=null Status=Open Score=35 Notes=
SQL: INSERT INTO "Ticket" ("Id", "ProjectId", "Title", "Priority", "Attempts", "Retries") VALUES (@Id, @ProjectId, @Title, @Priority, @Attempts, @Retries) ON CONFLICT ("Id") DO UPDATE SET "ProjectId" = @ProjectId, "Title" = @Title, "Priority" = @Priority, "Attempts" = @Attempts, "Retries" = @Retries, "Status" = @Status RETURNING "Id", "ProjectId", "Title", "Priority", "Attempts", "Retries", "Status", "Score"
Upsert 1: Id=500 Priority=3 Attempts=0 Retries=null Status=New Score=30 Notes=
SQL: INSERT INTO "Ticket" ("Id", "ProjectId", "Title", "Priority", "Attempts", "Retries") VALUES (@Id, @ProjectId, @Title, @Priority, @Attempts, @Retries) ON CONFLICT ("Id") DO UPDATE SET "ProjectId" = @ProjectId, "Title" = @Title, "Priority" = @Priority, "Attempts" = @Attempts, "Retries" = @Retries, "Status" = @Status RETURNING "Id", "ProjectId", "Title", "Priority", "Attempts", "Retries", "Status", "Score"
Upsert 2: Id=500 Priority=3 Attempts=0 Retries=null Status=Closed Score=30 Notes=
SQL: INSERT INTO "Ticket" ("ProjectId", "Title", "Priority", "Attempts", "Retries") VALUES (@ProjectId, @Title, @Priority, @Attempts, @Retries)
Inserted 1 row. Input: Id=null Priority=3 Attempts=0 Retries=null Status=Ignored Score=0 Notes=
SQL: SELECT "Id", "ProjectId", "Title", "Priority", "Attempts", "Retries", "Status", "Score" FROM "Ticket" WHERE "ProjectId" = @ProjectId ORDER BY "Id" ASC
Stored:   Id=1 Priority=3 Attempts=5 Retries=null Status=Open Score=35 Notes=
Stored:   Id=500 Priority=3 Attempts=0 Retries=null Status=Closed Score=30 Notes=
Stored:   Id=501 Priority=3 Attempts=0 Retries=null Status=New Score=30 Notes=
SQL: INSERT INTO "TBL_NOTES" ("PROJECT_ID", "BODY") VALUES (@ProjectId, @Body) RETURNING "NOTE_ID", "PROJECT_ID", "BODY", "SOURCE"
Note:     Id=1 Source=web
```

What the output shows:

- **Generated DDL.** `Attempts` and `Retries` declare `DEFAULT 0`, `Status` declares `DEFAULT 'New'`, and
  `Score` is a computed column. `TBL_NOTES` is absent because of `GenerateDdl = false`, and `Notes` is
  absent because it has no mapping attribute.
- **Insert.** The column list has `Priority`, `Attempts` and `Retries`, but no `Id`, `Status` or
  `Score`. The program assigned `Id = 900`, `Status = "Ignored"` and `Score = 999`. The saved row has the
  identity `1`, the default `New`, and the computed `32`.
- **CLR initializer.** `Priority = 3` comes from the property initializer. Inquiry sends it like any
  other value. An initializer never becomes a database default.
- **`DefaultExpression` alone.** `Attempts` stores `2`, the value the program set. `Retries` stores
  `null`, not `0`: Inquiry sends the explicit `null`, so the default does not apply.
- **Input object.** After the insert, `input` still has `Id=900`, `Status=Ignored` and `Score=999`. Use
  the returned object. A `Task<int>` insert returns the affected row count, and leaves `draft.Id` null.
- **Update.** `SET` includes `Status`, so a `UseDatabaseDefault` column changes to `Open` on update. It
  does not include `Id` or `Score`. The database recomputes `Score` as `35`.
- **Upsert.** The first call inserts row `500` without `Status`, so it gets `New`. The second call
  conflicts on `Id` and sets `Status` to `Closed`.
- **Existing table.** The `TBL_NOTES` insert omits `SOURCE`, so the deployed default `web` applies.

## Database defaults

`DefaultExpression` describes the database: Inquiry copies it into the generated `CREATE TABLE`.
Generated statements still send the property value, so the default applies only to SQL that leaves
the column out, such as an import script. The one runtime use is on MySQL, which evaluates the
`DefaultExpression` of a database-defaulted key to read the new row back (see
[CRUD](features/crud.md#upsert-concurrency-semantics)).

`UseDatabaseDefault = true` controls the statements: generated inserts leave the column out. It does
not create a default. With generated DDL, also set `DefaultExpression`. With a table you do not own,
the deployed table must have the default. Without one, the database stores `NULL` in a nullable
column and rejects the insert for a `NOT NULL` column. The build warns about that last case when
Inquiry generates the table (`INQ117`).

To stamp a row with the database clock, combine both, with an expression for your provider:

```csharp
[InquiryColumn(DefaultExpression = "CURRENT_TIMESTAMP", UseDatabaseDefault = true)]
public DateTime CreatedAt { get; set; }
```

Updates still write this column. Use [`[InquiryCreatedAt]`](features/auditing.md) for a creation
timestamp that updates never change.

The optional lint `INQ066` reports a nullable column with a `DefaultExpression`, because an explicit
`null` bypasses the default. See [DDL safety lints](features/schema-ddl.md#ddl-safety-lints-opt-in).

## Generated keys

`[InquiryKey(IsGenerated = true)]` makes the database assign the key from an integer identity column
(`INQ030` for other types). It cannot also have a `SqlType`, `DefaultExpression` or `UseDatabaseDefault`
(`INQ074`). An insert-returning method gives you the new key. For client-generated `Guid` keys, see
[sequential GUIDs](features/crud.md#key-generation-sequential-guids).

An upsert with a `null` key inserts a new row. An upsert with a key value uses that value, as row
`500` shows. Oracle cannot upsert an entity whose key the database supplies, and reports `INQ039`.

## Computed columns

`Computed` is raw SQL that the database evaluates. Inquiry does not translate it from C#. The
expression `Priority * 10 + Attempts` in the example runs on SQLite. Other providers can need other
SQL: PostgreSQL folds unquoted names to lower case, so it needs quoted names for mixed-case columns,
and MySQL and MariaDB need `CONCAT(...)` instead of `||`. Add `[InquiryComputedExpression("<provider>",
"<sql>")]` for each provider that needs its own expression, and keep `Computed` as the fallback. The
provider ids are `sqlite`, `sqlserver`, `postgresql`, `mysql`, `mariadb` and `oracle`. A computed column cannot be a key, have a default, or be
an auditing, soft-delete or concurrency-token column (`INQ057`). See
[Server-computed columns](features/schema-ddl.md#server-computed-columns).

## Who owns the schema

`InquiryGeneratedSchema.Ddl` is a run-once baseline for an empty database. It never migrates an
existing table: a changed `DefaultExpression`, `Length` or `Computed` takes effect only in a new
database. On SQLite, `CREATE TABLE IF NOT EXISTS` skips an existing table. On the other providers,
cyclic foreign keys are added with unguarded `ALTER TABLE ... ADD CONSTRAINT` statements, which fail
or change an existing table if the script runs again. Change a deployed table with a migration; see
[Migrations recipe](features/migrations.md).

Set `[InquiryTable(GenerateDdl = false)]` when another tool owns the table. Inquiry still generates
its stores and materializers, but leaves it out of the DDL and the schema checks. See
[Schema DDL](features/schema-ddl.md).

## Foreign keys

Name the referenced entity instead of repeating its physical names:

```csharp
// Before: the table and key column names are repeated as strings.
[InquiryForeignKey("CategoryID", "Categories", "CategoryID", IsIndexed = true)]
public int? CategoryID { get; set; }

// After: Inquiry reads them from the Category mapping.
[InquiryForeignKey(typeof(Category), IsIndexed = true)]
public int? CategoryID { get; set; }
```

Both forms generate the same DDL. The typed form resolves the table and schema from the target's
`[InquiryTable]`, and the column from its single `[InquiryKey]`:

- The local column is the property name, or the name in an `[InquiryKey("...")]` or
  `[InquiryColumn("...")]` on the same property.
- The local and referenced names can differ. `Order.ShipVia` references `Shipper` this way.
- An entity can reference itself, as `Employee.ReportsTo` does with `typeof(Employee)`.
- To reference a column other than the key, or one column of a composite key, name the target
  property: `[InquiryForeignKey(typeof(Tag), nameof(Tag.TenantId))]`. The name is a property of the
  target, not a column. A name that matches no property fails the build (`INQ116`).
- The target must have `[InquiryTable]` (`INQ084`) and exactly one `[InquiryKey]` unless you name the
  property (`INQ085`).

Keep the string form, `[InquiryForeignKey("PROJECT_ID", "Project", "Id")]`, when the referenced table
has no entity class, or when the mapping records an existing schema by its physical names, as
`LegacyNote` does. Only the generator resolves a typed reference. Code that reads the attribute
through reflection finds `ReferencedType`, and `ReferencedTable` is `null`.

## Provider notes

Every provider reads back all mapped columns, including defaulted, computed and generated values:

| Provider | How a returning insert reads the row |
|---|---|
| SQLite, PostgreSQL, MariaDB | `INSERT ... RETURNING` |
| SQL Server | `OUTPUT INSERTED.*` into a table variable, then `SELECT` |
| MySQL | `INSERT`, then `SELECT` by the key or `LAST_INSERT_ID()` |
| Oracle | A PL/SQL block that inserts, then selects the row by its key |

Each provider's integration suite runs the same checks. `DefaultValueIntegrationTests` confirms that
an insert uses the database default even when the entity has a value, and that update and upsert
write the entity value. `ComputedColumnIntegrationTests` confirms that computed values are never
written and are read back after insert and update.

## See also

- [CRUD](features/crud.md) for the generated store methods and their return values.
- [Schema DDL](features/schema-ddl.md) for generated DDL, indexes, checks and foreign-key actions.
- [Auditing](features/auditing.md) for created and modified columns.
- [Value converters](features/value-converters.md) for mapping types that have no column type.
