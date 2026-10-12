using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ShipmentTracking.Application.Features.Shipments.Commands.CreateShipment;
using Xunit;
namespace ShipmentTracking.Tests.Integration;
public sealed class OperationalEventTests(ShipmentTrackingFactory factory):IClassFixture<ShipmentTrackingFactory>
{
    private HttpClient Client(string tenant) {var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Test-Customer",tenant);return client;}
    private static object Event(string id,string type,string time,string reference="visit")=>new {eventId=id,referenceId=reference,type,occurredAt=$"2026-10-12T{time}:00Z",observedAt=$"2026-10-12T{time}:30Z",source="synthetic-test",detail=type};
    [Fact]
    public async Task Versioned_events_replay_without_duplicates_and_preserve_the_customer_boundary()
    {
        using var a=Client("events-a-"+Guid.NewGuid());using var b=Client("events-b-"+Guid.NewGuid());
        var shipment=(await (await a.PostAsJsonAsync("/api/shipments",new CreateShipmentCommand("S","A","R","B","Events",1))).Content.ReadFromJsonAsync<CreateShipmentResult>())!;
        var url=$"/api/shipments/{shipment.Id}/events";
        var first=await a.PostAsJsonAsync(url,Event("arrival","gate_in","08:20"));Assert.Equal(HttpStatusCode.Created,first.StatusCode);
        var envelope=await first.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(1,envelope.GetProperty("schemaVersion").GetInt32());Assert.Equal("visit",envelope.GetProperty("correlationId").GetString());
        var replay=await a.PostAsJsonAsync(url,Event("arrival","gate_in","08:20"));Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.True(replay.Headers.Contains("Event-Replayed"));
        Assert.Equal(HttpStatusCode.Conflict,(await a.PostAsJsonAsync(url,Event("arrival","gate_in","08:21"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await b.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await b.PostAsJsonAsync(url,Event("other","gate_in","08:20"))).StatusCode);
        Assert.Empty((await b.GetFromJsonAsync<JsonElement>("/api/operations")).GetProperty("shipments").EnumerateArray());
        Assert.Single((await a.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());
    }
    [Fact]
    public async Task Departure_is_blocked_until_the_matching_hold_is_released()
    {
        using var a=Client("events-"+Guid.NewGuid());
        var shipment=(await (await a.PostAsJsonAsync("/api/shipments",new CreateShipmentCommand("S","A","R","B","Holds",1))).Content.ReadFromJsonAsync<CreateShipmentResult>())!;
        var url=$"/api/shipments/{shipment.Id}/events";
        Assert.Equal(HttpStatusCode.BadRequest,(await a.PostAsJsonAsync(url,Event("out","gate_out","07:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await a.PostAsJsonAsync(url,Event("in","gate_in","08:20"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await a.PostAsJsonAsync(url,Event("hold","hold_opened","08:30","customs"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await a.PostAsJsonAsync(url,Event("out","gate_out","10:20"))).StatusCode);
        foreach(var status in new[]{1,2}) Assert.Equal(HttpStatusCode.NoContent,(await a.PutAsJsonAsync($"/api/shipments/{shipment.Id}/status",new {newStatus=status,notes="Synthetic progression"})).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await a.PutAsJsonAsync($"/api/shipments/{shipment.Id}/status",new {newStatus=3,notes="Blocked by hold"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await a.PostAsJsonAsync(url,Event("release-wrong","hold_released","09:00","wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await a.PostAsJsonAsync(url,Event("release","hold_released","09:00","customs"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await a.PostAsJsonAsync(url,Event("out","gate_out","10:20"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await a.PostAsJsonAsync(url,Event("second-in","gate_in","11:20"))).StatusCode);
    }
    [Fact]
    public async Task Timezone_less_times_are_rejected_before_recording_evidence()
    {
        using var a=Client("events-"+Guid.NewGuid());
        var shipment=(await (await a.PostAsJsonAsync("/api/shipments",new CreateShipmentCommand("S","A","R","B","Bad time",1))).Content.ReadFromJsonAsync<CreateShipmentResult>())!;
        var result=await a.PostAsJsonAsync($"/api/shipments/{shipment.Id}/events",new {eventId="bad",referenceId="visit",type="gate_in",occurredAt="2026-10-12T08:20:00",observedAt="2026-10-12T08:22:00Z",source="synthetic",detail="Bad timestamp"});
        Assert.Equal(HttpStatusCode.BadRequest,result.StatusCode);
    }
}
