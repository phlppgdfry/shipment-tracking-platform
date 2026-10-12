using System.Net;
using System.Net.Http.Json;
using System.Text;
using ShipmentTracking.Application.Features.Shipments.Commands.CreateShipment;
using ShipmentTracking.Application.Features.Shipments.Queries.GetShipment;
using ShipmentTracking.Application.Features.Shipments.Queries.GetShipments;
using Xunit;

namespace ShipmentTracking.Tests.Integration;

public sealed class CustomerIsolationTests(ShipmentTrackingFactory factory) : IClassFixture<ShipmentTrackingFactory>
{
    private HttpClient Client(string customer, string role = "Dispatcher", string user = "test-user")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer", customer);
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-User", user);
        return client;
    }
    private static CreateShipmentCommand Command(string description = "Synthetic isolation test") => new("Sender", "A", "Recipient", "B", description, 1.5m);
    private static async Task<CreateShipmentResult> Create(HttpClient client, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shipments") { Content = JsonContent.Create(Command()) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateShipmentResult>())!;
    }
    private static MultipartFormDataContent Pdf()
    {
        var multipart = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\nSynthetic fixture\n%%EOF"));
        content.Headers.ContentType = new("application/pdf");
        multipart.Add(content, "file", "synthetic.pdf"); return multipart;
    }
    [Fact]
    public async Task Lists_search_detail_and_cache_never_cross_customer_boundaries()
    {
        using var a = Client("a-" + Guid.NewGuid()); using var b = Client("b-" + Guid.NewGuid(), "Administrator");
        var shipment = await Create(a);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/shipments/{shipment.Id}")).StatusCode); // populate cache
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/shipments/{shipment.Id}")).StatusCode);
        var list = (await b.GetFromJsonAsync<PagedResult<ShipmentSummaryDto>>($"/api/shipments?search={shipment.TrackingNumber}"))!;
        Assert.Empty(list.Items); Assert.Equal(0, list.TotalCount);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/shipments/{shipment.Id}/status", new { newStatus = 1, notes = "forbidden" })).StatusCode);
    }
    [Fact]
    public async Task Documents_are_scoped_for_list_upload_download_and_cached_details()
    {
        using var a = Client("a-" + Guid.NewGuid()); using var b = Client("b-" + Guid.NewGuid());
        var shipment = await Create(a);
        await a.GetAsync($"/api/shipments/{shipment.Id}"); // cache before upload
        using var pdf = Pdf(); var uploaded = await a.PostAsync($"/api/shipments/{shipment.Id}/documents", pdf);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var response = await uploaded.Content.ReadFromJsonAsync<Dictionary<string, Guid>>(); var documentId = response!["documentId"];
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/documents/{documentId}/download")).StatusCode);
        var detail = (await a.GetFromJsonAsync<ShipmentDetailDto>($"/api/shipments/{shipment.Id}"))!;
        Assert.Single(detail.Documents);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/shipments/{shipment.Id}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/documents/{documentId}/download")).StatusCode);
        using var forbidden = Pdf();
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/shipments/{shipment.Id}/documents", forbidden)).StatusCode);
    }
    [Fact]
    public async Task Replay_is_scoped_by_customer_and_user_and_is_not_an_authentication_bypass()
    {
        var tenant = "a-" + Guid.NewGuid(); using var a = Client(tenant, user: "alice");
        using var colleague = Client(tenant, user: "colleague"); using var b = Client("b-" + Guid.NewGuid());
        var key = Guid.NewGuid().ToString(); var original = await Create(a, key); var retry = await Create(a, key);
        Assert.Equal(original.Id, retry.Id);
        Assert.NotEqual(original.Id, (await Create(colleague, key)).Id);
        Assert.NotEqual(original.Id, (await Create(b, key)).Id);
        using var anonymous = factory.CreateClient(); anonymous.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shipments") { Content = JsonContent.Create(Command()) };
        request.Headers.Add("Idempotency-Key", key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(request)).StatusCode);
        using var customer = Client(tenant, "Customer", "alice");
        using var forbidden = new HttpRequestMessage(HttpMethod.Post, "/api/shipments") { Content = JsonContent.Create(Command()) };
        forbidden.Headers.Add("Idempotency-Key", key);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.SendAsync(forbidden)).StatusCode);
    }
    [Fact]
    public async Task Different_body_with_same_key_conflicts_without_creating_a_second_shipment()
    {
        using var a = Client("a-" + Guid.NewGuid()); var key = Guid.NewGuid().ToString(); await Create(a, key);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shipments") { Content = JsonContent.Create(Command("different")) };
        request.Headers.Add("Idempotency-Key", key);
        Assert.Equal(HttpStatusCode.Conflict, (await a.SendAsync(request)).StatusCode);
        var list = (await a.GetFromJsonAsync<PagedResult<ShipmentSummaryDto>>("/api/shipments"))!;
        Assert.Equal(1, list.TotalCount);
    }
    [Fact]
    public async Task Full_status_lifecycle_rejects_invalid_transition_then_recovers()
    {
        using var a = Client("a-" + Guid.NewGuid()); var shipment = await Create(a);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.PutAsJsonAsync($"/api/shipments/{shipment.Id}/status", new {newStatus=4, notes="Invalid direct delivery"})).StatusCode);
        foreach(var status in new[]{1,2,3,4})
            Assert.Equal(HttpStatusCode.NoContent,(await a.PutAsJsonAsync($"/api/shipments/{shipment.Id}/status",new {newStatus=status, notes="Synthetic lifecycle"})).StatusCode);
        var detail = (await a.GetFromJsonAsync<ShipmentDetailDto>($"/api/shipments/{shipment.Id}"))!;
        Assert.Equal("Delivered", detail.StatusLabel); Assert.Equal(5, detail.StatusHistory.Count);
    }
}
