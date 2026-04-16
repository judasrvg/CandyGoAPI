SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.cg_local_business_rules', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.cg_local_business_rules (
        id INT NOT NULL CONSTRAINT PK_cg_local_business_rules PRIMARY KEY,
        delivery_fee DECIMAL(18,2) NOT NULL CONSTRAINT DF_cg_local_business_rules_delivery_fee DEFAULT (0),
        reward_percent DECIMAL(5,2) NOT NULL CONSTRAINT DF_cg_local_business_rules_reward_percent DEFAULT (10),
        cash_conversion_rate DECIMAL(10,4) NOT NULL CONSTRAINT DF_cg_local_business_rules_cash_conversion_rate DEFAULT (1),
        last_remote_updated_at DATETIME2(0) NULL,
        last_synced_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_local_business_rules_last_synced_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_cg_local_business_rules_single_row CHECK (id = 1),
        CONSTRAINT CK_cg_local_business_rules_delivery_fee CHECK (delivery_fee >= 0),
        CONSTRAINT CK_cg_local_business_rules_reward_percent CHECK (reward_percent >= 0 AND reward_percent <= 100),
        CONSTRAINT CK_cg_local_business_rules_cash_rate CHECK (cash_conversion_rate > 0)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.cg_local_business_rules WHERE id = 1)
BEGIN
    INSERT INTO dbo.cg_local_business_rules
    (
        id,
        delivery_fee,
        reward_percent,
        cash_conversion_rate
    )
    VALUES
    (
        1,
        0,
        10,
        1
    );
END
GO

