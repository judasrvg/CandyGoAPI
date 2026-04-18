SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID('cg.push_subscriptions', 'U') IS NULL
BEGIN
    CREATE TABLE cg.push_subscriptions (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_push_subscriptions PRIMARY KEY,
        client_id BIGINT NOT NULL,
        endpoint NVARCHAR(700) NOT NULL,
        p256dh NVARCHAR(255) NOT NULL,
        auth_secret NVARCHAR(120) NOT NULL,
        content_encoding NVARCHAR(40) NULL,
        user_agent NVARCHAR(320) NULL,
        is_active BIT NOT NULL CONSTRAINT DF_cg_push_subscriptions_is_active DEFAULT (1),
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_push_subscriptions_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_push_subscriptions_updated_at DEFAULT SYSUTCDATETIME(),
        last_seen_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_push_subscriptions_last_seen_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION,
        CONSTRAINT FK_cg_push_subscriptions_client_id FOREIGN KEY (client_id) REFERENCES cg.clients(id),
        CONSTRAINT CK_cg_push_subscriptions_endpoint CHECK (LEN(endpoint) >= 20),
        CONSTRAINT CK_cg_push_subscriptions_p256dh CHECK (LEN(p256dh) >= 20),
        CONSTRAINT CK_cg_push_subscriptions_auth_secret CHECK (LEN(auth_secret) >= 8)
    );

    CREATE UNIQUE INDEX UX_cg_push_subscriptions_endpoint ON cg.push_subscriptions(endpoint);
    CREATE INDEX IX_cg_push_subscriptions_client_active ON cg.push_subscriptions(client_id, is_active, updated_at DESC);
END

IF OBJECT_ID('cg.notification_templates', 'U') IS NULL
BEGIN
    CREATE TABLE cg.notification_templates (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_notification_templates PRIMARY KEY,
        template_name NVARCHAR(90) NOT NULL,
        title NVARCHAR(120) NOT NULL,
        message_body NVARCHAR(450) NOT NULL,
        icon_url NVARCHAR(450) NULL,
        target_url NVARCHAR(320) NULL,
        is_active BIT NOT NULL CONSTRAINT DF_cg_notification_templates_is_active DEFAULT (1),
        created_by NVARCHAR(120) NULL,
        updated_by NVARCHAR(120) NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_notification_templates_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_notification_templates_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_cg_notification_templates_name CHECK (LEN(template_name) >= 3),
        CONSTRAINT CK_cg_notification_templates_title CHECK (LEN(title) >= 3),
        CONSTRAINT CK_cg_notification_templates_message CHECK (LEN(message_body) >= 3)
    );

    CREATE UNIQUE INDEX UX_cg_notification_templates_name ON cg.notification_templates(template_name);
    CREATE INDEX IX_cg_notification_templates_active ON cg.notification_templates(is_active, updated_at DESC);
END

IF OBJECT_ID('cg.notification_campaigns', 'U') IS NULL
BEGIN
    CREATE TABLE cg.notification_campaigns (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_notification_campaigns PRIMARY KEY,
        template_id BIGINT NULL,
        title NVARCHAR(120) NOT NULL,
        message_body NVARCHAR(450) NOT NULL,
        icon_url NVARCHAR(450) NULL,
        target_url NVARCHAR(320) NULL,
        audience_type NVARCHAR(20) NOT NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT DF_cg_notification_campaigns_status DEFAULT ('QUEUED'),
        audience_payload_json NVARCHAR(MAX) NULL,
        total_targets INT NOT NULL CONSTRAINT DF_cg_notification_campaigns_total_targets DEFAULT (0),
        total_sent INT NOT NULL CONSTRAINT DF_cg_notification_campaigns_total_sent DEFAULT (0),
        total_failed INT NOT NULL CONSTRAINT DF_cg_notification_campaigns_total_failed DEFAULT (0),
        created_by NVARCHAR(120) NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_notification_campaigns_created_at DEFAULT SYSUTCDATETIME(),
        sent_at_utc DATETIME2(0) NULL,
        CONSTRAINT FK_cg_notification_campaigns_template_id FOREIGN KEY (template_id) REFERENCES cg.notification_templates(id),
        CONSTRAINT CK_cg_notification_campaigns_audience_type CHECK (audience_type IN ('ALL', 'CLIENTS')),
        CONSTRAINT CK_cg_notification_campaigns_status CHECK (status IN ('QUEUED', 'SENT', 'PARTIAL', 'FAILED'))
    );

    CREATE INDEX IX_cg_notification_campaigns_created ON cg.notification_campaigns(created_at DESC);
    CREATE INDEX IX_cg_notification_campaigns_status ON cg.notification_campaigns(status, created_at DESC);
