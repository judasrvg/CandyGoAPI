using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace CandyGo.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (KeyNotFoundException ex)
        {
            await WriteError(context, HttpStatusCode.NotFound, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            await WriteError(context, HttpStatusCode.Unauthorized, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await WriteError(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (ArgumentException ex)
        {
            await WriteError(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (SqlException ex) when (ex.Number is 50001 or 50002 or 50003 or 50004)
        {
            await WriteError(context, HttpStatusCode.BadRequest, ex.Message, ex.Number.ToString());
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await WriteError(context, HttpStatusCode.Conflict, "El registro ya existe o viola una restricción única.", ex.Number.ToString());
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            await WriteError(context, HttpStatusCode.BadRequest, "La operación viola una restricción de datos.", ex.Number.ToString());
        }
        catch (SqlException ex) when (ex.Number == 0)
        {
            _logger.LogError(ex, "Database connectivity exception");
            await WriteError(
                context,
                HttpStatusCode.ServiceUnavailable,
                "No se pudo conectar al servidor de base de datos. Verifica host, puerto, credenciales y firewall.",
                ex.Number.ToString());
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            _logger.LogError(ex, "Database timeout exception");
            await WriteError(
                context,
                HttpStatusCode.GatewayTimeout,
                "La conexión a la base de datos excedió el tiempo de espera.",
                ex.Number.ToString());
        }
        catch (SqlException ex) when (ex.Number == 18456)
        {
            _logger.LogError(ex, "Database login failed");
            await WriteError(
                context,
                HttpStatusCode.Unauthorized,
                "Credenciales SQL inválidas para la base de datos configurada.",
                ex.Number.ToString());
        }
        catch (SqlException ex) when (ex.Number == 4060)
        {
            _logger.LogError(ex, "Database open failed");
            await WriteError(
                context,
                HttpStatusCode.BadGateway,
                "No se pudo abrir la base de datos especificada en la cadena de conexión.",
                ex.Number.ToString());
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database exception");
            await WriteError(context, HttpStatusCode.InternalServerError, "Error interno de base de datos.", ex.Number.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteError(context, HttpStatusCode.InternalServerError, "Error interno del servidor.");
        }
    }

    private static async Task WriteError(HttpContext context, HttpStatusCode statusCode, string message, string? errorCode = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            error = message,
            status = context.Response.StatusCode,
            errorCode
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
