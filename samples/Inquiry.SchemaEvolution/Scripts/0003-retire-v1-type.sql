-- Release 3: retire V1's table type. Ship it only after no V1 instance, rollback build or job
-- can still send that type. Dropping it removes the option to roll back to V1.
IF TYPE_ID(N'[dbo].[Inquiry_Tvp_97901ad0afa7a9860614b4af2b675765f70e0cc178bf52ef01c6c39cb91fc5e1]') IS NOT NULL
    DROP TYPE [dbo].[Inquiry_Tvp_97901ad0afa7a9860614b4af2b675765f70e0cc178bf52ef01c6c39cb91fc5e1];
