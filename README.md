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

## Utilidad local: bajar imágenes de productos desde API remota
Script:
- `scripts/download-product-images.ps1`

Ejemplo (PowerShell):
```powershell
cd D:\BIBLIOTECA\MyTools\Clientes\CandyGo\CandyGoAPI
powershell -ExecutionPolicy Bypass -File .\scripts\download-product-images.ps1 `
  -ApiBaseUrl "https://candygoapi.onrender.com" `
  -AdminPhone "TU_TELEFONO_ADMIN" `
  -AdminPassword "TU_PASSWORD_ADMIN" `
  -ClearOutput
```

Salida por defecto:
- `src/CandyGo.Api/wwwroot/images/products`

Opción 1 clic:
- Ejecuta `scripts/download-product-images.cmd` (doble clic en Windows).
- Por defecto usa admin `54831128 / Admin123*` (puedes editar el `.cmd`).
