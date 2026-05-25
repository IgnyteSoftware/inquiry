using Microsoft.Data.SqlClient;

namespace Inquiry.Sample.Data;

internal static class SampleDatabase
{
    public static async Task CreateSchemaAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.TOrganization', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TOrganization (
                    [Key] uniqueidentifier NOT NULL CONSTRAINT PK_TOrganization PRIMARY KEY,
                    [Name] nvarchar(200) NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_TOrganization_IsActive DEFAULT (1)
                );
            END;

            IF OBJECT_ID(N'dbo.TUser', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TUser (
                    [Key] uniqueidentifier NOT NULL CONSTRAINT PK_TUser PRIMARY KEY,
                    FirstName nvarchar(100) NOT NULL,
                    LastName nvarchar(100) NOT NULL,
                    Email nvarchar(320) NOT NULL CONSTRAINT UQ_TUser_Email UNIQUE
                );
            END;

            IF OBJECT_ID(N'dbo.TOrganizationToUser', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TOrganizationToUser (
                    [Key] uniqueidentifier NOT NULL CONSTRAINT PK_TOrganizationToUser PRIMARY KEY,
                    TOrganizationKey uniqueidentifier NOT NULL,
                    TUserKey uniqueidentifier NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_TOrganizationToUser_IsActive DEFAULT (1),
                    CONSTRAINT FK_TOrganizationToUser_TOrganization
                        FOREIGN KEY (TOrganizationKey) REFERENCES dbo.TOrganization([Key]),
                    CONSTRAINT FK_TOrganizationToUser_TUser
                        FOREIGN KEY (TUserKey) REFERENCES dbo.TUser([Key])
                );
            END;

            IF OBJECT_ID(N'dbo.TCategory', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TCategory (
                    [Key] uniqueidentifier NOT NULL CONSTRAINT PK_TCategory PRIMARY KEY,
                    [Name] nvarchar(200) NOT NULL
                );
            END;

            IF OBJECT_ID(N'dbo.TProduct', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TProduct (
                    [Key] uniqueidentifier NOT NULL CONSTRAINT PK_TProduct PRIMARY KEY,
                    [Name] nvarchar(200) NOT NULL,
                    Price decimal(18, 2) NOT NULL,
                    TCategoryKey uniqueidentifier NOT NULL,
                    CONSTRAINT FK_TProduct_TCategory
                        FOREIGN KEY (TCategoryKey) REFERENCES dbo.TCategory([Key])
                );
            END;
            """;

        await command.ExecuteNonQueryAsync();
    }
}
