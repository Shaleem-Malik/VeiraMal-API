using OfficeOpenXml;
using System.Globalization;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class LeaveBalanceService : ILeaveBalanceService
    {
        private readonly AppDbContext _context;
        public LeaveBalanceService(AppDbContext context) => _context = context;

        public async Task<string> UploadAsync(IFormFile file)
        {
            var batch = new UploadBatch { CreatedAt = DateTime.UtcNow, FilesMeta = file.FileName };
            _context.UploadBatches.Add(batch);
            await _context.SaveChangesAsync();

            var list = await ParseLeaveBalanceAsync(file, batch.Id);

            // Option: remove previous leave balances or keep historic. Recommendation: remove old then add.
            _context.LeaveBalances.RemoveRange(_context.LeaveBalances);
            await _context.SaveChangesAsync();

            await _context.LeaveBalances.AddRangeAsync(list);
            await _context.SaveChangesAsync();

            return $"{list.Count} LeaveBalance records uploaded (replaced previous balances).";
        }

        private async Task<List<LeaveBalance>> ParseLeaveBalanceAsync(IFormFile file, int batchId)
        {
            var list = new List<LeaveBalance>();
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            if (file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                using var sr = new StreamReader(stream);
                string header = await sr.ReadLineAsync();
                while (!sr.EndOfStream)
                {
                    var cols = SplitCsvLine(await sr.ReadLineAsync());
                    var lb = new LeaveBalance
                    {
                        UploadBatchId = batchId,
                        EmployeeId = SafeInt(cols, 0),
                        EmployeeName = SafeString(cols, 1),
                        ALNextAnniversary = SafeParseDate(cols, 2),
                        ALBalance = SafeDecimal(cols, 3),
                        RoundedBalanceDays = (int)Math.Ceiling(SafeDecimal(cols, 3)), // always round UP
                        Function = SafeString(cols, 5)
                    };
                    list.Add(lb);
                }
            }
            else
            {
                using var package = new ExcelPackage(stream);
                var ws = package.Workbook.Worksheets[0];
                int rc = ws.Dimension?.Rows ?? 0;
                for (int r = 2; r <= rc; r++)
                {
                    var raw = GetStringValue(ws.Cells[r, 4]); // AL Balance
                    decimal alBalance = GetDecimalFromString(raw);
                    var lb = new LeaveBalance
                    {
                        UploadBatchId = batchId,
                        EmployeeId = GetIntValue(ws.Cells[r, 1]),
                        EmployeeName = GetStringValue(ws.Cells[r, 2]),
                        ALNextAnniversary = GetDateTimeValue(ws.Cells[r, 3]),
                        ALBalance = alBalance,
                        RoundedBalanceDays = (int)Math.Ceiling(alBalance),
                        Function = GetStringValue(ws.Cells[r, 6])
                    };
                    list.Add(lb);
                }
            }
            return list;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var cur = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ',' && !inQuotes) { result.Add(cur.ToString()); cur.Clear(); continue; }
                cur.Append(c);
            }
            result.Add(cur.ToString());
            return result;
        }

        private static string SafeString(List<string> cols, int idx) => (idx < cols.Count) ? cols[idx].Trim() : string.Empty;
        private static int SafeInt(List<string> cols, int idx) => int.TryParse(SafeString(cols, idx), out var v) ? v : 0;
        private static decimal SafeDecimal(List<string> cols, int idx) => decimal.TryParse(SafeString(cols, idx), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d :
                                                                     decimal.TryParse(SafeString(cols, idx), out d) ? d : 0m;
        private static string SafeDecimalString(List<string> cols, int idx) => SafeString(cols, idx);
        private static DateTime SafeParseDate(List<string> cols, int idx)
        {
            var s = SafeString(cols, idx);
            if (DateTime.TryParse(s, out var d)) return d;
            if (double.TryParse(s, out var oaDate)) // maybe Excel serial passed into CSV
                return DateTime.FromOADate(oaDate);
            return DateTime.MinValue;
        }

        // EPPlus helpers copied/adapted
        private string? GetStringValue(ExcelRange cell) => cell.Value?.ToString()?.Trim();
        private int GetIntValue(ExcelRange cell) => int.TryParse(cell.Value?.ToString(), out int r) ? r : 0;
        private DateTime GetDateTimeValue(ExcelRange cell) =>
            cell.Value is DateTime dt ? dt :
            DateTime.TryParse(cell.Value?.ToString(), out var parsed) ? parsed : DateTime.MinValue;
        private decimal GetDecimalFromString(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
            if (decimal.TryParse(s, out d)) return d;
            return 0m;
        }
    }

}
