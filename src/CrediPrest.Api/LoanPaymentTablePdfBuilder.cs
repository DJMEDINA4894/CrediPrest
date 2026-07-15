using System.Globalization;
using System.Text;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.Services;
using CrediPrest.Domain.Enums;

namespace CrediPrest.Api;

internal static class LoanPaymentTablePdfBuilder
{
    private static readonly CultureInfo NicaraguaCulture = CultureInfo.GetCultureInfo("es-NI");

    public static byte[] Build(LoanDetailDto detail)
    {
        const decimal pageWidth = 842;
        const decimal pageHeight = 595;
        const decimal margin = 32;
        const decimal rowHeight = 18;
        var columns = new[]
        {
            new Column("#", 25, 4),
            new Column("Vence", 85, 18),
            new Column("Capital", 75, 18),
            new Column("Interes", 75, 18),
            new Column("Cuota", 80, 18),
            new Column("Mora", 62, 18),
            new Column("Pagado", 80, 18),
            new Column("Pendiente", 85, 18),
            new Column("Debe", 78, 18),
            new Column("Estado", 72, 16)
        };
        var tableWidth = columns.Sum(column => column.Width);
        var currency = detail.Loan.Currency == CurrencyType.Usd ? "USD" : "C$";
        var rows = detail.Installments.Select(installment =>
        {
            var lateFee = GetLateFeeAllocation(detail, installment);
            return new[]
            {
                installment.InstallmentNumber.ToString(CultureInfo.InvariantCulture),
                FormatDate(installment.DueDate),
                Money(installment.PrincipalAmount, currency),
                Money(installment.InterestAmount, currency),
                Money(installment.PaymentAmount, currency),
                lateFee.Amount > 0 ? Money(lateFee.Amount, currency) : "-",
                Money(installment.AmountPaid + lateFee.AmountPaid, currency),
                Money(Math.Max(0, installment.PaymentAmount - installment.AmountPaid) + lateFee.PendingAmount, currency),
                Money(installment.RemainingBalance, currency),
                InstallmentStatusLabel(installment.Status)
            };
        }).ToList();

        var pages = new List<string>();
        var rowIndex = 0;
        var pageNumber = 1;

        while (rowIndex < rows.Count || pages.Count == 0)
        {
            var lines = new List<string> { "0.2 w" };
            var y = pageHeight - margin;

            lines.Add(PdfText(margin, y, "CrediPrest - Tabla de pagos", 16, "F2"));
            lines.Add(PdfText(pageWidth - 115, y, $"Pagina {pageNumber}", 8));
            y -= 22;
            lines.Add(PdfText(margin, y, $"Cliente: {detail.Loan.ClientName}", 10, "F2"));
            lines.Add(PdfText(360, y, $"Frecuencia: {FrequencyLabel(detail.Loan.PaymentFrequency)}", 10));
            y -= 16;
            if (!string.IsNullOrWhiteSpace(detail.Loan.LenderName))
            {
                lines.Add(PdfText(margin, y, $"Prestamista: {detail.Loan.LenderName}", 9));
                y -= 16;
            }
            if (!string.IsNullOrWhiteSpace(detail.Loan.ReferenceName))
            {
                lines.Add(PdfText(margin, y, $"Referencia: {detail.Loan.ReferenceName}", 9));
                y -= 16;
            }
            lines.Add(PdfText(margin, y, $"Prestado: {Money(detail.Loan.PrincipalAmount, currency)}", 9));
            lines.Add(PdfText(190, y, $"Interes mensual: {detail.Loan.MonthlyInterestRate.ToString("0.##", NicaraguaCulture)}%", 9));
            lines.Add(PdfText(360, y, $"Total: {Money(detail.Loan.TotalToPay, currency)}", 9));
            lines.Add(PdfText(520, y, $"Pagado: {Money(detail.Loan.TotalPaid, currency)}", 9));
            lines.Add(PdfText(670, y, $"Debe: {Money(detail.Loan.PendingBalance, currency)}", 9));
            y -= 22;
            if (detail.Loan.LateFeesPending > 0)
            {
                lines.Add(PdfText(margin, y, $"Mora pendiente: {Money(detail.Loan.LateFeesPending, currency)}", 9, "F2"));
                y -= 18;
            }

            var headerTop = y + 6;
            var headerBottom = y - rowHeight + 5;
            lines.Add(PdfLine(margin, headerTop, margin + tableWidth, headerTop));
            lines.Add(PdfLine(margin, headerBottom, margin + tableWidth, headerBottom));

            var x = margin + 4;
            foreach (var column in columns)
            {
                lines.Add(PdfText(x, y - 7, column.Title, 8, "F2"));
                x += column.Width;
            }
            y -= rowHeight;

            while (rowIndex < rows.Count && y > margin + 32)
            {
                x = margin + 4;
                for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
                {
                    lines.Add(PdfText(x, y - 7, Truncate(rows[rowIndex][columnIndex], columns[columnIndex].MaxLength), 8));
                    x += columns[columnIndex].Width;
                }
                lines.Add(PdfLine(margin, y - rowHeight + 5, margin + tableWidth, y - rowHeight + 5));
                y -= rowHeight;
                rowIndex++;
            }

            lines.Add(PdfText(margin, margin, $"Generado: {FormatDate(BusinessClock.Today)}", 8));
            pages.Add(string.Join('\n', lines));
            pageNumber++;
        }

        return BuildPdfDocument(pages, pageWidth, pageHeight);
    }

    public static string FileName(LoanDto loan)
    {
        var displayName = string.IsNullOrWhiteSpace(loan.ReferenceName)
            ? loan.ClientName
            : $"{loan.ClientName}-{loan.ReferenceName}";
        var safeName = PrintableText(displayName).ToLowerInvariant();
        safeName = string.Join('-', safeName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return $"tabla-pagos-{(string.IsNullOrWhiteSpace(safeName) ? "prestamo" : safeName)}.pdf";
    }

    private static byte[] BuildPdfDocument(IReadOnlyList<string> pages, decimal pageWidth, decimal pageHeight)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pages.Select((_, index) => $"{5 + index * 2} 0 R"))}] /Count {pages.Count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"
        };

