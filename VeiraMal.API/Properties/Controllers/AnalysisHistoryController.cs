using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using VeiraMal.API.Models;
using VeiraMal.API.DTOs;
using System.Globalization;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnalysisHistoryController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AnalysisHistoryController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveAnalysis([FromBody] SaveAnalysisRequest request)
        {
            var history = new AnalysisHistory
            {
                Year = request.Year,
                Month = request.Month,
                CreatedAt = DateTime.Now,
                IsFinal = request.IsFinal
            };

            if (request.Headcount != null)
                history.HeadcountData = JsonSerializer.Serialize(request.Headcount);

            if (request.NHT != null)
                history.NHTData = JsonSerializer.Serialize(request.NHT);

            if (request.Terms != null)
                history.TermsData = JsonSerializer.Serialize(request.Terms);

            _context.AnalysisHistory.Add(history);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Analysis saved successfully", historyId = history.Id });
        }


        [HttpGet("all")]
        public async Task<IActionResult> GetAllHistory()
        {
            var histories = await _context.AnalysisHistory
                .OrderByDescending(h => h.CreatedAt)
                .Select(h => new
                {
                    h.Id,
                    h.Year,
                    h.Month,
                    MonthName = h.Month >= 1 && h.Month <= 12
                        ? System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(h.Month)
                        : "Unknown",
                    h.IsFinal,
                    h.CreatedAt
                })
                .ToListAsync();

            return Ok(histories);
        }




        [HttpGet("{id}")]
        public async Task<IActionResult> GetAnalysisById(int id)
        {
            var history = await _context.AnalysisHistory.FindAsync(id);
            if (history == null) return NotFound();

            return Ok(new
            {
                history.Id,
                history.Year,
                history.Month,
                Headcount = history.HeadcountData != null
                    ? JsonSerializer.Deserialize<object>(history.HeadcountData)
                    : null,
                NHT = history.NHTData != null
                    ? JsonSerializer.Deserialize<object>(history.NHTData)
                    : null,
                Terms = history.TermsData != null
                    ? JsonSerializer.Deserialize<object>(history.TermsData)
                    : null,
                history.IsFinal
            });
        }

        // using directives near top of file

    [HttpGet("ceo/ytd")]
    public async Task<IActionResult> GetCeoYtd()
    {
        var currentYear = DateTime.Now.Year;
        var currentMonth = DateTime.Now.Month;

        // Get final snapshots for current year up to current month
        var snapshots = await _context.AnalysisHistory
            .AsNoTracking()
            .Where(h => h.IsFinal && h.Year == currentYear && h.Month >= 1 && h.Month <= currentMonth)
            .OrderBy(h => h.Month) // order ascending by month
            .ToListAsync();

        if (!snapshots.Any())
        {
            return Ok(new
            {
                year = currentYear,
                untilMonth = currentMonth,
                headcount = new object[0],
                nht = new object[0],
                terms = new object[0]
            });
        }

        // Helper local funcs: safe property getters from deserialized JSON objects
        static string GetString(Dictionary<string, JsonElement> dict, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (dict.TryGetValue(k, out var je) && je.ValueKind != JsonValueKind.Null)
                    return je.ToString();
            }
            return null;
        }
        static int GetInt(Dictionary<string, JsonElement> dict, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (dict.TryGetValue(k, out var je) && je.ValueKind != JsonValueKind.Null)
                {
                    if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var v)) return v;
                    if (int.TryParse(je.ToString(), out var p)) return p;
                }
            }
            return 0;
        }
        static double GetDouble(Dictionary<string, JsonElement> dict, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (dict.TryGetValue(k, out var je) && je.ValueKind != JsonValueKind.Null)
                {
                    if (je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var v)) return v;
                    if (double.TryParse(je.ToString(), out var p)) return p;
                }
            }
            return 0;
        }

        // We'll build maps keyed by department (normalized)
        var deptHeadcountHistory = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var latestHeadcountSnapshotPerDept = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.OrdinalIgnoreCase);
        int latestMonthPresent = int.MinValue;

        // NHT cumulative counters per dept
        var nhtCounters = new Dictionary<string, (int newTotal, int newMale, int newFemale, int transferTotal, int transferMale, int transferFemale)>(StringComparer.OrdinalIgnoreCase);

        // Terms cumulative counts per dept
        var termCounters = new Dictionary<string, (int voluntaryTotal, int voluntaryMale, int voluntaryFemale, int involuntaryTotal, int involuntaryMale, int involuntaryFemale)>(StringComparer.OrdinalIgnoreCase);

        // Process snapshots month by month
        foreach (var snap in snapshots)
        {
            if (snap.Month > latestMonthPresent) latestMonthPresent = snap.Month;

            // ----- HEADCOUNT DATA -----
            if (!string.IsNullOrWhiteSpace(snap.HeadcountData))
            {
                try
                {
                    // Deserialize to flexible structure: List<Dictionary<string, JsonElement>>
                    var headList = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(snap.HeadcountData);

                    if (headList != null)
                    {
                        foreach (var item in headList)
                        {
                            // Accept multiple candidate property names: Department, department, OrganizationalUnit, organizationalUnit
                            var deptName = GetString(item, "Department", "department", "OrganizationalUnit", "organizationalUnit") ?? "Unknown";
                            var headcount = GetInt(item, "Headcount", "headcount", "TotalCount", "totalCount");

                            if (!deptHeadcountHistory.TryGetValue(deptName, out var list))
                            {
                                list = new List<int>();
                                deptHeadcountHistory[deptName] = list;
                            }
                            list.Add(headcount);

                            // keep latest snapshot row for headcount display if this month is the latest seen so far
                            if (snap.Month >= latestMonthPresent)
                            {
                                latestHeadcountSnapshotPerDept[deptName] = item;
                            }
                        }
                    }
                }
                catch
                {
                    // ignore malformed snapshot; continue
                }
            }

            // ----- NHT DATA -----
            if (!string.IsNullOrWhiteSpace(snap.NHTData))
            {
                try
                {
                    var nhtList = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(snap.NHTData);
                    if (nhtList != null)
                    {
                        foreach (var item in nhtList)
                        {
                            var deptName = GetString(item, "Department", "department", "OrganizationalUnit", "organizationalUnit") ?? "Unknown";
                            var newTotal = GetInt(item, "NewHireTotal", "NewHireTotal", "newHireTotal");
                            var newMale = GetInt(item, "NewHireMale", "newHireMale", "newHireMale");
                            var newFemale = GetInt(item, "NewHireFemale", "newHireFemale", "newHireFemale");
                            var transferTotal = GetInt(item, "TransferTotal", "TransferTotal", "transferTotal");
                            var transferMale = GetInt(item, "TransferMale", "transferMale", "transferMale");
                            var transferFemale = GetInt(item, "TransferFemale", "transferFemale", "transferFemale");

                            if (!nhtCounters.TryGetValue(deptName, out var t))
                                t = (0, 0, 0, 0, 0, 0);

                            t.newTotal += newTotal;
                            t.newMale += newMale;
                            t.newFemale += newFemale;
                            t.transferTotal += transferTotal;
                            t.transferMale += transferMale;
                            t.transferFemale += transferFemale;
                            nhtCounters[deptName] = t;
                        }
                    }
                }
                catch { }
            }

            // ----- TERMS DATA -----
            if (!string.IsNullOrWhiteSpace(snap.TermsData))
            {
                try
                {
                    var termsList = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(snap.TermsData);
                    if (termsList != null)
                    {
                        foreach (var item in termsList)
                        {
                            var deptName = GetString(item, "Department", "department", "OrganizationalUnit", "organizationalUnit") ?? "Unknown";

                            // Try to read counts (the original snapshot code had VoluntaryTotalCount and such)
                            var voluntaryCount = GetInt(item, "VoluntaryTotalCount", "VoluntaryTotalCount", "voluntaryTotalCount");
                            var voluntaryMale = GetInt(item, "VoluntaryMaleCount", "voluntaryMaleCount", "VoluntaryMaleCount");
                            var voluntaryFemale = GetInt(item, "VoluntaryFemaleCount", "voluntaryFemaleCount", "VoluntaryFemaleCount");
                            var involuntaryCount = GetInt(item, "InvoluntaryTotalCount", "InvoluntaryTotalCount", "involuntaryTotalCount");
                            var involuntaryMale = GetInt(item, "InvoluntaryMaleCount", "involuntaryMaleCount", "InvoluntaryMaleCount");
                            var involuntaryFemale = GetInt(item, "InvoluntaryFemaleCount", "involuntaryFemaleCount", "InvoluntaryFemaleCount");

                            if (!termCounters.TryGetValue(deptName, out var t))
                                t = (0, 0, 0, 0, 0, 0);

                            t.voluntaryTotal += voluntaryCount;
                            t.voluntaryMale += voluntaryMale;
                            t.voluntaryFemale += voluntaryFemale;
                            t.involuntaryTotal += involuntaryCount;
                            t.involuntaryMale += involuntaryMale;
                            t.involuntaryFemale += involuntaryFemale;

                            termCounters[deptName] = t;
                        }
                    }
                }
                catch { }
            }
        }

        // Build HEADCOUNT output: use latest snapshot month for point-in-time headcount rows
        var headcountOutput = new List<object>();
        // if latestHeadcountSnapshotPerDept empty but deptHeadcountHistory present, fallback to last known headcount number
        foreach (var kv in deptHeadcountHistory)
        {
            var dept = kv.Key;
            // try to read from latest snapshot row (if exists)
            if (latestHeadcountSnapshotPerDept.TryGetValue(dept, out var latestRow))
            {
                // convert to simpler structure for frontend — try to extract keys used by your widgets
                var headcount = GetInt(latestRow, "Headcount", "headcount", "TotalCount");
                var HeadcountPercentage = GetDouble(latestRow, "HeadcountPercentage", "headcountPercentage");
                var maleCount = GetInt(latestRow, "MaleCount", "maleCount", "Male");
                var femaleCount = GetInt(latestRow, "FemaleCount", "femaleCount", "Female");
                var avgAge = GetDouble(latestRow, "AverageAge", "averageAge");
                var avgTenure = GetDouble(latestRow, "AverageTenure", "averageTenure");

                headcountOutput.Add(new
                {
                    Department = dept,
                    Headcount = headcount,
                    HeadcountPercentage = HeadcountPercentage,
                    TempPercentage = GetDouble(latestRow, "TempPercentage", "tempPercentage"),
                    MaleCount = maleCount,
                    MalePercentage = 0.0,
                    FemaleCount = femaleCount,
                    FemalePercentage = 0.0,
                    TempCount = GetInt(latestRow, "TempCount", "tempCount"),
                    AverageAge = avgAge,
                    AverageTenure = avgTenure
                });
            }
            else
            {
                // fallback: use last seen count in history list for dept
                var lastCount = kv.Value.LastOrDefault();
                headcountOutput.Add(new
                {
                    Department = dept,
                    Headcount = lastCount,
                    HeadcountPercentage = 0.0,
                    TempPercentage = 0.0,
                    MaleCount = 0,
                    MalePercentage = 0.0,
                    FemaleCount = 0,
                    FemalePercentage = 0.0,
                    TempCount = 0,
                    AverageAge = 0.0,
                    AverageTenure = 0.0
                });
            }
        }

        // compute percentages for headcount output's HC% and male/female % relative to total headcount
        var totalHeadcount = headcountOutput.Sum(x => (int)((JsonElement?)null == null ? ((dynamic)x).Headcount : ((dynamic)x).Headcount)); // dynamic cast trick
                                                                                                                                            // safer compute:
        totalHeadcount = headcountOutput.Sum(obj => (int)((obj as dynamic).Headcount));
        var headcountOutputFinal = headcountOutput.Select(obj =>
        {
            var d = obj as dynamic;
            int hc = (int)d.Headcount;
            int male = (int)d.MaleCount;
            int female = (int)d.FemaleCount;
            //double hcPct = totalHeadcount > 0 ? Math.Round(hc * 100.0 / totalHeadcount, 2) : 0.0;
            double hcPct = d.HeadcountPercentage; // use stored percentage if present
            double malePct = hc > 0 ? Math.Round(male * 100.0 / hc, 2) : 0.0;
            double femalePct = hc > 0 ? Math.Round(female * 100.0 / hc, 2) : 0.0;

            return new
            {
                Department = (string)d.Department,
                Headcount = hc,
                HeadcountPercentage = hcPct,
                TempPercentage = (double)d.TempPercentage,
                MaleCount = male,
                MalePercentage = malePct,
                FemaleCount = female,
                FemalePercentage = femalePct,
                TempCount = (int)d.TempCount,
                AverageAge = (double)d.AverageAge,
                AverageTenure = (double)d.AverageTenure
            };
        }).OrderByDescending(x => x.Headcount).ToList();

        // Build NHT output: sum counts, compute internal hire rate as sumTransfers / sum(totalVacant)
        var nhtOutput = new List<object>();
        foreach (var kv in nhtCounters)
        {
            var dept = kv.Key;
            var t = kv.Value;
            int totalVacant = t.newTotal + t.transferTotal;
            double internalHireRate = totalVacant > 0 ? Math.Round(t.transferTotal * 100.0 / totalVacant, 2) : 0.0;

            nhtOutput.Add(new
            {
                Department = dept,
                NewHireTotal = t.newTotal,
                NewHireMale = t.newMale,
                NewHireFemale = t.newFemale,
                TransferTotal = t.transferTotal,
                TransferMale = t.transferMale,
                TransferFemale = t.transferFemale,
                InternalHireRate = internalHireRate
            });
        }

        // Build Terms output:
        // Need average headcount per dept across months for denominator
        var avgHeadcountPerDept = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in deptHeadcountHistory)
        {
            var dept = kv.Key;
            var list = kv.Value;
            avgHeadcountPerDept[dept] = list.Any() ? list.Average() : 0.0;
        }

        var termsOutput = new List<object>();
        foreach (var kv in termCounters)
        {
            var dept = kv.Key;
            var tc = kv.Value;
            double avgHC = avgHeadcountPerDept.TryGetValue(dept, out var v) ? v : 0.0;
            double safeRate(double num, double den) => den > 0 ? Math.Round(num * 100.0 / den, 2) : 0.0;

            int volTotal = tc.voluntaryTotal;
            int volMale = tc.voluntaryMale;
            int volFemale = tc.voluntaryFemale;
            int involTotal = tc.involuntaryTotal;
            int involMale = tc.involuntaryMale;
            int involFemale = tc.involuntaryFemale;
            double totalRate = safeRate(volTotal + involTotal, avgHC);
            double voluntaryRate = safeRate(volTotal, avgHC);
            double involuntaryRate = safeRate(involTotal, avgHC);

            termsOutput.Add(new
            {
                Department = dept,
                VoluntaryTotalCount = volTotal,
                VoluntaryTotalRate = voluntaryRate,
                VoluntaryMaleCount = volMale,
                VoluntaryMaleRate = safeRate(volMale, avgHC > 0 ? avgHC * (/*male share unknown*/0.5) : avgHC), // best-effort if male/female headcounts not present
                VoluntaryFemaleCount = volFemale,
                VoluntaryFemaleRate = safeRate(volFemale, avgHC > 0 ? avgHC * 0.5 : avgHC),
                InvoluntaryTotalCount = involTotal,
                InvoluntaryTotalRate = involuntaryRate,
                InvoluntaryMaleCount = involMale,
                InvoluntaryFemaleCount = involFemale,
            });
        }

        return Ok(new
        {
            year = currentYear,
            untilMonth = latestMonthPresent,
            headcount = headcountOutputFinal,
            nht = nhtOutput.OrderBy(x => ((dynamic)x).Department).ToList(),
            terms = termsOutput.OrderBy(x => ((dynamic)x).Department).ToList()
        });
    }


}
}
