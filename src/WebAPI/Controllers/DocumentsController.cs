using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShipmentTracking.Application.Common.Interfaces;
using ShipmentTracking.Application.Features.Documents.Commands.UploadDocument;
using ShipmentTracking.WebAPI.Security;

namespace ShipmentTracking.WebAPI.Controllers;

/// <summary>
/// Document upload en download per zending.
/// </summary>
[ApiController]
[Route("api/shipments/{shipmentId:guid}/documents")]
[Authorize]
[Produces("application/json")]
public sealed class DocumentsController(
    IMediator mediator,
    IDocumentRepository documentRepository,
    IShipmentRepository shipmentRepository,
    IBlobStorageService blobStorage) : ControllerBase
{
    /// <summary>
    /// Laadt een document op voor een zending (max 20 MB).
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upload(
        Guid shipmentId,
        IFormFile file,
        CancellationToken ct)
    {
        if (await shipmentRepository.GetByIdAsync(shipmentId, ct) is null) return NotFound();
        if (file.Length == 0)
            return BadRequest("Bestand is leeg.");
        if (!await FileSignatureValidator.MatchesDeclaredTypeAsync(file, ct))
            return BadRequest("Bestandinhoud komt niet overeen met het opgegeven type.");

        await using var stream = file.OpenReadStream();

        var documentId = await mediator.Send(new UploadDocumentCommand(
            shipmentId,
            file.FileName,
            file.ContentType,
            file.Length,
            stream), ct);

        return CreatedAtAction(nameof(GetDocuments),
            new { shipmentId },
            new { documentId });
    }

    /// <summary>
    /// Haalt de lijst van documenten op voor een zending.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocuments(Guid shipmentId, CancellationToken ct)
    {
        if (await shipmentRepository.GetByIdAsync(shipmentId, ct) is null) return NotFound();
        var documents = await documentRepository.GetByShipmentIdAsync(shipmentId, ct);
        return Ok(documents.Select(d => new
        {
            d.Id, d.FileName, d.ContentType, d.FileSizeBytes, d.UploadedBy, d.UploadedAt
        }));
    }

    /// <summary>
    /// Downloadt een document.
    /// </summary>
    [HttpGet("~/api/documents/{documentId:guid}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid documentId, CancellationToken ct)
    {
        var document = await documentRepository.GetByIdAsync(documentId, ct);
        if (document is null || await shipmentRepository.GetByIdAsync(document.ShipmentId, ct) is null) return NotFound();

        var stream = await blobStorage.DownloadAsync(document.BlobUri, ct);
        return File(stream, document.ContentType, document.FileName);
    }
}