END

IF OBJECT_ID('cg.notification_campaign_targets', 'U') IS NULL
BEGIN
    CREATE TABLE cg.notification_campaign_targets (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_notification_campaign_targets PRIMARY KEY,
        campaign_id BIGINT NOT NULL,
        client_id BIGINT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_notification_campaign_targets_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_cg_notification_campaign_targets_campaign_id FOREIGN KEY (campaign_id) REFERENCES cg.notification_campaigns(id),
        CONSTRAINT FK_cg_notification_campaign_targets_client_id FOREIGN KEY (client_id) REFERENCES cg.clients(id)
    );

    CREATE UNIQUE INDEX UX_cg_notification_campaign_targets ON cg.notification_campaign_targets(campaign_id, client_id);
    CREATE INDEX IX_cg_notification_campaign_targets_client ON cg.notification_campaign_targets(client_id, created_at DESC);
END

IF OBJECT_ID('cg.notification_deliveries', 'U') IS NULL
BEGIN
    CREATE TABLE cg.notification_deliveries (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_notification_deliveries PRIMARY KEY,
        campaign_id BIGINT NOT NULL,
        client_id BIGINT NOT NULL,
        subscription_id BIGINT NOT NULL,
        status NVARCHAR(20) NOT NULL,
        http_status_code INT NULL,
        error_message NVARCHAR(500) NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_notification_deliveries_created_at DEFAULT SYSUTCDATETIME(),
        delivered_at_utc DATETIME2(0) NULL,
        CONSTRAINT FK_cg_notification_deliveries_campaign_id FOREIGN KEY (campaign_id) REFERENCES cg.notification_campaigns(id),
        CONSTRAINT FK_cg_notification_deliveries_client_id FOREIGN KEY (client_id) REFERENCES cg.clients(id),
        CONSTRAINT FK_cg_notification_deliveries_subscription_id FOREIGN KEY (subscription_id) REFERENCES cg.push_subscriptions(id),
        CONSTRAINT CK_cg_notification_deliveries_status CHECK (status IN ('SENT', 'FAILED', 'EXPIRED'))
    );

    CREATE UNIQUE INDEX UX_cg_notification_deliveries_campaign_subscription ON cg.notification_deliveries(campaign_id, subscription_id);
    CREATE INDEX IX_cg_notification_deliveries_client ON cg.notification_deliveries(client_id, created_at DESC);
END

IF OBJECT_ID('cg.contests', 'U') IS NULL
BEGIN
    CREATE TABLE cg.contests (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_contests PRIMARY KEY,
        contest_name NVARCHAR(120) NOT NULL,
        contest_slug NVARCHAR(90) NOT NULL,
        contest_type NVARCHAR(30) NOT NULL,
        description NVARCHAR(500) NULL,
        icon_name NVARCHAR(80) NULL,
        config_json NVARCHAR(MAX) NOT NULL,
        audience_type NVARCHAR(20) NOT NULL CONSTRAINT DF_cg_contests_audience_type DEFAULT ('ALL'),
        max_plays_per_client INT NOT NULL CONSTRAINT DF_cg_contests_max_plays DEFAULT (1),
        starts_at_utc DATETIME2(0) NULL,
        ends_at_utc DATETIME2(0) NULL,
        is_active BIT NOT NULL CONSTRAINT DF_cg_contests_is_active DEFAULT (0),
        created_by NVARCHAR(120) NULL,
        updated_by NVARCHAR(120) NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_contests_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_contests_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION,
        CONSTRAINT CK_cg_contests_slug CHECK (LEN(contest_slug) >= 3),
        CONSTRAINT CK_cg_contests_type CHECK (contest_type IN ('PICK_A_BOX', 'SLOT_TRIPLE')),
        CONSTRAINT CK_cg_contests_audience_type CHECK (audience_type IN ('ALL', 'CLIENTS')),
        CONSTRAINT CK_cg_contests_max_plays CHECK (max_plays_per_client >= 1 AND max_plays_per_client <= 20),
        CONSTRAINT CK_cg_contests_date_range CHECK (ends_at_utc IS NULL OR starts_at_utc IS NULL OR ends_at_utc >= starts_at_utc)
    );

    CREATE UNIQUE INDEX UX_cg_contests_slug ON cg.contests(contest_slug);
    CREATE INDEX IX_cg_contests_active_window ON cg.contests(is_active, starts_at_utc, ends_at_utc, updated_at DESC);
END

