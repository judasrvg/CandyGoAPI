using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using CandyGo.Api.Infrastructure;
using CandyGo.Api.Middleware;
using CandyGo.Api.Security;
using CandyGo.Api.Services;
using Dapper;
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
builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientApps", policy =>
    {
        if (configuredOrigins.Length > 0)
        {
            policy.WithOrigins(configuredOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();

            return;
        }

        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
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
