using Microsoft.AspNetCore.Identity;

namespace ShipmentTracking.Infrastructure.Persistence;

public sealed class ApplicationUser : IdentityUser
{
    public string CustomerId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
