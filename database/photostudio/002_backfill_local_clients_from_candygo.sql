/*
Script opcional.
Uso recomendado:
1) Primero ejecutar sync pull de clientes CandyGo hacia dbo.cg_local_clients.
2) Luego ejecutar este backfill para crear eventos de recompensa pendientes
   en reservas ya completadas que pertenezcan a clientes CandyGo.

IMPORTANTE:
- En PhotoStudio actual la reserva "completada" se maneja con CurrentStateType = 1 (Confirmed).
- No marca clientes como CandyGo; usa únicamente los ya sincronizados en cg_local_clients.
*/

SET XACT_ABORT ON;
GO

DECLARE @CompletedStateType INT = 1; -- Confirmed en PhotoStudio actual

INSERT INTO dbo.cg_local_reservation_reward_queue (
    reservation_id,
    client_phone,
    reservation_total
)
SELECT
    r.Id,
    r.ClientPhone,
    ISNULL(r.TotalAmount, 0)
FROM dbo.Reservation r
INNER JOIN dbo.cg_local_clients c
    ON c.phone = r.ClientPhone
WHERE r.CurrentStateType = @CompletedStateType
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.cg_local_reservation_reward_queue q
      WHERE q.reservation_id = r.Id
  );
GO
