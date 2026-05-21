using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using OfficeOpenXml;
using Stripe;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using VeiraMal.API;
using VeiraMal.API.Models;
using VeiraMal.API.Services;
using VeiraMal.API.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Collections.Generic;

ExcelPackage.License.SetNonCommercialPersonal("Your Name");

var builder = WebApplication.CreateBuilder(args);

// ===== EARLY DIAGNOSTICS =====
Console.WriteLine("🚀 APPLICATION STARTING - CONFIGURATION CHECK");
Console.WriteLine("Checking connection string...");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine("❌ CONNECTION STRING: NOT FOUND");
}
else
{
    Console.WriteLine("✅ CONNECTION STRING: FOUND");
    // Show first part of connection string (without password)
    var safeString = connectionString.Split(';')
        .Where(part => !part.ToLower().Contains("password"))
        .Take(3)
        .ToArray();
    Console.WriteLine($"Connection details: {string.Join("; ", safeString)}...");
}

// --------------------- Your Existing Services ---------------------
builder.Services.AddScoped<IHeadcountService, HeadcountService>();
builder.Services.AddScoped<INHTService, NHTService>();
builder.Services.AddScoped<ITermsService, TermsService>();

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<CompanyService>();
builder.Services.AddScoped<ISubscriptionService, VeiraMal.API.Services.SubscriptionService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<ISubCompanyResolver, SubCompanyResolver>();
builder.Services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();

builder.Services.AddScoped<ILeaveBalanceService, LeaveBalanceService>();
builder.Services.AddScoped<ILeaveTakenService, LeaveTakenService>();
builder.Services.AddScoped<IBaseRatesService, BaseRatesService>();
builder.Services.AddScoped<ILiabilityService, LiabilityService>();

builder.Services.AddDataProtection();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.Configure<AbnLookupOptions>(
    builder.Configuration.GetSection("AbnLookup"));

builder.Services.AddHttpClient<IAbnLookupService, AbnLookupService>();

// Register PasswordValidator as a service
builder.Services.AddScoped<PasswordValidator>();

StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// --------------------- Controllers / Swagger ---------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --------------------- Database Configuration ---------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptions =>
        {
            sqlServerOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            sqlServerOptions.CommandTimeout(180);
        }
    ));

// --------------------- JWT Configuration ---------------------
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"] ?? throw new Exception("JWT Key is missing");
var issuer = jwtSettings["Issuer"] ?? throw new Exception("Issuer is missing");
var audience = jwtSettings["Audience"] ?? throw new Exception("Audience is missing");

// --------------------- Authentication: Cookies + JWT Bearer ---------------------
// We'll register both cookie auth (used for impersonation sign-in) and JWT bearer (used for API auth).
builder.Services.AddAuthentication(options =>
{
    // Keep JWT as the default authenticate/challenge scheme for API endpoints
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

    // Use cookie scheme for sign-in actions (SignInAsync will use this scheme)
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = "veiramal_auth";
    options.Cookie.HttpOnly = true;

    // DEV ONLY: allow insecure cookies on localhost for development.
    // In production: set SameSite=None and SecurePolicy = Always and ensure HTTPS.
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.None;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            try
            {
                var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                if (!string.IsNullOrWhiteSpace(jti))
                {
                    var blacklist = context.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();
                    var revoked = await blacklist.IsTokenRevokedAsync(jti);
                    if (revoked)
                    {
                        context.Fail("Token revoked.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                context.Fail("Token validation failed: " + ex.Message);
            }
        },
        OnAuthenticationFailed = context =>
        {
#if DEBUG
            var logger = context.HttpContext.RequestServices.GetService<ILogger<Program>>();
            logger?.LogWarning("Authentication failed: {Message}", context.Exception?.Message);
#endif
            return Task.CompletedTask;
        }
    };
});

// --------------------- CORS ---------------------
// IMPORTANT: AllowCredentials() is required when the client uses withCredentials
// and the server will set cookies. Use a specific origin (no wildcard) when allowing credentials.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "https://dev.hranalytix.com"
            )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // <--- critical for cookie flow
    });
});

