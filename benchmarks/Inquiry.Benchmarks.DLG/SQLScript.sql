-- Developer's comment header
-- Categories.sql
-- 
-- history:   6/4/2026 10:07:08 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectOneWithProductsUsingCategoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectOneWithProductsUsingCategoryID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'Categories_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[Categories] DROP CONSTRAINT [Categories_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'Categories'))
  BEGIN
      ALTER TABLE [dbo].[Categories] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@CategoryID int  
		@CategoryName nvarchar(40)  
		@Description text = null  
		@Picture image = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'Categories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_Insert]
@CategoryID int , 
@CategoryName nvarchar(40) , 
@Description text = null , 
@Picture image = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1



--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@CategoryID IS NULL)
	INSERT INTO [dbo].[Categories]( [CategoryName],[Description],[Picture] )
	OUTPUT inserted.*
	VALUES ( @CategoryName,@Description,@Picture )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[Categories]( [CategoryName],[Description],[Picture] )
	OUTPUT inserted.*
	VALUES ( @CategoryName,@Description,@Picture )
ELSE
	INSERT INTO [dbo].[Categories]( [CategoryName],[Description],[Picture] )
	VALUES ( @CategoryName,@Description,@Picture )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@CategoryID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'Categories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_Update]
@GenericUpdateInstructionXml XML,		
@CategoryID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'Categories')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[Categories] ' + 'SET ' + @SetClause + ' WHERE [CategoryID] = @CategoryID';

EXEC sp_executesql @SQL, N'@CategoryID int ', @CategoryID = @CategoryID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@CategoryID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'Categories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_Delete]
@CategoryID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[Categories]
WHERE
[CategoryID] = @CategoryID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'Categories'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[Categories]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[Categories] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@useNoLock bit
		@CategoryID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Categories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_SelectByPrimaryKey]
@useNoLock BIT = 0,
@CategoryID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[Categories] WITH (NOLOCK)
        WHERE [CategoryID] = @CategoryID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[Categories]
        WHERE [CategoryID] = @CategoryID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'Categories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Categories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'Categories'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Categories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_Categories_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'Categories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Categories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'Categories' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Categories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Categories_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'Categories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Categories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Categories' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Categories] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Categories] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@CategoryID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Categories' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_ExistsByPrimaryKey]
@CategoryID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[Categories] WITH (NOLOCK)
WHERE [CategoryID] = @CategoryID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@CategoryID int  
		@CategoryName nvarchar(40)  
		@Description text = null  
		@Picture image = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'Categories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_Upsert]
@GenericUpdateInstructionXml XML,
@CategoryID int , 
@CategoryName nvarchar(40) , 
@Description text = null , 
@Picture image = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON



--Check Primary Key First for Speed
IF (@CategoryID IS NULL)
	EXEC [dbo].[gsp_Categories_Insert] @CategoryID,@CategoryName,@Description,@Picture,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[Categories] WHERE [CategoryID] = @CategoryID)
	EXEC [dbo].[gsp_Categories_Update] @GenericUpdateInstructionXml, 	@CategoryID = @CategoryID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_Categories_Insert] @CategoryID,@CategoryName,@Description,@Picture,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Categories_SelectOneWithProductsUsingCategoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Categories_SelectOneWithProductsUsingCategoryID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Categories_SelectOneWithProductsUsingCategoryID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:08 PM

INPUTS	: 
		@useNoLock bit
		@CategoryID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Categories' and also the respective child records from 'Products'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Categories_SelectOneWithProductsUsingCategoryID]
@useNoLock BIT = 0,
@CategoryID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Categories_SelectByPrimaryKey] @CategoryID = @CategoryID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_Products_SelectAllByForeignKeyCategoryID]  @CategoryID = @CategoryID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				







-- Developer's comment header
-- Region.sql
-- 
-- history:   6/4/2026 10:07:09 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectOneWithTerritoriesUsingRegionID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectOneWithTerritoriesUsingRegionID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'Region_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[Region] DROP CONSTRAINT [Region_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'Region'))
  BEGIN
      ALTER TABLE [dbo].[Region] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@RegionID int  
		@RegionDescription nvarchar(60)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'Region' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_Insert]
@RegionID int , 
@RegionDescription nvarchar(60) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1



--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@RegionID IS NULL)
	INSERT INTO [dbo].[Region]( [RegionID],[RegionDescription] )
	OUTPUT inserted.*
	VALUES ( DEFAULT,@RegionDescription )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[Region]( [RegionID],[RegionDescription] )
	OUTPUT inserted.*
	VALUES ( @RegionID,@RegionDescription )
ELSE
	INSERT INTO [dbo].[Region]( [RegionID],[RegionDescription] )
	VALUES ( @RegionID,@RegionDescription )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@RegionID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'Region' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_Update]
@GenericUpdateInstructionXml XML,		
@RegionID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'Region')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[Region] ' + 'SET ' + @SetClause + ' WHERE [RegionID] = @RegionID';

EXEC sp_executesql @SQL, N'@RegionID int ', @RegionID = @RegionID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@RegionID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'Region' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_Delete]
@RegionID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[Region]
WHERE
[RegionID] = @RegionID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'Region'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[Region]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[Region] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@RegionID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Region' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_SelectByPrimaryKey]
@useNoLock BIT = 0,
@RegionID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[Region] WITH (NOLOCK)
        WHERE [RegionID] = @RegionID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[Region]
        WHERE [RegionID] = @RegionID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'Region' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Region]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'Region'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Region]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_Region_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'Region' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Region]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'Region' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Region]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Region_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'Region' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Region]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Region' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Region] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Region] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@RegionID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Region' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_ExistsByPrimaryKey]
@RegionID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[Region] WITH (NOLOCK)
WHERE [RegionID] = @RegionID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@RegionID int  
		@RegionDescription nvarchar(60)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'Region' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_Upsert]
@GenericUpdateInstructionXml XML,
@RegionID int , 
@RegionDescription nvarchar(60) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON



--Check Primary Key First for Speed
IF (@RegionID IS NULL)
	EXEC [dbo].[gsp_Region_Insert] @RegionID,@RegionDescription,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[Region] WHERE [RegionID] = @RegionID)
	EXEC [dbo].[gsp_Region_Update] @GenericUpdateInstructionXml, 	@RegionID = @RegionID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_Region_Insert] @RegionID,@RegionDescription,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Region_SelectOneWithTerritoriesUsingRegionID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Region_SelectOneWithTerritoriesUsingRegionID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Region_SelectOneWithTerritoriesUsingRegionID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@RegionID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Region' and also the respective child records from 'Territories'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Region_SelectOneWithTerritoriesUsingRegionID]
@useNoLock BIT = 0,
@RegionID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Region_SelectByPrimaryKey] @RegionID = @RegionID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_Territories_SelectAllByForeignKeyRegionID]  @RegionID = @RegionID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				







-- Developer's comment header
-- Territories.sql
-- 
-- history:   6/4/2026 10:07:09 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_DeleteAllByForeignKeyRegionID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_DeleteAllByForeignKeyRegionID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAllByForeignKeyRegionID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAllByForeignKeyRegionID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAllByForeignKeyRegionIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAllByForeignKeyRegionIDPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAllCountByForeignKeyRegionID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAllCountByForeignKeyRegionID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectOneWithEmployeeTerritoriesUsingTerritoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectOneWithEmployeeTerritoriesUsingTerritoryID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'Territories_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[Territories] DROP CONSTRAINT [Territories_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'Territories'))
  BEGIN
      ALTER TABLE [dbo].[Territories] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@TerritoryID nvarchar(40)  
		@TerritoryDescription nvarchar(60)  
		@RegionID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'Territories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_Insert]
@TerritoryID nvarchar(40) , 
@TerritoryDescription nvarchar(60) , 
@RegionID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1



--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@TerritoryID IS NULL)
	INSERT INTO [dbo].[Territories]( [TerritoryID],[TerritoryDescription],[RegionID] )
	OUTPUT inserted.*
	VALUES ( DEFAULT,@TerritoryDescription,@RegionID )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[Territories]( [TerritoryID],[TerritoryDescription],[RegionID] )
	OUTPUT inserted.*
	VALUES ( @TerritoryID,@TerritoryDescription,@RegionID )
ELSE
	INSERT INTO [dbo].[Territories]( [TerritoryID],[TerritoryDescription],[RegionID] )
	VALUES ( @TerritoryID,@TerritoryDescription,@RegionID )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@TerritoryID nvarchar(40)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'Territories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_Update]
@GenericUpdateInstructionXml XML,		
@TerritoryID nvarchar(40) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'Territories')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[Territories] ' + 'SET ' + @SetClause + ' WHERE [TerritoryID] = @TerritoryID';

EXEC sp_executesql @SQL, N'@TerritoryID nvarchar(40) ', @TerritoryID = @TerritoryID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@TerritoryID nvarchar(40)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'Territories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_Delete]
@TerritoryID nvarchar(40) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[Territories]
WHERE
[TerritoryID] = @TerritoryID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'Territories'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[Territories]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[Territories] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@TerritoryID nvarchar(40) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Territories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_SelectByPrimaryKey]
@useNoLock BIT = 0,
@TerritoryID nvarchar(40) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[Territories] WITH (NOLOCK)
        WHERE [TerritoryID] = @TerritoryID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[Territories]
        WHERE [TerritoryID] = @TerritoryID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'Territories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Territories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'Territories'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Territories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_Territories_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'Territories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Territories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'Territories' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Territories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Territories_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'Territories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Territories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Territories' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Territories] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Territories] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@TerritoryID nvarchar(40) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Territories' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_ExistsByPrimaryKey]
@TerritoryID nvarchar(40) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[Territories] WITH (NOLOCK)
WHERE [TerritoryID] = @TerritoryID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@TerritoryID nvarchar(40)  
		@TerritoryDescription nvarchar(60)  
		@RegionID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'Territories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_Upsert]
@GenericUpdateInstructionXml XML,
@TerritoryID nvarchar(40) , 
@TerritoryDescription nvarchar(60) , 
@RegionID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON



--Check Primary Key First for Speed
IF (@TerritoryID IS NULL)
	EXEC [dbo].[gsp_Territories_Insert] @TerritoryID,@TerritoryDescription,@RegionID,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[Territories] WHERE [TerritoryID] = @TerritoryID)
	EXEC [dbo].[gsp_Territories_Update] @GenericUpdateInstructionXml, 	@TerritoryID = @TerritoryID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_Territories_Insert] @TerritoryID,@TerritoryDescription,@RegionID,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectOneWithEmployeeTerritoriesUsingTerritoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectOneWithEmployeeTerritoriesUsingTerritoryID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_SelectOneWithEmployeeTerritoriesUsingTerritoryID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@TerritoryID nvarchar(40) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Territories' and also the respective child records from 'EmployeeTerritories'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_SelectOneWithEmployeeTerritoriesUsingTerritoryID]
@useNoLock BIT = 0,
@TerritoryID nvarchar(40) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Territories_SelectByPrimaryKey] @TerritoryID = @TerritoryID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryID]  @TerritoryID = @TerritoryID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAllByForeignKeyRegionID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAllByForeignKeyRegionID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_SelectAllByForeignKeyRegionID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@RegionID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Territories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_SelectAllByForeignKeyRegionID]
@useNoLock BIT = 0,
@RegionID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[Territories] WITH (NOLOCK)
	 WHERE [RegionID] = @RegionID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[Territories] 
	 WHERE [RegionID] = @RegionID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAllByForeignKeyRegionIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAllByForeignKeyRegionIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_SelectAllByForeignKeyRegionIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@RegionID int 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Territories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_SelectAllByForeignKeyRegionIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@RegionID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Territories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Territories_SelectAllByForeignKeyRegionID] @RegionID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [RegionID] = @RegionID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@RegionID int ,
 @PageSize int, @PageNumber int',
			@RegionID = @RegionID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [RegionID] = @RegionID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@RegionID int ,
 @PageSize int, @PageNumber int',
			@RegionID = @RegionID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_SelectAllCountByForeignKeyRegionID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_SelectAllCountByForeignKeyRegionID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_SelectAllCountByForeignKeyRegionID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@RegionID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Territories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_SelectAllCountByForeignKeyRegionID]
