SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.cg_local_clients', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.cg_local_clients (
        remote_client_id BIGINT NOT NULL CONSTRAINT PK_cg_local_clients PRIMARY KEY,
        phone NVARCHAR(20) NOT NULL,
        full_name NVARCHAR(120) NOT NULL,
        is_active BIT NOT NULL,
        last_remote_updated_at DATETIME2(0) NULL,
        last_synced_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_local_clients_last_synced_at DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_cg_local_clients_phone ON dbo.cg_local_clients(phone);
END
GO

IF OBJECT_ID('dbo.cg_local_orders', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.cg_local_orders (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_local_orders PRIMARY KEY,
        remote_order_id BIGINT NOT NULL,
        remote_client_id BIGINT NULL,
        client_phone NVARCHAR(20) NOT NULL,
        client_name NVARCHAR(120) NULL,
        status NVARCHAR(20) NOT NULL,
        fulfillment_type NVARCHAR(20) NOT NULL,
        subtotal_candycash DECIMAL(18,2) NOT NULL,
        delivery_fee DECIMAL(18,2) NOT NULL,
        total_candycash DECIMAL(18,2) NOT NULL,
        delivery_address NVARCHAR(320) NULL,
        notes NVARCHAR(500) NULL,
        remote_created_at DATETIME2(0) NOT NULL,
        remote_updated_at DATETIME2(0) NOT NULL,
        sync_state NVARCHAR(20) NOT NULL CONSTRAINT DF_cg_local_orders_sync_state DEFAULT ('SINCRONIZADO'),
        local_last_viewed_at DATETIME2(0) NULL,
        last_sync_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_local_orders_last_sync_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION,
        CONSTRAINT CK_cg_local_orders_status CHECK (status IN ('PENDIENTE', 'CONFIRMADA', 'ENTREGADA', 'CANCELADA')),
        CONSTRAINT CK_cg_local_orders_fulfillment_type CHECK (fulfillment_type IN ('PICKUP', 'DELIVERY')),
        CONSTRAINT CK_cg_local_orders_totals CHECK (
            subtotal_candycash >= 0
            AND delivery_fee >= 0
            AND total_candycash >= 0
            AND total_candycash = subtotal_candycash + delivery_fee
        ),
        CONSTRAINT CK_cg_local_orders_sync_state CHECK (sync_state IN ('SINCRONIZADO', 'PENDIENTE_SYNC', 'ERROR_SYNC'))
    );

    CREATE UNIQUE INDEX UX_cg_local_orders_remote_order_id ON dbo.cg_local_orders(remote_order_id);
    CREATE INDEX IX_cg_local_orders_status_remote_updated ON dbo.cg_local_orders(status, remote_updated_at DESC);
END
GO

IF OBJECT_ID('dbo.cg_local_order_items', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.cg_local_order_items (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_local_order_items PRIMARY KEY,
        local_order_id BIGINT NOT NULL,
        remote_order_item_id BIGINT NULL,
        remote_product_id BIGINT NULL,
        product_name_snapshot NVARCHAR(180) NOT NULL,
        unit_price_candycash DECIMAL(18,2) NOT NULL,
        quantity INT NOT NULL,
        line_total_candycash AS (unit_price_candycash * quantity) PERSISTED,
        CONSTRAINT FK_cg_local_order_items_local_order_id FOREIGN KEY (local_order_id) REFERENCES dbo.cg_local_orders(id) ON DELETE CASCADE,
        CONSTRAINT CK_cg_local_order_items_price CHECK (unit_price_candycash >= 0),
        CONSTRAINT CK_cg_local_order_items_quantity CHECK (quantity > 0)
    );

    CREATE INDEX IX_cg_local_order_items_local_order_id ON dbo.cg_local_order_items(local_order_id);
    CREATE UNIQUE INDEX UX_cg_local_order_items_remote_item ON dbo.cg_local_order_items(local_order_id, remote_order_item_id) WHERE remote_order_item_id IS NOT NULL;
END
GO

IF OBJECT_ID('dbo.cg_local_outbox', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.cg_local_outbox (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_local_outbox PRIMARY KEY,
        operation_type NVARCHAR(60) NOT NULL,
        entity_key NVARCHAR(120) NULL,
        payload_json NVARCHAR(MAX) NOT NULL,
        idempotency_key UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cg_local_outbox_idempotency_key DEFAULT NEWID(),
        status NVARCHAR(20) NOT NULL CONSTRAINT DF_cg_local_outbox_status DEFAULT ('PENDIENTE'),
        priority TINYINT NOT NULL CONSTRAINT DF_cg_local_outbox_priority DEFAULT (5),
        attempt_count INT NOT NULL CONSTRAINT DF_cg_local_outbox_attempt_count DEFAULT (0),
        next_retry_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_local_outbox_next_retry_at DEFAULT SYSUTCDATETIME(),
        last_attempt_at DATETIME2(0) NULL,
        processed_at DATETIME2(0) NULL,
        last_error NVARCHAR(500) NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_local_outbox_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_cg_local_outbox_status CHECK (status IN ('PENDIENTE', 'PROCESANDO', 'ENVIADO', 'ERROR'))
    );

    CREATE UNIQUE INDEX UX_cg_local_outbox_idempotency_key ON dbo.cg_local_outbox(idempotency_key);
    CREATE INDEX IX_cg_local_outbox_status_retry ON dbo.cg_local_outbox(status, next_retry_at, priority);
END
GO

IF OBJECT_ID('dbo.cg_local_sync_state', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.cg_local_sync_state (
        sync_target NVARCHAR(40) NOT NULL CONSTRAINT PK_cg_local_sync_state PRIMARY KEY,
        last_pull_utc DATETIME2(0) NULL,
        last_pull_order_id BIGINT NULL,
        last_success_sync_utc DATETIME2(0) NULL,
        last_error NVARCHAR(500) NULL,
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_local_sync_state_updated_at DEFAULT SYSUTCDATETIME()
    );

    IF NOT EXISTS (SELECT 1 FROM dbo.cg_local_sync_state WHERE sync_target = 'CANDYGO_REMOTE')
    BEGIN
        INSERT INTO dbo.cg_local_sync_state(sync_target)
        VALUES ('CANDYGO_REMOTE');
    END
END
GO

IF OBJECT_ID('dbo.cg_local_reservation_reward_queue', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.cg_local_reservation_reward_queue (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_local_reservation_reward_queue PRIMARY KEY,
        reservation_id BIGINT NOT NULL,
        client_phone NVARCHAR(20) NOT NULL,
        reservation_total DECIMAL(18,2) NOT NULL,
        external_event_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cg_local_reservation_reward_queue_external_event_id DEFAULT NEWID(),
        status NVARCHAR(20) NOT NULL CONSTRAINT DF_cg_local_reservation_reward_queue_status DEFAULT ('PENDIENTE'),
        attempt_count INT NOT NULL CONSTRAINT DF_cg_local_reservation_reward_queue_attempt_count DEFAULT (0),
        last_attempt_at DATETIME2(0) NULL,
        processed_at DATETIME2(0) NULL,
        last_error NVARCHAR(500) NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_local_reservation_reward_queue_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_cg_local_reservation_reward_queue_status CHECK (status IN ('PENDIENTE', 'ENVIADO', 'ERROR')),
        CONSTRAINT CK_cg_local_reservation_reward_queue_total CHECK (reservation_total >= 0)
    );

    CREATE UNIQUE INDEX UX_cg_local_reservation_reward_queue_reservation_id ON dbo.cg_local_reservation_reward_queue(reservation_id);
    CREATE UNIQUE INDEX UX_cg_local_reservation_reward_queue_external_event_id ON dbo.cg_local_reservation_reward_queue(external_event_id);
    CREATE INDEX IX_cg_local_reservation_reward_queue_status ON dbo.cg_local_reservation_reward_queue(status, created_at);
END
GO

IF OBJECT_ID('dbo.vw_cg_local_orders_resume', 'V') IS NULL
BEGIN
    EXEC('CREATE VIEW dbo.vw_cg_local_orders_resume AS
        SELECT
            o.id,
            o.remote_order_id,
            o.client_phone,
            o.client_name,
            o.status,
            o.fulfillment_type,
            o.total_candycash,
            o.sync_state,
            o.remote_updated_at,
            o.last_sync_at
        FROM dbo.cg_local_orders o');
END
GO
