SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'cg')
BEGIN
    EXEC('CREATE SCHEMA cg');
END
GO

IF OBJECT_ID('cg.business_rules', 'U') IS NULL
BEGIN
    CREATE TABLE cg.business_rules (
        id INT NOT NULL CONSTRAINT PK_cg_business_rules PRIMARY KEY,
        delivery_fee DECIMAL(18,2) NOT NULL CONSTRAINT DF_cg_business_rules_delivery_fee DEFAULT (0),
        reward_percent DECIMAL(5,2) NOT NULL CONSTRAINT DF_cg_business_rules_reward_percent DEFAULT (10),
        cash_conversion_rate DECIMAL(10,4) NOT NULL CONSTRAINT DF_cg_business_rules_cash_conversion_rate DEFAULT (1),
        updated_by NVARCHAR(120) NULL,
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_business_rules_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION,
        CONSTRAINT CK_cg_business_rules_single_row CHECK (id = 1),
        CONSTRAINT CK_cg_business_rules_reward_percent CHECK (reward_percent >= 0 AND reward_percent <= 100),
        CONSTRAINT CK_cg_business_rules_delivery_fee CHECK (delivery_fee >= 0),
        CONSTRAINT CK_cg_business_rules_cash_rate CHECK (cash_conversion_rate > 0)
    );
END
GO

IF OBJECT_ID('cg.admin_users', 'U') IS NULL
BEGIN
    CREATE TABLE cg.admin_users (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_admin_users PRIMARY KEY,
        full_name NVARCHAR(120) NOT NULL,
        phone NVARCHAR(20) NOT NULL,
        password_hash VARBINARY(256) NOT NULL,
        password_salt VARBINARY(128) NOT NULL,
        role_name NVARCHAR(30) NOT NULL CONSTRAINT DF_cg_admin_users_role DEFAULT ('ADMIN'),
        is_active BIT NOT NULL CONSTRAINT DF_cg_admin_users_is_active DEFAULT (1),
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_admin_users_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_admin_users_updated_at DEFAULT SYSUTCDATETIME(),
        last_login_at DATETIME2(0) NULL,
        row_version ROWVERSION
    );

    CREATE UNIQUE INDEX UX_cg_admin_users_phone ON cg.admin_users(phone);
END
GO

IF OBJECT_ID('cg.clients', 'U') IS NULL
BEGIN
    CREATE TABLE cg.clients (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_clients PRIMARY KEY,
        full_name NVARCHAR(120) NOT NULL,
        phone NVARCHAR(20) NOT NULL,
        password_hash VARBINARY(256) NOT NULL,
        password_salt VARBINARY(128) NOT NULL,
        is_active BIT NOT NULL CONSTRAINT DF_cg_clients_is_active DEFAULT (1),
        source_system NVARCHAR(30) NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_clients_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_clients_updated_at DEFAULT SYSUTCDATETIME(),
        last_login_at DATETIME2(0) NULL,
        row_version ROWVERSION
    );

    CREATE UNIQUE INDEX UX_cg_clients_phone ON cg.clients(phone);
    CREATE INDEX IX_cg_clients_is_active ON cg.clients(is_active);
END
GO

IF OBJECT_ID('cg.wallet_accounts', 'U') IS NULL
BEGIN
    CREATE TABLE cg.wallet_accounts (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_wallet_accounts PRIMARY KEY,
        client_id BIGINT NOT NULL,
        balance DECIMAL(18,2) NOT NULL CONSTRAINT DF_cg_wallet_accounts_balance DEFAULT (0),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_wallet_accounts_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION,
        CONSTRAINT FK_cg_wallet_accounts_client_id FOREIGN KEY (client_id) REFERENCES cg.clients(id),
        CONSTRAINT CK_cg_wallet_accounts_balance CHECK (balance >= 0)
    );

    CREATE UNIQUE INDEX UX_cg_wallet_accounts_client_id ON cg.wallet_accounts(client_id);