@useNoLock BIT = 0,
@RegionID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[Territories] WITH (NOLOCK) 
	  WHERE [RegionID] = @RegionID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[Territories]
	 WHERE [RegionID] = @RegionID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Territories_DeleteAllByForeignKeyRegionID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Territories_DeleteAllByForeignKeyRegionID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Territories_DeleteAllByForeignKeyRegionID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@RegionID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Territories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Territories_DeleteAllByForeignKeyRegionID]
@RegionID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[Territories]
WHERE [RegionID] = @RegionID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				





-- Developer's comment header
-- Suppliers.sql
-- 
-- history:   6/4/2026 10:07:09 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectOneWithProductsUsingSupplierID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectOneWithProductsUsingSupplierID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'Suppliers_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[Suppliers] DROP CONSTRAINT [Suppliers_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'Suppliers'))
  BEGIN
      ALTER TABLE [dbo].[Suppliers] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@SupplierID int  
		@CompanyName nvarchar(40)  
		@ContactName text = null  
		@ContactTitle text = null  
		@Address text = null  
		@City text = null  
		@Region text = null  
		@PostalCode nvarchar(20) = null  
		@Country text = null  
		@Phone text = null  
		@Fax text = null  
		@HomePage text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'Suppliers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_Insert]
@SupplierID int , 
@CompanyName nvarchar(40) , 
@ContactName text = null , 
@ContactTitle text = null , 
@Address text = null , 
@City text = null , 
@Region text = null , 
@PostalCode nvarchar(20) = null , 
@Country text = null , 
@Phone text = null , 
@Fax text = null , 
@HomePage text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1



--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@SupplierID IS NULL)
	INSERT INTO [dbo].[Suppliers]( [CompanyName],[ContactName],[ContactTitle],[Address],[City],[Region],[PostalCode],[Country],[Phone],[Fax],[HomePage] )
	OUTPUT inserted.*
	VALUES ( @CompanyName,@ContactName,@ContactTitle,@Address,@City,@Region,@PostalCode,@Country,@Phone,@Fax,@HomePage )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[Suppliers]( [CompanyName],[ContactName],[ContactTitle],[Address],[City],[Region],[PostalCode],[Country],[Phone],[Fax],[HomePage] )
	OUTPUT inserted.*
	VALUES ( @CompanyName,@ContactName,@ContactTitle,@Address,@City,@Region,@PostalCode,@Country,@Phone,@Fax,@HomePage )
ELSE
	INSERT INTO [dbo].[Suppliers]( [CompanyName],[ContactName],[ContactTitle],[Address],[City],[Region],[PostalCode],[Country],[Phone],[Fax],[HomePage] )
	VALUES ( @CompanyName,@ContactName,@ContactTitle,@Address,@City,@Region,@PostalCode,@Country,@Phone,@Fax,@HomePage )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@SupplierID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'Suppliers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_Update]
@GenericUpdateInstructionXml XML,		
@SupplierID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'Suppliers')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[Suppliers] ' + 'SET ' + @SetClause + ' WHERE [SupplierID] = @SupplierID';

EXEC sp_executesql @SQL, N'@SupplierID int ', @SupplierID = @SupplierID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@SupplierID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'Suppliers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_Delete]
@SupplierID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[Suppliers]
WHERE
[SupplierID] = @SupplierID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'Suppliers'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[Suppliers]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[Suppliers] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@SupplierID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Suppliers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_SelectByPrimaryKey]
@useNoLock BIT = 0,
@SupplierID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[Suppliers] WITH (NOLOCK)
        WHERE [SupplierID] = @SupplierID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[Suppliers]
        WHERE [SupplierID] = @SupplierID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'Suppliers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Suppliers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'Suppliers'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Suppliers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_Suppliers_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'Suppliers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Suppliers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'Suppliers' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Suppliers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Suppliers_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'Suppliers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Suppliers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Suppliers' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Suppliers] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Suppliers] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@SupplierID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Suppliers' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_ExistsByPrimaryKey]
@SupplierID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[Suppliers] WITH (NOLOCK)
WHERE [SupplierID] = @SupplierID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@SupplierID int  
		@CompanyName nvarchar(40)  
		@ContactName text = null  
		@ContactTitle text = null  
		@Address text = null  
		@City text = null  
		@Region text = null  
		@PostalCode nvarchar(20) = null  
		@Country text = null  
		@Phone text = null  
		@Fax text = null  
		@HomePage text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'Suppliers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_Upsert]
@GenericUpdateInstructionXml XML,
@SupplierID int , 
@CompanyName nvarchar(40) , 
@ContactName text = null , 
@ContactTitle text = null , 
@Address text = null , 
@City text = null , 
@Region text = null , 
@PostalCode nvarchar(20) = null , 
@Country text = null , 
@Phone text = null , 
@Fax text = null , 
@HomePage text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON



--Check Primary Key First for Speed
IF (@SupplierID IS NULL)
	EXEC [dbo].[gsp_Suppliers_Insert] @SupplierID,@CompanyName,@ContactName,@ContactTitle,@Address,@City,@Region,@PostalCode,@Country,@Phone,@Fax,@HomePage,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[Suppliers] WHERE [SupplierID] = @SupplierID)
	EXEC [dbo].[gsp_Suppliers_Update] @GenericUpdateInstructionXml, 	@SupplierID = @SupplierID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_Suppliers_Insert] @SupplierID,@CompanyName,@ContactName,@ContactTitle,@Address,@City,@Region,@PostalCode,@Country,@Phone,@Fax,@HomePage,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Suppliers_SelectOneWithProductsUsingSupplierID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Suppliers_SelectOneWithProductsUsingSupplierID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Suppliers_SelectOneWithProductsUsingSupplierID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@SupplierID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Suppliers' and also the respective child records from 'Products'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Suppliers_SelectOneWithProductsUsingSupplierID]
@useNoLock BIT = 0,
@SupplierID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Suppliers_SelectByPrimaryKey] @SupplierID = @SupplierID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_Products_SelectAllByForeignKeySupplierID]  @SupplierID = @SupplierID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				







-- Developer's comment header
-- Customers.sql
-- 
-- history:   6/4/2026 10:07:09 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectOneWithCustomerCustomerDemosUsingCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectOneWithCustomerCustomerDemosUsingCustomerID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectOneWithOrdersUsingCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectOneWithOrdersUsingCustomerID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'Customers_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[Customers] DROP CONSTRAINT [Customers_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'Customers'))
  BEGIN
      ALTER TABLE [dbo].[Customers] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerID nvarchar(5)  
		@CompanyName nvarchar(40)  
		@ContactName text = null  
		@ContactTitle text = null  
		@Address text = null  
		@City nvarchar(60) = null  
		@Region nvarchar(60) = null  
		@PostalCode nvarchar(20) = null  
		@Country text = null  
		@Phone text = null  
		@Fax text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'Customers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_Insert]
@CustomerID nvarchar(5) , 
@CompanyName nvarchar(40) , 
@ContactName text = null , 
@ContactTitle text = null , 
@Address text = null , 
@City nvarchar(60) = null , 
@Region nvarchar(60) = null , 
@PostalCode nvarchar(20) = null , 
@Country text = null , 
@Phone text = null , 
@Fax text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1



--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@CustomerID IS NULL)
	INSERT INTO [dbo].[Customers]( [CustomerID],[CompanyName],[ContactName],[ContactTitle],[Address],[City],[Region],[PostalCode],[Country],[Phone],[Fax] )
	OUTPUT inserted.*
	VALUES ( DEFAULT,@CompanyName,@ContactName,@ContactTitle,@Address,@City,@Region,@PostalCode,@Country,@Phone,@Fax )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[Customers]( [CustomerID],[CompanyName],[ContactName],[ContactTitle],[Address],[City],[Region],[PostalCode],[Country],[Phone],[Fax] )
	OUTPUT inserted.*
	VALUES ( @CustomerID,@CompanyName,@ContactName,@ContactTitle,@Address,@City,@Region,@PostalCode,@Country,@Phone,@Fax )
ELSE
	INSERT INTO [dbo].[Customers]( [CustomerID],[CompanyName],[ContactName],[ContactTitle],[Address],[City],[Region],[PostalCode],[Country],[Phone],[Fax] )
	VALUES ( @CustomerID,@CompanyName,@ContactName,@ContactTitle,@Address,@City,@Region,@PostalCode,@Country,@Phone,@Fax )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@CustomerID nvarchar(5)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'Customers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_Update]
@GenericUpdateInstructionXml XML,		
@CustomerID nvarchar(5) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'Customers')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[Customers] ' + 'SET ' + @SetClause + ' WHERE [CustomerID] = @CustomerID';

EXEC sp_executesql @SQL, N'@CustomerID nvarchar(5) ', @CustomerID = @CustomerID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerID nvarchar(5)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'Customers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_Delete]
@CustomerID nvarchar(5) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[Customers]
WHERE
[CustomerID] = @CustomerID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'Customers'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[Customers]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[Customers] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@CustomerID nvarchar(5) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Customers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_SelectByPrimaryKey]
@useNoLock BIT = 0,
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[Customers] WITH (NOLOCK)
        WHERE [CustomerID] = @CustomerID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[Customers]
        WHERE [CustomerID] = @CustomerID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'Customers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Customers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'Customers'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Customers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_Customers_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'Customers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Customers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'Customers' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Customers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Customers_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'Customers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Customers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Customers' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Customers] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Customers] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerID nvarchar(5) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Customers' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_ExistsByPrimaryKey]
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[Customers] WITH (NOLOCK)
WHERE [CustomerID] = @CustomerID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerID nvarchar(5)  
		@CompanyName nvarchar(40)  
		@ContactName text = null  
		@ContactTitle text = null  
		@Address text = null  
		@City nvarchar(60) = null  
		@Region nvarchar(60) = null  
		@PostalCode nvarchar(20) = null  
		@Country text = null  
		@Phone text = null  
		@Fax text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'Customers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_Upsert]
@GenericUpdateInstructionXml XML,
@CustomerID nvarchar(5) , 
@CompanyName nvarchar(40) , 
@ContactName text = null , 
@ContactTitle text = null , 
@Address text = null , 
@City nvarchar(60) = null , 
@Region nvarchar(60) = null , 
@PostalCode nvarchar(20) = null , 
@Country text = null , 
@Phone text = null , 
@Fax text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON



--Check Primary Key First for Speed
IF (@CustomerID IS NULL)
	EXEC [dbo].[gsp_Customers_Insert] @CustomerID,@CompanyName,@ContactName,@ContactTitle,@Address,@City,@Region,@PostalCode,@Country,@Phone,@Fax,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[Customers] WHERE [CustomerID] = @CustomerID)
	EXEC [dbo].[gsp_Customers_Update] @GenericUpdateInstructionXml, 	@CustomerID = @CustomerID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_Customers_Insert] @CustomerID,@CompanyName,@ContactName,@ContactTitle,@Address,@City,@Region,@PostalCode,@Country,@Phone,@Fax,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectOneWithCustomerCustomerDemosUsingCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectOneWithCustomerCustomerDemosUsingCustomerID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_SelectOneWithCustomerCustomerDemosUsingCustomerID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@CustomerID nvarchar(5) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Customers' and also the respective child records from 'CustomerCustomerDemos'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_SelectOneWithCustomerCustomerDemosUsingCustomerID]
@useNoLock BIT = 0,
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Customers_SelectByPrimaryKey] @CustomerID = @CustomerID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerID]  @CustomerID = @CustomerID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Customers_SelectOneWithOrdersUsingCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Customers_SelectOneWithOrdersUsingCustomerID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Customers_SelectOneWithOrdersUsingCustomerID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@CustomerID nvarchar(5) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Customers' and also the respective child records from 'Orders'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Customers_SelectOneWithOrdersUsingCustomerID]
@useNoLock BIT = 0,
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Customers_SelectByPrimaryKey] @CustomerID = @CustomerID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_Orders_SelectAllByForeignKeyCustomerID]  @CustomerID = @CustomerID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				







-- Developer's comment header
-- CustomerDemographics.sql
-- 
-- history:   6/4/2026 10:07:09 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectOneWithCustomerCustomerDemosUsingCustomerTypeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectOneWithCustomerCustomerDemosUsingCustomerTypeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'CustomerDemographics_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[CustomerDemographics] DROP CONSTRAINT [CustomerDemographics_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'CustomerDemographics'))
  BEGIN
      ALTER TABLE [dbo].[CustomerDemographics] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerTypeID nvarchar(10)  
		@CustomerDesc text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'CustomerDemographics' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_Insert]
@CustomerTypeID nvarchar(10) , 
@CustomerDesc text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1



