SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- Homologa estados legacy al nuevo flujo simple.
    UPDATE cg.orders
    SET status = 'CONFIRMADA',
        updated_at = SYSUTCDATETIME()
    WHERE status IN ('PREPARANDO', 'LISTA');

    UPDATE cg.order_status_history
    SET from_status = 'CONFIRMADA'
    WHERE from_status IN ('PREPARANDO', 'LISTA');

    UPDATE cg.order_status_history
    SET to_status = 'CONFIRMADA'
    WHERE to_status IN ('PREPARANDO', 'LISTA');

    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID('cg.orders')
          AND name = 'CK_cg_orders_status'
    )
    BEGIN
        ALTER TABLE cg.orders DROP CONSTRAINT CK_cg_orders_status;
    END

    ALTER TABLE cg.orders
        ADD CONSTRAINT CK_cg_orders_status
        CHECK (status IN ('PENDIENTE', 'CONFIRMADA', 'ENTREGADA', 'CANCELADA'));

    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID('cg.order_status_history')
          AND name = 'CK_cg_order_status_history_to_status'
    )
    BEGIN
        ALTER TABLE cg.order_status_history DROP CONSTRAINT CK_cg_order_status_history_to_status;
    END

    ALTER TABLE cg.order_status_history
        ADD CONSTRAINT CK_cg_order_status_history_to_status
        CHECK (to_status IN ('PENDIENTE', 'CONFIRMADA', 'ENTREGADA', 'CANCELADA'));

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

