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
            user = new ApplicationUser { UserName = name, Email = "dispatcher@example.invalid", CustomerId = "synthetic-demo" };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
                throw new InvalidOperationException("Demo dispatcher creation failed: " + string.Join(", ", created.Errors.Select(e => e.Code)));
        }
        if (user.CustomerId != "synthetic-demo")
        {
            if (user.Email != "dispatcher@example.invalid" || !await users.IsInRoleAsync(user,"Dispatcher"))
                throw new InvalidOperationException("An existing untrusted account uses the reserved demo name; use a fresh demo database.");
            user.CustomerId = "synthetic-demo"; await users.UpdateAsync(user);
        }
        if (!await users.IsInRoleAsync(user, "Dispatcher"))
        {
            var added = await users.AddToRoleAsync(user, "Dispatcher");
            if (!added.Succeeded) throw new InvalidOperationException("Demo dispatcher role could not be assigned.");
        }
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var definitions = new[] { ("Synthetic package demo: spare parts", 2), ("Synthetic equipment", 4), ("Pending arrival", 1) };
        foreach(var (description,status) in definitions)
        {
            var shipment = await db.Shipments.IgnoreQueryFilters().SingleOrDefaultAsync(s=>s.CustomerId=="synthetic-demo" && s.Description==description);
            if(shipment is null)
            {
                shipment=Shipment.Create("Demo Sender","Synthetic address A","Demo Recipient","Synthetic address B",description,12.5m,name,"synthetic-demo");
                foreach(var step in Enumerable.Range(1,status)) shipment.UpdateStatus((ShipmentTracking.Domain.Enums.ShipmentStatus)step,"Synthetic baseline",name);
                db.Shipments.Add(shipment); await db.SaveChangesAsync();
            }
            var events=description.StartsWith("Synthetic package") ? new[]{("plan","visit","arrival_plan","08:00"),("in","visit","gate_in","08:20"),("hold","customs","hold_opened","08:30")}
                : description=="Synthetic equipment" ? new[]{("plan","visit","arrival_plan","05:00"),("in","visit","gate_in","05:04"),("out","visit","gate_out","08:04")}
                : new[]{("plan","visit","arrival_plan","10:00")};
            foreach(var (eventId,reference,type,time) in events)
            {
                if(await db.OperationalRecords.IgnoreQueryFilters().AnyAsync(e=>e.ShipmentId==shipment.Id && e.EventId==eventId)) continue;
                var occurrence=DateTimeOffset.Parse("2026-10-12T"+time+":00Z");
                db.OperationalRecords.Add(new(){ShipmentId=shipment.Id,CustomerId="synthetic-demo",EventId=eventId,ReferenceId=reference,
                    Type=type,OccurredAt=occurrence,ObservedAt=type=="arrival_plan"?DateTimeOffset.Parse("2026-10-12T04:00:00Z"):occurrence.AddMinutes(2),Source="synthetic-baseline",Detail=type switch{
                    "arrival_plan"=>"Planned arrival", "gate_in"=>"Gate arrival", "gate_out"=>"Gate departure", _=>"Customs hold opened"}});
                await db.SaveChangesAsync();
            }
        }
    }
}