--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@CustomerTypeID IS NULL)
	INSERT INTO [dbo].[CustomerDemographics]( [CustomerTypeID],[CustomerDesc] )
	OUTPUT inserted.*
	VALUES ( DEFAULT,@CustomerDesc )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[CustomerDemographics]( [CustomerTypeID],[CustomerDesc] )
	OUTPUT inserted.*
	VALUES ( @CustomerTypeID,@CustomerDesc )
ELSE
	INSERT INTO [dbo].[CustomerDemographics]( [CustomerTypeID],[CustomerDesc] )
	VALUES ( @CustomerTypeID,@CustomerDesc )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@CustomerTypeID nvarchar(10)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'CustomerDemographics' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_Update]
@GenericUpdateInstructionXml XML,		
@CustomerTypeID nvarchar(10) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'CustomerDemographics')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[CustomerDemographics] ' + 'SET ' + @SetClause + ' WHERE [CustomerTypeID] = @CustomerTypeID';

EXEC sp_executesql @SQL, N'@CustomerTypeID nvarchar(10) ', @CustomerTypeID = @CustomerTypeID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerTypeID nvarchar(10)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'CustomerDemographics' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_Delete]
@CustomerTypeID nvarchar(10) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[CustomerDemographics]
WHERE
[CustomerTypeID] = @CustomerTypeID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'CustomerDemographics'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[CustomerDemographics]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[CustomerDemographics] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@CustomerTypeID nvarchar(10) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerDemographics' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_SelectByPrimaryKey]
@useNoLock BIT = 0,
@CustomerTypeID nvarchar(10) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[CustomerDemographics] WITH (NOLOCK)
        WHERE [CustomerTypeID] = @CustomerTypeID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[CustomerDemographics]
        WHERE [CustomerTypeID] = @CustomerTypeID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'CustomerDemographics' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerDemographics]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'CustomerDemographics'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerDemographics]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_CustomerDemographics_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'CustomerDemographics' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerDemographics]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'CustomerDemographics' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerDemographics]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_CustomerDemographics_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'CustomerDemographics' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerDemographics]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'CustomerDemographics' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[CustomerDemographics] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[CustomerDemographics] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerTypeID nvarchar(10) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'CustomerDemographics' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_ExistsByPrimaryKey]
@CustomerTypeID nvarchar(10) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[CustomerDemographics] WITH (NOLOCK)
WHERE [CustomerTypeID] = @CustomerTypeID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerTypeID nvarchar(10)  
		@CustomerDesc text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'CustomerDemographics' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_Upsert]
@GenericUpdateInstructionXml XML,
@CustomerTypeID nvarchar(10) , 
@CustomerDesc text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON



--Check Primary Key First for Speed
IF (@CustomerTypeID IS NULL)
	EXEC [dbo].[gsp_CustomerDemographics_Insert] @CustomerTypeID,@CustomerDesc,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[CustomerDemographics] WHERE [CustomerTypeID] = @CustomerTypeID)
	EXEC [dbo].[gsp_CustomerDemographics_Update] @GenericUpdateInstructionXml, 	@CustomerTypeID = @CustomerTypeID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_CustomerDemographics_Insert] @CustomerTypeID,@CustomerDesc,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerDemographics_SelectOneWithCustomerCustomerDemosUsingCustomerTypeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerDemographics_SelectOneWithCustomerCustomerDemosUsingCustomerTypeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerDemographics_SelectOneWithCustomerCustomerDemosUsingCustomerTypeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@CustomerTypeID nvarchar(10) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerDemographics' and also the respective child records from 'CustomerCustomerDemos'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerDemographics_SelectOneWithCustomerCustomerDemosUsingCustomerTypeID]
@useNoLock BIT = 0,
@CustomerTypeID nvarchar(10) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_CustomerDemographics_SelectByPrimaryKey] @CustomerTypeID = @CustomerTypeID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeID]  @CustomerTypeID = @CustomerTypeID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				







-- Developer's comment header
-- CustomerCustomerDemo.sql
-- 
-- history:   6/4/2026 10:07:09 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerTypeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerTypeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerIDPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeIDPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerTypeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerTypeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'CustomerCustomerDemo_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[CustomerCustomerDemo] DROP CONSTRAINT [CustomerCustomerDemo_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'CustomerCustomerDemo'))
  BEGIN
      ALTER TABLE [dbo].[CustomerCustomerDemo] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerID nvarchar(5)  
		@CustomerTypeID nvarchar(10)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'CustomerCustomerDemo' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_Insert]
@CustomerID nvarchar(5) , 
@CustomerTypeID nvarchar(10) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1



--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@CustomerID IS NULL AND
 @CustomerTypeID IS NULL)
	INSERT INTO [dbo].[CustomerCustomerDemo]( [CustomerID],[CustomerTypeID] )
	OUTPUT inserted.*
	VALUES ( DEFAULT,DEFAULT )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[CustomerCustomerDemo]( [CustomerID],[CustomerTypeID] )
	OUTPUT inserted.*
	VALUES ( @CustomerID,@CustomerTypeID )
ELSE
	INSERT INTO [dbo].[CustomerCustomerDemo]( [CustomerID],[CustomerTypeID] )
	VALUES ( @CustomerID,@CustomerTypeID )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@CustomerID nvarchar(5)  
		@CustomerTypeID nvarchar(10)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'CustomerCustomerDemo' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_Update]
@GenericUpdateInstructionXml XML,		
@CustomerID nvarchar(5) , 
@CustomerTypeID nvarchar(10) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'CustomerCustomerDemo')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[CustomerCustomerDemo] ' + 'SET ' + @SetClause + ' WHERE [CustomerID] = @CustomerID
AND [CustomerTypeID] = @CustomerTypeID';

EXEC sp_executesql @SQL, N'@CustomerID nvarchar(5) ,@CustomerTypeID nvarchar(10) ', @CustomerID = @CustomerID,@CustomerTypeID = @CustomerTypeID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerID nvarchar(5)  
		@CustomerTypeID nvarchar(10)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'CustomerCustomerDemo' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_Delete]
@CustomerID nvarchar(5) , 
@CustomerTypeID nvarchar(10) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[CustomerCustomerDemo]
WHERE
[CustomerID] = @CustomerID
AND [CustomerTypeID] = @CustomerTypeID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'CustomerCustomerDemo'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[CustomerCustomerDemo]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[CustomerCustomerDemo] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@CustomerID nvarchar(5) 
		@CustomerTypeID nvarchar(10) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectByPrimaryKey]
@useNoLock BIT = 0,
@CustomerID nvarchar(5) ,
@CustomerTypeID nvarchar(10) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[CustomerCustomerDemo] WITH (NOLOCK)
        WHERE [CustomerID] = @CustomerID
AND [CustomerTypeID] = @CustomerTypeID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[CustomerCustomerDemo]
        WHERE [CustomerID] = @CustomerID
AND [CustomerTypeID] = @CustomerTypeID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerCustomerDemo]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'CustomerCustomerDemo'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerCustomerDemo]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_CustomerCustomerDemo_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerCustomerDemo]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'CustomerCustomerDemo' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerCustomerDemo]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_CustomerCustomerDemo_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerCustomerDemo]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'CustomerCustomerDemo' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[CustomerCustomerDemo] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[CustomerCustomerDemo] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerID nvarchar(5) 
		@CustomerTypeID nvarchar(10) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'CustomerCustomerDemo' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_ExistsByPrimaryKey]
@CustomerID nvarchar(5) ,
@CustomerTypeID nvarchar(10) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[CustomerCustomerDemo] WITH (NOLOCK)
WHERE [CustomerID] = @CustomerID
AND [CustomerTypeID] = @CustomerTypeID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerID nvarchar(5)  
		@CustomerTypeID nvarchar(10)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'CustomerCustomerDemo' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_Upsert]
@GenericUpdateInstructionXml XML,
@CustomerID nvarchar(5) , 
@CustomerTypeID nvarchar(10) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON



--Check Primary Key First for Speed
IF (@CustomerID IS NULL AND
 @CustomerTypeID IS NULL)
	EXEC [dbo].[gsp_CustomerCustomerDemo_Insert] @CustomerID,@CustomerTypeID,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[CustomerCustomerDemo] WHERE [CustomerID] = @CustomerID
AND [CustomerTypeID] = @CustomerTypeID)
	EXEC [dbo].[gsp_CustomerCustomerDemo_Update] @GenericUpdateInstructionXml, 	@CustomerID = @CustomerID,	@CustomerTypeID = @CustomerTypeID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_CustomerCustomerDemo_Insert] @CustomerID,@CustomerTypeID,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				




if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@CustomerID nvarchar(5) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerID]
@useNoLock BIT = 0,
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[CustomerCustomerDemo] WITH (NOLOCK)
	 WHERE [CustomerID] = @CustomerID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[CustomerCustomerDemo] 
	 WHERE [CustomerID] = @CustomerID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@CustomerTypeID nvarchar(10) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeID]
@useNoLock BIT = 0,
@CustomerTypeID nvarchar(10) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[CustomerCustomerDemo] WITH (NOLOCK)
	 WHERE [CustomerTypeID] = @CustomerTypeID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[CustomerCustomerDemo] 
	 WHERE [CustomerTypeID] = @CustomerTypeID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@CustomerID nvarchar(5) 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerCustomerDemo]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerID] @CustomerID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [CustomerID] = @CustomerID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@CustomerID nvarchar(5) ,
 @PageSize int, @PageNumber int',
			@CustomerID = @CustomerID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [CustomerID] = @CustomerID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@CustomerID nvarchar(5) ,
 @PageSize int, @PageNumber int',
			@CustomerID = @CustomerID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@CustomerTypeID nvarchar(10) 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@CustomerTypeID nvarchar(10) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[CustomerCustomerDemo]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_CustomerCustomerDemo_SelectAllByForeignKeyCustomerTypeID] @CustomerTypeID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [CustomerTypeID] = @CustomerTypeID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@CustomerTypeID nvarchar(10) ,
 @PageSize int, @PageNumber int',
			@CustomerTypeID = @CustomerTypeID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [CustomerTypeID] = @CustomerTypeID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@CustomerTypeID nvarchar(10) ,
 @PageSize int, @PageNumber int',
			@CustomerTypeID = @CustomerTypeID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@CustomerID nvarchar(5) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerID]
@useNoLock BIT = 0,
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[CustomerCustomerDemo] WITH (NOLOCK) 
	  WHERE [CustomerID] = @CustomerID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[CustomerCustomerDemo]
	 WHERE [CustomerID] = @CustomerID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerTypeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerTypeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerTypeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@useNoLock bit
		@CustomerTypeID nvarchar(10) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_SelectAllCountByForeignKeyCustomerTypeID]
@useNoLock BIT = 0,
@CustomerTypeID nvarchar(10) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[CustomerCustomerDemo] WITH (NOLOCK) 
	  WHERE [CustomerTypeID] = @CustomerTypeID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[CustomerCustomerDemo]
	 WHERE [CustomerTypeID] = @CustomerTypeID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerID nvarchar(5) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerID]
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[CustomerCustomerDemo]
WHERE [CustomerID] = @CustomerID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerTypeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerTypeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerTypeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:09 PM

INPUTS	: 
		@CustomerTypeID nvarchar(10) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'CustomerCustomerDemo' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_CustomerCustomerDemo_DeleteAllByForeignKeyCustomerTypeID]
@CustomerTypeID nvarchar(10) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[CustomerCustomerDemo]
WHERE [CustomerTypeID] = @CustomerTypeID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				





-- Developer's comment header
-- Employees.sql
-- 
-- history:   6/4/2026 10:07:10 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectOneWithEmployeesUsingReportsTo]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectOneWithEmployeesUsingReportsTo]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectOneWithEmployeeTerritoriesUsingEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectOneWithEmployeeTerritoriesUsingEmployeeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectOneWithOrdersUsingEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectOneWithOrdersUsingEmployeeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'Employees_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[Employees] DROP CONSTRAINT [Employees_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'Employees'))
  BEGIN
      ALTER TABLE [dbo].[Employees] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@EmployeeID int  
		@LastName nvarchar(40)  
		@FirstName nvarchar(40)  
		@Title text = null  
		@TitleOfCourtesy text = null  
		@BirthDate datetime = null  
		@HireDate datetime = null  
		@Address text = null  
		@City text = null  
		@Region text = null  
		@PostalCode nvarchar(20) = null  
		@Country text = null  
		@HomePhone text = null  
		@Extension text = null  
		@Photo image = null  
		@Notes text = null  
		@ReportsTo int = null  
		@PhotoPath text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'Employees' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_Insert]