        for (var index = 0; index < pages.Count; index++)
        {
            var content = pages[index];
            var contentObjectId = 6 + index * 2;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(pageWidth)} {Number(pageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjectId} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        }

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }
        pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xrefOffset).Append("\n%%EOF");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static LateFeeAllocation GetLateFeeAllocation(LoanDetailDto detail, InstallmentDto installment)
    {
        var allocation = detail.Charges.SelectMany(charge => charge.Allocations)
            .FirstOrDefault(item => item.InstallmentId == installment.Id);
        return allocation is null
            ? new LateFeeAllocation(0, 0, 0)
            : new LateFeeAllocation(allocation.Amount, allocation.AmountPaid, allocation.PendingAmount);
    }

    private static string Money(decimal value, string currency)
        => $"{currency} {value.ToString("N2", NicaraguaCulture)}";

    private static string FormatDate(DateTime value)
    {
        var month = NicaraguaCulture.TextInfo.ToTitleCase(NicaraguaCulture.DateTimeFormat.GetMonthName(value.Month));
        return $"{value.Day} {month} {value.Year}";
    }

    private static string FrequencyLabel(PaymentFrequency frequency) => frequency switch
    {
        PaymentFrequency.Weekly => "Semanal",
        PaymentFrequency.Biweekly => "Quincenal",
        _ => "Mensual"
    };

    private static string InstallmentStatusLabel(InstallmentStatus status) => status switch
    {
        InstallmentStatus.Partial => "Parcial",
        InstallmentStatus.Paid => "Pagada",
        InstallmentStatus.Overdue => "Retrasada",
        _ => "Pendiente"
    };

    private static string PdfText(decimal x, decimal y, string value, int size = 9, string font = "F1")
        => $"BT /{font} {size} Tf {Number(x)} {Number(y)} Td ({EscapePdfText(value)}) Tj ET";

    private static string PdfLine(decimal x1, decimal y1, decimal x2, decimal y2)
        => $"{Number(x1)} {Number(y1)} m {Number(x2)} {Number(y2)} l S";

    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string EscapePdfText(string value)
        => PrintableText(value).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string Truncate(string value, int maxLength)
    {
        var text = PrintableText(value);
        return text.Length > maxLength ? $"{text[..Math.Max(0, maxLength - 3)]}..." : text;
    }

    private static string PrintableText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder();
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            result.Append(character is >= ' ' and <= '~' ? character : ' ');
        }
        return string.Join(' ', result.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record Column(string Title, decimal Width, int MaxLength);
    private sealed record LateFeeAllocation(decimal Amount, decimal AmountPaid, decimal PendingAmount);
}
