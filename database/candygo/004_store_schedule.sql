SET XACT_ABORT ON;
GO

IF COL_LENGTH('cg.business_rules', 'store_open_time') IS NULL
BEGIN
    ALTER TABLE cg.business_rules
    ADD store_open_time TIME(0) NOT NULL
        CONSTRAINT DF_cg_business_rules_store_open_time DEFAULT ('09:00');
END
GO

IF COL_LENGTH('cg.business_rules', 'store_close_time') IS NULL
BEGIN
    ALTER TABLE cg.business_rules
    ADD store_close_time TIME(0) NOT NULL
        CONSTRAINT DF_cg_business_rules_store_close_time DEFAULT ('18:00');
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_cg_business_rules_store_schedule'
      AND parent_object_id = OBJECT_ID('cg.business_rules')
)
BEGIN
    ALTER TABLE cg.business_rules
    ADD CONSTRAINT CK_cg_business_rules_store_schedule
        CHECK (store_open_time <> store_close_time);
END
GO
