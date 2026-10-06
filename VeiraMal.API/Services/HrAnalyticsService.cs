using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using VeiraMal.API;
using VeiraMal.API.DTOs;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services;

public sealed class HrAnalyticsService : IHrAnalyticsService
{
    private sealed class MonthSnapshot
    {
        public DateTime Month { get; init; }
        public List<Employee> Active { get; init; } = new();
        public int Hires { get; init; }
        public int Exits { get; init; }
    }

    private const decimal DefaultTurnoverHealthyThreshold = 10m;
    private const decimal DefaultTurnoverWatchThreshold = 15m;
    private const decimal DefaultAbsenceHealthyThreshold = 2.5m;
    private const decimal DefaultAbsenceWatchThreshold = 4.5m;
    private const int DefaultWorkingDaysPerYear = 260;
    private const int DefaultRollingAverageWindowMonths = 3;
    private const string DefaultCurrency = "USD";
    private const int DefaultRequisitionAgeDays = 45;

    private readonly AppDbContext _db;
    private readonly ILogger<HrAnalyticsService> _logger;

    public HrAnalyticsService(
        AppDbContext db,
        ILogger<HrAnalyticsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HrAnalyticsSettingsDto> GetSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var settings = await _db.HrAnalyticsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        return settings is null
            ? CreateDefaultSettingsDto()
            : ToSettingsDto(settings);
    }

    public async Task<HrAnalyticsSettingsDto> UpdateSettingsAsync(
        Guid companyId,
        HrAnalyticsSettingsUpdateDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.TurnoverWatchThreshold < request.TurnoverHealthyThreshold)
            throw new InvalidOperationException(
                "Turnover Watch threshold must be greater than or equal to the Turnover Healthy threshold.");

        if (request.AbsenceWatchThreshold < request.AbsenceHealthyThreshold)
            throw new InvalidOperationException(
                "Absence Watch threshold must be greater than or equal to the Absence Healthy threshold.");

        var currency = (request.Currency ?? string.Empty).Trim().ToUpperInvariant();

        if (!System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$"))
            throw new InvalidOperationException(
                "Currency must be a 3-letter ISO currency code such as AUD, USD or GBP.");

        var settings = await _db.HrAnalyticsSettings
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        if (settings is null)
        {
            settings = new HrAnalyticsSettings
            {
                CompanyId = companyId,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.HrAnalyticsSettings.Add(settings);
        }

        settings.TurnoverHealthyThreshold = request.TurnoverHealthyThreshold;
        settings.TurnoverWatchThreshold = request.TurnoverWatchThreshold;
        settings.AbsenceHealthyThreshold = request.AbsenceHealthyThreshold;
        settings.AbsenceWatchThreshold = request.AbsenceWatchThreshold;
        settings.WorkingDaysPerYear = request.WorkingDaysPerYear;
        settings.RollingAverageWindowMonths = request.RollingAverageWindowMonths;
        settings.Currency = currency;
        settings.RequisitionAgeDays = request.RequisitionAgeDays;
        settings.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ToSettingsDto(settings);
    }

    public async Task<HrDashboardDto> GetDashboardAsync(
        Guid companyId,
        DateTime reportingDate,
        CancellationToken cancellationToken = default)
    {
        var end = reportingDate.Date;
        var settings = await LoadSettingsAsync(companyId, cancellationToken);

        // 12-month reporting window.
        var start = new DateTime(end.Year, end.Month, 1).AddMonths(-11);

        var monthEnds = Enumerable
            .Range(0, 12)
            .Select(i => start.AddMonths(i))
            .Select(x => new DateTime(
                x.Year,
                x.Month,
                DateTime.DaysInMonth(x.Year, x.Month)))
            .ToList();

        // -----------------------------------------------------------------
        // EMPLOYEES
        // -----------------------------------------------------------------

        var employees = await _db.Employees
            .AsNoTracking()
            .Where(e =>
                e.CompanyId == companyId &&
                e.HireDate <= end)
            .ToListAsync(cancellationToken);

        // -----------------------------------------------------------------
        // LEAVE
        // -----------------------------------------------------------------

        var leaves = await _db.LeaveTakens
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.StartDate <= end &&
                x.EndDate >= start)
            .ToListAsync(cancellationToken);

        // -----------------------------------------------------------------
        // ENGAGEMENT
        // -----------------------------------------------------------------

        var engagements = await _db.HrEngagementScores
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.SurveyDate <= end)
            .OrderByDescending(x => x.SurveyDate)
            .ToListAsync(cancellationToken);

        // -----------------------------------------------------------------
        // OPEN ROLES
        // -----------------------------------------------------------------

