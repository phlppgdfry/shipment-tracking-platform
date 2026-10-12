using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShipmentTracking.Domain.Entities;
using ShipmentTracking.Infrastructure.Persistence;

namespace ShipmentTracking.WebAPI.Demo;

/// <summary>Opt-in synthetic data and dispatcher for the local package demonstration.</summary>
public static class PackageDemoSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Demo:Seed")) return;
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException("Demo seeding is restricted to Development.");
        var password = app.Configuration["Demo:DispatcherPassword"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Demo:DispatcherPassword is required when Demo:Seed is enabled.");
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        const string name = "demo-dispatcher";
        var user = await users.FindByNameAsync(name);
        if (user is null)
        {
            user = new ApplicationUser { UserName = name, Email = "dispatcher@example.invalid" };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
                throw new InvalidOperationException("Demo dispatcher creation failed: " + string.Join(", ", created.Errors.Select(e => e.Code)));
        }
        if (!await users.IsInRoleAsync(user, "Dispatcher"))
        {
            var added = await users.AddToRoleAsync(user, "Dispatcher");
            if (!added.Succeeded) throw new InvalidOperationException("Demo dispatcher role could not be assigned.");
        }
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        const string marker = "Synthetic package demo: spare parts";
        if (!await db.Shipments.AnyAsync(s => s.Description == marker))
        {
            db.Shipments.Add(Shipment.Create("Demo Sender", "Synthetic address A", "Demo Recipient",
                "Synthetic address B", marker, 12.5m, name));
            await db.SaveChangesAsync();
        }
    }
}
