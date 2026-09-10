using System.Globalization;
using ClosedXML.Excel;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Services;

public class MaterialImportResult
{
    public string Message { get; set; } = "";
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int CategoriesCreated { get; set; }
    public int DocumentsLinked { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Bulk material upload from the legacy Excel template (<c>testblikupld2.xlsx</c>): one sheet per material type,
/// row 1 = headers, one material per row. Sheet names, headers and values are trimmed; "$", tabs and thousands
/// separators are stripped from numbers. Categories are matched by name + type (created when missing) and the
/// "Document File" column is matched to the uploaded documents by file name.
/// Not registered in DI (Program.cs is foundation) — construct it with its dependencies.
/// </summary>
public class MaterialImportService(AppDbContext db, FileStorage files, AuditService audit, CurrentUser me)
{
    public const string ColCategory = "Material Category";
    public const string ColName = "Product Name";
    public const string ColCode = "Product Rex #";
    public const string ColDensity = "Lb/Gal";
    public const string ColPrice = "Price Per Gallon/Pc.";
    public const string ColVoc = "VOC's";
    public const string ColHap = "Hap's";
    public const string ColTap = "TAP's";
    public const string ColDocument = "Document File";

    public static readonly string[] Headers = [ColCategory, ColName, ColCode, ColDensity, ColPrice, ColVoc, ColHap, ColTap, ColDocument];

    public static readonly (string sheet, MaterialType type)[] Sheets =
    [
        ("Base Materials", MaterialType.Base), ("Pigment Materials", MaterialType.Pigment), ("Dye Materials", MaterialType.Dye),
        ("Sundry Materials", MaterialType.Sundry), ("Equipment", MaterialType.Equipment),
    ];

    /// <summary>Header aliases (normalised: lower-case, no spaces/punctuation) → canonical header.</summary>
    private static readonly Dictionary<string, string> HeaderAliases = new()
    {
        ["materialcategory"] = ColCategory, ["category"] = ColCategory,
        ["productname"] = ColName, ["name"] = ColName,
        ["productrex"] = ColCode, ["productrexno"] = ColCode, ["product"] = ColCode, ["productno"] = ColCode,
        ["productnumber"] = ColCode, ["productcode"] = ColCode,
        ["lbgal"] = ColDensity, ["density"] = ColDensity, ["densitylbgal"] = ColDensity,
        ["pricepergallonpc"] = ColPrice, ["pricepergallon"] = ColPrice, ["price"] = ColPrice, ["unit"] = ColPrice, ["pricegal"] = ColPrice,
        ["vocs"] = ColVoc, ["voc"] = ColVoc,
        ["haps"] = ColHap, ["hap"] = ColHap,
        ["taps"] = ColTap, ["tap"] = ColTap,
        ["documentfile"] = ColDocument, ["document"] = ColDocument, ["documents"] = ColDocument, ["file"] = ColDocument,
    };

    public static readonly string[] DocumentExtensions =
        [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

    private static string Normalize(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static MaterialType? SheetType(string sheetName)
    {
        var n = Normalize(sheetName);
        if (n.StartsWith("base")) return MaterialType.Base;
        if (n.StartsWith("pigment")) return MaterialType.Pigment;
        if (n.StartsWith("dye")) return MaterialType.Dye;
        if (n.StartsWith("sundr")) return MaterialType.Sundry;
        if (n.StartsWith("equipment")) return MaterialType.Equipment;
        if (n.StartsWith("product")) return MaterialType.Product;
        return null;
    }

    // ------------------------------------------------------------------ template

    public static byte[] BuildTemplate()
    {
        var samples = new Dictionary<MaterialType, object[]>
        {
            [MaterialType.Base] = ["Reducer", "Thinner Blend - TZ35", "6660074", 7.09, 18.00, 7.0939, 0, 0, ""],
            [MaterialType.Pigment] = ["OptiColor XP Solvent", "Solvent RO Red Oxide", "4144", 16.4, 0, 0, 0, 0, ""],
            [MaterialType.Dye] = ["Ilva PF5", "Ilva Cherry - PF 5T02", "6630234", 7.76, 103.23, 0.8286, 0, 0, ""],
            [MaterialType.Sundry] = ["Sheet Abrasives", "Mirka Abranet 180 Grit 3x4\" Abrasive", "9A-129-180", 0, 44.45, 0, 0, 0, ""],
            [MaterialType.Equipment] = ["Hand Orbital Sander", "Klingspor 5\" 3/16 Non-Vacuum Orbital Sander", "4144", 0, 289.00, 0, 0, 0, "sander-manual.pdf"],
        };
        using var wb = new XLWorkbook();
        foreach (var (sheet, type) in Sheets)
        {
            var ws = wb.Worksheets.Add(sheet);
            for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
            var header = ws.Range(1, 1, 1, Headers.Length);
            header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#FDE7D6"));
            var sample = samples[type];
            for (var i = 0; i < sample.Length; i++)
            {
                var cell = ws.Cell(2, i + 1);
                switch (sample[i])
                {
                    case string s: cell.Value = s; break;
                    case int n: cell.Value = n; break;
                    case double d: cell.Value = d; break;
                }
            }
            ws.Cell(2, 5).Style.NumberFormat.Format = "$#,##0.00";
            ws.SheetView.FreezeRows(1);
            ws.Columns(1, Headers.Length).AdjustToContents();
            foreach (var col in ws.Columns(1, Headers.Length)) col.Width = Math.Max(col.Width, 14);
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ import

    private sealed record ParsedRow(string Sheet, int Row, MaterialType Type, string? Category, string Name, string? Code,
        decimal Density, decimal Price, decimal Voc, decimal Hap, decimal Tap, string? Document);

    public async Task<MaterialImportResult> ImportAsync(int groupId, IFormFile? file, List<IFormFile> documents)
    {
        if (file == null || file.Length == 0) throw ApiException.Bad("Please choose an Excel (.xlsx) file to upload.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx")
            throw ApiException.Bad($"Invalid file \"{file.FileName}\". Only Excel .xlsx files are supported — download the sample template and save your sheet as .xlsx.");
        foreach (var d in documents)
        {
            var dext = Path.GetExtension(d.FileName).ToLowerInvariant();
            if (!DocumentExtensions.Contains(dext))
                throw ApiException.Bad($"Invalid extension for file \"{d.FileName}\". Only \"{string.Join(", ", DocumentExtensions.Where(e => e != ".jpeg").Select(e => e.TrimStart('.')))}\" files are supported.");
        }

        var result = new MaterialImportResult();
        var parsed = new List<ParsedRow>();

        XLWorkbook wb;
        try
        {
            await using var stream = file.OpenReadStream();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            wb = new XLWorkbook(ms);
        }
        catch (Exception)
        {
            throw ApiException.Bad($"\"{file.FileName}\" could not be read as an Excel workbook. Make sure it is a valid .xlsx file (not password protected).");
        }

        using (wb)
        {
            var recognised = 0;
            foreach (var ws in wb.Worksheets)
            {
                var sheet = ws.Name.Trim();
                var type = SheetType(sheet);
                if (type == null)
                {
                    result.Warnings.Add($"Sheet '{sheet}' was ignored: the sheet name must be one of {string.Join(", ", Sheets.Select(s => $"'{s.sheet}'"))}.");
                    continue;
                }
                recognised++;
                ParseSheet(ws, sheet, type.Value, parsed, result);
            }
            if (recognised == 0)
                throw ApiException.Bad($"No material sheets were found. The workbook must contain sheets named {string.Join(", ", Sheets.Select(s => $"'{s.sheet}'"))}.");
        }

        if (parsed.Count == 0 && result.Errors.Count == 0)
        {
            result.Message = "The file contains no materials to import.";
            return result;
        }

        var docsByName = new Dictionary<string, IFormFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in documents)
        {
            var name = Path.GetFileName(d.FileName);
            docsByName.TryAdd(name, d);
            docsByName.TryAdd(Path.GetFileNameWithoutExtension(name), d);
        }

        var savedFiles = new List<string>();
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                // Reset counters in case the strategy retries the whole block.
                result.Created = 0; result.Skipped = 0; result.CategoriesCreated = 0; result.DocumentsLinked = 0;
                var rowWarnings = new List<string>();
                db.ChangeTracker.Clear();
                foreach (var f in savedFiles) files.Delete(f);
                savedFiles.Clear();

                await using var tx = await db.Database.BeginTransactionAsync();

                var categories = await db.MaterialCategories.Where(c => c.GroupId == groupId).ToListAsync();
                var existing = await db.Materials.AsNoTracking().Where(m => m.GroupId == groupId && !m.IsDeleted)
                    .Select(m => new { m.MaterialType, m.ProductName, m.ProductCode }).ToListAsync();
                var existingKeys = existing.Select(m => Key(m.MaterialType, m.ProductName, m.ProductCode)).ToHashSet();
                var documentCache = new Dictionary<IFormFile, Document>();
                var created = new List<(Material m, ParsedRow row, Document? doc)>();

                foreach (var row in parsed)
                {
                    var key = Key(row.Type, row.Name, row.Code);
                    if (!existingKeys.Add(key))
                    {
                        result.Skipped++;
                        rowWarnings.Add($"Sheet '{row.Sheet}' row {row.Row}: \"{row.Name}\"{(row.Code == null ? "" : $" ({row.Code})")} already exists as a {MaterialTypes.Label(row.Type)} material — skipped.");
                        continue;
                    }

                    MaterialCategory? category = null;
                    if (row.Category != null)
                    {
                        category = categories.FirstOrDefault(c => c.MaterialType == row.Type && string.Equals(c.Name.Trim(), row.Category, StringComparison.OrdinalIgnoreCase));
                        if (category == null)
                        {
                            category = new MaterialCategory { GroupId = groupId, Name = row.Category, MaterialType = row.Type };
                            db.MaterialCategories.Add(category);
                            categories.Add(category);
                            result.CategoriesCreated++;
                        }
                    }

                    var m = new Material
                    {
                        GroupId = groupId, MaterialType = row.Type, Category = category, ProductName = row.Name, ProductCode = row.Code,
                        Density = row.Density, Price = row.Price,
                        Voc = row.Type == MaterialType.Equipment ? 0 : row.Voc,
                        Hap = row.Type == MaterialType.Equipment ? 0 : row.Hap,
                        Tap = row.Type == MaterialType.Equipment ? 0 : row.Tap,
                    };
                    db.Materials.Add(m);

                    Document? doc = null;
                    if (row.Document != null)
                    {
                        if (docsByName.TryGetValue(Path.GetFileName(row.Document), out var upload) ||
                            docsByName.TryGetValue(Path.GetFileNameWithoutExtension(row.Document), out upload))
                        {
                            if (!documentCache.TryGetValue(upload, out doc))
                            {
                                var stored = await files.SaveAsync(upload, $"documents/{groupId}", DocumentExtensions);
                                savedFiles.Add(stored);
                                var fileName = Path.GetFileName(upload.FileName);
                                doc = new Document
                                {
                                    GroupId = groupId, Name = Path.GetFileNameWithoutExtension(fileName), FileName = fileName, StoredFile = stored,
                                    ContentType = string.IsNullOrEmpty(upload.ContentType) ? FileStorage.ContentTypeFor(fileName) : upload.ContentType,
                                    FileSize = upload.Length, CreatedBy = me.Id == 0 ? null : me.Id,
                                };
                                db.Documents.Add(doc);
                                documentCache[upload] = doc;
                            }
                        }
                        else
                        {
                            rowWarnings.Add($"Sheet '{row.Sheet}' row {row.Row}: document \"{row.Document}\" was not among the uploaded documents — material imported without it.");
                        }
                    }
                    created.Add((m, row, doc));
                }

                await db.SaveChangesAsync();

                foreach (var (m, row, doc) in created)
                {
                    if (doc != null)
                    {
                        db.DocumentLinks.Add(new DocumentLink { DocumentId = doc.Id, EntityType = LinkEntityTypes.Material, EntityId = m.Id });
                        result.DocumentsLinked++;
                    }
                    audit.Log(LinkEntityTypes.Material, m.Id, "Created", $"Imported from \"{file.FileName}\" (sheet '{row.Sheet}', row {row.Row})", groupId);
                }
                foreach (var d in documentCache.Values)
                    audit.Log("Document", d.Id, "Created", $"Uploaded with material import \"{file.FileName}\"", groupId);
                await db.SaveChangesAsync();
                await tx.CommitAsync();

                result.Created = created.Count;
                result.Warnings.AddRange(rowWarnings);
            });
        }
        catch
        {
            foreach (var f in savedFiles) files.Delete(f);
            throw;
        }

        var parts = new List<string> { result.Created == 1 ? "Imported 1 material" : $"Imported {result.Created} materials" };
        if (result.CategoriesCreated > 0) parts.Add($"{result.CategoriesCreated} new categor{(result.CategoriesCreated == 1 ? "y" : "ies")}");
        if (result.DocumentsLinked > 0) parts.Add($"{result.DocumentsLinked} document link{(result.DocumentsLinked == 1 ? "" : "s")}");
        if (result.Skipped > 0) parts.Add($"{result.Skipped} skipped (already exist)");
        if (result.Errors.Count > 0) parts.Add($"{result.Errors.Count} row{(result.Errors.Count == 1 ? "" : "s")} with errors");
        result.Message = string.Join(", ", parts) + ".";
        return result;
    }

    private static string Key(MaterialType t, string name, string? code) =>
        $"{(int)t}|{name.Trim().ToLowerInvariant()}|{(code ?? "").Trim().ToLowerInvariant()}";

    private static void ParseSheet(IXLWorksheet ws, string sheet, MaterialType type, List<ParsedRow> parsed, MaterialImportResult result)
    {
        var used = ws.RangeUsed();
        if (used == null) return;
        var lastCol = used.LastColumn().ColumnNumber();
        var lastRow = used.LastRow().RowNumber();

        var cols = new Dictionary<string, int>();
        for (var c = 1; c <= lastCol; c++)
        {
            var h = Clean(ws.Cell(1, c).GetFormattedString());
            if (h == null) continue;
            if (HeaderAliases.TryGetValue(Normalize(h), out var canonical)) cols.TryAdd(canonical, c);
        }
        if (!cols.ContainsKey(ColName))
        {
            result.Errors.Add($"Sheet '{sheet}': header \"{ColName}\" was not found in row 1. Expected headers: {string.Join(", ", Headers)}.");
            return;
        }
        var missing = Headers.Where(h => !cols.ContainsKey(h)).ToList();
        if (missing.Count > 0)
            result.Warnings.Add($"Sheet '{sheet}': column{(missing.Count == 1 ? "" : "s")} {string.Join(", ", missing.Select(m => $"\"{m}\""))} not found — treated as blank.");

        string? Str(int row, string col) => cols.TryGetValue(col, out var c) ? Clean(CellText(ws.Cell(row, c))) : null;

        for (var r = 2; r <= lastRow; r++)
        {
            var values = cols.Values.Select(c => Clean(CellText(ws.Cell(r, c)))).ToList();
            if (values.All(v => v == null)) continue; // blank row

            var rowErrors = new List<string>();
            var name = Str(r, ColName);
            if (name == null) rowErrors.Add($"{ColName} is required.");
            else if (name.Length > 400) rowErrors.Add($"{ColName} is too long (400 characters max).");

            decimal Num(string col, string label)
            {
                if (!cols.TryGetValue(col, out var c)) return 0;
                var cell = ws.Cell(r, c);
                if (cell.DataType == XLDataType.Number) return ToDecimal(cell.GetDouble(), label, rowErrors);
                var s = Clean(CellText(cell));
                if (s == null) return 0;
                var stripped = s.Replace("$", "").Replace(",", "").Replace("\t", "").Replace(" ", "").Trim();
                if (stripped.Length == 0 || stripped == "-") return 0;
                if (decimal.TryParse(stripped, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                {
                    if (d < 0) rowErrors.Add($"{label} cannot be negative.");
                    return d;
                }
                rowErrors.Add($"{label} \"{s}\" is not a valid number.");
                return 0;
            }

            var density = Num(ColDensity, ColDensity);
            var price = Num(ColPrice, ColPrice);
            var voc = Num(ColVoc, ColVoc);
            var hap = Num(ColHap, ColHap);
            var tap = Num(ColTap, ColTap);

            if (rowErrors.Count > 0)
            {
                result.Errors.AddRange(rowErrors.Select(e => $"Sheet '{sheet}' row {r}: {e}"));
                continue;
            }
            var category = Str(r, ColCategory);
            parsed.Add(new ParsedRow(sheet, r, type, category is { Length: > 400 } ? category[..400] : category, name!,
                Str(r, ColCode), density, price, voc, hap, tap, Str(r, ColDocument)));
        }
    }

    private static decimal ToDecimal(double d, string label, List<string> errors)
    {
        if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e12) { errors.Add($"{label} is not a valid number."); return 0; }
        if (d < 0) errors.Add($"{label} cannot be negative.");
        return Math.Round((decimal)d, 4);
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number)
            return cell.GetDouble().ToString(CultureInfo.InvariantCulture); // "4144" rather than "4144.0" / formatted currency
        try { return cell.GetFormattedString(); }
        catch { return cell.Value.ToString(); }
    }

    private static string? Clean(string? s)
    {
        if (s == null) return null;
        var t = s.Replace('\u00A0', ' ').Trim().Trim('\t').Trim();
        return t.Length == 0 ? null : t;
    }
}
