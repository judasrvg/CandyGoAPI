SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM cg.business_rules WHERE id = 1)
BEGIN
    INSERT INTO cg.business_rules (
        id,
        delivery_fee,
        reward_percent,
        cash_conversion_rate,
        updated_by
    )
    VALUES (
        1,
        150.00,  -- costo fijo inicial de mensajería
        10.00,   -- % inicial de recompensa por reserva completada
        1.0000,  -- tasa inicial CandyCash x1
        'seed'
    );
END

COMMIT TRANSACTION;
GO

/*
Provisioning de primer admin (recomendado desde API segura):
- No se inserta usuario admin por SQL plano para evitar exponer credenciales.
- Crear primer admin desde endpoint de bootstrap protegido en ambiente inicial.
*/