END
GO

IF OBJECT_ID('cg.wallet_movements', 'U') IS NULL
BEGIN
    CREATE TABLE cg.wallet_movements (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_wallet_movements PRIMARY KEY,
        wallet_account_id BIGINT NOT NULL,
        movement_type NVARCHAR(20) NOT NULL,
        amount DECIMAL(18,2) NOT NULL,
        signed_amount DECIMAL(18,2) NOT NULL,
        reason NVARCHAR(250) NULL,
        source_type NVARCHAR(50) NULL,
        source_ref NVARCHAR(100) NULL,
        idempotency_key UNIQUEIDENTIFIER NULL,
        created_by NVARCHAR(120) NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_wallet_movements_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_cg_wallet_movements_wallet_account_id FOREIGN KEY (wallet_account_id) REFERENCES cg.wallet_accounts(id),
        CONSTRAINT CK_cg_wallet_movements_type CHECK (movement_type IN ('CREDIT', 'DEBIT', 'ADJUSTMENT')),
        CONSTRAINT CK_cg_wallet_movements_amount CHECK (amount > 0),
        CONSTRAINT CK_cg_wallet_movements_signed_amount CHECK (signed_amount <> 0)
    );

    CREATE INDEX IX_cg_wallet_movements_wallet_created ON cg.wallet_movements(wallet_account_id, created_at DESC);
    CREATE UNIQUE INDEX UX_cg_wallet_movements_idempotency_key ON cg.wallet_movements(idempotency_key) WHERE idempotency_key IS NOT NULL;
END
GO

IF OBJECT_ID('cg.products', 'U') IS NULL
BEGIN
    CREATE TABLE cg.products (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_products PRIMARY KEY,
        name NVARCHAR(160) NOT NULL,
        description NVARCHAR(600) NULL,
        image_url NVARCHAR(500) NULL,
        price_candycash DECIMAL(18,2) NOT NULL,
        is_active BIT NOT NULL CONSTRAINT DF_cg_products_is_active DEFAULT (1),
        sort_order INT NOT NULL CONSTRAINT DF_cg_products_sort_order DEFAULT (0),
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_products_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_products_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_cg_products_price CHECK (price_candycash >= 0)
    );

    CREATE INDEX IX_cg_products_active_sort ON cg.products(is_active, sort_order, name);
END
GO

IF OBJECT_ID('cg.orders', 'U') IS NULL
BEGIN
    CREATE TABLE cg.orders (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_orders PRIMARY KEY,
        client_id BIGINT NOT NULL,
        status NVARCHAR(20) NOT NULL,
        fulfillment_type NVARCHAR(20) NOT NULL,
        subtotal_candycash DECIMAL(18,2) NOT NULL,
        delivery_fee DECIMAL(18,2) NOT NULL CONSTRAINT DF_cg_orders_delivery_fee DEFAULT (0),
        total_candycash DECIMAL(18,2) NOT NULL,
        payment_method NVARCHAR(20) NOT NULL CONSTRAINT DF_cg_orders_payment_method DEFAULT ('CANDYCASH'),
        delivery_address NVARCHAR(320) NULL,
        notes NVARCHAR(500) NULL,
        wallet_movement_id BIGINT NULL,
        client_request_id UNIQUEIDENTIFIER NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_orders_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_orders_updated_at DEFAULT SYSUTCDATETIME(),
        confirmed_at DATETIME2(0) NULL,
        delivered_at DATETIME2(0) NULL,
        cancelled_at DATETIME2(0) NULL,
        last_status_by NVARCHAR(120) NULL,
        row_version ROWVERSION,
        CONSTRAINT FK_cg_orders_client_id FOREIGN KEY (client_id) REFERENCES cg.clients(id),
        CONSTRAINT FK_cg_orders_wallet_movement_id FOREIGN KEY (wallet_movement_id) REFERENCES cg.wallet_movements(id),
        CONSTRAINT CK_cg_orders_status CHECK (status IN ('PENDIENTE', 'CONFIRMADA', 'PREPARANDO', 'LISTA', 'ENTREGADA', 'CANCELADA')),
        CONSTRAINT CK_cg_orders_fulfillment_type CHECK (fulfillment_type IN ('PICKUP', 'DELIVERY')),
        CONSTRAINT CK_cg_orders_payment_method CHECK (payment_method IN ('CANDYCASH')),
        CONSTRAINT CK_cg_orders_totals CHECK (
            subtotal_candycash >= 0
            AND delivery_fee >= 0
            AND total_candycash >= 0
            AND total_candycash = subtotal_candycash + delivery_fee
        )
    );

    CREATE INDEX IX_cg_orders_client_created ON cg.orders(client_id, created_at DESC);
    CREATE INDEX IX_cg_orders_status_updated ON cg.orders(status, updated_at DESC);
    CREATE UNIQUE INDEX UX_cg_orders_client_request_id ON cg.orders(client_request_id) WHERE client_request_id IS NOT NULL;
