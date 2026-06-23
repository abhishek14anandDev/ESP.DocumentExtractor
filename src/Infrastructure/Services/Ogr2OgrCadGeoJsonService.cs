using System.Diagnostics;
using ESP.DocumentExtractor.Application.DTOs;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.ResultPattern;
using ESP.DocumentExtractor.Infrastructure.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.Infrastructure.Services;

public sealed class Ogr2OgrCadGeoJsonService(
    IOptions<CadConversionOptions> options,
    ILogger<Ogr2OgrCadGeoJsonService> logger) : ICadGeoJsonService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dwg",
        ".dxf"
    };

    public async Task<Result<CadGeoJsonResponse>> ConvertAsync(
        string fileName,
        Stream content,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result<CadGeoJsonResponse>.Failure(new Error("cad.file_name.required", "File name is required."));
        }

        var extension = Path.GetExtension(fileName);
        if (!SupportedExtensions.Contains(extension))
        {
            return Result<CadGeoJsonResponse>.Failure(new Error("cad.file_type.unsupported", "Only DWG and DXF files can be converted to GeoJSON."));
        }

        var workingDirectory = Path.Combine(Path.GetTempPath(), "esp-document-extractor", correlationId);
        Directory.CreateDirectory(workingDirectory);

        var inputPath = Path.Combine(workingDirectory, $"input{extension}");
        var outputPath = Path.Combine(workingDirectory, "output.geojson");

        try
        {
            await using (var fileStream = File.Create(inputPath))
            {
                await content.CopyToAsync(fileStream, cancellationToken);
            }

            var conversionResult = await RunOgr2OgrAsync(inputPath, outputPath, cancellationToken);
            if (conversionResult.IsFailure)
            {
                return Result<CadGeoJsonResponse>.Failure(conversionResult.Error);
            }

            if (!File.Exists(outputPath))
            {
                return Result<CadGeoJsonResponse>.Failure(new Error("cad.geojson.missing", "CAD conversion completed without producing a GeoJSON file."));
            }

            var geoJson = await File.ReadAllTextAsync(outputPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(geoJson))
            {
                return Result<CadGeoJsonResponse>.Failure(new Error("cad.geojson.empty", "CAD conversion produced an empty GeoJSON document."));
            }

            return Result<CadGeoJsonResponse>.Success(new CadGeoJsonResponse
            {
                CorrelationId = correlationId,
                FileName = fileName,
                GeoJson = geoJson
            });
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    private async Task<Result<object>> RunOgr2OgrAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var cadOptions = options.Value;
        var timeout = TimeSpan.FromSeconds(Math.Max(1, cadOptions.TimeoutSeconds));
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(cadOptions.Ogr2OgrPath) ? "ogr2ogr" : cadOptions.Ogr2OgrPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("GeoJSON");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add(inputPath);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return Result<object>.Failure(new Error(
                "cad.converter.not_found",
                $"The CAD converter '{startInfo.FileName}' was not found. Install GDAL/ogr2ogr or set CadConversion:Ogr2OgrPath."));
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var completedTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(timeout, cancellationToken);

        if (await Task.WhenAny(completedTask, timeoutTask) == timeoutTask)
        {
            TryKill(process);
            return Result<object>.Failure(new Error("cad.converter.timeout", $"CAD conversion exceeded the {timeout.TotalSeconds:N0} second timeout."));
        }

        await completedTask;
        var standardOutput = await outputTask;
        var standardError = await errorTask;

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
            logger.LogWarning("ogr2ogr failed with exit code {ExitCode}: {ConverterOutput}", process.ExitCode, detail);

            return Result<object>.Failure(new Error(
                "cad.converter.failed",
                string.IsNullOrWhiteSpace(detail)
                    ? $"CAD conversion failed with exit code {process.ExitCode}."
                    : detail.Trim()));
        }

        return Result<object>.Success(new object());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup after a converter timeout.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary files should not make an otherwise completed request fail.
        }
    }
}
