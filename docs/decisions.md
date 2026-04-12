# CandyGo - Decisiones Base (v1)

## 1) Identidad de cliente
- `CandyGo` maneja su propio catálogo de clientes en su propia DB.
- La clave funcional principal es `phone` (normalizado), con unicidad.
- `PhotoStudio` no crea clientes CandyGo directamente: sincroniza la lista de clientes CandyGo y los identifica por `phone`.

## 2) Autenticación cliente
- Login cliente: `telefono + password`.
- Passwords nunca en texto plano: hash PBKDF2/Argon2 + salt único.
- Sesión con JWT corto + refresh token rotativo (fase implementación API).

## 3) CandyCash (seguridad de saldo)
- El saldo persiste en DB remota de CandyGo.
- El saldo se gestiona con modelo **ledger**:
  - `wallet_accounts` guarda saldo actual.
  - `wallet_movements` guarda historial inmutable (crédito/débito).
- Toda operación usa transacción SQL con bloqueo de fila (`UPDLOCK, HOLDLOCK`) para evitar race conditions.
- Se exige idempotencia por `idempotency_key` para impedir dobles cargos por reintento.
- Regla: no se permite saldo negativo.

## 4) Reglas de negocio configurables (admin)
- `delivery_fee` (costo fijo de mensajería).
- `reward_percent` (porcentaje de reserva completada que convierte a base CandyCash).
- `cash_conversion_rate` (factor multiplicador de CandyCash).
- Fórmula aprobada:
  - `reward_base = reservation_total * (reward_percent / 100)`
  - `candycash_credit = reward_base * cash_conversion_rate`

## 5) Estados de orden (simple + robusto)
Estados oficiales:
- `PENDIENTE`
- `CONFIRMADA`
- `PREPARANDO`
- `LISTA`
- `ENTREGADA`
- `CANCELADA`

Transiciones permitidas:
- `PENDIENTE -> CONFIRMADA | CANCELADA`
- `CONFIRMADA -> PREPARANDO | CANCELADA`
- `PREPARANDO -> LISTA | CANCELADA`
- `LISTA -> ENTREGADA | CANCELADA`
- `ENTREGADA` y `CANCELADA` son terminales.

## 6) Sync PhotoStudio (offline/online)
- `CandyGo Admin` es online en tiempo real y fuente central.
- `PhotoStudio` puede operar local y luego sincronizar.
- Estrategia de sync:
  1. **Push** outbox local pendiente (cambios locales).
  2. **Pull** cambios remotos desde último cursor (`updated_at` + `id`).
  3. Confirmar cursor local al final.
- Política de conflicto:
  - Regla general: **server wins** como fuente de verdad.
  - Para cambios locales aún no enviados: se intenta push primero.
  - Si push falla por estado inválido/concurrencia: queda en error, se registra causa, se aplica estado remoto y se notifica para revisión.
- Después de sync exitoso, local y remoto deben converger al mismo dataset del módulo CandyGo.

## 7) Integración con reservas PhotoStudio
- Solo reservas cuyo `phone` pertenezca a cliente CandyGo generan CandyCash.
- Al completar reserva en PhotoStudio:
  - se crea evento de recompensa local (outbox),
  - en sync se envía al API CandyGo,
  - API aplica crédito idempotente usando `external_source + external_event_id`.

## 8) Compatibilidad con DB existente de PhotoStudio
- No se alteran flujos existentes de inventario/reservas/transacciones.
- Se crean tablas nuevas prefijadas con `cg_` para aislamiento.
- Scripts separados por DB y orden de ejecución documentado.
