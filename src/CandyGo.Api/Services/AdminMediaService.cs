using CandyGo.Api.DTOs;
using Microsoft.Extensions.Options;

namespace CandyGo.Api.Services;

public sealed class MediaStorageOptions
{
    public string ProductsRelativePath { get; set; } = "images/products";
    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024;
}

public interface IAdminMediaService
{
    Task<ProductImageUploadResultDto> SaveProductImageAsync(IFormFile? file, CancellationToken cancellationToken);
}

public sealed class AdminMediaService : IAdminMediaService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webp",
        ".png",
        ".jpg",
        ".jpeg"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/webp",
        "image/png",
        "image/jpeg"
    };

    private readonly IWebHostEnvironment _environment;
    private readonly MediaStorageOptions _options;

    public AdminMediaService(IWebHostEnvironment environment, IOptions<MediaStorageOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public async Task<ProductImageUploadResultDto> SaveProductImageAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            throw new ArgumentException("No se recibió imagen para subir.");
        }

        if (file.Length > _options.MaxUploadBytes)
        {
            throw new ArgumentException($"La imagen supera el límite permitido ({_options.MaxUploadBytes / (1024 * 1024)} MB).");
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new ArgumentException("Formato inválido. Solo se permiten webp, png, jpg y jpeg.");
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType))
        {
            var normalizedType = file.ContentType.Split(';', 2)[0].Trim();
            if (!AllowedContentTypes.Contains(normalizedType))
            {
                throw new ArgumentException("Tipo de contenido inválido para imagen.");
            }
        }

        var normalizedExtension = extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : extension.ToLowerInvariant();
        var safeFolder = NormalizeRelativeFolder(_options.ProductsRelativePath);
        var safeFileName = $"cg_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{normalizedExtension}";

        var webRoot = ResolveWebRoot();
        var targetFolder = Path.Combine(webRoot, safeFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(targetFolder);

        var targetPath = Path.Combine(targetFolder, safeFileName);

        await using (var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        await using (var input = file.OpenReadStream())
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        var relativeUrl = $"/{safeFolder}/{safeFileName}".Replace("//", "/");
        return new ProductImageUploadResultDto(safeFileName, relativeUrl, file.Length, file.ContentType ?? "application/octet-stream");
    }

    private string ResolveWebRoot()
    {
        if (!string.IsNullOrWhiteSpace(_environment.WebRootPath))
        {
            return _environment.WebRootPath;
        }

        return Path.Combine(_environment.ContentRootPath, "wwwroot");
    }

    private static string NormalizeRelativeFolder(string? rawFolder)
    {
        var normalized = string.IsNullOrWhiteSpace(rawFolder)
            ? "images/products"
            : rawFolder.Replace('\\', '/').Trim();

        normalized = normalized.Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "images/products";
        }

        var parts = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (part is "." or "..")
            {
                throw new ArgumentException("Ruta de almacenamiento inválida.");
            }
        }

        return string.Join("/", parts);
    }
}
