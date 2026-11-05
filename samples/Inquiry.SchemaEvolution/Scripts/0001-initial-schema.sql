-- Release 1: a snapshot of V1's InquiryGeneratedSchema.Ddl. Never edit or regenerate it.
IF TYPE_ID(N'[dbo].[Inquiry_Tvp_97901ad0afa7a9860614b4af2b675765f70e0cc178bf52ef01c6c39cb91fc5e1]') IS NULL EXEC(N'CREATE TYPE [dbo].[Inquiry_Tvp_97901ad0afa7a9860614b4af2b675765f70e0cc178bf52ef01c6c39cb91fc5e1] AS TABLE ([Value] NVARCHAR(32) NOT NULL)');

IF OBJECT_ID(N'[CatalogItems]', N'U') IS NULL
BEGIN
    CREATE TABLE [CatalogItems] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
    [Code] NVARCHAR(32) NOT NULL
    );
END;;
