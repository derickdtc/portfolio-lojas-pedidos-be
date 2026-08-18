using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace backend.Services;

public sealed class ProductSpreadsheetImporter
{
    private const int MaxReturnedWarnings = 20;

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace RelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly XNamespace OfficeRelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly IReadOnlyDictionary<string, ProductColumn> HeaderAliases =
        new Dictionary<string, ProductColumn>(StringComparer.OrdinalIgnoreCase)
        {
            ["codigo"] = ProductColumn.ItemCode,
            ["codigodoitem"] = ProductColumn.ItemCode,
            ["coditem"] = ProductColumn.ItemCode,
            ["item"] = ProductColumn.ItemCode,

            ["descricao"] = ProductColumn.Description,
            ["descrio"] = ProductColumn.Description,
            ["descrica"] = ProductColumn.Description,
            ["desc"] = ProductColumn.Description,

            ["precodecompra"] = ProductColumn.PurchasePrice,
            ["precocompra"] = ProductColumn.PurchasePrice,
            ["pocompra"] = ProductColumn.PurchasePrice,
            ["pcompra"] = ProductColumn.PurchasePrice,

            ["precodevenda"] = ProductColumn.SalePrice,
            ["precovenda"] = ProductColumn.SalePrice,
            ["povenda"] = ProductColumn.SalePrice,
            ["pvenda"] = ProductColumn.SalePrice,

            ["saldodeitens"] = ProductColumn.StockBalance,
            ["saldo"] = ProductColumn.StockBalance,
            ["estoque"] = ProductColumn.StockBalance,
            ["quantidade"] = ProductColumn.StockBalance,

            ["cfop"] = ProductColumn.Cfop,
            ["csosn"] = ProductColumn.Csosn,
            ["ncm"] = ProductColumn.Ncm,
            ["cst"] = ProductColumn.Cst,

            ["referencia"] = ProductColumn.Reference,
            ["referncia"] = ProductColumn.Reference,
            ["refer"] = ProductColumn.Reference,
        };