@EmployeeID int , 
@LastName nvarchar(40) , 
@FirstName nvarchar(40) , 
@Title text = null , 
@TitleOfCourtesy text = null , 
@BirthDate datetime = null , 
@HireDate datetime = null , 
@Address text = null , 
@City text = null , 
@Region text = null , 
@PostalCode nvarchar(20) = null , 
@Country text = null , 
@HomePhone text = null , 
@Extension text = null , 
@Photo image = null , 
@Notes text = null , 
@ReportsTo int = null , 
@PhotoPath text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1



--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@EmployeeID IS NULL)
	INSERT INTO [dbo].[Employees]( [LastName],[FirstName],[Title],[TitleOfCourtesy],[BirthDate],[HireDate],[Address],[City],[Region],[PostalCode],[Country],[HomePhone],[Extension],[Photo],[Notes],[ReportsTo],[PhotoPath] )
	OUTPUT inserted.*
	VALUES ( @LastName,@FirstName,@Title,@TitleOfCourtesy,@BirthDate,@HireDate,@Address,@City,@Region,@PostalCode,@Country,@HomePhone,@Extension,@Photo,@Notes,@ReportsTo,@PhotoPath )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[Employees]( [LastName],[FirstName],[Title],[TitleOfCourtesy],[BirthDate],[HireDate],[Address],[City],[Region],[PostalCode],[Country],[HomePhone],[Extension],[Photo],[Notes],[ReportsTo],[PhotoPath] )
	OUTPUT inserted.*
	VALUES ( @LastName,@FirstName,@Title,@TitleOfCourtesy,@BirthDate,@HireDate,@Address,@City,@Region,@PostalCode,@Country,@HomePhone,@Extension,@Photo,@Notes,@ReportsTo,@PhotoPath )
ELSE
	INSERT INTO [dbo].[Employees]( [LastName],[FirstName],[Title],[TitleOfCourtesy],[BirthDate],[HireDate],[Address],[City],[Region],[PostalCode],[Country],[HomePhone],[Extension],[Photo],[Notes],[ReportsTo],[PhotoPath] )
	VALUES ( @LastName,@FirstName,@Title,@TitleOfCourtesy,@BirthDate,@HireDate,@Address,@City,@Region,@PostalCode,@Country,@HomePhone,@Extension,@Photo,@Notes,@ReportsTo,@PhotoPath )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@EmployeeID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'Employees' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_Update]
@GenericUpdateInstructionXml XML,		
@EmployeeID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'Employees')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[Employees] ' + 'SET ' + @SetClause + ' WHERE [EmployeeID] = @EmployeeID';

EXEC sp_executesql @SQL, N'@EmployeeID int ', @EmployeeID = @EmployeeID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@EmployeeID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'Employees' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_Delete]
@EmployeeID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[Employees]
WHERE
[EmployeeID] = @EmployeeID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'Employees'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[Employees]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[Employees] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@EmployeeID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Employees' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_SelectByPrimaryKey]
@useNoLock BIT = 0,
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[Employees] WITH (NOLOCK)
        WHERE [EmployeeID] = @EmployeeID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[Employees]
        WHERE [EmployeeID] = @EmployeeID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'Employees' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Employees]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'Employees'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Employees]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_Employees_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'Employees' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Employees]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'Employees' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Employees]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Employees_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'Employees' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Employees]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Employees' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Employees] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Employees] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@EmployeeID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Employees' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_ExistsByPrimaryKey]
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[Employees] WITH (NOLOCK)
WHERE [EmployeeID] = @EmployeeID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@EmployeeID int  
		@LastName nvarchar(40)  
		@FirstName nvarchar(40)  
		@Title text = null  
		@TitleOfCourtesy text = null  
		@BirthDate datetime = null  
		@HireDate datetime = null  
		@Address text = null  
		@City text = null  
		@Region text = null  
		@PostalCode nvarchar(20) = null  
		@Country text = null  
		@HomePhone text = null  
		@Extension text = null  
		@Photo image = null  
		@Notes text = null  
		@ReportsTo int = null  
		@PhotoPath text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'Employees' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_Upsert]
@GenericUpdateInstructionXml XML,
@EmployeeID int , 
@LastName nvarchar(40) , 
@FirstName nvarchar(40) , 
@Title text = null , 
@TitleOfCourtesy text = null , 
@BirthDate datetime = null , 
@HireDate datetime = null , 
@Address text = null , 
@City text = null , 
@Region text = null , 
@PostalCode nvarchar(20) = null , 
@Country text = null , 
@HomePhone text = null , 
@Extension text = null , 
@Photo image = null , 
@Notes text = null , 
@ReportsTo int = null , 
@PhotoPath text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON



--Check Primary Key First for Speed
IF (@EmployeeID IS NULL)
	EXEC [dbo].[gsp_Employees_Insert] @EmployeeID,@LastName,@FirstName,@Title,@TitleOfCourtesy,@BirthDate,@HireDate,@Address,@City,@Region,@PostalCode,@Country,@HomePhone,@Extension,@Photo,@Notes,@ReportsTo,@PhotoPath,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[Employees] WHERE [EmployeeID] = @EmployeeID)
	EXEC [dbo].[gsp_Employees_Update] @GenericUpdateInstructionXml, 	@EmployeeID = @EmployeeID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_Employees_Insert] @EmployeeID,@LastName,@FirstName,@Title,@TitleOfCourtesy,@BirthDate,@HireDate,@Address,@City,@Region,@PostalCode,@Country,@HomePhone,@Extension,@Photo,@Notes,@ReportsTo,@PhotoPath,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectOneWithEmployeesUsingReportsTo]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectOneWithEmployeesUsingReportsTo]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_SelectOneWithEmployeesUsingReportsTo
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@EmployeeID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Employees' and also the respective child records from 'Employees'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_SelectOneWithEmployeesUsingReportsTo]
@useNoLock BIT = 0,
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Employees_SelectByPrimaryKey] @EmployeeID = @EmployeeID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_Employees_SelectAllByForeignKeyReportsTo]  @EmployeeID = @EmployeeID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectOneWithEmployeeTerritoriesUsingEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectOneWithEmployeeTerritoriesUsingEmployeeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_SelectOneWithEmployeeTerritoriesUsingEmployeeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@EmployeeID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Employees' and also the respective child records from 'EmployeeTerritories'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_SelectOneWithEmployeeTerritoriesUsingEmployeeID]
@useNoLock BIT = 0,
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Employees_SelectByPrimaryKey] @EmployeeID = @EmployeeID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeID]  @EmployeeID = @EmployeeID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Employees_SelectOneWithOrdersUsingEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Employees_SelectOneWithOrdersUsingEmployeeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Employees_SelectOneWithOrdersUsingEmployeeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@EmployeeID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Employees' and also the respective child records from 'Orders'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Employees_SelectOneWithOrdersUsingEmployeeID]
@useNoLock BIT = 0,
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Employees_SelectByPrimaryKey] @EmployeeID = @EmployeeID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeID]  @EmployeeID = @EmployeeID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				







-- Developer's comment header
-- EmployeeTerritories.sql
-- 
-- history:   6/4/2026 10:07:10 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_DeleteAllByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_DeleteAllByForeignKeyEmployeeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_DeleteAllByForeignKeyTerritoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_DeleteAllByForeignKeyTerritoryID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeIDPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryIDPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllCountByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllCountByForeignKeyEmployeeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllCountByForeignKeyTerritoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllCountByForeignKeyTerritoryID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'EmployeeTerritories_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[EmployeeTerritories] DROP CONSTRAINT [EmployeeTerritories_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'EmployeeTerritories'))
  BEGIN
      ALTER TABLE [dbo].[EmployeeTerritories] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@EmployeeID int  
		@TerritoryID nvarchar(40)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'EmployeeTerritories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_Insert]
@EmployeeID int , 
@TerritoryID nvarchar(40) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1



--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@EmployeeID IS NULL AND
 @TerritoryID IS NULL)
	INSERT INTO [dbo].[EmployeeTerritories]( [EmployeeID],[TerritoryID] )
	OUTPUT inserted.*
	VALUES ( DEFAULT,DEFAULT )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[EmployeeTerritories]( [EmployeeID],[TerritoryID] )
	OUTPUT inserted.*
	VALUES ( @EmployeeID,@TerritoryID )
ELSE
	INSERT INTO [dbo].[EmployeeTerritories]( [EmployeeID],[TerritoryID] )
	VALUES ( @EmployeeID,@TerritoryID )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@EmployeeID int  
		@TerritoryID nvarchar(40)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'EmployeeTerritories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_Update]
@GenericUpdateInstructionXml XML,		
@EmployeeID int , 
@TerritoryID nvarchar(40) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'EmployeeTerritories')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[EmployeeTerritories] ' + 'SET ' + @SetClause + ' WHERE [EmployeeID] = @EmployeeID
AND [TerritoryID] = @TerritoryID';

EXEC sp_executesql @SQL, N'@EmployeeID int ,@TerritoryID nvarchar(40) ', @EmployeeID = @EmployeeID,@TerritoryID = @TerritoryID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@EmployeeID int  
		@TerritoryID nvarchar(40)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'EmployeeTerritories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_Delete]
@EmployeeID int , 
@TerritoryID nvarchar(40) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[EmployeeTerritories]
WHERE
[EmployeeID] = @EmployeeID
AND [TerritoryID] = @TerritoryID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'EmployeeTerritories'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[EmployeeTerritories]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[EmployeeTerritories] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@EmployeeID int 
		@TerritoryID nvarchar(40) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectByPrimaryKey]
@useNoLock BIT = 0,
@EmployeeID int ,
@TerritoryID nvarchar(40) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[EmployeeTerritories] WITH (NOLOCK)
        WHERE [EmployeeID] = @EmployeeID
AND [TerritoryID] = @TerritoryID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[EmployeeTerritories]
        WHERE [EmployeeID] = @EmployeeID
AND [TerritoryID] = @TerritoryID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[EmployeeTerritories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'EmployeeTerritories'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[EmployeeTerritories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_EmployeeTerritories_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[EmployeeTerritories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'EmployeeTerritories' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[EmployeeTerritories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_EmployeeTerritories_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[EmployeeTerritories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'EmployeeTerritories' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[EmployeeTerritories] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[EmployeeTerritories] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@EmployeeID int 
		@TerritoryID nvarchar(40) 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'EmployeeTerritories' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_ExistsByPrimaryKey]
@EmployeeID int ,
@TerritoryID nvarchar(40) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[EmployeeTerritories] WITH (NOLOCK)
WHERE [EmployeeID] = @EmployeeID
AND [TerritoryID] = @TerritoryID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@EmployeeID int  
		@TerritoryID nvarchar(40)  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'EmployeeTerritories' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_Upsert]
@GenericUpdateInstructionXml XML,
@EmployeeID int , 
@TerritoryID nvarchar(40) , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON



--Check Primary Key First for Speed
IF (@EmployeeID IS NULL AND
 @TerritoryID IS NULL)
	EXEC [dbo].[gsp_EmployeeTerritories_Insert] @EmployeeID,@TerritoryID,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[EmployeeTerritories] WHERE [EmployeeID] = @EmployeeID
AND [TerritoryID] = @TerritoryID)
	EXEC [dbo].[gsp_EmployeeTerritories_Update] @GenericUpdateInstructionXml, 	@EmployeeID = @EmployeeID,	@TerritoryID = @TerritoryID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_EmployeeTerritories_Insert] @EmployeeID,@TerritoryID,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				




if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@TerritoryID nvarchar(40) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryID]
@useNoLock BIT = 0,
@TerritoryID nvarchar(40) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[EmployeeTerritories] WITH (NOLOCK)
	 WHERE [TerritoryID] = @TerritoryID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[EmployeeTerritories] 
	 WHERE [TerritoryID] = @TerritoryID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@EmployeeID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeID]
