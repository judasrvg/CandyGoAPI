SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- Homologa estados legacy en la copia local del sync.
    UPDATE dbo.cg_local_orders
    SET status = 'CONFIRMADA',
        last_sync_at = SYSUTCDATETIME()
    WHERE status IN ('PREPARANDO', 'LISTA');

    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID('dbo.cg_local_orders')
          AND name = 'CK_cg_local_orders_status'
    )
    BEGIN
        ALTER TABLE dbo.cg_local_orders DROP CONSTRAINT CK_cg_local_orders_status;
    END

    ALTER TABLE dbo.cg_local_orders
        ADD CONSTRAINT CK_cg_local_orders_status
        CHECK (status IN ('PENDIENTE', 'CONFIRMADA', 'ENTREGADA', 'CANCELADA'));

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

