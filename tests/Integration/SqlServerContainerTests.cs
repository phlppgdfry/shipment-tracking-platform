using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShipmentTracking.Domain.Entities;
using ShipmentTracking.Infrastructure.Persistence;
using Testcontainers.MsSql;
using Xunit;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ShipmentTracking.Tests.Integration;

/// <summary>Runs against SQL Server in Docker. Requires a running Docker daemon.</summary>
[Trait("Category", "Container")]
public sealed class SqlServerContainerTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private sealed class ContainerUser : ShipmentTracking.Application.Common.Interfaces.ICurrentUserService
    { public string CustomerId => "container-customer"; public string UserId => "runner"; public string UserName => "test-runner"; }

    public async Task InitializeAsync() => await _sql.StartAsync();
    public async Task DisposeAsync() => await _sql.DisposeAsync();

    [Fact]
    public async Task Shipment_is_persisted_with_its_status_history()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_sql.GetConnectionString())
            .Options;
        await using var db = new ApplicationDbContext(options, new ContainerUser());
        await db.Database.MigrateAsync();

        var shipment = Shipment.Create("Sender", "Sender address", "Recipient", "Recipient address", "Container test", 1.5m, "test-runner", "container-customer");
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.Shipments.Include(x => x.StatusHistory).SingleAsync(x => x.Id == shipment.Id);
        stored.StatusHistory.Should().ContainSingle();
        stored.UpdateStatus(ShipmentTracking.Domain.Enums.ShipmentStatus.Confirmed,"Synthetic status proof","test-runner");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await db.Shipments.Include(x=>x.StatusHistory).SingleAsync()).StatusHistory.Should().HaveCount(2);
        stored.TrackingNumber.Should().Be(shipment.TrackingNumber);
    }

    [Fact]
    public async Task Legacy_schema_upgrade_assigns_known_owners_and_quarantines_unknown_records()
    {
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        await using var db=new ApplicationDbContext(options,new LegacyUser());
        await db.GetService<IMigrator>().MigrateAsync("20260820120000_InitialCreate");
        await db.Database.ExecuteSqlRawAsync("""
          INSERT INTO AspNetUsers (Id,UserName,NormalizedUserName,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount,CreatedAt)
          VALUES ('legacy-customer','legacy-user','LEGACY-USER',0,0,0,0,0,SYSUTCDATETIME());
          INSERT INTO Shipments (Id,TrackingNumber,SenderName,SenderAddress,RecipientName,RecipientAddress,Description,WeightKg,Status,IsDeleted,CreatedAt,UpdatedAt,CreatedBy)
          VALUES ('11111111-1111-1111-1111-111111111111','LEGACY-OWNED','S','A','R','B','Known owner',1,'Draft',0,SYSUTCDATETIME(),SYSUTCDATETIME(),'legacy-user'),
          ('22222222-2222-2222-2222-222222222222','LEGACY-UNKNOWN','S','A','R','B','Unattributed',1,'Draft',0,SYSUTCDATETIME(),SYSUTCDATETIME(),'unknown-user');
          INSERT INTO Documents (Id,ShipmentId,FileName,ContentType,FileSizeBytes,BlobUri,UploadedBy,UploadedAt)
          VALUES ('33333333-3333-3333-3333-333333333333','11111111-1111-1111-1111-111111111111','synthetic.pdf','application/pdf',10,'file:///synthetic-fixture.pdf','legacy-user',SYSUTCDATETIME());
          """);
        await db.Database.MigrateAsync();
        (await db.Shipments.SingleAsync()).TrackingNumber.Should().Be("LEGACY-OWNED");
        (await db.Documents.SingleAsync()).CustomerId.Should().Be("legacy-customer");
        (await db.Shipments.IgnoreQueryFilters().SingleAsync(s=>s.TrackingNumber=="LEGACY-UNKNOWN")).CustomerId.Should().BeEmpty();
        await using var noScope=new ApplicationDbContext(options);
        (await noScope.Shipments.CountAsync()).Should().Be(0);
    }
    private sealed class LegacyUser:ShipmentTracking.Application.Common.Interfaces.ICurrentUserService
    {public string CustomerId=>"legacy-customer";public string UserId=>"legacy-customer";public string UserName=>"legacy-user";}
}