var app = builder.Build();

// resolve IWebHostEnvironment from DI
var env = app.Services.GetRequiredService<IWebHostEnvironment>();

// ensure uploads folder exists under wwwroot
var uploadsRoot = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
if (!Directory.Exists(uploadsRoot))
{
    Directory.CreateDirectory(uploadsRoot);
}

// serve wwwroot normally
app.UseStaticFiles();

// serve uploads specifically with caching
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=86400";
    }
});

// ===== DATABASE DIAGNOSTICS & SEEDING =====
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("🔍 STARTING DATABASE DIAGNOSTICS");

    try
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Test Connection
        logger.LogInformation("Testing database connection...");
        var canConnect = await context.Database.CanConnectAsync();

        if (canConnect)
        {
            logger.LogInformation("✅ DATABASE CONNECTION: SUCCESS");

            // Check Migrations
            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync();

            logger.LogInformation("Applied migrations: {Count}", appliedMigrations.Count());
            logger.LogInformation("Pending migrations: {Count}", pendingMigrations.Count());

            // Apply Pending Migrations
            if (pendingMigrations.Any())
            {
                logger.LogInformation("🔄 Applying {Count} pending migrations...", pendingMigrations.Count());
                await context.Database.MigrateAsync();
                logger.LogInformation("✅ MIGRATIONS APPLIED SUCCESSFULLY");
            }
            else
            {
                logger.LogInformation("✅ DATABASE IS UP TO DATE");
            }

            // List Tables (Optional)
            try
            {
                var tables = await context.Database.SqlQueryRaw<string>(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'").ToListAsync();
                logger.LogInformation("📊 Database tables: {TableCount}", tables.Count);
                foreach (var table in tables.Take(10)) // Show first 10 tables
                {
                    logger.LogInformation("   - {Table}", table);
                }
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not list tables: {Message}", tableEx.Message);
            }

            // ------------------- SuperAdmin seeding -------------------
            try
            {
                // Only seed if no user with AccessLevel 'superAdmin' exists
                var hasSuperAdmin = await context.Users.AnyAsync(u => u.AccessLevel != null && u.AccessLevel.ToLower() == "superadmin");
                if (!hasSuperAdmin)
                {
                    logger.LogInformation("No superAdmin found. Creating default company + superAdmin user...");

                    // Read optional seed settings from configuration (appsettings.json)
                    var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var seedEmail = cfg["Seed:SuperAdminEmail"] ?? "admin@hranalytix.local";
                    var seedFirstName = cfg["Seed:SuperAdminFirstName"] ?? "Super";
                    var seedLastName = cfg["Seed:SuperAdminLastName"] ?? "Admin";
                    var seedCompanyName = cfg["Seed:CompanyName"] ?? "VeiraMal Admin";

                    // Create company
                    var company = new Company
                    {
                        CompanyId = Guid.NewGuid(),
                        CompanyName = seedCompanyName,
                        CompanyABN = null,
                        ContactNumber = null,
                        Location = null,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Companies.AddAsync(company);
                    await context.SaveChangesAsync(); // ensure company persisted for FK

                    // Build user with AccessLevel = "superAdmin"
                    var user = new User
                    {
                        CompanyId = company.CompanyId,
                        EmployeeNumber = 1,
                        FirstName = seedFirstName,
                        LastName = seedLastName,
                        Email = seedEmail,
                        BusinessUnit = "Management",
                        AccessLevel = "superAdmin", // IMPORTANT: superAdmin (not superUser)
                        IsPasswordResetRequired = true,
                        IsActive = true,
                        IsFirstLogin = true,
                        ContactNumber = null,
                        Location = null,
                        CreatedAt = DateTime.UtcNow
                    };

                    // use IUserService to generate password and hash
                    var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
                    var tempPassword = await userService.GenerateTemporaryPasswordAsync();
                    await userService.SetPasswordHashAsync(user, tempPassword);

                    // add user and save
                    await context.Users.AddAsync(user);
                    await context.SaveChangesAsync();

                    // Try to send email with temp password
                    var emailService = scope.ServiceProvider.GetService<IEmailService>();
                    if (emailService != null)
                    {
                        try
                        {
                            var subject = "Your SuperAdmin account for " + seedCompanyName;
                            var signinLink = cfg["ClientApp:BaseUrl"] ?? "http://localhost:3000";
                            var body = $@"
                                <div style='font-family: Arial, sans-serif;max-width:600px;margin:0 auto;padding:16px;'>
                                    <h3>Welcome {System.Net.WebUtility.HtmlEncode(seedFirstName)}</h3>
                                    <p>Your SuperAdmin account has been created for <strong>{System.Net.WebUtility.HtmlEncode(seedCompanyName)}</strong>.</p>
                                    <p><strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(seedEmail)}</p>
                                    <p><strong>Temporary password:</strong> <code style='font-family:monospace;background:#f4f4f4;padding:6px;border-radius:4px;'>{System.Net.WebUtility.HtmlEncode(tempPassword)}</code></p>
                                    <p>Please sign in and change your password immediately.</p>
                                    <p>Sign in: <a href='{signinLink}'>{signinLink}</a></p>
                                </div>";
                            await emailService.SendEmailAsync(seedEmail, subject, body);
                            logger.LogInformation("Seed SuperAdmin email sent to {Email}", seedEmail);
                        }
                        catch (Exception emailEx)
                        {
                            logger.LogWarning(emailEx, "Failed to send seed SuperAdmin email. Temporary password will be printed in logs.");
                            logger.LogWarning("Seed SuperAdmin temp password for {Email}: {Temp}", seedEmail, tempPassword);
                        }
                    }
                    else
                    {
                        // No email service registered — print to logs (dev only)
                        logger.LogWarning("IEmailService not available; printing seed SuperAdmin temporary password to logs (dev only).");
                        logger.LogWarning("Seed SuperAdmin temp password for {Email}: {Temp}", seedEmail, tempPassword);
                    }

                    logger.LogInformation("✅ Default company and superAdmin user created. Email: {Email}", seedEmail);
                }
                else
                {
                    logger.LogInformation("SuperAdmin user already exists; skipping seeding.");
                }
            }
            catch (Exception seedEx)
            {
                logger.LogError(seedEx, "Failed to seed default superAdmin/company.");
            }
            // ---------------------------------------------------------
        }
        else
        {
            logger.LogError("❌ DATABASE CONNECTION: FAILED");
            logger.LogError("Please check:");
            logger.LogError("1. Connection string in Azure Configuration");
            logger.LogError("2. SQL Server firewall settings");
            logger.LogError("3. Database exists and user has permissions");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "💥 DATABASE DIAGNOSTICS FAILED");
        logger.LogError("Error: {Message}", ex.Message);

        if (ex.InnerException != null)
        {
            logger.LogError("Inner Error: {InnerMessage}", ex.InnerException.Message);
        }

        // Specific error guidance
        if (ex.Message.Contains("Login failed", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("🔑 SOLUTION: Check SQL username and password in connection string");
        }
        else if (ex.Message.Contains("Cannot open database", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("🗄️ SOLUTION: Database 'veiraMalDB' might not exist");
        }
        else if (ex.Message.Contains("firewall", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("🔥 SOLUTION: Enable 'Allow Azure services' in SQL Server firewall");
        }
    }

    logger.LogInformation("🏁 DATABASE DIAGNOSTICS COMPLETE");
    logger.LogInformation("🎯 APPLICATION STARTED SUCCESSFULLY");
}

// --------------------- Pipeline Configuration ---------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

// IMPORTANT: CORS must be enabled BEFORE authentication so preflight responses contain required headers
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();