@useNoLock BIT = 0,
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[EmployeeTerritories] WITH (NOLOCK)
	 WHERE [EmployeeID] = @EmployeeID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[EmployeeTerritories] 
	 WHERE [EmployeeID] = @EmployeeID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@TerritoryID nvarchar(40) 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@TerritoryID nvarchar(40) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[EmployeeTerritories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyTerritoryID] @TerritoryID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [TerritoryID] = @TerritoryID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@TerritoryID nvarchar(40) ,
 @PageSize int, @PageNumber int',
			@TerritoryID = @TerritoryID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [TerritoryID] = @TerritoryID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@TerritoryID nvarchar(40) ,
 @PageSize int, @PageNumber int',
			@TerritoryID = @TerritoryID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@EmployeeID int 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[EmployeeTerritories]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_EmployeeTerritories_SelectAllByForeignKeyEmployeeID] @EmployeeID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [EmployeeID] = @EmployeeID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@EmployeeID int ,
 @PageSize int, @PageNumber int',
			@EmployeeID = @EmployeeID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [EmployeeID] = @EmployeeID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@EmployeeID int ,
 @PageSize int, @PageNumber int',
			@EmployeeID = @EmployeeID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllCountByForeignKeyTerritoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllCountByForeignKeyTerritoryID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectAllCountByForeignKeyTerritoryID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@TerritoryID nvarchar(40) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectAllCountByForeignKeyTerritoryID]
@useNoLock BIT = 0,
@TerritoryID nvarchar(40) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[EmployeeTerritories] WITH (NOLOCK) 
	  WHERE [TerritoryID] = @TerritoryID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[EmployeeTerritories]
	 WHERE [TerritoryID] = @TerritoryID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_SelectAllCountByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_SelectAllCountByForeignKeyEmployeeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_SelectAllCountByForeignKeyEmployeeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@EmployeeID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_SelectAllCountByForeignKeyEmployeeID]
@useNoLock BIT = 0,
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[EmployeeTerritories] WITH (NOLOCK) 
	  WHERE [EmployeeID] = @EmployeeID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[EmployeeTerritories]
	 WHERE [EmployeeID] = @EmployeeID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_DeleteAllByForeignKeyTerritoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_DeleteAllByForeignKeyTerritoryID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_DeleteAllByForeignKeyTerritoryID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@TerritoryID nvarchar(40) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_DeleteAllByForeignKeyTerritoryID]
@TerritoryID nvarchar(40) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[EmployeeTerritories]
WHERE [TerritoryID] = @TerritoryID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_EmployeeTerritories_DeleteAllByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_EmployeeTerritories_DeleteAllByForeignKeyEmployeeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_EmployeeTerritories_DeleteAllByForeignKeyEmployeeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@EmployeeID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'EmployeeTerritories' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_EmployeeTerritories_DeleteAllByForeignKeyEmployeeID]
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[EmployeeTerritories]
WHERE [EmployeeID] = @EmployeeID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				





-- Developer's comment header
-- Shippers.sql
-- 
-- history:   6/4/2026 10:07:10 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectOneWithOrdersUsingShipVia]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectOneWithOrdersUsingShipVia]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'Shippers_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[Shippers] DROP CONSTRAINT [Shippers_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'Shippers'))
  BEGIN
      ALTER TABLE [dbo].[Shippers] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@ShipperID int  
		@CompanyName nvarchar(40)  
		@Phone text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'Shippers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_Insert]
@ShipperID int , 
@CompanyName nvarchar(40) , 
@Phone text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1



--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@ShipperID IS NULL)
	INSERT INTO [dbo].[Shippers]( [CompanyName],[Phone] )
	OUTPUT inserted.*
	VALUES ( @CompanyName,@Phone )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[Shippers]( [CompanyName],[Phone] )
	OUTPUT inserted.*
	VALUES ( @CompanyName,@Phone )
ELSE
	INSERT INTO [dbo].[Shippers]( [CompanyName],[Phone] )
	VALUES ( @CompanyName,@Phone )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@ShipperID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'Shippers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_Update]
@GenericUpdateInstructionXml XML,		
@ShipperID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'Shippers')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[Shippers] ' + 'SET ' + @SetClause + ' WHERE [ShipperID] = @ShipperID';

EXEC sp_executesql @SQL, N'@ShipperID int ', @ShipperID = @ShipperID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@ShipperID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'Shippers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_Delete]
@ShipperID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[Shippers]
WHERE
[ShipperID] = @ShipperID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'Shippers'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[Shippers]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[Shippers] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@ShipperID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Shippers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_SelectByPrimaryKey]
@useNoLock BIT = 0,
@ShipperID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[Shippers] WITH (NOLOCK)
        WHERE [ShipperID] = @ShipperID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[Shippers]
        WHERE [ShipperID] = @ShipperID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'Shippers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Shippers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'Shippers'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Shippers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_Shippers_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'Shippers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Shippers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'Shippers' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Shippers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Shippers_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'Shippers' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Shippers]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Shippers' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Shippers] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Shippers] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@ShipperID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Shippers' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_ExistsByPrimaryKey]
@ShipperID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[Shippers] WITH (NOLOCK)
WHERE [ShipperID] = @ShipperID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@ShipperID int  
		@CompanyName nvarchar(40)  
		@Phone text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'Shippers' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_Upsert]
@GenericUpdateInstructionXml XML,
@ShipperID int , 
@CompanyName nvarchar(40) , 
@Phone text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON



--Check Primary Key First for Speed
IF (@ShipperID IS NULL)
	EXEC [dbo].[gsp_Shippers_Insert] @ShipperID,@CompanyName,@Phone,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[Shippers] WHERE [ShipperID] = @ShipperID)
	EXEC [dbo].[gsp_Shippers_Update] @GenericUpdateInstructionXml, 	@ShipperID = @ShipperID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_Shippers_Insert] @ShipperID,@CompanyName,@Phone,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Shippers_SelectOneWithOrdersUsingShipVia]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Shippers_SelectOneWithOrdersUsingShipVia]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Shippers_SelectOneWithOrdersUsingShipVia
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@ShipperID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Shippers' and also the respective child records from 'Orders'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Shippers_SelectOneWithOrdersUsingShipVia]
@useNoLock BIT = 0,
@ShipperID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Shippers_SelectByPrimaryKey] @ShipperID = @ShipperID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_Orders_SelectAllByForeignKeyShipVia]  @ShipperID = @ShipperID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				







-- Developer's comment header
-- Products.sql
-- 
-- history:   6/4/2026 10:07:10 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_DeleteAllByForeignKeyCategoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_DeleteAllByForeignKeyCategoryID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_DeleteAllByForeignKeySupplierID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_DeleteAllByForeignKeySupplierID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllByForeignKeyCategoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllByForeignKeyCategoryID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllByForeignKeyCategoryIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllByForeignKeyCategoryIDPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllByForeignKeySupplierID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllByForeignKeySupplierID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllByForeignKeySupplierIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllByForeignKeySupplierIDPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllCountByForeignKeyCategoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllCountByForeignKeyCategoryID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllCountByForeignKeySupplierID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllCountByForeignKeySupplierID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectOneWithOrderDetailsUsingProductID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectOneWithOrderDetailsUsingProductID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'Products_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[Products] DROP CONSTRAINT [Products_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'Products'))
  BEGIN
      ALTER TABLE [dbo].[Products] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@ProductID int  
		@ProductName nvarchar(40)  
		@SupplierID int = null  
		@CategoryID int = null  
		@QuantityPerUnit text = null  
		@UnitPrice float = null  
		@UnitsInStock smallint = null  
		@UnitsOnOrder smallint = null  
		@ReorderLevel smallint = null  
		@Discontinued bit  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'Products' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_Insert]
@ProductID int , 
@ProductName nvarchar(40) , 
@SupplierID int = null , 
@CategoryID int = null , 
@QuantityPerUnit text = null , 
@UnitPrice float = null , 
@UnitsInStock smallint = null , 
@UnitsOnOrder smallint = null , 
@ReorderLevel smallint = null , 
@Discontinued bit , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1


--Check all columns with defined default values to see if we need to return the generated values
IF (@UnitPrice IS NULL OR
@UnitsInStock IS NULL OR
@UnitsOnOrder IS NULL OR
@ReorderLevel IS NULL OR
@Discontinued IS NULL)
	SET @ShouldReturnValues = 1


--Set default values for all defaulted columns in the table except for newsquentialid()
--since t-sql does not support this in stored procedures
SET @UnitPrice = ISNULL(@UnitPrice, ((0)));
SET @UnitsInStock = ISNULL(@UnitsInStock, ((0)));
SET @UnitsOnOrder = ISNULL(@UnitsOnOrder, ((0)));
SET @ReorderLevel = ISNULL(@ReorderLevel, ((0)));
SET @Discontinued = ISNULL(@Discontinued, ((0)));

--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@ProductID IS NULL)
	INSERT INTO [dbo].[Products]( [ProductName],[SupplierID],[CategoryID],[QuantityPerUnit],[UnitPrice],[UnitsInStock],[UnitsOnOrder],[ReorderLevel],[Discontinued] )
	OUTPUT inserted.*
	VALUES ( @ProductName,@SupplierID,@CategoryID,@QuantityPerUnit,@UnitPrice,@UnitsInStock,@UnitsOnOrder,@ReorderLevel,@Discontinued )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[Products]( [ProductName],[SupplierID],[CategoryID],[QuantityPerUnit],[UnitPrice],[UnitsInStock],[UnitsOnOrder],[ReorderLevel],[Discontinued] )
	OUTPUT inserted.*
	VALUES ( @ProductName,@SupplierID,@CategoryID,@QuantityPerUnit,@UnitPrice,@UnitsInStock,@UnitsOnOrder,@ReorderLevel,@Discontinued )
ELSE
	INSERT INTO [dbo].[Products]( [ProductName],[SupplierID],[CategoryID],[QuantityPerUnit],[UnitPrice],[UnitsInStock],[UnitsOnOrder],[ReorderLevel],[Discontinued] )
	VALUES ( @ProductName,@SupplierID,@CategoryID,@QuantityPerUnit,@UnitPrice,@UnitsInStock,@UnitsOnOrder,@ReorderLevel,@Discontinued )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@ProductID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'Products' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_Update]
@GenericUpdateInstructionXml XML,		
@ProductID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'Products')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[Products] ' + 'SET ' + @SetClause + ' WHERE [ProductID] = @ProductID';

EXEC sp_executesql @SQL, N'@ProductID int ', @ProductID = @ProductID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@ProductID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'Products' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_Delete]
@ProductID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[Products]
WHERE
[ProductID] = @ProductID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'Products'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[Products]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[Products] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@ProductID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectByPrimaryKey]
@useNoLock BIT = 0,
@ProductID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[Products] WITH (NOLOCK)
        WHERE [ProductID] = @ProductID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[Products]
        WHERE [ProductID] = @ProductID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Products]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'Products'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Products]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_Products_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Products]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'Products' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Products]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Products_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Products]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Products' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Products] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Products] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@ProductID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Products' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_ExistsByPrimaryKey]
@ProductID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[Products] WITH (NOLOCK)
WHERE [ProductID] = @ProductID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@ProductID int  
		@ProductName nvarchar(40)  
		@SupplierID int = null  
		@CategoryID int = null  
		@QuantityPerUnit text = null  
		@UnitPrice float = null  
		@UnitsInStock smallint = null  
		@UnitsOnOrder smallint = null  
		@ReorderLevel smallint = null  
		@Discontinued bit  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'Products' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_Upsert]
@GenericUpdateInstructionXml XML,
@ProductID int , 
@ProductName nvarchar(40) , 
@SupplierID int = null , 
@CategoryID int = null , 
@QuantityPerUnit text = null , 
@UnitPrice float = null , 
@UnitsInStock smallint = null , 
@UnitsOnOrder smallint = null , 
@ReorderLevel smallint = null , 
@Discontinued bit , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON


--Set default values for all defaulted columns in the table except for newsquentialid()
--since t-sql does not support this in stored procedures
SET @UnitPrice = ISNULL(@UnitPrice, ((0)));
SET @UnitsInStock = ISNULL(@UnitsInStock, ((0)));
SET @UnitsOnOrder = ISNULL(@UnitsOnOrder, ((0)));
SET @ReorderLevel = ISNULL(@ReorderLevel, ((0)));
SET @Discontinued = ISNULL(@Discontinued, ((0)));

--Check Primary Key First for Speed
IF (@ProductID IS NULL)
	EXEC [dbo].[gsp_Products_Insert] @ProductID,@ProductName,@SupplierID,@CategoryID,@QuantityPerUnit,@UnitPrice,@UnitsInStock,@UnitsOnOrder,@ReorderLevel,@Discontinued,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[Products] WHERE [ProductID] = @ProductID)
	EXEC [dbo].[gsp_Products_Update] @GenericUpdateInstructionXml, 	@ProductID = @ProductID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_Products_Insert] @ProductID,@ProductName,@SupplierID,@CategoryID,@QuantityPerUnit,@UnitPrice,@UnitsInStock,@UnitsOnOrder,@ReorderLevel,@Discontinued,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectOneWithOrderDetailsUsingProductID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectOneWithOrderDetailsUsingProductID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectOneWithOrderDetailsUsingProductID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@ProductID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Products' and also the respective child records from 'Order Details'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectOneWithOrderDetailsUsingProductID]
