using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using ShipmentTracking.Application.Common.Behaviours;
using ShipmentTracking.Application.Common.Interfaces;
using ShipmentTracking.Infrastructure.Persistence;
using ShipmentTracking.Infrastructure.Repositories;
using ShipmentTracking.Infrastructure.Services.Auth;
using ShipmentTracking.Infrastructure.Services.Azure;
using ShipmentTracking.WebAPI.Middleware;
using System.Reflection;
using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Identity;
using ShipmentTracking.Infrastructure.Services.Caching;
using ShipmentTracking.Infrastructure.Services.Messaging;
using ShipmentTracking.Infrastructure.Services.Storage;
using ShipmentTracking.WebAPI.BackgroundServices;
using ShipmentTracking.WebAPI.Observability;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());

// ── Serilog ──────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// ── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

// Redis is optional for local development; production should supply Redis__ConnectionString.
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (string.IsNullOrWhiteSpace(redisConnection))
    builder.Services.AddDistributedMemoryCache();
else
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
builder.Services.AddSingleton<IShipmentCache, RedisShipmentCache>();

// ── MediatR + Pipeline behaviours ────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(
        typeof(ShipmentTracking.Application.Features.Shipments.Commands
            .CreateShipment.CreateShipmentCommand).Assembly));

builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));

// ── FluentValidation ─────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssembly(
    typeof(ShipmentTracking.Application.Features.Shipments.Commands
        .CreateShipment.CreateShipmentValidator).Assembly);

// ── Repositories ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IShipmentRepository, ShipmentRepository>();
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();

// ── Azure Services ───────────────────────────────────────────────────────────
if (string.IsNullOrWhiteSpace(builder.Configuration["Azure:BlobStorage:ConnectionString"]))
    builder.Services.AddSingleton<IBlobStorageService, LocalBlobStorageService>();
else
    builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
builder.Services.AddScoped<IShipmentReleasePolicy, ShipmentReleasePolicy>();
builder.Services.AddHostedService<ShipmentEventsWorker>();

// ── Auth ─────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton<JwtTokenService>();

var jwtSecret = builder.Configuration["JwtSettings:Secret"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization(options =>
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(context => !string.IsNullOrWhiteSpace(context.User.FindFirst("customer_id")?.Value))
        .Build());
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("api", limiter =>
{
    limiter.PermitLimit = 120;
    limiter.Window = TimeSpan.FromMinutes(1);
    limiter.QueueLimit = 0;
}));

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Shipment Tracking Platform API",
        Version = "v1",
        Description = "REST API voor het beheren en opvolgen van logistieke zendingen."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Formaat: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
});

builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new ShipmentTracking.WebAPI.Security.ExplicitOffsetConverter()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("sql");

// Application Insights exports traces, dependency telemetry and request metrics when configured.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics => metrics.AddMeter(Telemetry.Meter.Name))
        .UseAzureMonitor();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseSerilogRequestLogging();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Shipment Tracking API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseDefaultFiles();
app.UseStaticFiles();
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Authorization must run before replaying any saved response.
app.UseMiddleware<IdempotencyMiddleware>();
app.MapControllers().RequireRateLimiting("api");
app.MapHealthChecks("/health");
app.MapHealthChecks("/ready");

// ── Auto-migratie bij startup (dev only, niet in-memory) ─────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (dbContext.Database.IsRelational())
    {
        // The repository starts clean locally; production deployments run EF migrations explicitly.
        if (dbContext.Database.GetMigrations().Any())
            await dbContext.Database.MigrateAsync();
        else
            await dbContext.Database.EnsureCreatedAsync();
    }

}

// Roles are application data, not a development-only concern. Migrations are run separately in production.
using (var roleScope = app.Services.CreateScope())
{
    var roleManager = roleScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Customer", "Dispatcher", "Driver", "Administrator" })
        if (!await roleManager.RoleExistsAsync(role)) await roleManager.CreateAsync(new IdentityRole(role));
}

await ShipmentTracking.WebAPI.Demo.PackageDemoSeeder.SeedAsync(app);

await app.RunAsync();

// Testbaar maken via WebApplicationFactory
public partial class Program { }
