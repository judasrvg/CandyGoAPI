SET XACT_ABORT ON;
GO

IF OBJECT_ID('cg.products', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('cg.products', 'is_special') IS NULL
    BEGIN
        ALTER TABLE cg.products
        ADD is_special BIT NOT NULL
            CONSTRAINT DF_cg_products_is_special DEFAULT (0);
    END
END
GO