    public async Task<ProductSpreadsheetImportResult> ReadAsync(
        Stream spreadsheet,
        CancellationToken cancellationToken)
    {
        // IFormFile streams are seekable. Reading them directly avoids keeping a
        // second copy of a multi-megabyte upload in RAM. Keep the fallback for other
        // callers that supply a forward-only stream.
        Stream archiveStream = spreadsheet;
        MemoryStream? bufferedCopy = null;

        if (!spreadsheet.CanSeek)
        {
            bufferedCopy = new MemoryStream();
            await spreadsheet.CopyToAsync(bufferedCopy, cancellationToken);
            bufferedCopy.Position = 0;
            archiveStream = bufferedCopy;
        }

        try
        {
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            var sharedStrings = ReadSharedStrings(archive);
            var sheetEntry = archive.GetEntry(GetFirstWorksheetPath(archive));

            if (sheetEntry is null)
            {
                return ProductSpreadsheetImportResult.Empty("A primeira planilha do arquivo nao foi encontrada.");
            }

            using var sheetStream = sheetEntry.Open();
            var sheetDocument = XDocument.Load(sheetStream);
            var header = FindHeader(ReadRows(sheetDocument, sharedStrings));

            if (header is null)
            {
                return ProductSpreadsheetImportResult.Empty(
                    "Nao encontrei uma linha de cabecalho com codigo, descricao, preco, saldo e impostos.");
            }

            var warnings = new List<string>(MaxReturnedWarnings);
            var productsByCode = new Dictionary<string, ProductSpreadsheetRow>(StringComparer.OrdinalIgnoreCase);
            var skipped = 0;

            // Enumerate the XML rows again instead of keeping a second in-memory
            // representation of every row and cell in the spreadsheet.
            foreach (var row in ReadRows(sheetDocument, sharedStrings).Where(row => row.RowIndex > header.RowIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (row.Cells.Values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var itemCode = NormalizeCode(GetCell(row, header.Columns, ProductColumn.ItemCode));
                var description = NormalizeDescription(GetCell(row, header.Columns, ProductColumn.Description));

                if (string.IsNullOrWhiteSpace(itemCode) && string.IsNullOrWhiteSpace(description))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(itemCode))
                {
                    skipped++;
                    AddWarning(warnings, $"Linha {row.RowIndex}: produto sem codigo foi ignorado.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(description))
                {
                    skipped++;
                    AddWarning(warnings, $"Linha {row.RowIndex}: produto {itemCode} sem descricao foi ignorado.");
                    continue;
                }

                var product = new ProductSpreadsheetRow(
                    itemCode,
                    description,
                    ParseDouble(GetCell(row, header.Columns, ProductColumn.PurchasePrice)),
                    ParseDouble(GetCell(row, header.Columns, ProductColumn.SalePrice)),
                    ParseInt(GetCell(row, header.Columns, ProductColumn.StockBalance)),
                    NormalizeCode(GetCell(row, header.Columns, ProductColumn.Cfop)),
                    NormalizeCode(GetCell(row, header.Columns, ProductColumn.Csosn)),
                    NormalizeCode(GetCell(row, header.Columns, ProductColumn.Ncm)),
                    NormalizeCode(GetCell(row, header.Columns, ProductColumn.Cst)),
                    NormalizeCode(GetCell(row, header.Columns, ProductColumn.Reference)));

                if (productsByCode.ContainsKey(itemCode))
                {
                    AddWarning(warnings, $"Linha {row.RowIndex}: codigo {itemCode} repetido; mantive a ultima ocorrencia.");
                }

                productsByCode[itemCode] = product;
            }

            return new ProductSpreadsheetImportResult(
                productsByCode.Values.OrderBy(product => product.Description).ToList(),
                skipped,
                warnings);
        }
        finally
        {
            if (bufferedCopy is not null)
            {
                await bufferedCopy.DisposeAsync();
            }
        }
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        return document
            .Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string GetFirstWorksheetPath(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");

        if (workbookEntry is null || relationshipsEntry is null)
        {
            return "xl/worksheets/sheet1.xml";
        }

        using var workbookStream = workbookEntry.Open();
        using var relationshipsStream = relationshipsEntry.Open();

        var workbook = XDocument.Load(workbookStream);
        var relationships = XDocument.Load(relationshipsStream);
        var firstSheet = workbook.Descendants(SpreadsheetNamespace + "sheet").FirstOrDefault();
        var relationshipId = firstSheet?.Attribute(OfficeRelationshipsNamespace + "id")?.Value;

        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return "xl/worksheets/sheet1.xml";
        }

        var target = relationships
            .Descendants(RelationshipsNamespace + "Relationship")
            .FirstOrDefault(relationship => relationship.Attribute("Id")?.Value == relationshipId)
            ?.Attribute("Target")
            ?.Value;

        if (string.IsNullOrWhiteSpace(target))
        {
            return "xl/worksheets/sheet1.xml";
        }

        return target.StartsWith('/')
            ? target.TrimStart('/')
            : $"xl/{target}";
    }

    private static IEnumerable<SpreadsheetRow> ReadRows(
        XDocument sheetDocument,
        IReadOnlyList<string> sharedStrings)
    {
        foreach (var row in sheetDocument.Descendants(SpreadsheetNamespace + "row"))
        {
            var rowIndex = int.TryParse(row.Attribute("r")?.Value, out var parsedRowIndex)
                ? parsedRowIndex
                : 0;
            var cells = new SortedDictionary<int, string>();
            var nextColumnIndex = 1;

            foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
            {
                var cellReference = cell.Attribute("r")?.Value;
                var columnIndex = string.IsNullOrWhiteSpace(cellReference)
                    ? nextColumnIndex
                    : GetColumnIndex(cellReference);

                cells[columnIndex] = ReadCellValue(cell, sharedStrings);
                nextColumnIndex = columnIndex + 1;
            }

            yield return new SpreadsheetRow(rowIndex, cells);
        }
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;

        if (type == "s")
        {
            var value = cell.Element(SpreadsheetNamespace + "v")?.Value;
            return int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Count
                ? sharedStrings[index]
                : string.Empty;
        }

        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value));
        }

        return cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;
    }

