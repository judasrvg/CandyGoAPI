SET XACT_ABORT ON;
GO

IF OBJECT_ID('cg.contests', 'U') IS NOT NULL
BEGIN
    DECLARE @dropSql NVARCHAR(MAX) = N'';

    SELECT @dropSql = @dropSql + N'ALTER TABLE cg.contests DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(10)
    FROM sys.check_constraints cc
    WHERE cc.parent_object_id = OBJECT_ID('cg.contests')
      AND cc.definition LIKE '%contest_type%';

    IF LEN(@dropSql) > 0
    BEGIN
        EXEC sp_executesql @dropSql;
    END

    ALTER TABLE cg.contests
        ADD CONSTRAINT CK_cg_contests_type
            CHECK (contest_type IN ('PICK_A_BOX', 'SLOT_TRIPLE'));
END
GO
