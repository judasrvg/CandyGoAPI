SET XACT_ABORT ON;
GO

IF OBJECT_ID('cg.contests', 'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = 'CK_cg_contests_type'
          AND parent_object_id = OBJECT_ID('cg.contests')
    )
    BEGIN
        ALTER TABLE cg.contests DROP CONSTRAINT CK_cg_contests_type;
    END

    ALTER TABLE cg.contests
    ADD CONSTRAINT CK_cg_contests_type
        CHECK (contest_type IN ('PICK_A_BOX', 'SLOT_TRIPLE'));
END
GO
