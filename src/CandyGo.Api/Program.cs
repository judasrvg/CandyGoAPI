using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using CandyGo.Api.Infrastructure;
using CandyGo.Api.Middleware;
using CandyGo.Api.Security;
using CandyGo.Api.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

DefaultTypeMap.MatchNamesWithUnderscores = true;
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("Sync"));

builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ISyncService, SyncService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    throw new InvalidOperationException("Jwt:Key no está configurado.");
}

var jwtKeyBytes = Encoding.UTF8.GetBytes(jwtOptions.Key);
if (jwtKeyBytes.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key debe tener al menos 32 bytes (256 bits).");
}

var signingKey = new SymmetricSecurityKey(jwtKeyBytes);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: remoteIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 240,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddFixedWindowLimiter("AuthEndpoints", policy =>
    {
        policy.PermitLimit = 12;
        policy.Window = TimeSpan.FromMinutes(1);
        policy.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        policy.QueueLimit = 0;
        policy.AutoReplenishment = true;
    });
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = false;
        options.IncludeErrorDetails = builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(15),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (!string.IsNullOrWhiteSpace(context.Token))
                {
                    return Task.CompletedTask;
                }

                var hasAuthHeader = !string.IsNullOrWhiteSpace(context.Request.Headers.Authorization);
                if (hasAuthHeader)
                {
                    // Prefer explicit Authorization header over cookie token.
                    return Task.CompletedTask;
                }

                var clientMarker = context.Request.Headers["X-CandyGo-Client"].ToString();
                if (string.IsNullOrWhiteSpace(clientMarker))
                {
                    // Cookie JWT is only accepted for known app clients sending marker header.
                    return Task.CompletedTask;
                }

                var requestPath = context.HttpContext.Request.Path;
                var preferAdminCookie = requestPath.StartsWithSegments("/api/admin");
                var preferredCookie = preferAdminCookie ? AuthCookieNames.AdminAccessToken : AuthCookieNames.ClientAccessToken;

                if (context.Request.Cookies.TryGetValue(preferredCookie, out var preferredToken)
                    && !string.IsNullOrWhiteSpace(preferredToken))
                {
                    context.Token = preferredToken;
                    return Task.CompletedTask;
                }

                if (context.Request.Cookies.TryGetValue(AuthCookieNames.ClientAccessToken, out var clientToken)
                    && !string.IsNullOrWhiteSpace(clientToken))
                {
                    context.Token = clientToken;
                    return Task.CompletedTask;
                }

                if (context.Request.Cookies.TryGetValue(AuthCookieNames.AdminAccessToken, out var adminToken)
                    && !string.IsNullOrWhiteSpace(adminToken))
                {
                    context.Token = adminToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var principal = context.Principal;
                var subject = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var jti = principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);
                var role = principal?.FindFirstValue(ClaimTypes.Role);

                if (!long.TryParse(subject, out _) || string.IsNullOrWhiteSpace(jti))
                {
                    context.Fail("Token inválido.");
                    return Task.CompletedTask;
                }

                if (!string.Equals(role, "client", StringComparison.Ordinal) && !string.Equals(role, "admin", StringComparison.Ordinal))
                {
                    context.Fail("Rol inválido en token.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
var allowedOriginSet = configuredOrigins
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

var configuredHostSuffixes = builder.Configuration.GetSection("Cors:AllowedHostSuffixes").Get<string[]>() ?? Array.Empty<string>();
var allowedHostSuffixes = configuredHostSuffixes
    .Where(suffix => !string.IsNullOrWhiteSpace(suffix))
    .Select(suffix => suffix.Trim().TrimStart('.'))
    .ToArray();

var allowAllOrigins = builder.Configuration.GetValue<bool>("Cors:AllowAllOrigins");

static bool HostMatchesSuffix(string host, string suffix)
{
    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(suffix))
    {
        return false;
    }

    return host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase);
}

bool IsAllowedOrigin(string origin)
{
    if (string.IsNullOrWhiteSpace(origin))
    {
        return false;
    }

    if (allowAllOrigins)
    {
        return true;
    }

    var normalizedOrigin = origin.Trim().TrimEnd('/');

    if (allowedOriginSet.Contains(normalizedOrigin))
    {
        return true;
    }

    if (builder.Environment.IsDevelopment() && string.Equals(normalizedOrigin, "null", StringComparison.OrdinalIgnoreCase))
    {
        // Allows local manual testing from file:// in development only.
        return true;
    }

    if (!Uri.TryCreate(normalizedOrigin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    var isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    if (!isHttp && !isHttps)
    {
        return false;
    }

    // Any localhost/loopback port is allowed for local tools.
    if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (uri.Scheme == Uri.UriSchemeHttps && allowedHostSuffixes.Any(suffix => HostMatchesSuffix(uri.Host, suffix)))
    {
        return true;
    }

    return false;
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientApps", policy =>
    {
        policy.SetIsOriginAllowed(IsAllowedOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CandyGo API",
        Version = "v1",
        Description = "API de CandyGo (cliente, admin, wallet y sync)."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startupLogger.LogInformation("CandyGo API environment: {EnvironmentName}", app.Environment.EnvironmentName);
var envConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (!string.IsNullOrWhiteSpace(envConnectionString))
{
    startupLogger.LogWarning("ConnectionStrings__DefaultConnection environment variable is set and overrides appsettings.");
    try
    {
        var envSqlBuilder = new SqlConnectionStringBuilder(envConnectionString);
        startupLogger.LogInformation(
            "SQL target from ENV: DataSource={DataSource}; InitialCatalog={InitialCatalog}; Encrypt={Encrypt}; TrustServerCertificate={TrustServerCertificate}",
            envSqlBuilder.DataSource,
            envSqlBuilder.InitialCatalog,
            envSqlBuilder.Encrypt,
            envSqlBuilder.TrustServerCertificate);
    }
    catch (Exception ex)
    {
        startupLogger.LogWarning(ex, "Could not parse ConnectionStrings__DefaultConnection environment variable.");
    }
}

try
{
    var connectionString = app.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        startupLogger.LogWarning("DefaultConnection is empty.");
    }
    else
    {
        var sqlBuilder = new SqlConnectionStringBuilder(connectionString);
        startupLogger.LogInformation(
            "SQL target configured: DataSource={DataSource}; InitialCatalog={InitialCatalog}; Encrypt={Encrypt}; TrustServerCertificate={TrustServerCertificate}",
            sqlBuilder.DataSource,
            sqlBuilder.InitialCatalog,
            sqlBuilder.Encrypt,
            sqlBuilder.TrustServerCertificate);

        var dataSource = sqlBuilder.DataSource?.Trim() ?? string.Empty;
        var isLocalSqlTarget =
            dataSource.StartsWith(@".\", StringComparison.OrdinalIgnoreCase)
            || string.Equals(dataSource, ".", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("(local)", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("::1", StringComparison.OrdinalIgnoreCase);

        if (!app.Environment.IsDevelopment() && isLocalSqlTarget)
        {
            startupLogger.LogWarning(
                "Non-development environment is using a local SQL target ({DataSource}). Verify ConnectionStrings__DefaultConnection in deployment settings.",
                dataSource);
        }
    }
}
catch (Exception ex)
{
    startupLogger.LogWarning(ex, "Could not parse DefaultConnection for startup diagnostics.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";

    await next();
});

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("ClientApps");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
