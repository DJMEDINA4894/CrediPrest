using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.Services;
using CrediPrest.Domain.Enums;

namespace CrediPrest.Api;

internal static class LoanAgreementDocumentBuilder
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly CultureInfo NicaraguaCulture = new("es-NI");
    private sealed record TextSegment(string Text, bool IsBold = false);

    public static byte[] Build(LoanDetailDto detail, string templatePath)
    {
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("No se encontró la plantilla del acuerdo de préstamo.", templatePath);
        }

        using var output = new MemoryStream();
        using (var template = File.OpenRead(templatePath))
        {
            template.CopyTo(output);
        }

        output.Position = 0;
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            var documentEntry = archive.GetEntry("word/document.xml")
                ?? throw new InvalidOperationException("La plantilla del acuerdo no contiene word/document.xml.");

            XDocument document;
            using (var reader = documentEntry.Open())
            {
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            }

            FillDocument(document, detail);

            documentEntry.Delete();
            var updatedEntry = archive.CreateEntry("word/document.xml", CompressionLevel.Optimal);
            using var writer = updatedEntry.Open();
            document.Save(writer, SaveOptions.DisableFormatting);
        }

        return output.ToArray();
    }

    public static string FileName(LoanDto loan)
    {
        var rawName = $"{loan.ClientName}-{loan.ReferenceName ?? "prestamo"}";
        var normalized = RemoveDiacritics(rawName).ToLowerInvariant();
        var safe = new string(normalized.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        safe = string.Join("-", safe.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return $"acuerdo-prestamo-{(string.IsNullOrWhiteSpace(safe) ? "prestamo" : safe)}.docx";
    }

    private static void FillDocument(XDocument document, LoanDetailDto detail)
    {
        var loan = detail.Loan;
        var currency = CurrencySymbol(loan.Currency);
        var installmentCount = detail.Installments.Count > 0 ? detail.Installments.Count : loan.TermMonths;
        var installmentAmount = detail.Installments.FirstOrDefault()?.PaymentAmount
            ?? loan.TotalToPay / Math.Max(installmentCount, 1);
        var monthlyInterestAmount = loan.PrincipalAmount * (loan.MonthlyInterestRate / 100m);
        var frequencyText = FrequencyAgreementText(loan.PaymentFrequency, installmentCount);
        var debtorIdentification = BlankIfMissing(loan.ClientIdentificationNumber);
        var lenderName = BlankIfMissing(loan.LenderName);
        var lenderIdentification = BlankIfMissing(loan.LenderIdentificationNumber);
        var agreementCity = BlankIfMissing(loan.AgreementCity);
        var lateFee = FormatLateFee(loan.LateFeeDescription);
        var now = BusinessClock.Now;

        var principalWords = AmountInWords(loan.PrincipalAmount, loan.Currency);
        var principalMoney = FormatMoney(loan.PrincipalAmount, currency);
        var monthlyInterestWords = AmountInWords(monthlyInterestAmount, loan.Currency);
        var monthlyInterestMoney = FormatMoney(monthlyInterestAmount, currency);
        var installmentMoney = FormatMoney(installmentAmount, currency);
        var installmentWords = AmountInWords(installmentAmount, loan.Currency);
        var totalWords = AmountInWords(loan.TotalToPay, loan.Currency);
        var totalMoney = FormatMoney(loan.TotalToPay, currency);
        var dueDate = FormatDate(loan.EndDate);
        var signDay = now.Day.ToString("00", NicaraguaCulture);
        var signMonth = NicaraguaCulture.DateTimeFormat.GetMonthName(now.Month);
        var signYear = now.Year.ToString(NicaraguaCulture);

        var intro = new[]
        {
            new TextSegment("Yo "),
            new TextSegment(loan.ClientName, true),
            new TextSegment(", mayor de edad, con numero de cedula, "),
            new TextSegment(debtorIdentification, true),
            new TextSegment(", declaro que he recibido en calidad de préstamo la cantidad de "),
            new TextSegment(principalWords, true),
            new TextSegment(", ("),
            new TextSegment(principalMoney, true),
            new TextSegment(") de parte de "),
            new TextSegment(lenderName, true),
            new TextSegment(", mayor de edad, con numero de cedula, "),
            new TextSegment(lenderIdentification, true)
        };
        var interest = new List<TextSegment>
        {
            new TextSegment("El préstamo genera interés del "),
            new TextSegment(FormatPercent(loan.MonthlyInterestRate), true),
            new TextSegment(" total, equivalente a "),
            new TextSegment(monthlyInterestWords, true),
            new TextSegment(", ("),
            new TextSegment(monthlyInterestMoney, true),
            new TextSegment(") mensual, pero el deudor decidió dar cuotas "),
            new TextSegment(frequencyText.Plural, true),
            new TextSegment(" por "),
            new TextSegment(frequencyText.PeriodMonths.ToString(NicaraguaCulture), true),
            new TextSegment(" meses")
        };

        if (loan.PaymentFrequency != PaymentFrequency.Monthly)
        {
            interest.AddRange([
                new TextSegment(" que seria "),
                new TextSegment(installmentCount.ToString(NicaraguaCulture), true),
                new TextSegment($" {frequencyText.UnitPlural}")
            ]);
        }

        interest.AddRange([
            new TextSegment(" de ("),
            new TextSegment(installmentMoney, true),
            new TextSegment(") "),
            new TextSegment(installmentWords, true),
            new TextSegment(", un total a pagar será "),
            new TextSegment(totalWords, true),
            new TextSegment("("),
            new TextSegment(totalMoney, true),
            new TextSegment(") en el periodo acordado.")
        ]);
        var due = new[]
        {
            new TextSegment("El deudor se compromete a pagar la cantidad de "),
            new TextSegment(totalMoney, true),
            new TextSegment(" a más tardar el día, "),
            new TextSegment(dueDate, true),
            new TextSegment(".")
        };
        var late = new[]
        {
            new TextSegment("En caso de incumplimiento en la fecha establecida, se aplicará un recargo por mora de "),
            new TextSegment(lateFee, true),
            new TextSegment(" Por cada mes de atraso.")
        };
        var signatureDate = new[]
        {
            new TextSegment("Firmamos el presente documento en la ciudad de"),
            new TextSegment(agreementCity, true),
            new TextSegment(", a los "),
            new TextSegment(signDay, true),
            new TextSegment(" días del mes de "),
            new TextSegment(signMonth, true),
            new TextSegment(" del año "),
            new TextSegment(signYear, true),
            new TextSegment(".")
        };

        var nameFieldIndex = 0;
        var identificationFieldIndex = 0;
        foreach (var paragraph in document.Descendants(W + "p"))
        {
            var text = ParagraphText(paragraph);

            if (text.StartsWith("Yo", StringComparison.OrdinalIgnoreCase))
            {
                SetParagraphText(paragraph, intro);
                continue;
            }

            if (text.StartsWith("El préstamo genera", StringComparison.OrdinalIgnoreCase))
            {
                SetParagraphText(paragraph, interest);
                continue;
            }

            if (text.StartsWith("El deudor se compromete", StringComparison.OrdinalIgnoreCase))
            {
                SetParagraphText(paragraph, due);
                continue;
            }

            if (text.StartsWith("En caso de incumplimiento", StringComparison.OrdinalIgnoreCase))
            {
                SetParagraphText(paragraph, late);
                continue;
            }

            if (text.StartsWith("Firmamos el presente", StringComparison.OrdinalIgnoreCase))
            {
                SetParagraphText(paragraph, signatureDate);
                continue;
            }

            if (text.StartsWith("Nombre y apellidos:", StringComparison.OrdinalIgnoreCase))
            {
                nameFieldIndex += 1;
                SetParagraphText(paragraph, [
                    new TextSegment("Nombre y apellidos: "),
                    new TextSegment(nameFieldIndex == 1 ? loan.ClientName : lenderName, true)
                ]);
                continue;
            }

            if (text.StartsWith("Cedula:", StringComparison.OrdinalIgnoreCase))
            {
                identificationFieldIndex += 1;
                SetParagraphText(paragraph, [
                    new TextSegment("Cedula: "),
                    new TextSegment(identificationFieldIndex == 1 ? debtorIdentification : lenderIdentification, true)
                ]);
            }
        }
    }

    private static string ParagraphText(XElement paragraph)
        => string.Concat(paragraph.Descendants(W + "t").Select(text => text.Value));

    private static void SetParagraphText(XElement paragraph, IReadOnlyList<TextSegment> segments)
    {
        var firstRun = paragraph.Elements(W + "r").FirstOrDefault();
        var baseRunProperties = firstRun?.Element(W + "rPr") is { } properties
            ? new XElement(properties)
            : new XElement(W + "rPr");

        paragraph.Elements(W + "r").Remove();

        var paragraphProperties = paragraph.Element(W + "pPr");
        var runs = segments.Select(segment =>
        {
            var runProperties = new XElement(baseRunProperties);
            SetBlackText(runProperties);
            SetBold(runProperties, segment.IsBold);

            return new XElement(W + "r",
                runProperties,
                new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), segment.Text));
        }).ToList();

        if (paragraphProperties is null)
        {
            paragraph.AddFirst(runs);
        }
        else
        {
            paragraphProperties.AddAfterSelf(runs);
        }
    }

    private static void SetBlackText(XElement runProperties)
    {
        var color = runProperties.Element(W + "color");
        if (color is null)
        {
            color = new XElement(W + "color");
            runProperties.Add(color);
        }

        color.SetAttributeValue(W + "val", "000000");
        color.Attribute(W + "themeColor")?.Remove();
    }

    private static void SetBold(XElement runProperties, bool isBold)
    {
        runProperties.Elements(W + "b").Remove();

        if (isBold)
        {
            runProperties.Add(new XElement(W + "b"));
        }
    }

    private static string BlankIfMissing(string? value)
        => string.IsNullOrWhiteSpace(value) ? "________________________" : value.Trim();

    private static string FormatLateFee(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "________________________";
        }

        return TryReadAmount(value, out var amount)
            ? $"{amount:0.##}% de la tasa de interés corriente"
            : value.Trim();
    }

    private static bool TryReadAmount(string value, out decimal amount)
    {
        amount = 0;
        var match = Regex.Match(value, @"\d[\d.,]*");
        if (!match.Success)
        {
            return false;
        }

        var token = match.Value.Trim('.', ',');
        var normalized = NormalizeNumberToken(token);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static string NormalizeNumberToken(string value)
    {
        if (value.Contains('.') && value.Contains(','))
        {
            return value.Replace(",", "");
        }

        if (value.Contains(','))
        {
            var commaParts = value.Split(',');
            return commaParts.Last().Length == 3
                ? value.Replace(",", "")
                : value.Replace(",", ".");
        }

        if (value.Contains('.'))
        {
            var dotParts = value.Split('.');
            return dotParts.Last().Length == 3 ? value.Replace(".", "") : value;
        }

        return value;
    }

    private static string CurrencySymbol(CurrencyType currency)
        => currency == CurrencyType.Usd ? "USD " : "C$";

    private static string CurrencyName(CurrencyType currency, decimal amount)
        => currency == CurrencyType.Usd
            ? Math.Round(amount) == 1 ? "dólar" : "dólares"
            : Math.Round(amount) == 1 ? "córdoba" : "córdobas";

    private static string FormatMoney(decimal value, string currency)
    {
        var decimals = value == decimal.Truncate(value) ? 0 : 2;
        return $"{currency}{value.ToString($"N{decimals}", NicaraguaCulture)}";
    }

    private static string FormatPercent(decimal value)
        => $"{value.ToString(value == decimal.Truncate(value) ? "N0" : "N2", NicaraguaCulture)}%";

    private static string FormatDate(DateTime date)
        => $"{date.Day:00} de {NicaraguaCulture.DateTimeFormat.GetMonthName(date.Month)} {date.Year}";

    private static (string Plural, string UnitPlural, int PeriodMonths) FrequencyAgreementText(PaymentFrequency frequency, int installmentCount)
        => frequency switch
        {
            PaymentFrequency.Weekly => ("semanales", "semanas", Math.Max(1, (int)Math.Round(installmentCount / 4m, MidpointRounding.AwayFromZero))),
            PaymentFrequency.Biweekly => ("quincenales", "quincenas", Math.Max(1, (int)Math.Round(installmentCount / 2m, MidpointRounding.AwayFromZero))),
            _ => ("mensuales", "meses", installmentCount)
        };

    private static string AmountInWords(decimal value, CurrencyType currency)
    {
        var rounded = (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);
        return $"{NumberToSpanishWords(rounded)} {CurrencyName(currency, value)}";
    }

    private static string NumberToSpanishWords(long value)
    {
        if (value == 0)
        {
            return "cero";
        }

        if (value < 0)
        {
            return $"menos {NumberToSpanishWords(Math.Abs(value))}";
        }

        if (value < 1000)
        {
            return UnderThousand((int)value);
        }

        if (value < 1_000_000)
        {
            var thousands = value / 1000;
            var rest = value % 1000;
            var prefix = thousands == 1 ? "mil" : $"{UnderThousand((int)thousands)} mil";
            return rest == 0 ? prefix : $"{prefix} {UnderThousand((int)rest)}";
        }

        var millions = value / 1_000_000;
        var remainder = value % 1_000_000;
        var millionText = millions == 1 ? "un millón" : $"{NumberToSpanishWords(millions)} millones";
        return remainder == 0 ? millionText : $"{millionText} {NumberToSpanishWords(remainder)}";
    }

    private static string UnderThousand(int value)
    {
        string[] units = ["", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve"];
        string[] teens = ["diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve"];
        string[] tens = ["", "", "veinte", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"];
        string[] hundreds = ["", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos"];

        if (value == 100)
        {
            return "cien";
        }

        if (value < 10)
        {
            return units[value];
        }

        if (value < 20)
        {
            return teens[value - 10];
        }

        if (value < 30)
        {
            return value == 20 ? "veinte" : $"veinti{units[value - 20]}";
        }

        if (value < 100)
        {
            var unit = value % 10;
            return unit == 0 ? tens[value / 10] : $"{tens[value / 10]} y {units[unit]}";
        }

        var hundred = value / 100;
        var rest = value % 100;
        return rest == 0 ? hundreds[hundred] : $"{hundreds[hundred]} {UnderThousand(rest)}";
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(capacity: normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
