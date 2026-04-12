using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CandyGo.Api.Infrastructure;

public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SqlConnectionFactory> _logger;
    private readonly string? _appsettingsConnectionString;

    public SqlConnectionFactory(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<SqlConnectionFactory> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
        _appsettingsConnectionString = LoadAppsettingsConnectionString();
    }

    public SqlConnection CreateConnection()
    {
        var connectionString = NormalizeConnectionString(_configuration.GetConnectionString("DefaultConnection"));
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection no está configurado.");
        }

        if (!_environment.IsDevelopment()
            && IsLocalSqlTarget(connectionString)
            && !string.IsNullOrWhiteSpace(_appsettingsConnectionString)
            && !IsLocalSqlTarget(_appsettingsConnectionString))
        {
            _logger.LogWarning(
                "Detected local SQL target in non-development environment. Falling back to appsettings.json SQL target.");
            connectionString = _appsettingsConnectionString;
        }

        connectionString = EnsureTcpDataSource(connectionString);
        return new SqlConnection(connectionString);
    }

    private string? LoadAppsettingsConnectionString()
    {
        try
        {
            var appsettingsConfig = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

            return NormalizeConnectionString(appsettingsConfig.GetConnectionString("DefaultConnection"));
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeConnectionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if ((normalized.StartsWith('"') && normalized.EndsWith('"'))
            || (normalized.StartsWith('\'') && normalized.EndsWith('\'')))
        {
            normalized = normalized[1..^1].Trim();
        }

        return normalized;
    }

    private static bool IsLocalSqlTarget(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var dataSource = (builder.DataSource ?? string.Empty).Trim();
            if (dataSource.Length == 0)
            {
                return false;
            }

            return dataSource.StartsWith(@".\", StringComparison.OrdinalIgnoreCase)
                || string.Equals(dataSource, ".", StringComparison.OrdinalIgnoreCase)
                || dataSource.StartsWith("(local)", StringComparison.OrdinalIgnoreCase)
                || dataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
                || dataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || dataSource.StartsWith("::1", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string EnsureTcpDataSource(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var dataSource = (builder.DataSource ?? string.Empty).Trim();
            if (dataSource.Length == 0 || IsLocalSqlTarget(connectionString))
            {
                return connectionString;
            }

            var hasProtocolPrefix =
                dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)
                || dataSource.StartsWith("np:", StringComparison.OrdinalIgnoreCase)
                || dataSource.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase);

            var hasExplicitPort = dataSource.Contains(',') || dataSource.Contains('\\');
            if (!hasProtocolPrefix)
            {
                dataSource = $"tcp:{dataSource}";
            }

            if (!hasExplicitPort)
            {
                dataSource = $"{dataSource},1433";
            }

            builder.DataSource = dataSource;
            return builder.ConnectionString;
        }
        catch
        {
            return connectionString;
        }
    }
}