IF OBJECT_ID('cg.contest_targets', 'U') IS NULL
BEGIN
    CREATE TABLE cg.contest_targets (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_contest_targets PRIMARY KEY,
        contest_id BIGINT NOT NULL,
        client_id BIGINT NOT NULL,
        is_enabled BIT NOT NULL CONSTRAINT DF_cg_contest_targets_is_enabled DEFAULT (1),
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_contest_targets_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_cg_contest_targets_contest_id FOREIGN KEY (contest_id) REFERENCES cg.contests(id),
        CONSTRAINT FK_cg_contest_targets_client_id FOREIGN KEY (client_id) REFERENCES cg.clients(id)
    );

    CREATE UNIQUE INDEX UX_cg_contest_targets_contest_client ON cg.contest_targets(contest_id, client_id);
    CREATE INDEX IX_cg_contest_targets_client ON cg.contest_targets(client_id, is_enabled, contest_id);
END

IF OBJECT_ID('cg.contest_plays', 'U') IS NULL
BEGIN
    CREATE TABLE cg.contest_plays (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cg_contest_plays PRIMARY KEY,
        contest_id BIGINT NOT NULL,
        client_id BIGINT NOT NULL,
        selected_slot INT NOT NULL,
        result_code NVARCHAR(40) NOT NULL,
        result_message NVARCHAR(250) NULL,
        awarded_candycash DECIMAL(18,2) NOT NULL CONSTRAINT DF_cg_contest_plays_award DEFAULT (0),
        wallet_movement_id BIGINT NULL,
        client_request_id UNIQUEIDENTIFIER NULL,
        source_ip NVARCHAR(80) NULL,
        user_agent NVARCHAR(320) NULL,
        metadata_json NVARCHAR(MAX) NULL,
        played_at DATETIME2(0) NOT NULL CONSTRAINT DF_cg_contest_plays_played_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_cg_contest_plays_contest_id FOREIGN KEY (contest_id) REFERENCES cg.contests(id),
        CONSTRAINT FK_cg_contest_plays_client_id FOREIGN KEY (client_id) REFERENCES cg.clients(id),
        CONSTRAINT FK_cg_contest_plays_wallet_movement_id FOREIGN KEY (wallet_movement_id) REFERENCES cg.wallet_movements(id),
        CONSTRAINT CK_cg_contest_plays_selected_slot CHECK (selected_slot >= 1 AND selected_slot <= 12),
        CONSTRAINT CK_cg_contest_plays_award CHECK (awarded_candycash >= 0)
    );

    CREATE UNIQUE INDEX UX_cg_contest_plays_request ON cg.contest_plays(contest_id, client_id, client_request_id) WHERE client_request_id IS NOT NULL;
    CREATE INDEX IX_cg_contest_plays_contest_client ON cg.contest_plays(contest_id, client_id, played_at DESC);
    CREATE INDEX IX_cg_contest_plays_client ON cg.contest_plays(client_id, played_at DESC);
END

IF NOT EXISTS (SELECT 1 FROM cg.notification_templates WHERE template_name = 'PROMO_GENERAL')
BEGIN
    INSERT INTO cg.notification_templates (
        template_name,
        title,
        message_body,
        icon_url,
        target_url,
        created_by,
        updated_by
    )
    VALUES (
        'PROMO_GENERAL',
        'Candy Go',
        'Hay novedades en tu tienda Candy Go. Entra y descubre productos y premios.',
        '/assets/candygo-icon.svg',
        '/',
        'seed',
        'seed'
    );
END

IF NOT EXISTS (SELECT 1 FROM cg.contests WHERE contest_slug = 'escoge-una-caja')
BEGIN
    INSERT INTO cg.contests (
        contest_name,
        contest_slug,
        contest_type,
        description,
        icon_name,
        config_json,
        audience_type,
        max_plays_per_client,
        is_active,
        created_by,
        updated_by
    )
    VALUES (
        'Escoge una caja',
        'escoge-una-caja',
        'PICK_A_BOX',
        'Selecciona una caja y descubre tu premio en CandyCash.',
        'gift-box',
        '{"boxes":[{"slot":1,"label":"Caja 1","rewardCandyCash":25.00},{"slot":2,"label":"Caja 2","rewardCandyCash":8.00},{"slot":3,"label":"Caja 3","rewardCandyCash":0.00}],"emptyResultMessage":"Sigue intentando, pronto te toca.","winResultPrefix":"Ganaste"}',
        'ALL',
        1,
        0,
        'seed',
        'seed'
    );
END

COMMIT TRANSACTION;
GO