@useNoLock BIT = 0,
@ProductID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Products_SelectByPrimaryKey] @ProductID = @ProductID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_OrderDetails_SelectAllByForeignKeyProductID]  @ProductID = @ProductID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllByForeignKeyCategoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllByForeignKeyCategoryID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectAllByForeignKeyCategoryID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@CategoryID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectAllByForeignKeyCategoryID]
@useNoLock BIT = 0,
@CategoryID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[Products] WITH (NOLOCK)
	 WHERE [CategoryID] = @CategoryID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[Products] 
	 WHERE [CategoryID] = @CategoryID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllByForeignKeySupplierID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllByForeignKeySupplierID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectAllByForeignKeySupplierID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@SupplierID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectAllByForeignKeySupplierID]
@useNoLock BIT = 0,
@SupplierID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[Products] WITH (NOLOCK)
	 WHERE [SupplierID] = @SupplierID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[Products] 
	 WHERE [SupplierID] = @SupplierID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllByForeignKeyCategoryIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllByForeignKeyCategoryIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectAllByForeignKeyCategoryIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@CategoryID int 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectAllByForeignKeyCategoryIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@CategoryID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Products]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Products_SelectAllByForeignKeyCategoryID] @CategoryID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [CategoryID] = @CategoryID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@CategoryID int ,
 @PageSize int, @PageNumber int',
			@CategoryID = @CategoryID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [CategoryID] = @CategoryID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@CategoryID int ,
 @PageSize int, @PageNumber int',
			@CategoryID = @CategoryID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllByForeignKeySupplierIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllByForeignKeySupplierIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectAllByForeignKeySupplierIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@SupplierID int 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectAllByForeignKeySupplierIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@SupplierID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Products]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Products_SelectAllByForeignKeySupplierID] @SupplierID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [SupplierID] = @SupplierID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@SupplierID int ,
 @PageSize int, @PageNumber int',
			@SupplierID = @SupplierID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [SupplierID] = @SupplierID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@SupplierID int ,
 @PageSize int, @PageNumber int',
			@SupplierID = @SupplierID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllCountByForeignKeyCategoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllCountByForeignKeyCategoryID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectAllCountByForeignKeyCategoryID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@CategoryID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectAllCountByForeignKeyCategoryID]
@useNoLock BIT = 0,
@CategoryID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[Products] WITH (NOLOCK) 
	  WHERE [CategoryID] = @CategoryID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[Products]
	 WHERE [CategoryID] = @CategoryID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_SelectAllCountByForeignKeySupplierID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_SelectAllCountByForeignKeySupplierID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_SelectAllCountByForeignKeySupplierID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@SupplierID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_SelectAllCountByForeignKeySupplierID]
@useNoLock BIT = 0,
@SupplierID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[Products] WITH (NOLOCK) 
	  WHERE [SupplierID] = @SupplierID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[Products]
	 WHERE [SupplierID] = @SupplierID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_DeleteAllByForeignKeyCategoryID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_DeleteAllByForeignKeyCategoryID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_DeleteAllByForeignKeyCategoryID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@CategoryID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_DeleteAllByForeignKeyCategoryID]
@CategoryID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[Products]
WHERE [CategoryID] = @CategoryID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Products_DeleteAllByForeignKeySupplierID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Products_DeleteAllByForeignKeySupplierID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Products_DeleteAllByForeignKeySupplierID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@SupplierID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Products' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Products_DeleteAllByForeignKeySupplierID]
@SupplierID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[Products]
WHERE [SupplierID] = @SupplierID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				





-- Developer's comment header
-- Orders.sql
-- 
-- history:   6/4/2026 10:07:10 PM
--
--

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_Delete]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_DeleteAllByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_DeleteAllByForeignKeyCustomerID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_DeleteAllByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_DeleteAllByForeignKeyEmployeeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_DeleteAllByForeignKeyShipVia]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_DeleteAllByForeignKeyShipVia]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_DeleteByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_ExistsByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_ExistsByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_Insert]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAll]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyCustomerID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyCustomerIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyCustomerIDPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeIDPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyShipVia]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyShipVia]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyShipViaPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyShipViaPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllCount]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllCountByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllCountByForeignKeyCustomerID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllCountByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllCountByForeignKeyEmployeeID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllCountByForeignKeyShipVia]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllCountByForeignKeyShipVia]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectByField]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectByFieldPaged]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectByPrimaryKey]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectOneWithOrderDetailsUsingOrderID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectOneWithOrderDetailsUsingOrderID]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_Update]
GO


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_Upsert]
GO




------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'Orders_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [Orders_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'Orders'))
  BEGIN
      ALTER TABLE [dbo].[Orders] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@OrderID int  
		@CustomerID nvarchar(5) = null  
		@EmployeeID int = null  
		@OrderDate datetime = null  
		@RequiredDate datetime = null  
		@ShippedDate datetime = null  
		@ShipVia int = null  
		@Freight float = null  
		@ShipName text = null  
		@ShipAddress text = null  
		@ShipCity text = null  
		@ShipRegion text = null  
		@ShipPostalCode nvarchar(20) = null  
		@ShipCountry text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'Orders' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_Insert]
@OrderID int , 
@CustomerID nvarchar(5) = null , 
@EmployeeID int = null , 
@OrderDate datetime = null , 
@RequiredDate datetime = null , 
@ShippedDate datetime = null , 
@ShipVia int = null , 
@Freight float = null , 
@ShipName text = null , 
@ShipAddress text = null , 
@ShipCity text = null , 
@ShipRegion text = null , 
@ShipPostalCode nvarchar(20) = null , 
@ShipCountry text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1


--Check all columns with defined default values to see if we need to return the generated values
IF (@Freight IS NULL)
	SET @ShouldReturnValues = 1


--Set default values for all defaulted columns in the table except for newsquentialid()
--since t-sql does not support this in stored procedures
SET @Freight = ISNULL(@Freight, ((0)));

--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@OrderID IS NULL)
	INSERT INTO [dbo].[Orders]( [CustomerID],[EmployeeID],[OrderDate],[RequiredDate],[ShippedDate],[ShipVia],[Freight],[ShipName],[ShipAddress],[ShipCity],[ShipRegion],[ShipPostalCode],[ShipCountry] )
	OUTPUT inserted.*
	VALUES ( @CustomerID,@EmployeeID,@OrderDate,@RequiredDate,@ShippedDate,@ShipVia,@Freight,@ShipName,@ShipAddress,@ShipCity,@ShipRegion,@ShipPostalCode,@ShipCountry )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[Orders]( [CustomerID],[EmployeeID],[OrderDate],[RequiredDate],[ShippedDate],[ShipVia],[Freight],[ShipName],[ShipAddress],[ShipCity],[ShipRegion],[ShipPostalCode],[ShipCountry] )
	OUTPUT inserted.*
	VALUES ( @CustomerID,@EmployeeID,@OrderDate,@RequiredDate,@ShippedDate,@ShipVia,@Freight,@ShipName,@ShipAddress,@ShipCity,@ShipRegion,@ShipPostalCode,@ShipCountry )
ELSE
	INSERT INTO [dbo].[Orders]( [CustomerID],[EmployeeID],[OrderDate],[RequiredDate],[ShippedDate],[ShipVia],[Freight],[ShipName],[ShipAddress],[ShipCity],[ShipRegion],[ShipPostalCode],[ShipCountry] )
	VALUES ( @CustomerID,@EmployeeID,@OrderDate,@RequiredDate,@ShippedDate,@ShipVia,@Freight,@ShipName,@ShipAddress,@ShipCity,@ShipRegion,@ShipPostalCode,@ShipCountry )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@OrderID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'Orders' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_Update]
@GenericUpdateInstructionXml XML,		
@OrderID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'Orders')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[Orders] ' + 'SET ' + @SetClause + ' WHERE [OrderID] = @OrderID';

EXEC sp_executesql @SQL, N'@OrderID int ', @OrderID = @OrderID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@OrderID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'Orders' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_Delete]
@OrderID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[Orders]
WHERE
[OrderID] = @OrderID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'Orders'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[Orders]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[Orders] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@OrderID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectByPrimaryKey]
@useNoLock BIT = 0,
@OrderID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[Orders] WITH (NOLOCK)
        WHERE [OrderID] = @OrderID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[Orders]
        WHERE [OrderID] = @OrderID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Orders]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'Orders'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Orders]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_Orders_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Orders]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'Orders' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Orders]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Orders_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Orders]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Orders' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Orders] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Orders] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@OrderID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Orders' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_ExistsByPrimaryKey]
@OrderID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[Orders] WITH (NOLOCK)
WHERE [OrderID] = @OrderID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@OrderID int  
		@CustomerID nvarchar(5) = null  
		@EmployeeID int = null  
		@OrderDate datetime = null  
		@RequiredDate datetime = null  
		@ShippedDate datetime = null  
		@ShipVia int = null  
		@Freight float = null  
		@ShipName text = null  
		@ShipAddress text = null  
		@ShipCity text = null  
		@ShipRegion text = null  
		@ShipPostalCode nvarchar(20) = null  
		@ShipCountry text = null  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'Orders' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_Upsert]
@GenericUpdateInstructionXml XML,
@OrderID int , 
@CustomerID nvarchar(5) = null , 
@EmployeeID int = null , 
@OrderDate datetime = null , 
@RequiredDate datetime = null , 
@ShippedDate datetime = null , 
@ShipVia int = null , 
@Freight float = null , 
@ShipName text = null , 
@ShipAddress text = null , 
@ShipCity text = null , 
@ShipRegion text = null , 
@ShipPostalCode nvarchar(20) = null , 
@ShipCountry text = null , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON


--Set default values for all defaulted columns in the table except for newsquentialid()
--since t-sql does not support this in stored procedures
SET @Freight = ISNULL(@Freight, ((0)));

--Check Primary Key First for Speed
IF (@OrderID IS NULL)
	EXEC [dbo].[gsp_Orders_Insert] @OrderID,@CustomerID,@EmployeeID,@OrderDate,@RequiredDate,@ShippedDate,@ShipVia,@Freight,@ShipName,@ShipAddress,@ShipCity,@ShipRegion,@ShipPostalCode,@ShipCountry,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[Orders] WHERE [OrderID] = @OrderID)
	EXEC [dbo].[gsp_Orders_Update] @GenericUpdateInstructionXml, 	@OrderID = @OrderID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_Orders_Insert] @OrderID,@CustomerID,@EmployeeID,@OrderDate,@RequiredDate,@ShippedDate,@ShipVia,@Freight,@ShipName,@ShipAddress,@ShipCity,@ShipRegion,@ShipPostalCode,@ShipCountry,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectOneWithOrderDetailsUsingOrderID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectOneWithOrderDetailsUsingOrderID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectOneWithOrderDetailsUsingOrderID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@OrderID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' and also the respective child records from 'Order Details'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectOneWithOrderDetailsUsingOrderID]
@useNoLock BIT = 0,
@OrderID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the main table
											
EXEC [dbo].[gsp_Orders_SelectByPrimaryKey] @OrderID = @OrderID ,@dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
EXEC [dbo].[gsp_OrderDetails_SelectAllByForeignKeyOrderID]  @OrderID = @OrderID, @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				


if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyCustomerID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllByForeignKeyCustomerID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@CustomerID nvarchar(5) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllByForeignKeyCustomerID]
@useNoLock BIT = 0,
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[Orders] WITH (NOLOCK)
	 WHERE [CustomerID] = @CustomerID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[Orders] 
	 WHERE [CustomerID] = @CustomerID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllByForeignKeyEmployeeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@EmployeeID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeID]
