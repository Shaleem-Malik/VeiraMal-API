using OfficeOpenXml;
using VeiraMal.API;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using VeiraMal.API.Services.Interfaces;
using VeiraMal.API.Services;
using VeiraMal.API.Models;
using System.IdentityModel.Tokens.Jwt;

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
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<ISubCompanyResolver, SubCompanyResolver>();
builder.Services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();

// --------------------- JWT Configuration ---------------------
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"] ?? throw new Exception("JWT Key is missing");
var issuer = jwtSettings["Issuer"] ?? throw new Exception("Issuer is missing");
var audience = jwtSettings["Audience"] ?? throw new Exception("Audience is missing");

// --------------------- Core Services ---------------------
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

// --------------------- JWT Authentication ---------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost3000", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ===== DATABASE DIAGNOSTICS =====
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
app.UseCors("AllowLocalhost3000");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
