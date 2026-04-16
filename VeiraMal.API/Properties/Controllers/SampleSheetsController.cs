using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using VeiraMal.API.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace VeiraMal.API.Controllers
{
    [ApiController]
    [Route("api/admin/samples")]
    public class SampleSheetsController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<SampleSheetsController> _logger;
        private readonly long MAX_BYTES = 8 * 1024 * 1024; // 8 MB limit

        // Allowed logical sheet names and canonical file names (lowercase keys)
        private static readonly (string key, string display)[] AllowedSheets = new[]
        {
            ("headcounts", "Headcounts"),
            ("nhts", "NHTs"),
            ("terms", "Terms"),
            ("baserates", "Base Rates"),
            ("sapleavebalance", "SAP Leave Balance"),
            ("leavetaken", "Leave Taken")
        };

        public SampleSheetsController(IWebHostEnvironment env, ILogger<SampleSheetsController> logger)
        {
            _env = env;
            _logger = logger;
        }

        private string SamplesFolderPath => Path.Combine(_env.WebRootPath ?? "wwwroot", "samples");

        private void EnsureFolder()
        {
            if (!Directory.Exists(SamplesFolderPath))
                Directory.CreateDirectory(SamplesFolderPath);
        }

        private bool IsValidSheetKey(string key) =>
            !string.IsNullOrWhiteSpace(key) && AllowedSheets.Any(s => s.key == key.ToLowerInvariant());

        private string SanitizeExtension(string fileName)
        {
            var ext = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
            if (ext == ".csv" || ext == ".xlsx" || ext == ".xls") return ext;
            return "";
        }

        private string CanonicalFileName(string sheetKey, string ext)
        {
            // Example: headcounts.csv  or headcounts.xlsx
            return $"{sheetKey}{ext}";
        }

        /// <summary>
        /// Returns metadata for all sample sheets (whether present or not).
        /// </summary>
        [HttpGet]
        public IActionResult List()
        {
            EnsureFolder();

            var result = AllowedSheets.Select(s =>
            {
                var key = s.key;
                var display = s.display;

                // find existing file for this key (.csv prefer then .xlsx)
                var found = Directory.EnumerateFiles(SamplesFolderPath, $"{key}.*", SearchOption.TopDirectoryOnly)
                                     .Select(f => new FileInfo(f))
                                     .OrderByDescending(fi => fi.LastWriteTimeUtc)
                                     .FirstOrDefault();

                return new SampleSheetDto
                {
                    Key = key,
                    DisplayName = display,
                    Exists = found != null,
                    FileName = found?.Name,
                    Size = found?.Length ?? 0,
                    ContentType = found != null ? GetContentTypeFromExtension(found.Extension) : null,
                    UpdatedAtUtc = found?.LastWriteTimeUtc
                };
            }).ToList();

            return Ok(result);
        }

        /// <summary>
        /// Download a sample sheet by key (headcounts, nhts, terms, baserates, sapleavebalance, leavetaken).
        /// </summary>
        [HttpGet("{key}/download")]
        public IActionResult Download([FromRoute] string key)
        {
            if (!IsValidSheetKey(key))
                return BadRequest(new { message = "Invalid sample sheet key." });

            EnsureFolder();

            var file = Directory.EnumerateFiles(SamplesFolderPath, $"{key}.*", SearchOption.TopDirectoryOnly)
                        .Select(p => new FileInfo(p))
                        .OrderByDescending(fi => fi.LastWriteTimeUtc)
                        .FirstOrDefault();

            if (file == null) return NotFound(new { message = "Sample file not found." });

            var ct = GetContentTypeFromExtension(file.Extension);
            var fs = System.IO.File.OpenRead(file.FullName);
            return File(fs, ct, file.Name);
        }

        /// <summary>
        /// Upload or replace a sample sheet. Only superAdmin allowed.
        /// POST form-data: key=headcounts (string), file=@file
        /// </summary>
        //[Authorize]
        [HttpPost("upload")]
        public async Task<IActionResult> Upload()
        {
            // Authorization: require JWT claim "access" == "superAdmin"
            //var access = User.FindFirst("access")?.Value;
            //if (!string.Equals(access, "superAdmin", StringComparison.OrdinalIgnoreCase))
            //    return Forbid();

            if (!Request.HasFormContentType)
                return BadRequest(new { message = "Invalid request type (expecting form-data)." });

            var form = await Request.ReadFormAsync();
            var key = form["key"].FirstOrDefault()?.Trim().ToLowerInvariant();
            var file = form.Files.FirstOrDefault();

            if (!IsValidSheetKey(key))
                return BadRequest(new { message = "Invalid or missing 'key' (allowed: headcounts, nhts, terms, baserates, sapleavebalance, leavetaken)." });

            if (file == null)
                return BadRequest(new { message = "File is required (multipart/form-data)." });

            if (file.Length == 0)
                return BadRequest(new { message = "File is empty." });

            if (file.Length > MAX_BYTES)
                return BadRequest(new { message = $"File too large (max {MAX_BYTES / (1024 * 1024)} MB)." });

            var ext = SanitizeExtension(file.FileName);
            if (string.IsNullOrEmpty(ext))
                return BadRequest(new { message = "Invalid file type. Allowed: .csv, .xlsx, .xls" });

            EnsureFolder();

            // Save to canonical filename (overwrite)
            var destFileName = CanonicalFileName(key, ext);
            var destPath = Path.Combine(SamplesFolderPath, destFileName);

            // Remove any existing variants for the key (e.g., headcounts.csv and headcounts.xlsx)
            var existing = Directory.EnumerateFiles(SamplesFolderPath, $"{key}.*", SearchOption.TopDirectoryOnly).ToList();
            foreach (var e in existing)
            {
                try { System.IO.File.Delete(e); } catch { /* ignore */ }
            }

            // Save uploaded file
            using (var stream = new FileStream(destPath, FileMode.CreateNew))
            {
                await file.CopyToAsync(stream);
            }

            _logger.LogInformation("Sample sheet uploaded: {File} by {User}", destFileName, User.Identity?.Name ?? User.FindFirst("userId")?.Value);

            // Return metadata
            var fi = new FileInfo(destPath);
            var dto = new SampleSheetDto
            {
                Key = key,
                DisplayName = AllowedSheets.First(a => a.key == key).display,
                Exists = true,
                FileName = fi.Name,
                Size = fi.Length,
                ContentType = GetContentTypeFromExtension(fi.Extension),
                UpdatedAtUtc = fi.LastWriteTimeUtc
            };

            return Ok(dto);
        }

        private string GetContentTypeFromExtension(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            return ext switch
            {
                ".csv" => "text/csv",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xls" => "application/vnd.ms-excel",
                _ => "application/octet-stream"
            };
        }
    }

    // DTO returned to the client
    public class SampleSheetDto
    {
        public string Key { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public bool Exists { get; set; }
        public string? FileName { get; set; }
        public long Size { get; set; }
        public string? ContentType { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }
}