    private static SpreadsheetHeader? FindHeader(IEnumerable<SpreadsheetRow> rows)
    {
        foreach (var row in rows)
        {
            var columns = new Dictionary<ProductColumn, int>();

            foreach (var (columnIndex, value) in row.Cells)
            {
                var normalized = NormalizeHeader(value);
                if (HeaderAliases.TryGetValue(normalized, out var column) && !columns.ContainsKey(column))
                {
                    columns[column] = columnIndex;
                }
            }

            if (HasRequiredColumns(columns))
            {
                return new SpreadsheetHeader(row.RowIndex, columns);
            }
        }

        return null;
    }

    private static bool HasRequiredColumns(IReadOnlyDictionary<ProductColumn, int> columns)
    {
        return columns.ContainsKey(ProductColumn.ItemCode)
            && columns.ContainsKey(ProductColumn.Description)
            && columns.ContainsKey(ProductColumn.PurchasePrice)
            && columns.ContainsKey(ProductColumn.SalePrice)
            && columns.ContainsKey(ProductColumn.StockBalance)
            && columns.ContainsKey(ProductColumn.Cfop)
            && columns.ContainsKey(ProductColumn.Csosn)
            && columns.ContainsKey(ProductColumn.Ncm)
            && columns.ContainsKey(ProductColumn.Cst)
            && columns.ContainsKey(ProductColumn.Reference);
    }

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (warnings.Count < MaxReturnedWarnings)
        {
            warnings.Add(warning);
        }
    }

    private static string GetCell(
        SpreadsheetRow row,
        IReadOnlyDictionary<ProductColumn, int> columns,
        ProductColumn column)
    {
        return columns.TryGetValue(column, out var columnIndex)
            && row.Cells.TryGetValue(columnIndex, out var value)
                ? value
                : string.Empty;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var column = 0;

        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            column = (column * 26) + char.ToUpperInvariant(character) - 'A' + 1;
        }

        return column;
    }

    private static string NormalizeHeader(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string NormalizeDescription(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private static string NormalizeCode(string value)
    {
        var trimmed = value.Trim();

        if (Regex.IsMatch(trimmed, @"^-?\d+\.0+$"))
        {
            return trimmed[..trimmed.IndexOf('.')];
        }

        if (trimmed.Contains('E', StringComparison.OrdinalIgnoreCase)
            && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var scientificValue))
        {
            return scientificValue.ToString("0", CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    private static double ParseDouble(string value)
    {
        var cleaned = CleanNumber(value);

        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;
    }

    private static int ParseInt(string value)
    {
        var cleaned = CleanNumber(value);

        if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalNumber)
            ? (int)Math.Truncate(decimalNumber)
            : 0;
    }

    private static string CleanNumber(string value)
    {
        var cleaned = value
            .Trim()
            .Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty);

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            return lastComma > lastDot
                ? cleaned.Replace(".", string.Empty).Replace(',', '.')
                : cleaned.Replace(",", string.Empty);
        }

        return lastComma >= 0 ? cleaned.Replace(',', '.') : cleaned;
    }

    private sealed record SpreadsheetHeader(
        int RowIndex,
        IReadOnlyDictionary<ProductColumn, int> Columns);

    private sealed record SpreadsheetRow(
        int RowIndex,
        IReadOnlyDictionary<int, string> Cells);

    private enum ProductColumn
    {
        ItemCode,
        Description,
        PurchasePrice,
        SalePrice,
        StockBalance,
        Cfop,
        Csosn,
        Ncm,
        Cst,
        Reference,
    }
}

public sealed record ProductSpreadsheetImportResult(
    IReadOnlyList<ProductSpreadsheetRow> Products,
    int Skipped,
    IReadOnlyList<string> Warnings)
{
    public static ProductSpreadsheetImportResult Empty(string warning)
    {
        return new ProductSpreadsheetImportResult([], 0, [warning]);
    }
}

public sealed record ProductSpreadsheetRow(
    string ItemCode,
    string Description,
    double PurchasePrice,
    double SalePrice,
    int StockBalance,
    string Cfop,
    string Csosn,
    string Ncm,
    string Cst,
    string Reference);