END
GO

IF OBJECT_ID('cg.order_items', 'U') IS NULL
BEGIN
    CREATE TABLE cg.order_items (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_order_items PRIMARY KEY,
        order_id BIGINT NOT NULL,
        product_id BIGINT NULL,
        product_name_snapshot NVARCHAR(180) NOT NULL,
        unit_price_candycash DECIMAL(18,2) NOT NULL,
        quantity INT NOT NULL,
        line_total_candycash AS (unit_price_candycash * quantity) PERSISTED,
        CONSTRAINT FK_cg_order_items_order_id FOREIGN KEY (order_id) REFERENCES cg.orders(id),
        CONSTRAINT FK_cg_order_items_product_id FOREIGN KEY (product_id) REFERENCES cg.products(id),
        CONSTRAINT CK_cg_order_items_price CHECK (unit_price_candycash >= 0),
        CONSTRAINT CK_cg_order_items_quantity CHECK (quantity > 0)
    );

    CREATE INDEX IX_cg_order_items_order_id ON cg.order_items(order_id);
END
GO

IF OBJECT_ID('cg.order_status_history', 'U') IS NULL
BEGIN
    CREATE TABLE cg.order_status_history (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_order_status_history PRIMARY KEY,
        order_id BIGINT NOT NULL,
        from_status NVARCHAR(20) NULL,
        to_status NVARCHAR(20) NOT NULL,
        changed_by NVARCHAR(120) NULL,
        changed_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_order_status_history_changed_at DEFAULT SYSUTCDATETIME(),
        reason NVARCHAR(250) NULL,
        CONSTRAINT FK_cg_order_status_history_order_id FOREIGN KEY (order_id) REFERENCES cg.orders(id),
        CONSTRAINT CK_cg_order_status_history_to_status CHECK (to_status IN ('PENDIENTE', 'CONFIRMADA', 'PREPARANDO', 'LISTA', 'ENTREGADA', 'CANCELADA'))
    );

    CREATE INDEX IX_cg_order_status_history_order_changed ON cg.order_status_history(order_id, changed_at DESC);
END
GO

