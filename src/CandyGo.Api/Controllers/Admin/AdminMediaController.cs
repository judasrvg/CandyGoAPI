using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/media")]
public sealed class AdminMediaController : ControllerBase
{
    private readonly IAdminMediaService _mediaService;

    public AdminMediaController(IAdminMediaService mediaService)
    {
        _mediaService = mediaService;
    }

    [HttpPost("product-image")]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
    public async Task<ActionResult<ProductImageUploadResponse>> UploadProductImage([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        var stored = await _mediaService.SaveProductImageAsync(file, cancellationToken);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var absoluteUrl = $"{baseUrl}{stored.RelativeUrl}";

        return Ok(new ProductImageUploadResponse(
            stored.FileName,
            stored.RelativeUrl,
            absoluteUrl,
            stored.SizeBytes,
            stored.ContentType));
    }
}
