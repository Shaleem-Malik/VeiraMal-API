using OfficeOpenXml;
using VeiraMal.API;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using VeiraMal.API.Services.Interfaces;
using VeiraMal.API.Services;
using VeiraMal.API.Models;      // for User model
using System.IdentityModel.Tokens.Jwt; // for JwtRegisteredClaimNames

ExcelPackage.License.SetNonCommercialPersonal("Your Name");

var builder = WebApplication.CreateBuilder(args);

// --------------------- Existing domain services ---------------------
builder.Services.AddScoped<IHeadcountService, HeadcountService>();
builder.Services.AddScoped<INHTService, NHTService>();
builder.Services.AddScoped<ITermsService, TermsService>();

// --------------------- Add onboarding/auth services ---------------------
// Register password hasher for User
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
// Email service (SMTP) - implementation placed under VeiraMal.API.Services.EmailService
builder.Services.AddScoped<IEmailService, EmailService>();
// User and Company services implementing onboarding/login/reset logic
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

builder.Services.AddScoped<IUserManagementService, UserManagementService>();

builder.Services.AddScoped<ISubCompanyResolver, SubCompanyResolver>();

// --------------------- Token blacklist (logout) service ---------------------
// Note: ensure TokenBlacklistService and RevokedToken model are implemented (see recommended change list)
builder.Services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();

// --------------------- Read JWT config from appsettings.json ---------------------
var jwtSettings = builder.Configuration.GetSection("Jwt");

var jwtKey = jwtSettings["Key"] ?? throw new Exception("JWT Key is missing");
var issuer = jwtSettings["Issuer"] ?? throw new Exception("Issuer is missing");
var audience = jwtSettings["Audience"] ?? throw new Exception("Audience is missing");

// --------------------- Add Services ---------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --------------------- Add DbContext ---------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptions =>
        {
            sqlServerOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            sqlServerOptions.CommandTimeout(180); // 3 minutes for command timeout
        }
    ));

// --------------------- Add JWT Authentication (with revoked-token check) ---------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // set true in prod if using https
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1) // tighten clock skew if desired
        };

        // When a token has been validated by the standard checks, verify it is not revoked
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                try
                {
                    // get jti if present
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

                    // If no jti claim is present we allow token (backwards compatible),
                    // but you may choose to reject tokens missing jti by uncommenting:
                    // if (string.IsNullOrWhiteSpace(jti)) context.Fail("Token missing jti.");
                }
                catch (Exception ex)
                {
                    context.Fail("Token validation failed: " + ex.Message);
                }
            },
            OnAuthenticationFailed = context =>
            {
                // optional: useful for debugging during development
#if DEBUG
                var logger = context.HttpContext.RequestServices.GetService<ILogger<Program>>();
                logger?.LogWarning("Authentication failed: {Message}", context.Exception?.Message);
#endif
                return Task.CompletedTask;
            }
        };
    });

// --------------------- Add CORS ---------------------
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

// --------------------- Swagger in dev ---------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

app.UseRouting();

// --------------------- Apply CORS before authentication ---------------------
app.UseCors("AllowLocalhost3000");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
