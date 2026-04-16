using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class LeaveTakenService : ILeaveTakenService
    {
        private readonly AppDbContext _context;
        public LeaveTakenService(AppDbContext context) => _context = context;

        public async Task<string> UploadAsync(IFormFile file)
        {
            var list = await ParseLeaveTakenAsync(file);

            // Replace existing data completely
            _context.LeaveTakens.RemoveRange(_context.LeaveTakens);
            await _context.SaveChangesAsync();

            await _context.LeaveTakens.AddRangeAsync(list);
            await _context.SaveChangesAsync();

            return $"{list.Count} LeaveTaken records uploaded (old data replaced).";
        }

        private async Task<List<LeaveTaken>> ParseLeaveTakenAsync(IFormFile file)
        {
            var list = new List<LeaveTaken>();
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            if (IsCsv(file))
            {
                using var sr = new StreamReader(stream);
                string? headerLine = await sr.ReadLineAsync();
                var headers = SplitCsvLine(headerLine ?? string.Empty);

                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var cols = SplitCsvLine(line);

                    var lt = new LeaveTaken
                    {
                        PersonnelNumber = SafeInt(cols, 0),
                        PersonnelName = SafeString(cols, 1),
                        Position = SafeString(cols, 2),
                        PersonnelSubarea = SafeString(cols, 3),
                        PersonnelArea = SafeString(cols, 4),
                        Function = SafeString(cols, 5),
                        Manager = SafeString(cols, 6),
                        CostCentreNumber = SafeString(cols, 7),
                        CostCentreDescription = SafeString(cols, 8),
                        OrganizationalUnit = SafeString(cols, 9),
                        EmploymentPercentage = SafeDecimal(cols, 10),
                        WeeklyHours = SafeDecimal(cols, 11),
                        AttendanceAbsenceType = SafeString(cols, 13),
                        Hours = SafeDecimal(cols, 14),
                        Days = SafeDecimal(cols, 15),
                        StartDate = SafeParseDate(cols, 16),
                        EndDate = SafeParseDate(cols, 17),
                        ChangedOn = SafeParseDate(cols, 18),
                        Location = MapLocation(SafeString(cols, 19)),
                        Month = SafeString(cols, 20),
                        EmploymentType = SafeString(cols, 21),
                        BusinessUnit = SafeString(cols, 22)
                    };

                    list.Add(lt);
                }
            }
            else
            {
                using var package = new ExcelPackage(stream);
                var ws = package.Workbook.Worksheets[0];
                int rowCount = ws.Dimension?.Rows ?? 0;

                for (int row = 2; row <= rowCount; row++)
                {
                    var lt = new LeaveTaken
                    {
                        PersonnelNumber = GetIntValue(ws.Cells[row, 1]),
                        PersonnelName = GetStringValue(ws.Cells[row, 2]),
                        Position = GetStringValue(ws.Cells[row, 3]),
                        PersonnelSubarea = GetStringValue(ws.Cells[row, 4]),
                        PersonnelArea = GetStringValue(ws.Cells[row, 5]),
                        Function = GetStringValue(ws.Cells[row, 6]),
                        Manager = GetStringValue(ws.Cells[row, 7]),
                        CostCentreNumber = GetStringValue(ws.Cells[row, 8]),
                        CostCentreDescription = GetStringValue(ws.Cells[row, 9]),
                        OrganizationalUnit = GetStringValue(ws.Cells[row, 10]),
                        EmploymentPercentage = GetDecimalFromString(GetStringValue(ws.Cells[row, 11])),
                        WeeklyHours = GetDecimalFromString(GetStringValue(ws.Cells[row, 12])),
                        AttendanceAbsenceType = GetStringValue(ws.Cells[row, 14]),
                        Hours = GetDecimalFromString(GetStringValue(ws.Cells[row, 15])),
                        Days = GetDecimalFromString(GetStringValue(ws.Cells[row, 16])),
                        StartDate = GetDateTimeValue(ws.Cells[row, 17]),
                        EndDate = GetDateTimeValue(ws.Cells[row, 18]),
                        ChangedOn = GetDateTimeValue(ws.Cells[row, 19]),
                        Location = MapLocation(GetStringValue(ws.Cells[row, 20])),
                        Month = GetStringValue(ws.Cells[row, 21]),
                        EmploymentType = GetStringValue(ws.Cells[row, 22]),
                        BusinessUnit = GetStringValue(ws.Cells[row, 23])
                    };

                    list.Add(lt);
                }
            }

            return list;
        }

        private bool IsCsv(IFormFile file) =>
            file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
            (file.ContentType?.Contains("csv", StringComparison.OrdinalIgnoreCase) ?? false);

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var cur = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
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

        private static string SafeString(List<string> cols, int idx) =>
            (idx >= 0 && idx < cols.Count) ? cols[idx].Trim() : string.Empty;

        private static int SafeInt(List<string> cols, int idx) =>
            int.TryParse(SafeString(cols, idx), out var v) ? v : 0;

        private static decimal SafeDecimal(List<string> cols, int idx) =>
            decimal.TryParse(SafeString(cols, idx), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                ? d
                : decimal.TryParse(SafeString(cols, idx), out d)
                    ? d
                    : 0m;

        private static DateTime SafeParseDate(List<string> cols, int idx)
        {
            var s = SafeString(cols, idx);
            if (DateTime.TryParse(s, out var d)) return d;
            if (double.TryParse(s, out var oaDate)) return DateTime.FromOADate(oaDate);
            return DateTime.MinValue;
        }

        private string? GetStringValue(ExcelRange cell) => cell.Value?.ToString()?.Trim();

        private int GetIntValue(ExcelRange cell) =>
            int.TryParse(cell.Value?.ToString(), out int r) ? r : 0;

        private DateTime GetDateTimeValue(ExcelRange cell) =>
            cell.Value is DateTime dt
                ? dt
                : DateTime.TryParse(cell.Value?.ToString(), out var parsed)
                    ? parsed
                    : DateTime.MinValue;

        private decimal GetDecimalFromString(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
            if (decimal.TryParse(s, out d)) return d;
            return 0m;
        }

        private string MapLocation(string? personnelSubareaOrLocation)
        {
            var s = (personnelSubareaOrLocation ?? "").ToLowerInvariant();
            if (s.Contains("sydney")) return "Sydney";
            if (s.Contains("melbourne")) return "Melbourne";
            if (s.Contains("brisbane")) return "Brisbane";
            if (s.Contains("adelaide")) return "Adelaide";
            if (s.Contains("hawthorn")) return "Hawthorn";
            return "Auckland";
        }
    }
}