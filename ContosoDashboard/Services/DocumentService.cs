using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface IDocumentService
{
    Task<DocumentDto> CreateAsync(DocumentUploadRequest request, CancellationToken cancellationToken = default);
    Task<PaginatedResult<DocumentDto>> GetUserDocumentsAsync(int userId, string? category = null, int? projectId = null, string? searchTerm = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<PaginatedResult<DocumentDto>> SearchDocumentsAsync(int requestingUserId, string searchTerm, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<DocumentDto?> GetDocumentAsync(int documentId, int requestingUserId, CancellationToken cancellationToken = default);
    Task<DocumentDto> UpdateAsync(int documentId, DocumentUpdateRequest request, int requestingUserId, CancellationToken cancellationToken = default);
    Task<DocumentDto> ReplaceFileAsync(int documentId, Stream fileStream, string fileName, long fileSize, string mimeType, int requestingUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int documentId, int requestingUserId, CancellationToken cancellationToken = default);
    Task<bool> CanUserAccessAsync(int documentId, int userId, CancellationToken cancellationToken = default);
}

public class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IVirusScanQueueService _virusScanQueue;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService fileStorage,
        IVirusScanQueueService virusScanQueue)
    {
        _context = context;
        _fileStorage = fileStorage;
        _virusScanQueue = virusScanQueue;
    }

    public async Task<bool> CanUserAccessAsync(int documentId, int userId, CancellationToken cancellationToken = default)
    {
        var doc = await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);

        if (doc == null) return false;

        // Owner
        if (doc.UserId == userId) return true;

        // Admin role
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user?.Role == UserRole.Administrator) return true;

        // Project member (document belongs to a project the user is member of)
        if (doc.ProjectId.HasValue)
        {
            var isMember = await _context.ProjectMembers
                .AnyAsync(pm => pm.ProjectId == doc.ProjectId.Value && pm.UserId == userId, cancellationToken);
            if (isMember) return true;
        }

        // Explicitly shared
        var isShared = await _context.DocumentShares
            .AnyAsync(s => s.DocumentId == documentId && s.SharedWithUserId == userId, cancellationToken);
        return isShared;
    }

    public async Task<DocumentDto> CreateAsync(DocumentUploadRequest request, CancellationToken cancellationToken = default)
    {
        // Upload file to storage
        var storedPath = await _fileStorage.UploadAsync(request.FileStream, request.FileName, cancellationToken);

        var document = new Document
        {
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            Title = request.Title,
            Description = request.Description,
            Category = request.Category,
            Tags = request.Tags,
            UploadedDate = DateTime.UtcNow,
            FileSize = request.FileSize,
            MimeType = request.MimeType,
            FilePath = storedPath,
            CreatedBy = request.CreatedBy,
            ScanStatus = "Approved" // Training env: approve immediately
        };

        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);

        // Enqueue async virus scan (no-op in local env)
        await _virusScanQueue.EnqueueAsync(new VirusScanMessage
        {
            DocumentId = document.DocumentId,
            FilePath = storedPath,
            MimeType = request.MimeType,
            EnqueuedAt = DateTime.UtcNow
        }, cancellationToken);

        // Log activity
        await LogActivityAsync(document.DocumentId, request.UserId, "Upload", cancellationToken);

        return await BuildDtoAsync(document, cancellationToken);
    }

    public async Task<PaginatedResult<DocumentDto>> GetUserDocumentsAsync(
        int userId,
        string? category = null,
        int? projectId = null,
        string? searchTerm = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(d => d.Category == category);

        if (projectId.HasValue)
            query = query.Where(d => d.ProjectId == projectId.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(d =>
                d.Title.ToLower().Contains(term) ||
                (d.Description != null && d.Description.ToLower().Contains(term)) ||
                (d.Tags != null && d.Tags.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);

        var documents = await query
            .OrderByDescending(d => d.UploadedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(d => d.Uploader)
            .Include(d => d.Project)
            .ToListAsync(cancellationToken);

        var dtos = documents.Select(d => MapToDto(d)).ToList();

        return new PaginatedResult<DocumentDto>
        {
            Items = dtos,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PaginatedResult<DocumentDto>> SearchDocumentsAsync(
        int requestingUserId,
        string searchTerm,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var term = searchTerm.Trim().ToLower();

        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == requestingUserId, cancellationToken);

        IQueryable<Document> query = _context.Documents.AsNoTracking()
            .Include(d => d.Uploader)
            .Include(d => d.Project);

        // Admins see all; others see own + shared + project-member
        if (user?.Role != UserRole.Administrator)
        {
            var memberProjectIds = await _context.ProjectMembers
                .Where(pm => pm.UserId == requestingUserId)
                .Select(pm => pm.ProjectId)
                .ToListAsync(cancellationToken);

            var sharedDocIds = await _context.DocumentShares
                .Where(s => s.SharedWithUserId == requestingUserId)
                .Select(s => s.DocumentId)
                .ToListAsync(cancellationToken);

            query = query.Where(d =>
                d.UserId == requestingUserId ||
                (d.ProjectId != null && memberProjectIds.Contains(d.ProjectId.Value)) ||
                sharedDocIds.Contains(d.DocumentId));
        }

        query = query.Where(d =>
            d.Title.ToLower().Contains(term) ||
            (d.Description != null && d.Description.ToLower().Contains(term)) ||
            (d.Tags != null && d.Tags.ToLower().Contains(term)));

        var total = await query.CountAsync(cancellationToken);
        var docs = await query
            .OrderByDescending(d => d.UploadedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedResult<DocumentDto>
        {
            Items = docs.Select(MapToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<DocumentDto?> GetDocumentAsync(int documentId, int requestingUserId, CancellationToken cancellationToken = default)
    {
        var canAccess = await CanUserAccessAsync(documentId, requestingUserId, cancellationToken);
        if (!canAccess) return null;

        var doc = await _context.Documents
            .AsNoTracking()
            .Include(d => d.Uploader)
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);

        if (doc == null) return null;

        await LogActivityAsync(documentId, requestingUserId, "View", cancellationToken);

        return MapToDto(doc);
    }

    public async Task<DocumentDto> UpdateAsync(int documentId, DocumentUpdateRequest request, int requestingUserId, CancellationToken cancellationToken = default)
    {
        var doc = await _context.Documents
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        if (doc.UserId != requestingUserId)
        {
            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == requestingUserId, cancellationToken);
            if (user?.Role != UserRole.Administrator)
                throw new UnauthorizedAccessException("You do not have permission to edit this document.");
        }

        doc.Title = request.Title;
        doc.Description = request.Description;
        doc.Category = request.Category;
        doc.Tags = request.Tags;

        await _context.SaveChangesAsync(cancellationToken);
        await LogActivityAsync(documentId, requestingUserId, "Update", cancellationToken);

        await _context.Entry(doc).Reference(d => d.Uploader).LoadAsync(cancellationToken);
        if (doc.ProjectId.HasValue)
            await _context.Entry(doc).Reference(d => d.Project).LoadAsync(cancellationToken);

        return MapToDto(doc);
    }

    public async Task<DocumentDto> ReplaceFileAsync(int documentId, Stream fileStream, string fileName, long fileSize, string mimeType, int requestingUserId, CancellationToken cancellationToken = default)
    {
        var doc = await _context.Documents
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        if (doc.UserId != requestingUserId)
            throw new UnauthorizedAccessException("Only the document owner can replace the file.");

        var oldPath = doc.FilePath;

        var newPath = await _fileStorage.UploadAsync(fileStream, fileName, cancellationToken);
        doc.FilePath = newPath;
        doc.FileSize = fileSize;
        doc.MimeType = mimeType;
        doc.ScanStatus = "Approved";
        doc.UploadedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Delete old file (best effort)
        try { await _fileStorage.DeleteAsync(oldPath, cancellationToken); } catch { /* ignore */ }

        await _virusScanQueue.EnqueueAsync(new VirusScanMessage
        {
            DocumentId = doc.DocumentId,
            FilePath = newPath,
            MimeType = mimeType,
            EnqueuedAt = DateTime.UtcNow
        }, cancellationToken);

        await LogActivityAsync(documentId, requestingUserId, "ReplaceFile", cancellationToken);

        await _context.Entry(doc).Reference(d => d.Uploader).LoadAsync(cancellationToken);
        if (doc.ProjectId.HasValue)
            await _context.Entry(doc).Reference(d => d.Project).LoadAsync(cancellationToken);

        return MapToDto(doc);
    }

    public async Task DeleteAsync(int documentId, int requestingUserId, CancellationToken cancellationToken = default)
    {
        var doc = await _context.Documents
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        if (doc.UserId != requestingUserId)
        {
            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == requestingUserId, cancellationToken);
            if (user?.Role != UserRole.Administrator)
                throw new UnauthorizedAccessException("You do not have permission to delete this document.");
        }

        await LogActivityAsync(documentId, requestingUserId, "Delete", cancellationToken);

        var filePath = doc.FilePath;
        _context.Documents.Remove(doc);
        await _context.SaveChangesAsync(cancellationToken);

        // Delete file from storage (best effort)
        try { await _fileStorage.DeleteAsync(filePath, cancellationToken); } catch { /* ignore */ }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task LogActivityAsync(int? documentId, int userId, string activityType, CancellationToken cancellationToken)
    {
        _context.DocumentActivityLogs.Add(new DocumentActivityLog
        {
            DocumentId = documentId,
            UserId = userId,
            ActivityType = activityType,
            ActivityDate = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<DocumentDto> BuildDtoAsync(Document doc, CancellationToken cancellationToken)
    {
        await _context.Entry(doc).Reference(d => d.Uploader).LoadAsync(cancellationToken);
        if (doc.ProjectId.HasValue)
            await _context.Entry(doc).Reference(d => d.Project).LoadAsync(cancellationToken);
        return MapToDto(doc);
    }

    private static DocumentDto MapToDto(Document d) => new()
    {
        DocumentId = d.DocumentId,
        UserId = d.UserId,
        UploaderName = d.Uploader?.DisplayName ?? string.Empty,
        ProjectId = d.ProjectId,
        ProjectName = d.Project?.Name,
        Title = d.Title,
        Description = d.Description,
        Category = d.Category,
        Tags = d.Tags,
        UploadedDate = d.UploadedDate,
        FileSize = d.FileSize,
        MimeType = d.MimeType,
        FilePath = d.FilePath,
        CreatedBy = d.CreatedBy,
        ScanStatus = d.ScanStatus
    };
}