        var openRoles = await _db.HrOpenRoles
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (!x.AsOfDate.HasValue || x.AsOfDate.Value <= end))
            .ToListAsync(cancellationToken);

        // -----------------------------------------------------------------
        // MONTHLY SNAPSHOTS
        // -----------------------------------------------------------------

        var snapshots = monthEnds
            .Select(m => new MonthSnapshot
            {
                Month = m,

                Active = employees
                    .Where(e => IsActiveAt(e, m))
                    .ToList(),

                Hires = employees.Count(e =>
                    SameMonth(e.HireDate, m)),

                Exits = employees.Count(e =>
                    e.ExitDate.HasValue &&
                    SameMonth(e.ExitDate.Value, m))
            })
            .ToList();

        // -----------------------------------------------------------------
        // CURRENT KPIs
        // -----------------------------------------------------------------

        var current = snapshots.Count > 0
            ? snapshots[^1].Active
            : new List<Employee>();

        var activeHeadcount = current.Count;

        // IMPORTANT:
        // Explicitly calculate Average as decimal.
        // This prevents double -> decimal compile errors.
        var avgHeadcount = snapshots.Count == 0
            ? 0m
            : snapshots.Average(x => (decimal)x.Active.Count);

        var exits12 = snapshots.Sum(x => x.Exits);
        var hires12 = snapshots.Sum(x => x.Hires);

        var turnover = SafePercent(
            exits12,
            avgHeadcount);

        var totalSalary = current.Sum(e => e.BaseSalary * EffectiveFte(e));

        var avgSalary = activeHeadcount == 0
            ? 0m
            : current
                .Where(e => e.BaseSalary > 0)
                .Select(e => e.BaseSalary)
                .DefaultIfEmpty(0m)
                .Average();

        var activeFte = current.Sum(e => e.FTE);

        var avgTenure = AverageTenure(
            current,
            end);

        var femalePct = Percent(
            current.Count(e => IsFemale(e.Gender)),
            activeHeadcount);

        var openRoleCount = openRoles
            .Where(IsOpenRole)
            .Sum(x => Math.Max(0, x.OpenRoles));

        // -----------------------------------------------------------------
        // ANALYTICS
        // -----------------------------------------------------------------

        var absence = BuildAbsence(
            current,
            leaves,
            start,
            end,
            snapshots,
            settings.WorkingDaysPerYear,
            settings.AbsenceHealthyThreshold,
            settings.AbsenceWatchThreshold);

        var attrition = BuildAttrition(
            employees,
            snapshots,
            start,
            end,
            settings.TurnoverHealthyThreshold,
            settings.TurnoverWatchThreshold);

        var diversity = BuildDiversity(
            current,
            end);

        var departments = BuildDepartments(
            current,
            employees,
            leaves,
            snapshots,
            start,
            end,
            settings.WorkingDaysPerYear,
            settings.TurnoverHealthyThreshold,
            settings.TurnoverWatchThreshold,
            settings.AbsenceHealthyThreshold,
            settings.AbsenceWatchThreshold);

        var labour = BuildLabour(current);

        var activity = BuildActivity(
            employees,
            start,
            end);

        var risk = BuildRiskWatchlist(
            current,
            engagements,
            departments,
            end,
            settings.TurnoverHealthyThreshold,
            settings.TurnoverWatchThreshold);

        // -----------------------------------------------------------------
        // DASHBOARD DTO
        // -----------------------------------------------------------------

        return new HrDashboardDto
        {
            ReportingDate = end,

            ReportingMonth = end.ToString(
                "MMM yyyy",
                CultureInfo.InvariantCulture),

            Settings = ToSettingsDto(settings),

            Kpis = new HrKpiDto
            {
                ActiveHeadcount = activeHeadcount,

                ActiveFte = Round(activeFte),

                AverageTenureYears = Round(avgTenure),

                Hires = hires12,

                Exits = exits12,

                TurnoverRate = Round(turnover),
                TurnoverStatus = GetTurnoverStatus(
                    turnover,
                    settings.TurnoverHealthyThreshold,
                    settings.TurnoverWatchThreshold),

                LabourCost = Round(totalSalary),

                AverageSalary = Round(avgSalary),

                CostPerHead = activeHeadcount == 0
                    ? 0m
                    : Round(totalSalary / activeHeadcount),

                FemalePercentage = Round(femalePct),

                OpenRoles = openRoleCount,

                AbsenceRate = absence.AbsenceRate,
                AbsenceStatus = absence.Status,

                UnplannedLeaveDays = absence.UnplannedLeaveDays
            },

            HeadcountTrend = snapshots
                .Select(x => new HrMonthlyTrendDto
                {
                    Month = x.Month,

                    Label = x.Month.ToString(
                        "MMM yy",
                        CultureInfo.InvariantCulture),

                    Headcount = x.Active.Count,

                    Fte = Round(
                        x.Active.Sum(e => e.FTE)),
                    RollingAverageHeadcount = Round(
                        snapshots
                            .Take(snapshots.IndexOf(x) + 1)
                            .TakeLast(settings.RollingAverageWindowMonths)
                            .Average(s => (decimal)s.Active.Count))
                })
                .ToList(),

            HiresVsExits = snapshots
                .Select(x => new HrMonthlyHiresExitsDto
                {
                    Month = x.Month,

                    Label = x.Month.ToString(
                        "MMM yy",
                        CultureInfo.InvariantCulture),

                    Hires = x.Hires,

                    Exits = x.Exits
                })
                .ToList(),

            Departments = departments,

            Diversity = diversity,

            LabourCost = labour,

            Attrition = attrition,

            Absenteeism = absence,

            RecentActivity = activity,

            RiskWatchlist = risk
        };
    }

    // =====================================================================
    // EMPLOYEE SEARCH
    // =====================================================================

    public async Task<IReadOnlyList<HrEmployeeSearchDto>> SearchEmployeesAsync(
        Guid companyId,
        string query,
        CancellationToken cancellationToken = default)
    {
        query = (query ?? string.Empty).Trim();

        if (query.Length < 2)
            return Array.Empty<HrEmployeeSearchDto>();

        var pattern = query.ToLowerInvariant();

        var employees = await _db.Employees
            .AsNoTracking()
            .Where(e =>
                e.CompanyId == companyId &&
                (
                    ((e.EmployeeId ?? "")
                        .ToLower()
                        .Contains(pattern))

                    ||

                    ((e.EmployeeName ?? "")
                        .ToLower()
                        .Contains(pattern))

                    ||

                    ((e.Department ?? "")
                        .ToLower()
                        .Contains(pattern))

                    ||

                    ((e.PositionTitle ?? "")
                        .ToLower()
                        .Contains(pattern))
                ))
            .OrderBy(e => e.EmployeeName)
            .Take(8)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow.Date;

        return employees
            .Select(e => new HrEmployeeSearchDto
            {
                EmployeeId = e.EmployeeId ?? string.Empty,

                Name = e.EmployeeName
                    ?? e.EmployeeId
                    ?? "Unknown",

                Department = e.Department,

                Position = e.PositionTitle,

                // Deliberately do not expose employee email.
                Email = null,

                TenureYears = Round(
                    (decimal)Math.Max(
                        0,
                        (now - e.HireDate.Date).TotalDays / 365.25))
            })
            .ToList();
    }

    // =====================================================================
    // EXCEL IMPORT
    // =====================================================================

    public async Task<HrImportResultDto> ImportWorkbookAsync(
        Guid companyId,
        int uploadedByUserId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        ValidateUpload(file);

        var warnings = new List<string>();

        var employees = new List<Employee>();
        var engagement = new List<HrEngagementScore>();
        var roles = new List<HrOpenRole>();
        var leave = new List<LeaveTaken>();

        await using var stream = new MemoryStream();

        await file.CopyToAsync(
            stream,
            cancellationToken);

        stream.Position = 0;

        using var package = new ExcelPackage(stream);

        // -----------------------------------------------------------------
        // EMPLOYEE MASTER
        // -----------------------------------------------------------------

        var employeeRows = GetRows(
            package,
            "Employee_Master",
            "Employee Data",
            "employees",
            "employee",
            "staff",
            "data");

        if (employeeRows.Count == 0)
        {
            throw new InvalidOperationException(
                "The workbook must contain an Employee_Master (or supported alias) sheet.");
        }

        foreach (var row in employeeRows)
        {
            var id = Text(
                row,
                "employeeid",
                "employeeidnumber",
                "personnelnumber",
                "id");

            var hire = Date(
                row,
                "hiredate",
                "dateofhire",
                "startdate");

            if (string.IsNullOrWhiteSpace(id) ||
                !hire.HasValue)
            {
                warnings.Add(
                    "Skipped an employee row because Employee ID or Hire Date was missing/invalid.");

                continue;
            }

            var fte = Decimal(
                row,
                "fte",
                "fulltimeequivalent");

            employees.Add(new Employee
            {
                CompanyId = companyId,

                EmployeeId = id,

                EmployeeName = Text(
                    row,
                    "name",
                    "employeename",
                    "fullname",
                    "personnelname"),

                Email = Text(
                    row,
                    "email",
                    "emailaddress"),

                PositionTitle = Text(
                    row,
                    "jobtitle",
                    "positiontitle",
                    "position",
                    "role"),

                Department = Text(
                    row,
                    "department",
                    "division",
                    "function",
                    "organizationalunit")
                    ?? "Unassigned",

                BusinessUnit = Text(
                    row,
                    "businessunit"),

                OrgUnit = Text(
                    row,
                    "orgunit",
                    "organizationalunit"),

                Location = Text(
                    row,
                    "location",
                    "city",
                    "personnelarea"),

                Gender = Text(
                    row,
                    "gender",
                    "genderkey"),

                Ethnicity = Text(
                    row,
                    "ethnicity",
                    "ethnicgroup"),

                EmployeeType = Text(
                    row,
                    "employeetype",
                    "employmenttype",
                    "employeegroup"),

                WorkArrangement = Text(
                    row,
                    "workarrangement",
                    "workingarrangement",
                    "worklocation"),

                EmployeeStatus = Text(
                    row,
                    "employeestatus",
                    "status")
                    ?? "Active",

                TerminationReason = Text(
                    row,
                    "terminationreason",
                    "reasonforleaving",
                    "exitreason",
                    "reasonforaction"),

                CostCentre = Text(
                    row,
                    "costcentre",
                    "costcentrenumber",
                    "costcenter",
                    "costcenternumber"),

                DateOfBirth = Date(
                    row,
                    "dob",
                    "dateofbirth",
                    "birthdate")
                    ?? DateTime.MinValue,

                HireDate = hire.Value,

                ExitDate = Date(
                    row,
                    "exitdate",
                    "terminationdate",
                    "leavingdate",
                    "enddate"),

                BaseSalary = Decimal(
                    row,
                    "salary",
                    "basesalary",
                    "annualsalary",
                    "remuneration"),

                TotalRemuneration = Decimal(
                    row,
                    "totalremuneration",
                    "totalcompensation",
                    "totalpackage"),

                SuperPercentage = Decimal(
                    row,
                    "superpercentage",
                    "superannuation",
                    "super"),

                // Keep the original intended default of 1 FTE.
                FTE = fte > 0
                    ? fte
                    : 1m,

                HoursPerWeek = Decimal(
                    row,
                    "hoursperweek",
                    "weeklyhours",
                    "hours"),

                Level = Text(
                    row,
                    "level",
                    "grade",
                    "gradegrouping")
            });
        }

        // -----------------------------------------------------------------
        // ENGAGEMENT SURVEY
        // -----------------------------------------------------------------

        foreach (var row in GetRows(
            package,
            "Engagement_Survey",
            "Engagement",
            "engagementsurvey"))
        {
            var id = Text(
                row,
                "employeeid",
                "personnelnumber",
                "id");

            var score = Decimal(
                row,
                "engagementscore",
                "score",
                "engagement");

            var date = Date(
                row,
                "surveydate",
                "date",
                "month")
                ?? DateTime.UtcNow.Date;

            if (!string.IsNullOrWhiteSpace(id))
            {
                engagement.Add(
                    new HrEngagementScore
                    {
                        CompanyId = companyId,

                        EmployeeId = id,

                        Score = Math.Clamp(
                            score,
                            0m,
                            100m),

                        SurveyDate = date
                    });
            }
        }

        // -----------------------------------------------------------------
        // OPEN ROLES
        // -----------------------------------------------------------------

        foreach (var row in GetRows(
            package,
            "Open Roles",
            "vacancies",
            "openroles",
            "openpositions"))
        {
            var dept = Text(
                row,
                "department",
                "division",
                "businessunit")
                ?? "Unassigned";

            var countValue = Decimal(
                row,
                "openroles",
                "openpositions",
                "vacancies",
                "vacancycount",
                "count",
                "roles",
                "numberofroles");

            var count = (int)Math.Max(
                1m,
                countValue);

            var status = Text(
                row,
                "status",
                "stage");

            if (!IsClosedStatus(status))
            {
                roles.Add(
                    new HrOpenRole
                    {
                        CompanyId = companyId,

                        Department = dept,

                        RoleTitle = Text(
                            row,
                            "roletitle",
                            "jobtitle",
                            "position",
                            "role"),

                        OpenRoles = count,

                        Status = status,

                        AsOfDate = Date(
                            row,
                            "date",
                            "asofdate",
                            "month")
                    });
            }
        }

        // -----------------------------------------------------------------
        // LEAVE RECORDS
        // -----------------------------------------------------------------

        foreach (var row in GetRows(
            package,
            "Leave_Records",
            "Leave",
            "Absence",
            "leave"))
        {
            var idText = Text(
                row,
                "employeeid",
                "personnelnumber",
                "id");

            if (!int.TryParse(
                idText,
                out var id))
            {
                // Existing SAP LeaveTaken uses numeric personnel numbers.
                continue;
            }

            var startDate = Date(
                row,
                "startdate",
                "leave_start",
                "from");

            var endDate = Date(
                row,
                "enddate",
                "leave_end",
                "to")
                ?? startDate;

            if (!startDate.HasValue ||
                !endDate.HasValue ||
                endDate.Value < startDate.Value)
            {
                continue;
            }

            var days = Decimal(
                row,
                "businessdays",
                "days",
                "duration");

            if (days <= 0)
            {
                days =
                    (endDate.Value.Date - startDate.Value.Date).Days
                    + 1;
            }

            leave.Add(
                new LeaveTaken
                {
                    CompanyId = companyId,

                    PersonnelNumber = id,

                    PersonnelName = Text(
                        row,
                        "personnelname",
                        "name",
                        "employeename"),

                    Position = Text(
                        row,
                        "position",
                        "jobtitle"),

                    PersonnelSubarea = Text(
                        row,
                        "personnelsubarea"),

                    PersonnelArea = Text(
                        row,
                        "personnelarea"),

                    Function = Text(
                        row,
                        "function",
                        "department"),

                    Manager = Text(
                        row,
                        "manager",
                        "managername"),

                    CostCentreNumber = Text(
                        row,
                        "costcentrenumber",
                        "costcentre",
                        "costcenter"),

                    CostCentreDescription = Text(
                        row,
                        "costcentredescription",
                        "costcenterdescription"),

                    OrganizationalUnit = Text(
                        row,
                        "organizationalunit",
                        "department"),

                    EmploymentPercentage = Decimal(
                        row,
                        "employmentpercentage",
                        "fte"),

                    WeeklyHours = Decimal(
                        row,
                        "weeklyhours",
                        "hoursperweek"),

                    AttendanceAbsenceType = Text(
                        row,
                        "leavetype",
                        "type",
                        "attendanceabsencetype")
                        ?? "Other",

                    Hours = Decimal(
                        row,
                        "hours"),

                    Days = days,

                    StartDate = startDate.Value,

                    EndDate = endDate.Value,

                    ChangedOn = Date(
                        row,
                        "changedon",
                        "modifiedon")
                        ?? DateTime.UtcNow,

                    Location = Text(
                        row,
                        "location",
                        "personnelsubarea",
                        "city"),

                    Month = Text(
                        row,
                        "month"),

                    EmploymentType = Text(
                        row,
                        "employmenttype",
                        "employeetype"),

                    BusinessUnit = Text(
                        row,
                        "businessunit")
                });
        }

        // -----------------------------------------------------------------
        // VALIDATION
        // -----------------------------------------------------------------

        if (employees.Count == 0)
        {
            throw new InvalidOperationException(
                "No valid employee records were found.");
        }

        var duplicateIds = employees
            .GroupBy(
                x => x.EmployeeId,
                StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate Employee IDs detected: " +
                $"{string.Join(", ", duplicateIds.Take(10))}" +
                $"{(duplicateIds.Count > 10 ? "..." : "")}");
        }

        // -----------------------------------------------------------------
        // DATABASE TRANSACTION
        // -----------------------------------------------------------------

        // SQL Server has EnableRetryOnFailure() configured in Program.cs.
        // Therefore, the explicit transaction must be executed through
        // EF Core's execution strategy so the complete transaction can
        // safely be retried as one unit.
        var executionStrategy =
            _db.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(
            async () =>
            {
                await using var tx =
                    await _db.Database.BeginTransactionAsync(
                        cancellationToken);

                try
                {
                    // ---------------------------------------------------------
                    // Remove ONLY this company's existing HR Analytics data.
                    // Other companies remain untouched.
                    // ---------------------------------------------------------

                    _db.Employees.RemoveRange(
                        _db.Employees.Where(
                            x => x.CompanyId == companyId));

                    _db.HrEngagementScores.RemoveRange(
                        _db.HrEngagementScores.Where(
                            x => x.CompanyId == companyId));

                    _db.HrOpenRoles.RemoveRange(
                        _db.HrOpenRoles.Where(
                            x => x.CompanyId == companyId));

                    _db.LeaveTakens.RemoveRange(
                        _db.LeaveTakens.Where(
                            x => x.CompanyId == companyId));

                    await _db.SaveChangesAsync(
                        cancellationToken);

                    // ---------------------------------------------------------
                    // Insert the new company-scoped records.
                    // ---------------------------------------------------------

                    await _db.Employees.AddRangeAsync(
                        employees,
                        cancellationToken);

                    await _db.HrEngagementScores.AddRangeAsync(
                        engagement,
                        cancellationToken);

                    await _db.HrOpenRoles.AddRangeAsync(
                        roles,
                        cancellationToken);

                    await _db.LeaveTakens.AddRangeAsync(
                        leave,
                        cancellationToken);

                    // ---------------------------------------------------------
                    // Record upload metadata only.
                    // We do NOT store the uploaded workbook itself.
                    // ---------------------------------------------------------

                    var batch = new UploadBatch
                    {
                        CompanyId = companyId,

                        UploadedBy = uploadedByUserId
                            .ToString(CultureInfo.InvariantCulture),

                        Status = "Completed",

                        FilesMeta = JsonSerializer.Serialize(
                            new
                            {
                                fileName = SanitizeFileName(
                                    file.FileName),

                                size = file.Length,

                                employees = employees.Count,

                                engagement = engagement.Count,

                                openRoles = roles.Count,

                                leave = leave.Count
                            })
                    };

                    _db.UploadBatches.Add(batch);

                    await _db.SaveChangesAsync(
                        cancellationToken);

                    // ---------------------------------------------------------
                    // Commit everything atomically.
                    // ---------------------------------------------------------

                    await tx.CommitAsync(
                        cancellationToken);

                    return new HrImportResultDto
                    {
                        EmployeeRecords = employees.Count,

                        EngagementRecords = engagement.Count,

                        OpenRoleRecords = roles.Count,

                        LeaveRecords = leave.Count,

                        WarningCount = warnings.Count,

                        Warnings = warnings
                            .Take(100)
                            .ToList(),

                        UploadBatchId = batch.Id
                    };
                }
                catch
                {
                    await tx.RollbackAsync(
                        cancellationToken);

                    throw;
                }
            });
    }

    // =====================================================================
    // UPLOAD VALIDATION
    // =====================================================================

    private async Task<HrAnalyticsSettings> LoadSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var settings = await _db.HrAnalyticsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        return settings ?? new HrAnalyticsSettings
        {
            CompanyId = companyId,
            TurnoverHealthyThreshold = DefaultTurnoverHealthyThreshold,
            TurnoverWatchThreshold = DefaultTurnoverWatchThreshold,
            AbsenceHealthyThreshold = DefaultAbsenceHealthyThreshold,
            AbsenceWatchThreshold = DefaultAbsenceWatchThreshold,
            WorkingDaysPerYear = DefaultWorkingDaysPerYear,
            RollingAverageWindowMonths = DefaultRollingAverageWindowMonths,
            Currency = DefaultCurrency,
            RequisitionAgeDays = DefaultRequisitionAgeDays
        };
    }

    private static HrAnalyticsSettingsDto CreateDefaultSettingsDto()
    {
        return new HrAnalyticsSettingsDto
        {
            Id = 0,
            TurnoverHealthyThreshold = DefaultTurnoverHealthyThreshold,
            TurnoverWatchThreshold = DefaultTurnoverWatchThreshold,
            AbsenceHealthyThreshold = DefaultAbsenceHealthyThreshold,
            AbsenceWatchThreshold = DefaultAbsenceWatchThreshold,
            WorkingDaysPerYear = DefaultWorkingDaysPerYear,
            RollingAverageWindowMonths = DefaultRollingAverageWindowMonths,
            Currency = DefaultCurrency,
            RequisitionAgeDays = DefaultRequisitionAgeDays,
            UpdatedAtUtc = null
        };
    }

    private static HrAnalyticsSettingsDto ToSettingsDto(
        HrAnalyticsSettings settings)
    {
        return new HrAnalyticsSettingsDto
        {
            Id = settings.Id,
            TurnoverHealthyThreshold = settings.TurnoverHealthyThreshold,
            TurnoverWatchThreshold = settings.TurnoverWatchThreshold,
            AbsenceHealthyThreshold = settings.AbsenceHealthyThreshold,
            AbsenceWatchThreshold = settings.AbsenceWatchThreshold,
            WorkingDaysPerYear = settings.WorkingDaysPerYear,
            RollingAverageWindowMonths = settings.RollingAverageWindowMonths,
            Currency = settings.Currency,
            RequisitionAgeDays = settings.RequisitionAgeDays,
            UpdatedAtUtc = settings.Id == 0 ? null : settings.UpdatedAtUtc
        };
    }

    private static string GetTurnoverStatus(
        decimal rate,
        decimal healthyThreshold,
        decimal watchThreshold)
    {
        if (rate <= healthyThreshold) return "Healthy";
        if (rate <= watchThreshold) return "Watch";
        return "At risk";
    }

    private static string GetAbsenceStatus(
        decimal rate,
        decimal healthyThreshold,
        decimal watchThreshold)
    {
        if (rate <= healthyThreshold) return "Healthy";
        if (rate <= watchThreshold) return "Watch";
        return "High";
    }

    private static decimal EffectiveFte(Employee employee)
    {
        return employee.FTE > 0m ? employee.FTE : 1m;
    }

    private static void ValidateUpload(IFormFile file)
    {
        if (file is null ||
            file.Length == 0)
        {
            throw new InvalidOperationException(
                "A workbook file is required.");
        }

        if (file.Length > 20 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "The workbook exceeds the 20 MB upload limit.");
        }

        var ext = Path.GetExtension(
            file.FileName);

        if (!string.Equals(
            ext,
            ".xlsx",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only .xlsx workbooks are accepted.");
        }
    }

    // =====================================================================
    // GENERAL HELPERS
    // =====================================================================

    private static bool IsActiveAt(
        Employee e,
        DateTime date)
    {
        return
            e.HireDate.Date <= date.Date &&
            (
                !e.ExitDate.HasValue ||
                e.ExitDate.Value.Date > date.Date
            );
    }

    private static bool SameMonth(
        DateTime a,
        DateTime b)
    {
        return
            a.Year == b.Year &&
            a.Month == b.Month;
    }

    private static decimal Percent(
        decimal numerator,
        decimal denominator)
    {
        return denominator <= 0
            ? 0m
            : numerator * 100m / denominator;
    }

    private static decimal SafePercent(
        decimal numerator,
        decimal denominator)
    {
        return denominator <= 0
            ? 0m
            : numerator * 100m / denominator;
    }

    private static decimal Round(
        decimal value)
    {
        return Math.Round(
            value,
            1,
            MidpointRounding.AwayFromZero);
    }

    private static decimal AverageTenure(
        IEnumerable<Employee> employees,
        DateTime date)
    {
        var list = employees
            .Where(e =>
                e.HireDate != DateTime.MinValue)
            .Select(e =>
                Math.Max(
                    0,
                    (date.Date - e.HireDate.Date)
                        .TotalDays / 365.25))
            .ToList();

        return list.Count == 0
            ? 0m
            : (decimal)list.Average();
    }

    private static bool IsFemale(
        string? gender)
    {
        var value = gender?.Trim();

        return
            string.Equals(
                value,
                "female",
                StringComparison.OrdinalIgnoreCase)
            ||
            string.Equals(
                value,
                "f",
                StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================
    // DIVERSITY
    // =====================================================================

    private static HrDiversityDto BuildDiversity(
        List<Employee> current,
        DateTime end)
    {
        static List<HrBreakdownItemDto> Breakdown(
            IEnumerable<string?> values)
        {
            return values
                .GroupBy(
                    v => string.IsNullOrWhiteSpace(v)
                        ? "Not specified"
                        : v!.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(
                    g => g.Count())
                .Select(
                    g => new HrBreakdownItemDto
                    {
                        Label = g.Key,
                        Value = g.Count()
                    })
                .ToList();
        }

        static List<HrBreakdownItemDto> Age(
            IEnumerable<Employee> employees,
            DateTime date)
        {
            var labels = new[]
            {
                "<25",
                "25–34",
                "35–44",
                "45–54",
                "55+"
            };

            return labels
                .Select(
                    label =>
                        new HrBreakdownItemDto
                        {
                            Label = label,

                            Value = employees.Count(e =>
                            {
                                var age =
                                    (int)Math.Floor(
                                        (
                                            date -
                                            e.DateOfBirth
                                        ).TotalDays
                                        / 365.2425);

                                return label switch
                                {
                                    "<25" =>
                                        age < 25,

                                    "25–34" =>
                                        age >= 25 &&
                                        age < 35,

                                    "35–44" =>
                                        age >= 35 &&
                                        age < 45,

                                    "45–54" =>
                                        age >= 45 &&
                                        age < 55,

                                    _ =>
                                        age >= 55
                                };
                            })
                        })
                .ToList();
        }

        static List<HrBreakdownItemDto> Tenure(
            IEnumerable<Employee> employees,
            DateTime date)
        {
            var labels = new[]
            {
                "<1 yr",
                "1–2",
                "3–5",
                "6+"
            };

            return labels
                .Select(
                    label =>
                        new HrBreakdownItemDto
                        {
                            Label = label,

                            Value = employees.Count(e =>
                            {
                                var years =
                                    (
                                        date -
                                        e.HireDate
                                    ).TotalDays / 365.25;

                                return label switch
                                {
                                    "<1 yr" =>
                                        years < 1,

                                    "1–2" =>
                                        years >= 1 &&
                                        years < 3,

                                    "3–5" =>
                                        years >= 3 &&
                                        years < 6,

                                    _ =>
                                        years >= 6
                                };
                            })
                        })
                .ToList();
        }

        var departments = current
            .Select(e =>
                e.Department ?? "Unassigned")
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        return new HrDiversityDto
        {
            Gender = Breakdown(
                current.Select(e => e.Gender)),

            EmploymentType = Breakdown(
                current.Select(e => e.EmployeeType)),

            WorkArrangement = Breakdown(
                current.Select(e => e.WorkArrangement)),

            Age = Age(
                current,
                end),

            Tenure = Tenure(
                current,
                end),

            Location = Breakdown(
                current.Select(e => e.Location)),

            Ethnicity = Breakdown(
                current.Select(e => e.Ethnicity)),

            GenderByDepartment = departments
                .Select(
                    department =>
                        new HrGenderDepartmentDto
                        {
                            Department = department,

                            Male = current.Count(e =>
                                string.Equals(
                                    e.Department ??
                                    "Unassigned",
                                    department,
                                    StringComparison.OrdinalIgnoreCase)
                                &&
                                string.Equals(
                                    e.Gender?.Trim(),
                                    "male",
                                    StringComparison.OrdinalIgnoreCase)),

                            Female = current.Count(e =>
                                string.Equals(
                                    e.Department ??
                                    "Unassigned",
                                    department,
                                    StringComparison.OrdinalIgnoreCase)
                                &&
                                IsFemale(e.Gender)),

                            Other = current.Count(e =>
                                string.Equals(
                                    e.Department ??
                                    "Unassigned",
                                    department,
                                    StringComparison.OrdinalIgnoreCase)
                                &&
                                !string.Equals(
                                    e.Gender?.Trim(),
                                    "male",
                                    StringComparison.OrdinalIgnoreCase)
                                &&
                                !IsFemale(e.Gender))
                        })
                .ToList()
        };
    }

    // =====================================================================
    // LABOUR COST
    // =====================================================================

    private static HrLabourCostDto BuildLabour(
        List<Employee> current)
    {
        static List<HrBreakdownItemDto> SumBy(
            IEnumerable<Employee> employees,
            Func<Employee, string?> key,
            Func<Employee, decimal> value)
        {
            return employees
                .GroupBy(
                    e =>
                        string.IsNullOrWhiteSpace(key(e))
                            ? "Not specified"
                            : key(e)!.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(
                    g => g.Sum(value))
                .Select(
                    g =>
                        new HrBreakdownItemDto
                        {
                            Label = g.Key,
                            Value = Round(g.Sum(value))
                        })
                .ToList();
        }

        var total = current.Sum(
            e => e.BaseSalary * EffectiveFte(e));

        return new HrLabourCostDto
        {
            TotalAnnualBaseSalary = Round(
                total),

            MonthlyEquivalent = Round(
                total / 12m),

            CostByDepartment = SumBy(
                current,
                e => e.Department,
                e => e.BaseSalary * EffectiveFte(e)),

            SalaryDistribution = new[]
            {
                "<50k",
                "50–74k",
                "75–99k",
                "100–149k",
                "150k+"
            }
            .Select(
                band =>
                    new HrBreakdownItemDto
                    {
                        Label = band,

                        Value = current.Count(e =>
                            band switch
                            {
                                "<50k" =>
                                    e.BaseSalary < 50000,

                                "50–74k" =>
                                    e.BaseSalary >= 50000 &&
                                    e.BaseSalary < 75000,

                                "75–99k" =>
                                    e.BaseSalary >= 75000 &&
                                    e.BaseSalary < 100000,

                                "100–149k" =>
                                    e.BaseSalary >= 100000 &&
                                    e.BaseSalary < 150000,

                                _ =>
                                    e.BaseSalary >= 150000
                            })
                    })
            .ToList(),

            AverageSalaryByDepartment = current
                .GroupBy(
                    e =>
                        e.Department ??
                        "Unassigned",
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    g => g.Key)
                .Select(
                    g =>
                        new HrDepartmentSalaryDto
                        {
                            Department = g.Key,

                            AverageSalary = Round(
                                g.Average(
                                    e => e.BaseSalary))
                        })
                .ToList(),

            CostByEmploymentType = SumBy(
                current,
                e => e.EmployeeType,
                e => e.BaseSalary * EffectiveFte(e)),

            CostByLocation = SumBy(
                current,
                e => e.Location,
                e => e.BaseSalary * EffectiveFte(e))
        };
    }

    // =====================================================================
    // DEPARTMENTS
    // =====================================================================

    private static List<HrDepartmentDto> BuildDepartments(
        List<Employee> current,
        List<Employee> all,
        List<LeaveTaken> leaves,
        List<MonthSnapshot> snapshots,
        DateTime start,
        DateTime end,
        int workingDaysPerYear,
        decimal turnoverHealthyThreshold,
        decimal turnoverWatchThreshold,
        decimal absenceHealthyThreshold,
        decimal absenceWatchThreshold)
    {
        var departments = current
            .Select(e =>
                e.Department ?? "Unassigned")
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        return departments
            .Select(
                department =>
                {
                    var active = current
                        .Where(e =>
                            string.Equals(
                                e.Department ??
                                "Unassigned",
                                department,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var allDept = all
                        .Where(e =>
                            string.Equals(
                                e.Department ??
                                "Unassigned",
                                department,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    // IMPORTANT:
                    // Explicit decimal Average to avoid double/decimal
                    // conversion errors.
                    var avgHead = snapshots.Count == 0
                        ? 0m
                        : snapshots.Average(
                            s =>
                                (decimal)s.Active.Count(
                                    e =>
                                        string.Equals(
                                            e.Department ??
                                            "Unassigned",
                                            department,
                                            StringComparison.OrdinalIgnoreCase)));

                    var exits = allDept.Count(e =>
                        e.ExitDate.HasValue &&
                        e.ExitDate.Value >= start &&
                        e.ExitDate.Value <= end);

                    var deptLeave = leaves
                        .Where(l =>
                            string.Equals(
                                l.Function ??
                                l.OrganizationalUnit ??
                                "Unassigned",
                                department,
                                StringComparison.OrdinalIgnoreCase))
                        .Sum(l => l.Days);

                    var unplanned = leaves
                        .Where(l =>
                            string.Equals(
                                l.Function ??
                                l.OrganizationalUnit ??
                                "Unassigned",
                                department,
                                StringComparison.OrdinalIgnoreCase)
                            &&
                            IsUnplanned(
                                l.AttendanceAbsenceType))
                        .Sum(l => l.Days);

                    return new HrDepartmentDto
                    {
                        Name = department,

                        Headcount = active.Count,

                        Fte = Round(
                            active.Sum(
                                e => e.FTE)),

                        LabourCost = Round(
                            active.Sum(
                                e => e.BaseSalary * EffectiveFte(e))),

                        AverageSalary =
                            active.Count == 0
                                ? 0m
                                : Round(
                                    active.Average(
                                        e => e.BaseSalary)),

                        Turnover = Round(
                            SafePercent(
                                exits,
                                avgHead)),

                        FemalePercentage = Round(
                            Percent(
                                active.Count(
                                    IsFemaleByGender),
                                active.Count)),

                        AbsenceRate = Round(
                            SafePercent(
                                unplanned,
                                active.Count *
                                workingDaysPerYear))
                    };
                })
            .ToList();
    }

    // =====================================================================
    // ABSENTEEISM
    // =====================================================================

    private static HrAbsenteeismDto BuildAbsence(
        List<Employee> current,
        List<LeaveTaken> leaves,
        DateTime start,
        DateTime end,
        List<MonthSnapshot> snapshots,
        int workingDaysPerYear,
        decimal absenceHealthyThreshold,
        decimal absenceWatchThreshold)
    {
        var clipped = leaves
            .Select(
                leave => new
                {
                    Leave = leave,

                    Days = OverlapDays(
                        leave.StartDate,
                        leave.EndDate,
                        start,
                        end,
                        leave.Days)
                })
            .Where(x => x.Days > 0)
            .ToList();

        var total = clipped.Sum(
            x => x.Days);

        var unplanned = clipped
            .Where(x =>
                IsUnplanned(
                    x.Leave.AttendanceAbsenceType))
            .Sum(x => x.Days);

        // IMPORTANT:
        // Keep average headcount as decimal.
        var avgHead = snapshots.Count == 0
            ? 0m
            : snapshots.Average(
                x => (decimal)x.Active.Count);

        var departments = current
            .Select(e =>
                e.Department ?? "Unassigned")
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .Select(
                department =>
                {
                    var employees = current
                        .Where(e =>
                            string.Equals(
                                e.Department ??
                                "Unassigned",
                                department,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var leaveRecords = clipped
                        .Where(x =>
                            string.Equals(
                                x.Leave.Function ??
                                x.Leave.OrganizationalUnit ??
                                "Unassigned",
                                department,
                                StringComparison.OrdinalIgnoreCase));

                    var unplannedDays = leaveRecords
                        .Where(x =>
                            IsUnplanned(
                                x.Leave.AttendanceAbsenceType))
                        .Sum(x => x.Days);

                    return new HrDepartmentAbsenceDto
                    {
                        Department = department,

                        Headcount = employees.Count,

                        TotalLeaveDays = Round(
                            leaveRecords.Sum(
                                x => x.Days)),

                        UnplannedLeaveDays = Round(
                            unplannedDays),

                        AbsenceRate = Round(
                            SafePercent(
                                unplannedDays,
                                employees.Count *
                                workingDaysPerYear))
                    };
                })
            .ToList();

        return new HrAbsenteeismDto
        {
            TotalLeaveDays = Round(
                total),

            UnplannedLeaveDays = Round(
                unplanned),

            AbsenceRate = Round(
                SafePercent(
                    unplanned,
                    avgHead *
                    workingDaysPerYear)),

            Status = GetAbsenceStatus(
                SafePercent(
                    unplanned,
                    avgHead *
                    workingDaysPerYear),
                absenceHealthyThreshold,
                absenceWatchThreshold),

            // The source system currently does not have a dedicated
            // sick-days field, so unplanned leave is used here.
            SickDays = Round(
                clipped
                    .Where(x =>
                        IsUnplanned(
                            x.Leave.AttendanceAbsenceType))
                    .Sum(x => x.Days)),

            ByLeaveType = clipped
                .GroupBy(
                    x =>
                        string.IsNullOrWhiteSpace(
                            x.Leave.AttendanceAbsenceType)
                            ? "Other"
                            : x.Leave.AttendanceAbsenceType!.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(
                    g => g.Sum(
                        x => x.Days))
                .Select(
                    g =>
                        new HrBreakdownItemDto
                        {
                            Label = g.Key,

                            Value = Round(
                                g.Sum(
                                    x => x.Days))
                        })
                .ToList(),

            PlannedVsUnplanned =
                new List<HrBreakdownItemDto>
                {
                    new()
                    {
                        Label = "Planned",

                        Value = Round(
                            total - unplanned)
                    },

                    new()
                    {
                        Label = "Unplanned",

                        Value = Round(
                            unplanned)
                    }
                },

            ByLocation = clipped
                .Where(x =>
                    IsUnplanned(
                        x.Leave.AttendanceAbsenceType))
                .GroupBy(
                    x =>
                        string.IsNullOrWhiteSpace(
                            x.Leave.Location)
                            ? "Not specified"
                            : x.Leave.Location!.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(
                    g => g.Sum(
                        x => x.Days))
                .Select(
                    g =>
                        new HrBreakdownItemDto
                        {
                            Label = g.Key,

                            Value = Round(
                                g.Sum(
                                    x => x.Days))
                        })
                .ToList(),

            ByDepartment = departments,

            Trend = snapshots
                .Select(
                    snapshot =>
                        new HrMonthlyAbsenceDto
                        {
                            Month = snapshot.Month,

                            Label = snapshot.Month.ToString(
                                "MMM yy",
                                CultureInfo.InvariantCulture),

                            UnplannedLeaveDays = Round(
                                clipped
                                    .Where(x =>
                                        x.Leave.StartDate.Year ==
                                            snapshot.Month.Year
                                        &&
                                        x.Leave.StartDate.Month ==
                                            snapshot.Month.Month
                                        &&
                                        IsUnplanned(
                                            x.Leave.AttendanceAbsenceType))
                                    .Sum(x => x.Days))
                        })
                .ToList()
        };
    }

    // =====================================================================
    // ATTRITION
    // =====================================================================

    private static HrAttritionDto BuildAttrition(
        List<Employee> all,
        List<MonthSnapshot> snapshots,
        DateTime start,
        DateTime end,
        decimal turnoverHealthyThreshold,
        decimal turnoverWatchThreshold)
    {
        var exits = all
            .Where(e =>
                e.ExitDate.HasValue &&
                e.ExitDate.Value >= start &&
                e.ExitDate.Value <= end)
            .ToList();

        // IMPORTANT:
        // Average headcount is explicitly decimal.
        var avgHead = snapshots.Count == 0
            ? 0m
            : snapshots.Average(
                x => (decimal)x.Active.Count);

        var first = snapshots
            .Take(6)
            .ToList();

        var second = snapshots
            .Skip(6)
            .ToList();

        decimal Half(
            List<MonthSnapshot> months)
        {
            if (months.Count == 0)
                return 0m;

            var average = months.Average(
                x => (decimal)x.Active.Count);

            return SafePercent(
                months.Sum(x => x.Exits),
                average);
        }

        var voluntary = exits.Count(
            e => IsVoluntary(
                e.TerminationReason));

        var trend = snapshots
            .Select(
                x =>
                    new HrMonthlyTurnoverDto
                    {
                        Month = x.Month,

                        Label = x.Month.ToString(
                            "MMM yy",
                            CultureInfo.InvariantCulture),

                        Rate = Round(
                            SafePercent(
                                x.Exits,
                                Math.Max(
                                    1m,
                                    (decimal)x.Active.Count))
                            * 12m)
                    })
            .ToList();

        // -----------------------------------------------------------------
        // Department turnover
        // -----------------------------------------------------------------

        var departmentExits = exits
            .GroupBy(
                e =>
                    e.Department ??
                    "Unassigned",
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(
                g => g.Count())
            .Select(
                group =>
                {
                    // IMPORTANT:
                    // Explicit decimal average.
                    var departmentAverageHeadcount =
                        snapshots.Count == 0
                            ? 0m
                            : snapshots.Average(
                                snapshot =>
                                    (decimal)snapshot.Active.Count(
                                        employee =>
                                            string.Equals(
                                                employee.Department ??
                                                "Unassigned",
                                                group.Key,
                                                StringComparison.OrdinalIgnoreCase)));

                    return new HrBreakdownItemDto
                    {
                        Label = group.Key,

                        Value = Round(
                            SafePercent(
                                group.Count(),
                                Math.Max(
                                    1m,
                                    departmentAverageHeadcount)))
                    };
                })
            .ToList();

        // -----------------------------------------------------------------
        // Exit reasons
        // -----------------------------------------------------------------

        var reasons = exits
            .GroupBy(
                e =>
                    string.IsNullOrWhiteSpace(
                        e.TerminationReason)
                        ? "Not specified"
                        : e.TerminationReason!.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(
                g => g.Count())
            .Take(7)
            .Select(
                g =>
                    new HrBreakdownItemDto
                    {
                        Label = g.Key,
                        Value = g.Count()
                    })
            .ToList();

        // -----------------------------------------------------------------
        // Tenure at exit
        // -----------------------------------------------------------------

        var tenure = new[]
        {
            "<1 yr",
            "1–2",
            "3–5",
            "6+"
        }
        .Select(
            label =>
                new HrBreakdownItemDto
                {
                    Label = label,

                    Value = exits.Count(e =>
                    {
                        var years =
                            (
                                e.ExitDate!.Value -
                                e.HireDate
                            ).TotalDays / 365.25;

                        return label switch
                        {
                            "<1 yr" =>
                                years < 1,

                            "1–2" =>
                                years >= 1 &&
                                years < 3,

                            "3–5" =>
                                years >= 3 &&
                                years < 6,

                            _ =>
                                years >= 6
                        };
                    })
                })
        .ToList();

        var avgExitTenure = exits.Count == 0
            ? 0m
            : (decimal)exits.Average(
                e =>
                    (
                        e.ExitDate!.Value -
                        e.HireDate
                    ).TotalDays / 365.25);

        return new HrAttritionDto
        {
            TurnoverRate = Round(
                SafePercent(
                    exits.Count,
                    avgHead)),

            TurnoverStatus = GetTurnoverStatus(
                SafePercent(exits.Count, avgHead),
                turnoverHealthyThreshold,
                turnoverWatchThreshold),

            VoluntaryExitPercentage = Round(
                Percent(
                    voluntary,
                    exits.Count)),

            AverageTenureAtExitYears = Round(
                avgExitTenure),

            FirstHalfTurnover = Round(
                Half(first)),

            SecondHalfTurnover = Round(
                Half(second)),

            TurnoverDelta = Round(
                Half(second) -
                Half(first)),

            Trend = trend,

            ByDepartment = departmentExits,

            ExitReasons = reasons,

            TenureAtExit = tenure
        };
    }

    // =====================================================================
    // ATTRITION RISK WATCHLIST
    // =====================================================================

    private static List<HrEmployeeSearchDto> BuildRiskWatchlist(
        List<Employee> current,
        List<HrEngagementScore> engagement,
        List<HrDepartmentDto> departments,
        DateTime end,
        decimal turnoverHealthyThreshold,
        decimal turnoverWatchThreshold)
    {
        var latest = engagement
            .GroupBy(
                x => x.EmployeeId,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                    g.OrderByDescending(
                        x => x.SurveyDate)
                    .First()
                    .Score,
                StringComparer.OrdinalIgnoreCase);

        var deptTurn = departments
            .ToDictionary(
                x => x.Name,
                x => x.Turnover,
                StringComparer.OrdinalIgnoreCase);

        return current
            .Select(
                employee =>
                {
                    if (!latest.TryGetValue(
                        employee.EmployeeId ?? string.Empty,
                        out var engagementScore))
                    {
                        return null;
                    }

                    var departmentTurnover =
                        deptTurn.GetValueOrDefault(
                            employee.Department ??
                            "Unassigned");

                    var tenureYears =
                        (
                            end -
                            employee.HireDate
                        ).TotalDays / 365.25;

                    var score =
                        (engagementScore < 55m
                            ? 2
                            : engagementScore < 65m
                                ? 1
                                : 0)
                        +
                        (departmentTurnover > turnoverWatchThreshold
                            ? 2
                            : departmentTurnover > turnoverHealthyThreshold
                                ? 1
                                : 0)
                        +
                        (tenureYears < 1.5
                            ? 1
                            : 0);

                    var risk =
                        score >= 4
                            ? "High"
                            : score >= 2
                                ? "Medium"
                                : "Low";

                    return new HrEmployeeSearchDto
                    {
                        EmployeeId =
                            employee.EmployeeId
                            ?? string.Empty,

                        Name =
                            employee.EmployeeName
                            ?? employee.EmployeeId
                            ?? "Unknown",

                        Department =
                            employee.Department,

                        Position =
                            employee.PositionTitle,

                        // Do not expose employee email.
                        Email = null,

                        Engagement =
                            Round(engagementScore),

                        TenureYears =
                            Round(
                                (decimal)tenureYears),

                        Risk = risk,

                        RiskScore = score
                    };
                })
            .Where(x =>
                x != null &&
                x.Risk != "Low")
            .OrderByDescending(
                x => x!.RiskScore)
            .ThenBy(
                x => x!.Engagement)
            .Take(8)
            .Select(
                x => x!)
            .ToList();
    }

    // =====================================================================
    // RECENT ACTIVITY
    // =====================================================================

    private static List<HrActivityDto> BuildActivity(
        List<Employee> employees,
        DateTime start,
        DateTime end)
    {
        return employees
            .SelectMany(
                employee =>
                    new[]
                    {
                        employee.HireDate >= start &&
                        employee.HireDate <= end

                            ? new HrActivityDto
                            {
                                Type = "hire",

                                Date =
                                    employee.HireDate,

                                Department =
                                    employee.Department
                                    ?? "Unassigned",

                                Detail = ""
                            }

                            : null,

                        employee.ExitDate.HasValue &&
                        employee.ExitDate.Value >= start &&
                        employee.ExitDate.Value <= end

                            ? new HrActivityDto
                            {
                                Type = "exit",

                                Date =
                                    employee.ExitDate.Value,

                                Department =
                                    employee.Department
                                    ?? "Unassigned",

                                Detail =
                                    string.IsNullOrWhiteSpace(
                                        employee.TerminationReason)
                                        ? ""
                                        : employee.TerminationReason!
                            }

                            : null
                    }
                    .Where(x => x != null)
                    .Select(x => x!))
            .OrderByDescending(
                x => x.Date)
            .Take(12)
            .ToList();
    }

    // =====================================================================
    // LEAVE OVERLAP
    // =====================================================================

    private static decimal OverlapDays(
        DateTime start,
        DateTime end,
        DateTime windowStart,
        DateTime windowEnd,
        decimal recordedDays)
    {
        var overlapStart =
            start.Date > windowStart.Date
                ? start.Date
                : windowStart.Date;

        var overlapEnd =
            end.Date < windowEnd.Date
                ? end.Date
                : windowEnd.Date;

        if (overlapEnd < overlapStart)
            return 0m;

        var calendarDays =
            (overlapEnd - overlapStart).Days + 1;

        var fullDays =
            (end.Date - start.Date).Days + 1;

        return fullDays <= 0
            ? 0m
            : recordedDays *
              calendarDays /
              fullDays;
    }

    // =====================================================================
    // BUSINESS RULES
    // =====================================================================

    private static bool IsUnplanned(
        string? type)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            type ?? "",
            "sick|personal|carer|compassion",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool IsVoluntary(
        string? reason)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            reason ?? "",
            "resign|retire|voluntary",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool IsOpenRole(
        HrOpenRole role)
    {
        return !IsClosedStatus(
            role.Status);
    }

    private static bool IsClosedStatus(
        string? status)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            status ?? "",
            "hired|filled|closed|cancel|withdraw",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool IsFemaleByGender(
        Employee employee)
    {
        return IsFemale(
            employee.Gender);
    }

    // =====================================================================
    // EXCEL HELPERS
    // =====================================================================

    private static List<Dictionary<string, object?>> GetRows(
        ExcelPackage package,
        params string[] aliases)
    {
        var sheet =
            package.Workbook.Worksheets
                .FirstOrDefault(
                    ws =>
                        aliases.Any(
                            alias =>
                                Normalize(ws.Name) ==
                                Normalize(alias)))
            ??
            package.Workbook.Worksheets
                .FirstOrDefault(
                    ws =>
                        aliases.Any(
                            alias =>
                                Normalize(ws.Name)
                                    .Contains(
                                        Normalize(alias))));

        if (sheet?.Dimension == null)
            return new();

        var headers =
            Enumerable
                .Range(
                    1,
                    sheet.Dimension.Columns)
                .Select(
                    column =>
                        Normalize(
                            sheet.Cells[
                                1,
                                column].Text))
                .ToArray();

        var rows =
            new List<Dictionary<string, object?>>();

        for (
            var rowNumber = 2;
            rowNumber <= sheet.Dimension.Rows;
            rowNumber++)
        {
            var dictionary =
                new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

            var nonEmpty = false;

            for (
                var column = 1;
                column <= sheet.Dimension.Columns;
                column++)
            {
                var value =
                    sheet.Cells[
                        rowNumber,
                        column].Value;

                if (value != null &&
                    !string.IsNullOrWhiteSpace(
                        value.ToString()))
                {
                    nonEmpty = true;
                }

                dictionary[
                    headers[column - 1]] =
                    value;
            }

            if (nonEmpty)
                rows.Add(dictionary);
        }

        return rows;
    }

    private static string Normalize(
        string value)
    {
        return new string(
                value.Where(
                    char.IsLetterOrDigit)
                    .ToArray())
            .ToLowerInvariant();
    }

    private static object? Get(
        Dictionary<string, object?> row,
        params string[] aliases)
    {
        var keys = aliases
            .Select(Normalize)
            .ToList();

        var key = row.Keys
            .FirstOrDefault(
                existingKey =>
                    keys.Any(
                        alias =>
                            existingKey == alias ||
                            existingKey.Contains(alias)));

        return key == null
            ? null
            : row[key];
    }

    private static string? Text(
        Dictionary<string, object?> row,
        params string[] aliases)
    {
        return Get(
                row,
                aliases)
            ?.ToString()
            ?.Trim();
    }

    private static decimal Decimal(
        Dictionary<string, object?> row,
        params string[] aliases)
    {
        var value = Get(
            row,
            aliases);

        if (value is double doubleValue)
            return (decimal)doubleValue;

        if (value is float floatValue)
            return (decimal)floatValue;

        if (value is decimal decimalValue)
            return decimalValue;

        if (value is int intValue)
            return intValue;

        if (value is long longValue)
            return longValue;

        var text = value?
            .ToString()?
            .Replace(",", "")
            .Replace("$", "")
            .Replace("%", "");

        if (decimal.TryParse(
            text,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result))
        {
            return result;
        }

        return 0m;
    }

    private static DateTime? Date(
        Dictionary<string, object?> row,
        params string[] aliases)
    {
        var value = Get(
            row,
            aliases);

        if (value is DateTime dateTime)
            return dateTime.Date;

        if (value is double oa &&
            oa > 0)
        {
            try
            {
                return DateTime
                    .FromOADate(oa)
                    .Date;
            }
            catch
            {
                // Ignore invalid Excel serial date.
            }
        }

        if (value is decimal decimalOa &&
            decimalOa > 0)
        {
            try
            {
                return DateTime
                    .FromOADate((double)decimalOa)
                    .Date;
            }
            catch
            {
                // Ignore invalid Excel serial date.
            }
        }

        if (DateTime.TryParse(
            value?.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed))
        {
            return parsed.Date;
        }

        return null;
    }

    private static string SanitizeFileName(
        string name)
    {
        return Path
            .GetFileName(name)
            .Replace(
                "\"",
                "",
                StringComparison.Ordinal)
            .Replace(
                "\r",
                "")
            .Replace(
                "\n",
                "");
    }
}