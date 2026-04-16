using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System.Globalization;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class BaseRatesService : IBaseRatesService
    {
        private readonly AppDbContext _context;
        public BaseRatesService(AppDbContext context) => _context = context;

        /// <summary>
        /// Ensure all employees have a BaseRate row. If a file is provided, parse and upsert it; otherwise create defaults.
        /// defaultUnit: used for parsed rows if no unit is supplied and for created defaults.
        /// </summary>
        public async Task<string> EnsureBaseRatesAsync(IFormFile? file = null, RateUnit defaultUnit = RateUnit.Hourly)
        {
            if (file == null)
            {
                return "No file provided.";
            }

            var parsed = await ParseBaseRatesAsync(file, defaultUnit);

            _context.BaseRates.RemoveRange(_context.BaseRates);
            await _context.SaveChangesAsync();

            await _context.BaseRates.AddRangeAsync(parsed);
            await _context.SaveChangesAsync();

            return $"{parsed.Count} BaseRate records uploaded (old data replaced).";
        }



        private async Task<List<BaseRate>> ParseBaseRatesAsync(IFormFile file, RateUnit defaultUnit)
        {
            var list = new List<BaseRate>();
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            if (file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                using var sr = new StreamReader(stream);
                string? headerLine = await sr.ReadLineAsync();
                var headerCols = headerLine != null ? SplitCsvLine(headerLine).Select(h => h.Trim().ToLowerInvariant()).ToList() : new List<string>();

                // Try to find column indexes by common header names
                int idxEmp = IndexOfAny(headerCols, new[] { "pers.no.", "pers.no", "employeeid", "employee id", "persno", "employee", "emp id" });
                int idxRate = IndexOfAny(headerCols, new[] { "rate", "hourly rate", "base rate" });

                // fallback to positions if header mapping failed
                if (idxEmp == -1) idxEmp = 0;
                if (idxRate == -1) idxRate = headerCols.Count > 2 ? 2 : 1;

                while (!sr.EndOfStream)
                {
                    var line = await sr.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cols = SplitCsvLine(line);

                    int empId = SafeInt(cols, idxEmp);
                    decimal rate = SafeDecimal(cols, idxRate);

                    if (empId == 0) continue; // skip invalid rows

                    var br = new BaseRate
                    {
                        EmployeeId = empId,
                        Rate = rate,
                        RateUnit = defaultUnit,
                        IsDefault = false,
                        CreatedAt = DateTime.UtcNow
                    };
                    list.Add(br);
                }
            }
            else // assume Excel
            {
                using var package = new ExcelPackage(stream);
                var ws = package.Workbook.Worksheets.FirstOrDefault();
                if (ws == null) return list;

                int rc = ws.Dimension?.Rows ?? 0;
                // Read header row to determine columns
                var header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 1; c <= (ws.Dimension?.Columns ?? 3); c++)
                {
                    var h = ws.Cells[1, c].Value?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(h) && !header.ContainsKey(h)) header[h] = c;
                }

                int empCol = header.FirstOrDefault(kv => new[] { "Pers.No.", "Pers.No", "EmployeeId", "Employee Id", "EmpId" }.Any(n => string.Equals(n, kv.Key, StringComparison.OrdinalIgnoreCase))).Value;
                int rateCol = header.FirstOrDefault(kv => new[] { "Rate", "HourlyRate", "BaseRate" }.Any(n => string.Equals(n, kv.Key, StringComparison.OrdinalIgnoreCase))).Value;

                // fallback to positional indices if headers not found
                if (empCol == 0) empCol = 1;
                if (rateCol == 0) rateCol = 3;

                for (int r = 2; r <= rc; r++)
                {
                    int empId = GetIntValue(ws.Cells[r, empCol]);
                    var rawRate = GetStringValue(ws.Cells[r, rateCol]);
                    decimal rate = GetDecimalFromString(rawRate);

                    if (empId == 0) continue;

                    var br = new BaseRate
                    {
                        EmployeeId = empId,
                        Rate = rate,
                        RateUnit = defaultUnit,
                        IsDefault = false,
                        CreatedAt = DateTime.UtcNow
                    };
                    list.Add(br);
                }
            }

            return list;
        }

        #region CSV / Excel helpers (adapted from LeaveBalanceService)
        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            if (line == null) return result;
            bool inQuotes = false;
            var cur = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    // lookahead: handle double quotes within quotes as escaped quotes
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cur.Append('"');
                        i++; // skip next quote
                        continue;
                    }
                    inQuotes = !inQuotes;
                    continue;
                }
                if (c == ',' && !inQuotes)
                {
                    result.Add(cur.ToString());
                    cur.Clear();
                    continue;
                }
                cur.Append(c);
            }
            result.Add(cur.ToString());
            return result;
        }

        private static int IndexOfAny(List<string> cols, string[] candidates)
        {
            for (int i = 0; i < cols.Count; i++)
            {
                if (candidates.Any(c => string.Equals(c, cols[i], StringComparison.OrdinalIgnoreCase))) return i;
            }
            return -1;
        }

        private static string SafeString(List<string> cols, int idx) => (idx >= 0 && idx < cols.Count) ? cols[idx].Trim() : string.Empty;
        private static int SafeInt(List<string> cols, int idx) => int.TryParse(SafeString(cols, idx), out var v) ? v : 0;
        private static decimal SafeDecimal(List<string> cols, int idx) => decimal.TryParse(SafeString(cols, idx), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d :
                                                                     decimal.TryParse(SafeString(cols, idx), out d) ? d : 0m;

        private string? GetStringValue(ExcelRange cell) => cell.Value?.ToString()?.Trim();
        private int GetIntValue(ExcelRange cell) => int.TryParse(cell.Value?.ToString(), out int r) ? r : 0;
        private decimal GetDecimalFromString(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
            if (decimal.TryParse(s, out d)) return d;
            // Excel may give floating OA numbers; attempt parsing
            if (double.TryParse(s, out var dd)) return (decimal)dd;
            return 0m;
        }
        #endregion
    }
}
