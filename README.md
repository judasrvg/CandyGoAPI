# CandyGoAPI

Backend de CandyGo (tienda + CandyCash + admin + sync con PhotoStudio).

## Estado actual
- Base de decisiones funcionales en `docs/decisions.md`.
- Scripts SQL iniciales en `database/`:
  - `database/candygo/` para DB remota de CandyGo.
  - `database/photostudio/` para DB local de PhotoStudio (espejo + outbox sync).

## Siguiente paso de implementación
1. Scaffold API .NET 8 (Auth, Products, Orders, Wallet, Settings, Sync).
2. Aplicar scripts en staging.
3. Integrar flujo de sync en PhotoStudio (push/pull + botón Sync).
