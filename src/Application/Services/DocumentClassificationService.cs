using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Services;

public sealed class DocumentClassificationService : IDocumentClassificationService
{
    private static readonly Dictionary<string, DocumentType> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = DocumentType.Pdf,
        [".csv"] = DocumentType.Csv,
        [".xlsx"] = DocumentType.Excel,
        [".xlsm"] = DocumentType.Excel,
        [".xls"] = DocumentType.Excel,
        [".png"] = DocumentType.Image,
        [".jpg"] = DocumentType.Image,
        [".jpeg"] = DocumentType.Image,
        [".tif"] = DocumentType.Image,
        [".tiff"] = DocumentType.Image,
        [".bmp"] = DocumentType.Screenshot,
        [".doc"] = DocumentType.Word,
        [".docx"] = DocumentType.Word,
        [".dwg"] = DocumentType.Cad,
        [".dxf"] = DocumentType.Cad
    };

    public Result<DocumentType> Classify(BlobDocument document)
    {
        var extension = Path.GetExtension(document.BlobName);
        if (!string.IsNullOrWhiteSpace(extension) && ExtensionMap.TryGetValue(extension, out var type))
        {
            return Result<DocumentType>.Success(type);
        }

        var contentType = document.ContentType;
        if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Result<DocumentType>.Success(DocumentType.Pdf);
        }

        if (contentType.Contains("csv", StringComparison.OrdinalIgnoreCase))
        {
            return Result<DocumentType>.Success(DocumentType.Csv);
        }

        if (contentType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) || contentType.Contains("excel", StringComparison.OrdinalIgnoreCase))
        {
            return Result<DocumentType>.Success(DocumentType.Excel);
        }

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return Result<DocumentType>.Success(DetectImageVariant(document.Content));
        }

        if (contentType.Contains("word", StringComparison.OrdinalIgnoreCase) || contentType.Contains("officedocument.wordprocessingml", StringComparison.OrdinalIgnoreCase))
        {
            return Result<DocumentType>.Success(DocumentType.Word);
        }

        return DetectBySignature(document.Content);
    }

    private static DocumentType DetectImageVariant(byte[] bytes)
    {
        return bytes.Length > 0 && bytes.Length < 750_000
            ? DocumentType.Screenshot
            : DocumentType.Image;
    }

    private static Result<DocumentType> DetectBySignature(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
        {
            return Result<DocumentType>.Success(DocumentType.Pdf);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B)
        {
            return Result<DocumentType>.Success(DocumentType.Excel);
        }

        return Result<DocumentType>.Success(DocumentType.Unsupported);
    }
}