IF OBJECT_ID('cg.reservation_reward_events', 'U') IS NULL
BEGIN
    CREATE TABLE cg.reservation_reward_events (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_reservation_reward_events PRIMARY KEY,
        external_source NVARCHAR(40) NOT NULL,
        external_event_id UNIQUEIDENTIFIER NOT NULL,
        reservation_id BIGINT NULL,
        client_phone NVARCHAR(20) NOT NULL,
        reservation_total DECIMAL(18,2) NOT NULL,
        reward_percent DECIMAL(5,2) NOT NULL,
        cash_conversion_rate DECIMAL(10,4) NOT NULL,
        credited_candycash DECIMAL(18,2) NOT NULL,
        wallet_movement_id BIGINT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_reservation_reward_events_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_cg_reservation_reward_events_wallet_movement_id FOREIGN KEY (wallet_movement_id) REFERENCES cg.wallet_movements(id),
        CONSTRAINT CK_cg_reservation_reward_events_total CHECK (reservation_total >= 0),
        CONSTRAINT CK_cg_reservation_reward_events_credit CHECK (credited_candycash >= 0)
    );

    CREATE UNIQUE INDEX UX_cg_reservation_reward_events_source_event ON cg.reservation_reward_events(external_source, external_event_id);
    CREATE UNIQUE INDEX UX_cg_reservation_reward_events_source_reservation ON cg.reservation_reward_events(external_source, reservation_id) WHERE reservation_id IS NOT NULL;
END
GO