@useNoLock BIT = 0,
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[Orders] WITH (NOLOCK)
	 WHERE [EmployeeID] = @EmployeeID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[Orders] 
	 WHERE [EmployeeID] = @EmployeeID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyShipVia]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyShipVia]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllByForeignKeyShipVia
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@ShipperID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllByForeignKeyShipVia]
@useNoLock BIT = 0,
@ShipperID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[Orders] WITH (NOLOCK)
	 WHERE [ShipVia] = @ShipperID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[Orders] 
	 WHERE [ShipVia] = @ShipperID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyCustomerIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyCustomerIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllByForeignKeyCustomerIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@CustomerID nvarchar(5) 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllByForeignKeyCustomerIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Orders]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Orders_SelectAllByForeignKeyCustomerID] @CustomerID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [CustomerID] = @CustomerID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@CustomerID nvarchar(5) ,
 @PageSize int, @PageNumber int',
			@CustomerID = @CustomerID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [CustomerID] = @CustomerID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@CustomerID nvarchar(5) ,
 @PageSize int, @PageNumber int',
			@CustomerID = @CustomerID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllByForeignKeyEmployeeIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@EmployeeID int 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Orders]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Orders_SelectAllByForeignKeyEmployeeID] @EmployeeID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [EmployeeID] = @EmployeeID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@EmployeeID int ,
 @PageSize int, @PageNumber int',
			@EmployeeID = @EmployeeID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [EmployeeID] = @EmployeeID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@EmployeeID int ,
 @PageSize int, @PageNumber int',
			@EmployeeID = @EmployeeID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllByForeignKeyShipViaPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllByForeignKeyShipViaPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllByForeignKeyShipViaPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@ShipperID int 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllByForeignKeyShipViaPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@ShipperID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Orders]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_Orders_SelectAllByForeignKeyShipVia] @ShipperID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [ShipVia] = @ShipperID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@ShipperID int ,
 @PageSize int, @PageNumber int',
			@ShipperID = @ShipperID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [ShipVia] = @ShipperID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@ShipperID int ,
 @PageSize int, @PageNumber int',
			@ShipperID = @ShipperID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllCountByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllCountByForeignKeyCustomerID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllCountByForeignKeyCustomerID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@CustomerID nvarchar(5) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllCountByForeignKeyCustomerID]
@useNoLock BIT = 0,
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[Orders] WITH (NOLOCK) 
	  WHERE [CustomerID] = @CustomerID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[Orders]
	 WHERE [CustomerID] = @CustomerID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllCountByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllCountByForeignKeyEmployeeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllCountByForeignKeyEmployeeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@EmployeeID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllCountByForeignKeyEmployeeID]
@useNoLock BIT = 0,
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[Orders] WITH (NOLOCK) 
	  WHERE [EmployeeID] = @EmployeeID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[Orders]
	 WHERE [EmployeeID] = @EmployeeID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_SelectAllCountByForeignKeyShipVia]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_SelectAllCountByForeignKeyShipVia]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_SelectAllCountByForeignKeyShipVia
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@ShipperID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_SelectAllCountByForeignKeyShipVia]
@useNoLock BIT = 0,
@ShipperID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[Orders] WITH (NOLOCK) 
	  WHERE [ShipVia] = @ShipperID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[Orders]
	 WHERE [ShipVia] = @ShipperID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_DeleteAllByForeignKeyCustomerID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_DeleteAllByForeignKeyCustomerID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_DeleteAllByForeignKeyCustomerID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@CustomerID nvarchar(5) 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_DeleteAllByForeignKeyCustomerID]
@CustomerID nvarchar(5) ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[Orders]
WHERE [CustomerID] = @CustomerID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_DeleteAllByForeignKeyEmployeeID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_DeleteAllByForeignKeyEmployeeID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_DeleteAllByForeignKeyEmployeeID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@EmployeeID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_DeleteAllByForeignKeyEmployeeID]
@EmployeeID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[Orders]
WHERE [EmployeeID] = @EmployeeID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_Orders_DeleteAllByForeignKeyShipVia]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_Orders_DeleteAllByForeignKeyShipVia]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Orders_DeleteAllByForeignKeyShipVia
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@ShipperID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Orders' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_Orders_DeleteAllByForeignKeyShipVia]
@ShipperID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[Orders]
WHERE [ShipVia] = @ShipperID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				





-- Developer's comment header
-- Order Details.sql
-- 
-- history:   6/4/2026 10:07:10 PM
--
--



------------------------------------------------------------------------
  --DROP the DLGTimeStamp column that was needed for Optimistic locking
  IF EXISTS (SELECT 1 from sys.objects where name = 'Order Details_DLGTimeStamp_DF')
	  ALTER TABLE [dbo].[Order Details] DROP CONSTRAINT [Order Details_DLGTimeStamp_DF]
  GO
  
  IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'DLGTimeStamp' AND OBJECT_ID = OBJECT_ID(N'Order Details'))
  BEGIN
      ALTER TABLE [dbo].[Order Details] DROP COLUMN DLGTimeStamp
  END
  GO
------------------------------------------------------------------------

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_Insert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_Insert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_Insert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@OrderID int  
		@ProductID int  
		@UnitPrice float  
		@Quantity smallint  
		@Discount float  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert 1 row in the table 'Order Details' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_Insert]
@OrderID int , 
@ProductID int , 
@UnitPrice float , 
@Quantity smallint , 
@Discount float , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

DECLARE @ShouldReturnValues AS BIT = 1


--Check all columns with defined default values to see if we need to return the generated values
IF (@UnitPrice IS NULL OR
@Quantity IS NULL OR
@Discount IS NULL)
	SET @ShouldReturnValues = 1


--Set default values for all defaulted columns in the table except for newsquentialid()
--since t-sql does not support this in stored procedures
SET @UnitPrice = ISNULL(@UnitPrice, ((0)));
SET @Quantity = ISNULL(@Quantity, ((1)));
SET @Discount = ISNULL(@Discount, ((0)));

--Insert default value if the primary key column is using a default value of newsequentialid().
--Otherwise, we should just check @ShouldReturnValues = 1
IF (@OrderID IS NULL AND
 @ProductID IS NULL)
	INSERT INTO [dbo].[Order Details]( [OrderID],[ProductID],[UnitPrice],[Quantity],[Discount] )
	OUTPUT inserted.*
	VALUES ( DEFAULT,DEFAULT,@UnitPrice,@Quantity,@Discount )
ELSE IF (@ShouldReturnValues = 1)
	INSERT INTO [dbo].[Order Details]( [OrderID],[ProductID],[UnitPrice],[Quantity],[Discount] )
	OUTPUT inserted.*
	VALUES ( @OrderID,@ProductID,@UnitPrice,@Quantity,@Discount )
ELSE
	INSERT INTO [dbo].[Order Details]( [OrderID],[ProductID],[UnitPrice],[Quantity],[Discount] )
	VALUES ( @OrderID,@ProductID,@UnitPrice,@Quantity,@Discount )


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_Update]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_Update]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_Update
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@GenericUpdateInstruction dbo.GenericUpdateInstruction
		@OrderID int  
		@ProductID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will update 1 row in the table 'Order Details' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_Update]
@GenericUpdateInstructionXml XML,		
@OrderID int , 
@ProductID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- UPDATE a row in the table
DECLARE @SQL NVARCHAR(MAX);
DECLARE @SetClause NVARCHAR(MAX) = '';

DECLARE @UpdateData TABLE
    (
        ColumnName SYSNAME,
        NewValue NVARCHAR(MAX)
    );

    INSERT INTO @UpdateData (ColumnName, NewValue)
SELECT
    T.X.value('(ColumnName)[1]', 'SYSNAME'),
    NULLIF(T.X.value('(NewValue)[1]', 'NVARCHAR(MAX)'), '')
FROM @GenericUpdateInstructionXml.nodes('/DocumentElement/Table') AS T(X);

-- Build SetClause
SELECT @SetClause = STRING_AGG(
    QUOTENAME(s.ColumnName) + ' = ' +
    CASE 
        WHEN s.NewValue IS NOT NULL 
            THEN 'CAST(''' + REPLACE(CONVERT(NVARCHAR(MAX), s.NewValue), '''', '''''') + ''' AS ' + t.name +
                 CASE
                      WHEN t.name IN ('decimal', 'numeric')
                           THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                      WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                      WHEN t.name IN ('nvarchar', 'nchar')
                           THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                      ELSE ''
                 END + ')'
        WHEN dc.definition IS NOT NULL
        THEN CASE 
                 WHEN CHARINDEX('(', dc.definition) = 1 
                      AND CHARINDEX(')', REVERSE(dc.definition)) = 1
                 THEN SUBSTRING(dc.definition, 2, LEN(dc.definition) - 2)
                 ELSE dc.definition
             END
    ELSE 'NULL'
    END,
    ', '
)
FROM @UpdateData s
JOIN sys.columns c 
    ON c.name = s.ColumnName 
    AND c.object_id = OBJECT_ID('dbo.' + 'Order Details')
JOIN sys.types t 
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id 
    AND dc.parent_column_id = c.column_id

-- Construct SQL
SET @SQL = 'UPDATE [dbo].[Order Details] ' + 'SET ' + @SetClause + ' WHERE [OrderID] = @OrderID
AND [ProductID] = @ProductID';

EXEC sp_executesql @SQL, N'@OrderID int ,@ProductID int ', @OrderID = @OrderID,@ProductID = @ProductID;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_Delete]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_Delete]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_Delete
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@OrderID int  
		@ProductID int  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete 1 row from the table 'Order Details' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_Delete]
@OrderID int , 
@ProductID int , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON

-- DELETE a row from the table
DELETE FROM [dbo].[Order Details]
WHERE
[OrderID] = @OrderID
AND [ProductID] = @ProductID
				

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_DeleteByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_DeleteByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_DeleteByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will delete row(s) from the table 'Order Details'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_DeleteByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- DELETE row(s) from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')


IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
    SET @query = 'DELETE FROM [dbo].[Order Details]' + @whereClause;
END
ELSE
BEGIN
    SET @query = 'DELETE FROM [dbo].[Order Details] WHERE [' + @Field + '] = @Value';
END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@OrderID int 
		@ProductID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectByPrimaryKey]
@useNoLock BIT = 0,
@OrderID int ,
@ProductID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
					
IF @useNoLock = 1
BEGIN
        SELECT *
        FROM [dbo].[Order Details] WITH (NOLOCK)
        WHERE [OrderID] = @OrderID
AND [ProductID] = @ProductID;
END
ELSE
BEGIN
        SELECT *
        FROM [dbo].[Order Details]
        WHERE [OrderID] = @OrderID
AND [ProductID] = @ProductID;
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectAll]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectAll]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectAll
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@numberOfRecordsToReturn int
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectAll]
@whereClause NVARCHAR(MAX) = NULL,
@numberOfRecordsToReturn int = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Order Details]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

IF @numberOfRecordsToReturn IS NOT NULL
    BEGIN
        -- Limit the number of rows using TOP if @numberOfRecordsToReturn is provided
        SET @sql = 'SELECT TOP (' + CAST(@numberOfRecordsToReturn AS NVARCHAR) + ') * FROM ' + @tableClause;
    END
ELSE
    BEGIN
        -- If no limit, select all rows
        SET @sql = 'SELECT * FROM ' + @tableClause;
    END

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectAllPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectAllPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectAllPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table 'Order Details'
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectAllPaged]
@PageSize int = NULL,
@PageNumber int = NULL,
@OrderByStatement varchar(100)= NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Order Details]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

	
IF(@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL)
	EXEC [dbo].[gsp_OrderDetails_SelectAll] @dlgErrorCode=@dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY';

	EXEC sp_executesql @Query, 
	                   N'@PageNumber INT, @PageSize INT', 
	                   @PageNumber = @PageNumber, @PageSize = @PageSize;
END
ELSE
BEGIN
	SET @Query = 
		'SELECT	*, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber 
		FROM ' + @tableClause + '
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
	
	EXEC sp_executesql @Query,N'@PageNumber int, @PageSize int', @PageNumber=@PageNumber, @PageSize=@PageSize
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@OrderByField varchar(100)
		@OrderByDirection varchar(4)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select row(s) from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectByField]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@OrderByField varchar(100) = NULL,
