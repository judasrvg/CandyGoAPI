namespace CandyGo.Api.DTOs;

public sealed record ProductImageUploadResultDto(
    string FileName,
    string RelativeUrl,
    long SizeBytes,
    string ContentType);

public sealed record ProductImageUploadResponse(
    string FileName,
    string RelativeUrl,
    string Url,
    long SizeBytes,
    string ContentType);
