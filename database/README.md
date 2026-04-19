# Scripts DB - CandyGo

Este directorio separa scripts por base de datos para evitar confusiones.

## Carpetas
- `candygo/`: scripts para la **DB remota de CandyGo** (tienda + wallet + admin).
- `photostudio/`: scripts para la **DB local de PhotoStudio** (vista local, espejo y cola de sync).

## Orden de ejecución

### A) DB remota CandyGo
1. `candygo/001_schema.sql`
2. `candygo/002_seed_defaults.sql`
3. `candygo/003_simplify_order_statuses.sql`
4. `candygo/004_store_schedule.sql`
5. `candygo/005_notifications_and_contests.sql`
6. `candygo/006_allow_slot_triple_contests.sql`
7. `candygo/007_product_special_offer.sql`

Nota:
- Si la ruleta (`SLOT_TRIPLE`) no guarda en Admin, vuelve a ejecutar `006_allow_slot_triple_contests.sql`.
  Este script ahora elimina cualquier check constraint legado sobre `contest_type` y recrea el constraint correcto.

### B) DB local PhotoStudio
1. `photostudio/001_schema_local_sync.sql`
2. `photostudio/002_backfill_local_clients_from_candygo.sql` (opcional, tras primer pull/sync)

## Convenciones
- Todos los objetos nuevos usan prefijo `cg_` (tablas) o esquema `cg`.
- No se eliminan ni modifican tablas existentes de PhotoStudio.
- Scripts son idempotentes cuando aplica (`IF NOT EXISTS`, `IF OBJECT_ID IS NULL`).

## Notas operativas
- Ejecutar en ambiente de staging antes de producción.
- En producción: respaldar DB antes de correr scripts.
- El cálculo de CandyCash por reserva completada depende de `cg.business_rules`:
  - `reward_percent`
  - `cash_conversion_rate`
