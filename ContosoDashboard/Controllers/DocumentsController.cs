using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ContosoDashboard.Services;

namespace ContosoDashboard.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IFileStorageService _fileStorage;

    public DocumentsController(IDocumentService documentService, IFileStorageService fileStorage)
    {
        _documentService = documentService;
        _fileStorage = fileStorage;
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == 0) return Unauthorized();

        var doc = await _documentService.GetDocumentAsync(id, userId, cancellationToken);
        if (doc == null) return NotFound();

        if (doc.ScanStatus == "Rejected")
            return BadRequest("File has been flagged by the virus scanner and cannot be downloaded.");

        var stream = await _fileStorage.DownloadAsync(doc.FilePath, cancellationToken);
        var fileName = Path.GetFileName(doc.FilePath);
        return File(stream, doc.MimeType, $"{doc.Title}{Path.GetExtension(fileName)}");
    }

    [HttpGet("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == 0) return Unauthorized();

        var doc = await _documentService.GetDocumentAsync(id, userId, cancellationToken);
        if (doc == null) return NotFound();

        if (doc.ScanStatus == "Rejected")
            return BadRequest("File has been flagged and cannot be previewed.");

        // Only stream previewable types inline
        if (doc.MimeType != "application/pdf" && !doc.MimeType.StartsWith("image/"))
            return BadRequest("Preview not available for this file type.");

        var stream = await _fileStorage.DownloadAsync(doc.FilePath, cancellationToken);
        return File(stream, doc.MimeType);
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && int.TryParse(claim.Value, out int uid) ? uid : 0;
    }
}
