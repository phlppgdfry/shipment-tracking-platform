using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
namespace ShipmentTracking.Infrastructure.Persistence.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261012100000_OperationalEvents")]
public sealed class OperationalEvents : Migration
{
    protected override void Up(MigrationBuilder builder) => builder.Sql("""
        CREATE TABLE OperationalRecords (
          Id uniqueidentifier NOT NULL PRIMARY KEY, ShipmentId uniqueidentifier NOT NULL,
          CustomerId nvarchar(64) NOT NULL, EventId nvarchar(80) NOT NULL, ReferenceId nvarchar(80) NOT NULL,
          Type nvarchar(40) NOT NULL, OccurredAt datetimeoffset NOT NULL, ObservedAt datetimeoffset NOT NULL,
          ReceivedAt datetimeoffset NOT NULL, Source nvarchar(80) NOT NULL, Detail nvarchar(500) NOT NULL,
          CONSTRAINT FK_OperationalRecords_Shipments FOREIGN KEY (ShipmentId) REFERENCES Shipments(Id) ON DELETE CASCADE);
        CREATE UNIQUE INDEX IX_OperationalRecords_ShipmentId_EventId ON OperationalRecords (ShipmentId, EventId);
        CREATE UNIQUE INDEX IX_OperationalRecords_ShipmentId_Type ON OperationalRecords (ShipmentId, Type) WHERE Type IN ('gate_in', 'gate_out');
        """);
    protected override void Down(MigrationBuilder builder) => builder.DropTable("OperationalRecords");
}
