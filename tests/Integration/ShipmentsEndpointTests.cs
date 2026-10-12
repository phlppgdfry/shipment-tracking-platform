using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShipmentTracking.Application.Features.Shipments.Commands.CreateShipment;
using ShipmentTracking.Application.Features.Shipments.Queries.GetShipments;
using ShipmentTracking.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;

namespace ShipmentTracking.Tests.Integration;

// Vervangt JWT-authenticatie door een handler die altijd slaagt
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey("X-Test-Anonymous")) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Test-User"].FirstOrDefault() ?? "test-user-id"),
            new Claim("customer_id", Request.Headers["X-Test-Customer"].FirstOrDefault() ?? "test-customer"),
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Role, Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Dispatcher"),
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// Vervangt de echte database door een in-memory versie
public sealed class ShipmentTrackingFactory : WebApplicationFactory<Program>
{
    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();
    private readonly string _databaseName = $"TestDb_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove all production registrations before selecting the in-memory provider.
            // EF Core 10 registers IDbContextOptionsConfiguration separately.
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll(typeof(ApplicationDbContext));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<ApplicationDbContext>));

            // Voeg in-memory database toe
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName, DatabaseRoot));

            // Tests must never attempt the developer-machine Redis endpoint.
            services.RemoveAll(typeof(IDistributedCache));
            services.AddDistributedMemoryCache();

            // Zorg dat de DB aangemaakt is
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();

            // Vervang JWT-auth door test-scheme dat altijd authenticeert
            services.Configure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = "Test";
                o.DefaultChallengeScheme = "Test";
            });
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        });

        builder.UseEnvironment("Development");
    }
}

public sealed class ShipmentsEndpointTests(ShipmentTrackingFactory factory)
    : IClassFixture<ShipmentTrackingFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task POST_CreateShipment_WithValidData_Returns201WithTrackingNumber()
    {
        // Arrange
        var command = new CreateShipmentCommand(
            "Sender NV", "Industrieweg 1, Gent",
            "Ontvanger BV", "Havenstraat 5, Antwerpen",
            "Elektronica", 8.5m);

        // Act
        var response = await _client.PostAsJsonAsync("/api/shipments", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CreateShipmentResult>();
        result.Should().NotBeNull();
        result!.Id.Should().NotBeEmpty();
        result.TrackingNumber.Should().StartWith("STP-");
    }

    [Fact]
    public async Task POST_CreateShipment_WithInvalidData_Returns400()
    {
        // Arrange — gewicht = 0 is ongeldig
        var command = new CreateShipmentCommand(
            "", "Adres", "Ontvanger", "Adres", "Pakket", 0m);

        // Act
        var response = await _client.PostAsJsonAsync("/api/shipments", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_CreateShipment_WithSameIdempotencyKey_ReplaysOriginalResponse()
    {
        var command = new CreateShipmentCommand("Sender", "Address", "Recipient", "Address", "Idempotent package", 1m);
        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/shipments") { Content = JsonContent.Create(command) };
        first.Headers.Add("Idempotency-Key", "a3f8c257-caa1-4c22-8050-1980e94a98e5");
        var firstResponse = await _client.SendAsync(first);
        using var retry = new HttpRequestMessage(HttpMethod.Post, "/api/shipments") { Content = JsonContent.Create(command) };
        retry.Headers.Add("Idempotency-Key", "a3f8c257-caa1-4c22-8050-1980e94a98e5");
        var retryResponse = await _client.SendAsync(retry);

        retryResponse.StatusCode.Should().Be(firstResponse.StatusCode);
        retryResponse.Headers.GetValues("Idempotency-Replayed").Should().ContainSingle().Which.Should().Be("true");
    }

    [Fact]
    public async Task GET_Shipments_ReturnsPagedResult()
    {
        // Act
        var response = await _client.GetAsync("/api/shipments?page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ShipmentSummaryDto>>();
        result.Should().NotBeNull();
        result!.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task GET_Shipment_WithUnknownId_Returns404()
    {
        // Act
        var response = await _client.GetAsync($"/api/shipments/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
