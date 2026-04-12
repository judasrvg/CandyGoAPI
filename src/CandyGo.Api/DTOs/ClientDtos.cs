namespace CandyGo.Api.DTOs;

public sealed class ClientDto
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public decimal Balance { get; set; }
    public string? SourceSystem { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CreateClientByAdminRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? SourceSystem { get; set; }
}

public sealed class ClientDeleteResultDto
{
    public long ClientId { get; set; }
    public string Action { get; set; } = string.Empty;
    public bool HadDependencies { get; set; }
    public bool IsActive { get; set; }
    public int OrdersCount { get; set; }
    public int WalletMovementsCount { get; set; }
    public string Message { get; set; } = string.Empty;
}
