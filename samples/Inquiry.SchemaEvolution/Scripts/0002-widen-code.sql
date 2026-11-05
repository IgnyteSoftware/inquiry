-- Release 2: widen Code and add V2's table type. Nothing is dropped, so V1 keeps working.
ALTER TABLE [dbo].[CatalogItems] ALTER COLUMN [Code] NVARCHAR(64) NOT NULL;

-- A snapshot of V2's InquiryGeneratedSchema.ProviderArtifactsDdl.
IF TYPE_ID(N'[dbo].[Inquiry_Tvp_f2eaaa262a5392ae45922f38ea30b9ed4c414a6e6c502340e41458a5e1eded0f]') IS NULL EXEC(N'CREATE TYPE [dbo].[Inquiry_Tvp_f2eaaa262a5392ae45922f38ea30b9ed4c414a6e6c502340e41458a5e1eded0f] AS TABLE ([Value] NVARCHAR(64) NOT NULL)');
