using CrediPrest.Domain.Entities;

namespace CrediPrest.Application.Services;

internal static class PaymentReceiptFactory
{
    public static PaymentReceipt? Create(
        string? imageBase64,
        string? fileName,
        string? contentType)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return null;
        }

        var normalizedContentType = contentType?.Trim().ToLowerInvariant();
        if (normalizedContentType is not ("image/jpeg" or "image/png" or "image/webp"))
        {
            throw new InvalidOperationException("El comprobante debe ser una imagen JPG, PNG o WEBP.");
        }

        var base64 = imageBase64.Trim();
        var separatorIndex = base64.IndexOf(',');
        if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && separatorIndex >= 0)
        {
            base64 = base64[(separatorIndex + 1)..];
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("La imagen del comprobante no tiene un formato válido.");
        }

        const int maxImageBytes = 5 * 1024 * 1024;
        if (content.Length == 0
            || content.Length > maxImageBytes
            || !HasValidImageSignature(content, normalizedContentType))
        {
            throw new InvalidOperationException("El comprobante debe ser una imagen válida de hasta 5 MB.");
        }

        var normalizedFileName = Path.GetFileName(fileName?.Trim() ?? "comprobante");
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            normalizedFileName = "comprobante";
        }

        return new PaymentReceipt
        {
            FileName = normalizedFileName.Length > 180 ? normalizedFileName[..180] : normalizedFileName,
            ContentType = normalizedContentType,
            Content = content
        };
    }

    private static bool HasValidImageSignature(byte[] content, string contentType)
        => contentType switch
        {
            "image/jpeg" => content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF,
            "image/png" => content.Length >= 8 && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47,
            "image/webp" => content.Length >= 12 && content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46 && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50,
            _ => false
        };
}
