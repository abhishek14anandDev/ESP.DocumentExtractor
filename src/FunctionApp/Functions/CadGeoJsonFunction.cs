using System.Text.Json;
using ESP.DocumentExtractor.Application.DTOs;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.ResultPattern;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ESP.DocumentExtractor.FunctionApp.Functions;

public sealed class CadGeoJsonFunction(
    ICadGeoJsonService cadGeoJsonService,
    ILogger<CadGeoJsonFunction> logger)
{
    [Function(nameof(CadGeoJsonFunction))]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cad/geojson")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            var conversionResult = request.HasFormContentType
                ? await ConvertUploadedFileAsync(request, correlationId, cancellationToken)
                : await ConvertLocalFileAsync(request, correlationId, cancellationToken);

            if (conversionResult.IsFailure)
            {
                return new BadRequestObjectResult(new
                {
                    correlationId,
                    error = conversionResult.Error.Code,
                    message = conversionResult.Error.Message
                });
            }

            request.HttpContext.Response.Headers["x-correlation-id"] = conversionResult.Value.CorrelationId;
            return new ContentResult
            {
                Content = conversionResult.Value.GeoJson,
                ContentType = "application/geo+json",
                StatusCode = StatusCodes.Status200OK
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Received invalid CAD GeoJSON request JSON.");
            return new BadRequestObjectResult(new
            {
                correlationId,
                error = "request.invalid_json",
                message = "Request body contains invalid JSON."
            });
        }
    }

    private async Task<Result<CadGeoJsonResponse>> ConvertUploadedFileAsync(
        HttpRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Result<CadGeoJsonResponse>.Failure(new Error(
                "cad.file.required",
                "Upload a DWG or DXF file using the multipart form field named 'file'."));
        }

        var requestedCorrelationId = form.TryGetValue("correlationId", out var formCorrelationId) && !string.IsNullOrWhiteSpace(formCorrelationId.ToString())
            ? formCorrelationId.ToString()
            : correlationId;

        await using var stream = file.OpenReadStream();
        return await cadGeoJsonService.ConvertAsync(file.FileName, stream, requestedCorrelationId, cancellationToken);
    }

    private async Task<Result<CadGeoJsonResponse>> ConvertLocalFileAsync(
        HttpRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var cadRequest = await request.ReadFromJsonAsync<CadGeoJsonRequest>(cancellationToken);
        if (cadRequest is null || string.IsNullOrWhiteSpace(cadRequest.FilePath))
        {
            return Result<CadGeoJsonResponse>.Failure(new Error(
                "cad.file_path.required",
                "Provide multipart form-data with a file field, or JSON with a filePath value."));
        }

        if (!File.Exists(cadRequest.FilePath))
        {
            return Result<CadGeoJsonResponse>.Failure(new Error(
                "cad.file_path.not_found",
                $"File '{cadRequest.FilePath}' does not exist."));
        }

        var requestedCorrelationId = string.IsNullOrWhiteSpace(cadRequest.CorrelationId)
            ? correlationId
            : cadRequest.CorrelationId;

        await using var stream = File.OpenRead(cadRequest.FilePath);
        return await cadGeoJsonService.ConvertAsync(Path.GetFileName(cadRequest.FilePath), stream, requestedCorrelationId, cancellationToken);
    }
}