@OrderByDirection varchar(4) = 'ASC', 
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @Operation = REPLACE(@Operation,'''','''''')
SET @OrderByField = ISNULL(@OrderByField, '')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Order Details]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


SET @Query = N'SELECT *
			FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field, ']', ']]') + ']'

IF @Value2 IS NOT NULL AND @Value2 <> ''
	BEGIN
	SET @Query = @Query + N' BETWEEN @Value AND @Value2'
		SET @ParamDefinition = N'@Value nvarchar(2000), @Value2 nvarchar(2000)'
	END
ELSE
	BEGIN
	SET @Query = @Query + @Operation + N' @Value'
		SET @ParamDefinition = N'@Value nvarchar(2000)'
	END

-- Check if the OrderByField and OrderByDirection are provided
IF @OrderByField IS NOT NULL AND @OrderByField <> ''
BEGIN    
    SET @OrderByDirection = UPPER(ISNULL(@OrderByDirection, 'ASC')) -- Default to ASC if not specified
	
	-- Append the ORDER BY clause to the query
    SET @Query = @Query + N' ORDER BY [' + REPLACE(@OrderByField, ']', ']]') + N'] ' + @OrderByDirection
END

-- Execute the final query with correct parameters
IF @Value2 IS NOT NULL AND @Value2 <> ''
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value, @Value2
END
ELSE
BEGIN
    EXEC sp_executesql @Query, @ParamDefinition, @Value
END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectByFieldPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectByFieldPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectByFieldPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@Value2 varchar(1000)
		@Operation varchar(10)
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select the specified number of entries from the specified record number in the table'Order Details' 
				using the value of the field specified
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectByFieldPaged]
@Field varchar(100),
@Value varchar(1000),
@Value2 varchar(1000)='',
@Operation varchar(10),
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
SET @Value = REPLACE(@Value,'''','''''')
SET @Value2 = REPLACE(@Value2,'''','''''')
SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
-- SELECT row(s) from the table
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Order Details]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;


IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_OrderDetails_SelectByField] @Field, @Value, @Value2, @Operation, @dlgErrorCode, @useNoLock
ELSE IF (@Value2 IS NOT NULL AND @Value2 <> '' AND @Operation = 'Between')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement+') AS RowNumber
		FROM ' + @tableClause + '
		WHERE [' + REPLACE(@Field,']',']]') + '] BETWEEN  @Value AND @Value2
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END 
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
	SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement + ') AS RowNumber
		FROM ' + @tableClause + ' WHERE [' + REPLACE(@Field,']',']]') + ']' + @Operation + ' @Value
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@Value nvarchar(max), @Value2 nvarchar(max), @PageSize int, @PageNumber int',
			@Value = @Value,
			@Value2 = @Value2,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectAllCount]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectAllCount]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectAllCount
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@whereClause varchar(MAX)
		@useNoLock bit
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select all rows count from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectAllCount]
@whereClause NVARCHAR(MAX) = NULL,
@useNoLock BIT = 0,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT all row(s) count from the table

DECLARE @sql NVARCHAR(MAX);
DECLARE @tableClause NVARCHAR(200);

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Order Details]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;

SET @sql = 'SELECT Count(*) FROM ' + @tableClause;
IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
    BEGIN
        SET @sql = @sql  + @whereClause;
    END
	
-- Execute the dynamically built SQL query
EXEC sp_executesql @sql;


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_ExistsByField]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_ExistsByField]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_ExistsByField
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@Field varchar(100)
		@Value varchar(1000)
		@whereClause varchar(MAX)

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Order Details' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_ExistsByField]
@Field varchar(100) = NULL,
@Value varchar(1000) = NULL,
@whereClause varchar(MAX) = NULL,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
DECLARE @query nvarchar(2000)
DECLARE @ParamDefinition nvarchar(500)
SET @ParamDefinition = '@Value nvarchar(2000)'
DECLARE @FieldValue nvarchar(2000)
SET @FieldValue = REPLACE(@Value, '''', '''''')

IF @whereClause IS NOT NULL AND LTRIM(RTRIM(@whereClause)) <> ''
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Order Details] WITH (NOLOCK)
	' + @whereClause + ')
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
ELSE
BEGIN
	SET @query = 'SELECT CASE WHEN EXISTS (SELECT 1
	FROM [dbo].[Order Details] WITH (NOLOCK)
	WHERE [' + @Field + '] = @Value)
	THEN CAST (1 AS BIT)
	ELSE CAST (0 AS BIT) END'
END
EXEC sp_executesql @query, @ParamDefinition, @Value = @FieldValue


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_ExistsByPrimaryKey]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_ExistsByPrimaryKey]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_ExistsByPrimaryKey
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@OrderID int 
		@ProductID int 

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will check if the row exists in the table 'Order Details' or not
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_ExistsByPrimaryKey]
@OrderID int ,
@ProductID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON
-- SELECT row from the table
SELECT CASE WHEN EXISTS (SELECT 1
FROM [dbo].[Order Details] WITH (NOLOCK)
WHERE [OrderID] = @OrderID
AND [ProductID] = @ProductID)
THEN CAST (1 AS BIT)
ELSE CAST (0 AS BIT) END


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				

if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_Upsert]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_Upsert]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_Upsert
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@OrderID int  
		@ProductID int  
		@UnitPrice float  
		@Quantity smallint  
		@Discount float  

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will insert/Update 1 row in the table 'Order Details' 

----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_Upsert]
@GenericUpdateInstructionXml XML,
@OrderID int , 
@ProductID int , 
@UnitPrice float , 
@Quantity smallint , 
@Discount float , 
@dlgErrorCode int OUTPUT

AS

SET NOCOUNT ON


--Set default values for all defaulted columns in the table except for newsquentialid()
--since t-sql does not support this in stored procedures
SET @UnitPrice = ISNULL(@UnitPrice, ((0)));
SET @Quantity = ISNULL(@Quantity, ((1)));
SET @Discount = ISNULL(@Discount, ((0)));

--Check Primary Key First for Speed
IF (@OrderID IS NULL AND
 @ProductID IS NULL)
	EXEC [dbo].[gsp_OrderDetails_Insert] @OrderID,@ProductID,@UnitPrice,@Quantity,@Discount,@dlgErrorCode
--Check if record exists for update, if not insert.
ELSE IF EXISTS(SELECT 1 FROM [dbo].[Order Details] WHERE [OrderID] = @OrderID
AND [ProductID] = @ProductID)
	EXEC [dbo].[gsp_OrderDetails_Update] @GenericUpdateInstructionXml, 	@OrderID = @OrderID,	@ProductID = @ProductID,@dlgErrorCode = @dlgErrorCode
ELSE
	EXEC [dbo].[gsp_OrderDetails_Insert] @OrderID,@ProductID,@UnitPrice,@Quantity,@Discount,@dlgErrorCode


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				




if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectAllByForeignKeyProductID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectAllByForeignKeyProductID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectAllByForeignKeyProductID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@ProductID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectAllByForeignKeyProductID]
@useNoLock BIT = 0,
@ProductID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[Order Details] WITH (NOLOCK)
	 WHERE [ProductID] = @ProductID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[Order Details] 
	 WHERE [ProductID] = @ProductID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectAllByForeignKeyOrderID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectAllByForeignKeyOrderID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectAllByForeignKeyOrderID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@OrderID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectAllByForeignKeyOrderID]
@useNoLock BIT = 0,
@OrderID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- SELECT a row from the table
						
IF @useNoLock = 1
BEGIN
     SELECT *
	 FROM [dbo].[Order Details] WITH (NOLOCK)
	 WHERE [OrderID] = @OrderID
END
ELSE
BEGIN
	 SELECT *
	 FROM [dbo].[Order Details] 
	 WHERE [OrderID] = @OrderID
END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectAllByForeignKeyProductIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectAllByForeignKeyProductIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectAllByForeignKeyProductIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@ProductID int 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectAllByForeignKeyProductIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@ProductID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Order Details]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_OrderDetails_SelectAllByForeignKeyProductID] @ProductID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [ProductID] = @ProductID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@ProductID int ,
 @PageSize int, @PageNumber int',
			@ProductID = @ProductID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [ProductID] = @ProductID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@ProductID int ,
 @PageSize int, @PageNumber int',
			@ProductID = @ProductID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectAllByForeignKeyOrderIDPaged]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectAllByForeignKeyOrderIDPaged]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectAllByForeignKeyOrderIDPaged
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	:
		@PageSize int
		@PageNumber int
		@OrderByStatement varchar(100)
		@useNoLock bit
		@OrderID int 
		
		
OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectAllByForeignKeyOrderIDPaged]
@PageSize int,
@PageNumber int,
@OrderByStatement varchar(100),
@useNoLock BIT = 0,
@OrderID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

SET @OrderByStatement = REPLACE(@OrderByStatement,'''','''''')
DECLARE @Query nvarchar(max);
DECLARE @tableClause NVARCHAR(200);

-- SELECT a row from the table

-- Build table clause with or without NOLOCK
SET @tableClause = '[dbo].[Order Details]' + CASE WHEN @useNoLock = 1 THEN ' WITH (NOLOCK)' ELSE '' END;
	
IF (@PageSize IS NULL OR @PageSize = 0 OR @PageNumber IS NULL OR @PageNumber = 0)
	EXEC [dbo].[gsp_OrderDetails_SelectAllByForeignKeyOrderID] @OrderID, @dlgErrorCode, @useNoLock=@useNoLock
ELSE IF (@OrderByStatement IS NULL OR @OrderByStatement = '')
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) as RowNumber
		FROM ' + @tableClause + '
		WHERE [OrderID] = @OrderID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'
		
		EXEC sp_executesql @Query,
			N'@OrderID int ,
 @PageSize int, @PageNumber int',
			@OrderID = @OrderID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
ELSE
	BEGIN
		SET @Query = '
		SELECT *, ROW_NUMBER() OVER(ORDER BY '+ @OrderByStatement +') AS RowNumber
		FROM ' + @tableClause + ' 
		WHERE [OrderID] = @OrderID
		ORDER BY RowNumber
		OFFSET (@PageNumber - 1) * @PageSize ROWS
		FETCH NEXT @PageSize ROWS ONLY'

		EXEC sp_executesql @Query,
			N'@OrderID int ,
 @PageSize int, @PageNumber int',
			@OrderID = @OrderID ,
			@PageSize = @PageSize,
			@PageNumber = @PageNumber;
	END
	

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectAllCountByForeignKeyProductID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectAllCountByForeignKeyProductID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectAllCountByForeignKeyProductID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@ProductID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectAllCountByForeignKeyProductID]
@useNoLock BIT = 0,
@ProductID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[Order Details] WITH (NOLOCK) 
	  WHERE [ProductID] = @ProductID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[Order Details]
	 WHERE [ProductID] = @ProductID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_SelectAllCountByForeignKeyOrderID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_SelectAllCountByForeignKeyOrderID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_SelectAllCountByForeignKeyOrderID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@useNoLock bit
		@OrderID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_SelectAllCountByForeignKeyOrderID]
@useNoLock BIT = 0,
@OrderID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

-- Count rows of the table
						
IF @useNoLock = 1
BEGIN
	  SELECT COUNT(*)
	  FROM [dbo].[Order Details] WITH (NOLOCK) 
	  WHERE [OrderID] = @OrderID

END
ELSE
BEGIN
	 SELECT COUNT(*)
	 FROM [dbo].[Order Details]
	 WHERE [OrderID] = @OrderID

END

-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_DeleteAllByForeignKeyProductID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_DeleteAllByForeignKeyProductID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_DeleteAllByForeignKeyProductID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@ProductID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_DeleteAllByForeignKeyProductID]
@ProductID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[Order Details]
WHERE [ProductID] = @ProductID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				
if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[gsp_OrderDetails_DeleteAllByForeignKeyOrderID]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
drop procedure [dbo].[gsp_OrderDetails_DeleteAllByForeignKeyOrderID]
GO


/*
---------------------------------------------------------------------------------------------------
OBJECT NAME : gsp_Order Details_DeleteAllByForeignKeyOrderID
						
AUTHOR	:	Inquiry © 2011 (DLG 6.0.1)
DATE	:	6/4/2026 10:07:10 PM

INPUTS	: 
		@OrderID int 
		

OUTPUTS	: 
		@Error     - The return code indicating if there was an error

DESCRIPTION : This stored procedure will select a row from the table 'Order Details' 
----------------------------------------------------------------------------------------------------
*/
CREATE PROCEDURE [dbo].[gsp_OrderDetails_DeleteAllByForeignKeyOrderID]
@OrderID int ,
@dlgErrorCode int OUTPUT
AS

SET NOCOUNT ON

DELETE	
FROM [dbo].[Order Details]
WHERE [OrderID] = @OrderID


-- Get the Error Code for the statment just executed
SET @dlgErrorCode = @@ERROR


GO
				