IF OBJECT_ID('cg.v_client_wallet_balance', 'V') IS NULL
BEGIN
    EXEC('CREATE VIEW cg.v_client_wallet_balance AS
        SELECT
            c.id AS client_id,
            c.full_name,
            c.phone,
            wa.balance,
            wa.updated_at AS wallet_updated_at
        FROM cg.clients c
        LEFT JOIN cg.wallet_accounts wa ON wa.client_id = c.id');
END
GO

IF OBJECT_ID('cg.sp_apply_wallet_movement', 'P') IS NULL
BEGIN
    EXEC('CREATE PROCEDURE cg.sp_apply_wallet_movement
        @ClientId BIGINT,
        @SignedAmount DECIMAL(18,2),
        @Reason NVARCHAR(250) = NULL,
        @SourceType NVARCHAR(50) = NULL,
        @SourceRef NVARCHAR(100) = NULL,
        @IdempotencyKey UNIQUEIDENTIFIER = NULL,
        @CreatedBy NVARCHAR(120) = NULL,
        @MovementId BIGINT OUTPUT
    AS
    BEGIN
        SET NOCOUNT ON;
        SET XACT_ABORT ON;

        IF @SignedAmount = 0
            THROW 50001, ''SignedAmount no puede ser 0.'', 1;

        IF @IdempotencyKey IS NOT NULL
        BEGIN
            SELECT @MovementId = wm.id
            FROM cg.wallet_movements wm
            WHERE wm.idempotency_key = @IdempotencyKey;

            IF @MovementId IS NOT NULL
                RETURN;
        END

        DECLARE @WalletAccountId BIGINT;
        DECLARE @CurrentBalance DECIMAL(18,2);
        DECLARE @NewBalance DECIMAL(18,2);
        DECLARE @MovementType NVARCHAR(20) = CASE WHEN @SignedAmount > 0 THEN ''CREDIT'' ELSE ''DEBIT'' END;

        BEGIN TRANSACTION;

        SELECT @WalletAccountId = wa.id
        FROM cg.wallet_accounts wa WITH (UPDLOCK, HOLDLOCK)
        WHERE wa.client_id = @ClientId;

        IF @WalletAccountId IS NULL
        BEGIN
            INSERT INTO cg.wallet_accounts (client_id, balance)
            VALUES (@ClientId, 0);

            SET @WalletAccountId = SCOPE_IDENTITY();
        END

        SELECT @CurrentBalance = wa.balance
        FROM cg.wallet_accounts wa WITH (UPDLOCK, HOLDLOCK)
        WHERE wa.id = @WalletAccountId;

        SET @NewBalance = @CurrentBalance + @SignedAmount;

        IF @NewBalance < 0
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50002, ''Saldo insuficiente para completar la operación.'', 1;
        END

        UPDATE cg.wallet_accounts
        SET balance = @NewBalance,
            updated_at = SYSUTCDATETIME()
        WHERE id = @WalletAccountId;

        INSERT INTO cg.wallet_movements (
            wallet_account_id,
            movement_type,
            amount,
            signed_amount,
            reason,
            source_type,
            source_ref,
            idempotency_key,
            created_by
        )
        VALUES (
            @WalletAccountId,
            @MovementType,
            ABS(@SignedAmount),
            @SignedAmount,
            @Reason,
            @SourceType,
            @SourceRef,
            @IdempotencyKey,
            @CreatedBy
        );

        SET @MovementId = SCOPE_IDENTITY();

        COMMIT TRANSACTION;
    END');
END
GO

IF OBJECT_ID('cg.sp_apply_reservation_reward', 'P') IS NULL
BEGIN
    EXEC('CREATE PROCEDURE cg.sp_apply_reservation_reward
        @ExternalSource NVARCHAR(40),
        @ExternalEventId UNIQUEIDENTIFIER,
        @ReservationId BIGINT = NULL,
        @ClientPhone NVARCHAR(20),
        @ReservationTotal DECIMAL(18,2),
        @CreatedBy NVARCHAR(120) = NULL,
        @RewardMovementId BIGINT OUTPUT
    AS
    BEGIN
        SET NOCOUNT ON;
        SET XACT_ABORT ON;

        DECLARE @ExistingMovementId BIGINT;
        SELECT @ExistingMovementId = rre.wallet_movement_id
        FROM cg.reservation_reward_events rre
        WHERE rre.external_source = @ExternalSource
          AND rre.external_event_id = @ExternalEventId;

        IF @ExistingMovementId IS NOT NULL
        BEGIN
            SET @RewardMovementId = @ExistingMovementId;
            RETURN;
        END

        DECLARE @ClientId BIGINT;
        DECLARE @RewardPercent DECIMAL(5,2);
        DECLARE @CashRate DECIMAL(10,4);
        DECLARE @RewardBase DECIMAL(18,4);
        DECLARE @RewardAmount DECIMAL(18,2);
        DECLARE @MovementId BIGINT;
        DECLARE @ReservationSourceRef NVARCHAR(100);

        SELECT TOP 1
            @ClientId = c.id
        FROM cg.clients c
        WHERE c.phone = @ClientPhone
          AND c.is_active = 1;

        IF @ClientId IS NULL
            THROW 50003, ''Cliente CandyGo no encontrado para ese teléfono.'', 1;

        SELECT TOP 1
            @RewardPercent = br.reward_percent,
            @CashRate = br.cash_conversion_rate
        FROM cg.business_rules br
        WHERE br.id = 1;

        IF @RewardPercent IS NULL OR @CashRate IS NULL
            THROW 50004, ''No existen reglas de negocio activas para reward/cash rate.'', 1;

        SET @RewardBase = (@ReservationTotal * (@RewardPercent / 100.0));
        SET @RewardAmount = ROUND(@RewardBase * @CashRate, 2);
        SET @ReservationSourceRef = CONVERT(NVARCHAR(100), @ReservationId);

        BEGIN TRANSACTION;

        EXEC cg.sp_apply_wallet_movement
            @ClientId = @ClientId,
            @SignedAmount = @RewardAmount,
            @Reason = ''Reward por reserva completada'',
            @SourceType = ''RESERVATION_COMPLETED'',
            @SourceRef = @ReservationSourceRef,
            @IdempotencyKey = @ExternalEventId,
            @CreatedBy = @CreatedBy,
            @MovementId = @MovementId OUTPUT;

        INSERT INTO cg.reservation_reward_events (
            external_source,
            external_event_id,
            reservation_id,
            client_phone,
            reservation_total,
            reward_percent,
            cash_conversion_rate,
            credited_candycash,
            wallet_movement_id
        )
        VALUES (
            @ExternalSource,
            @ExternalEventId,
            @ReservationId,
            @ClientPhone,
            @ReservationTotal,
            @RewardPercent,
            @CashRate,
            @RewardAmount,
            @MovementId
        );

        SET @RewardMovementId = @MovementId;

        COMMIT TRANSACTION;
    END');
END
GO
