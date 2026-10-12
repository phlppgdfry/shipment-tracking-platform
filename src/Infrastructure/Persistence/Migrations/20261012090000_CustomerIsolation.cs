using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
namespace ShipmentTracking.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261012090000_CustomerIsolation")]
public sealed class CustomerIsolation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("CustomerId", "AspNetUsers", type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("CustomerId", "Shipments", type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("CustomerId", "Documents", type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "");
        // Existing users get their own boundary. Unattributable records stay quarantined with empty ownership.
        migrationBuilder.Sql("UPDATE AspNetUsers SET CustomerId = Id WHERE LEN(Id) <= 64;");
        migrationBuilder.Sql("UPDATE s SET s.CustomerId=u.CustomerId FROM Shipments s JOIN AspNetUsers u ON s.CreatedBy=u.UserName WHERE u.CustomerId<>'';");
        migrationBuilder.Sql("UPDATE d SET d.CustomerId=s.CustomerId FROM Documents d JOIN Shipments s ON d.ShipmentId=s.Id;");
        migrationBuilder.CreateIndex("IX_Shipments_CustomerId", "Shipments", "CustomerId");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Customer isolation cannot be downgraded without an audited ownership migration.");
}
