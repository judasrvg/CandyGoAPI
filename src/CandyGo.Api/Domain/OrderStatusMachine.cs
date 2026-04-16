namespace CandyGo.Api.Domain;

public static class OrderStatusMachine
{
    public const string Pending = "PENDIENTE";
    public const string Confirmed = "CONFIRMADA";
    public const string Delivered = "ENTREGADA";
    public const string Cancelled = "CANCELADA";

    private static readonly HashSet<string> ValidStatuses =
    [
        Pending,
        Confirmed,
        Delivered,
        Cancelled
    ];

    private static readonly Dictionary<string, HashSet<string>> Transitions = new(StringComparer.Ordinal)
    {
        [Pending] = [Confirmed, Cancelled],
        [Confirmed] = [Delivered, Cancelled],
        [Delivered] = [],
        [Cancelled] = []
    };

    private static string NormalizeLegacy(string status) => status switch
    {
        "PREPARANDO" => Confirmed,
        "LISTA" => Confirmed,
        _ => status
    };

    public static bool IsValid(string status) => ValidStatuses.Contains(NormalizeLegacy(status));

    public static bool CanTransition(string currentStatus, string targetStatus)
    {
        var normalizedCurrent = NormalizeLegacy(currentStatus);
        var normalizedTarget = NormalizeLegacy(targetStatus);

        if (!IsValid(normalizedCurrent) || !IsValid(normalizedTarget))
        {
            return false;
        }

        if (string.Equals(normalizedCurrent, normalizedTarget, StringComparison.Ordinal))
        {
            return true;
        }

        return Transitions.TryGetValue(normalizedCurrent, out var allowed) && allowed.Contains(normalizedTarget);
    }
}
