namespace CandyGo.Api.Domain;

public static class OrderStatusMachine
{
    public const string Pending = "PENDIENTE";
    public const string Confirmed = "CONFIRMADA";
    public const string Preparing = "PREPARANDO";
    public const string Ready = "LISTA";
    public const string Delivered = "ENTREGADA";
    public const string Cancelled = "CANCELADA";

    private static readonly HashSet<string> ValidStatuses =
    [
        Pending,
        Confirmed,
        Preparing,
        Ready,
        Delivered,
        Cancelled
    ];

    private static readonly Dictionary<string, HashSet<string>> Transitions = new(StringComparer.Ordinal)
    {
        [Pending] = [Confirmed, Cancelled],
        [Confirmed] = [Preparing, Cancelled],
        [Preparing] = [Ready, Cancelled],
        [Ready] = [Delivered, Cancelled],
        [Delivered] = [],
        [Cancelled] = []
    };

    public static bool IsValid(string status) => ValidStatuses.Contains(status);

    public static bool CanTransition(string currentStatus, string targetStatus)
    {
        if (!IsValid(currentStatus) || !IsValid(targetStatus))
        {
            return false;
        }

        if (string.Equals(currentStatus, targetStatus, StringComparison.Ordinal))
        {
            return true;
        }

        return Transitions.TryGetValue(currentStatus, out var allowed) && allowed.Contains(targetStatus);
    }
}
