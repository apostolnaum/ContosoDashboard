# Service Contract: DocumentService

**Feature**: Document Upload and Management  
**Layer**: Business Logic (Service Layer)  
**Access**: Internal; consumed by Razor Pages via dependency injection  
**Status**: Design Phase

## Interface: IDocumentService

```csharp
namespace ContosoDashboard.Services;

public interface IDocumentService {
  /// <summary>
  /// Retrieves all documents uploaded by the specified user.
  /// Results ordered by upload date (newest first).
  /// </summary>
  /// <param name="userId">The user ID to filter documents for</param>
  /// <param name="pageNumber">Optional pagination (1-based); default 1</param>
  /// <param name="pageSize">Optional page size; default 20, max 100</param>
  /// <returns>Paginated list of user's documents</returns>
  /// <exception cref="UnauthorizedAccessException">If user cannot be identified from context</exception>
  Task<PaginatedResult<DocumentDto>> GetUserDocumentsAsync(int userId, int pageNumber = 1, int pageSize = 20);

  /// <summary>
  /// Retrieves all documents associated with a project.
  /// Only returns documents if current user is a project member.
  /// </summary>
  /// <param name="projectId">The project to retrieve documents for</param>
  /// <param name="cancellationToken">Cancellation token</param>
  /// <returns>List of project documents (ordered newest first)</returns>
  /// <exception cref="UnauthorizedAccessException">If user is not a project member (IDOR protection)</exception>
  /// <exception cref="KeyNotFoundException">If project does not exist</exception>
  Task<List<DocumentDto>> GetProjectDocumentsAsync(int projectId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Searches for documents by title, description, or tags.
  /// Only returns documents the current user can access.
  /// </summary>
  /// <param name="searchTerm">Term to search for (case-insensitive)</param>
  /// <param name="currentUserId">User performing the search (for IDOR filtering)</param>
  /// <param name="pageNumber">Pagination (1-based)</param>
  /// <param name="pageSize">Results per page (default 20, max 100)</param>
  /// <returns>Search results ordered by upload date (newest first)</returns>
  /// <remarks>
  /// Search covers: Title (exact > partial), Description, Tags
  /// Response time target: ≤2 seconds for typical queries
  /// </remarks>
  Task<PaginatedResult<DocumentDto>> SearchDocumentsAsync(string searchTerm, int currentUserId, int pageNumber = 1, int pageSize = 20);

  /// <summary>
  /// Retrieves a single document by ID with full authorization check.
  /// </summary>
  /// <param name="documentId">Document to retrieve</param>
  /// <param name="currentUserId">User requesting the document (for IDOR check)</param>
  /// <returns>Document details if authorized</returns>
  /// <exception cref="UnauthorizedAccessException">If user cannot access this document (IDOR protection)</exception>
  /// <exception cref="KeyNotFoundException">If document does not exist</exception>
  Task<DocumentDto> GetDocumentAsync(int documentId, int currentUserId);

  /// <summary>
  /// Uploads a new document.
  /// </summary>
  /// <param name="request">Upload request containing file stream, metadata, current user ID</param>
  /// <returns>Created DocumentDto with assigned DocumentId</returns>
  /// <exception cref="ArgumentException">If file size > 25MB, unsupported type, title empty, or category invalid</exception>
  /// <exception cref="InvalidOperationException">If virus scan fails</exception>
  /// <remarks>
  /// Process:
  /// 1. Validate file (size, type, metadata)
  /// 2. Generate unique file path (GUID-based)
  /// 3. Scan for viruses (placeholder implementation)
  /// 4. Save file to disk (AppData/uploads)
  /// 5. Insert document record in database
  /// 6. Log activity (Upload)
  /// 7. Return created DocumentDto
  /// 
  /// Atomicity: File path generated before save to prevent orphaned DB records
  /// </remarks>
  Task<DocumentDto> CreateAsync(DocumentUploadRequest request);

  /// <summary>
  /// Updates document metadata (title, description, category, tags).
  /// File replacement uses a separate endpoint.
  /// </summary>
  /// <param name="documentId">Document to update</param>
  /// <param name="request">Updated metadata</param>
  /// <param name="currentUserId">User making the update (must be original uploader)</param>
  /// <returns>Updated DocumentDto</returns>
  /// <exception cref="UnauthorizedAccessException">If user is not document owner</exception>
  /// <exception cref="KeyNotFoundException">If document does not exist</exception>
  Task<DocumentDto> UpdateAsync(int documentId, DocumentUpdateRequest request, int currentUserId);

  /// <summary>
  /// Replaces the file for an existing document while preserving metadata.
  /// Only the document owner or a Project Manager can replace.
  /// </summary>
  /// <param name="documentId">Document to update</param>
  /// <param name="newFileStream">New file content</param>
  /// <param name="fileName">New file name (for MIME type detection)</param>
  /// <param name="currentUserId">User making the replacement</param>
  /// <returns>Updated DocumentDto with new file size and MIME type</returns>
  /// <exception cref="UnauthorizedAccessException">If user is not owner or PM</exception>
  /// <exception cref="ArgumentException">If file invalid (size, type)</exception>
  Task<DocumentDto> ReplaceFileAsync(int documentId, Stream newFileStream, string fileName, int currentUserId);

  /// <summary>
  /// Permanently deletes a document and its associated file.
  /// </summary>
  /// <param name="documentId">Document to delete</param>
  /// <param name="currentUserId">User requesting deletion</param>
  /// <param name="requireConfirmation">If true, caller must confirm by passing the document title</param>
  /// <returns>True if deleted; false if already deleted</returns>
  /// <exception cref="UnauthorizedAccessException">If user cannot delete (not owner, not PM of project, not admin)</exception>
  /// <exception cref="KeyNotFoundException">If document does not exist</exception>
  /// <remarks>
  /// Permissions:
  /// - Owner: Can delete own documents
  /// - Project Manager: Can delete any document in their projects
  /// - Administrator: Can delete any document
  /// 
  /// Process:
  /// 1. Verify authorization
  /// 2. Delete file from disk (AppData/uploads)
  /// 3. Delete document record from database
  /// 4. Log activity (Delete)
  /// </remarks>
  Task<bool> DeleteAsync(int documentId, int currentUserId);

  /// <summary>
  /// Checks if a user has authorization to access a document.
  /// Used by controllers before serving file downloads/previews.
  /// </summary>
  /// <param name="documentId">Document to check access for</param>
  /// <param name="userId">User requesting access</param>
  /// <returns>True if user can access; false otherwise</returns>
  /// <remarks>
  /// IDOR Protection: User can access if:
  /// - They are the document owner, OR
  /// - Document is in a project they are a member of, OR
  /// - Document has been shared with them (P2 feature), OR
  /// - They are an Administrator
  /// </remarks>
  Task<bool> CanUserAccessAsync(int documentId, int userId);
}
```

## Data Transfer Objects (DTOs)

```csharp
namespace ContosoDashboard.Services;

/// <summary>
/// Read-only DTO returned by DocumentService queries
/// </summary>
public class DocumentDto {
  public int DocumentId { get; set; }
  public string Title { get; set; }
  public string Description { get; set; }
  public string Category { get; set; }
  public string Tags { get; set; }
  public DateTime UploadedDate { get; set; }
  public long FileSize { get; set; }
  public string MimeType { get; set; }
  public string UploadedBy { get; set; } // Username
  public int? ProjectId { get; set; }
  public string ProjectName { get; set; } // If associated with project
  public bool CanEdit { get; set; } // Can current user edit? (for UI)
  public bool CanDelete { get; set; } // Can current user delete? (for UI)
  public bool CanShare { get; set; } // Can current user share? (P2)
}

/// <summary>
/// Request model for document upload
/// </summary>
public class DocumentUploadRequest {
  public IFormFile File { get; set; }
  [Required(ErrorMessage = "Document title is required")]
  [StringLength(255)]
  public string Title { get; set; }
  
  [StringLength(2000)]
  public string Description { get; set; }
  
  [Required(ErrorMessage = "Category is required")]
  [RegularExpression("^(Project Documents|Team Resources|Personal Files|Reports|Presentations|Other)$")]
  public string Category { get; set; }
  
  public int? ProjectId { get; set; }
  
  [StringLength(500)]
  public string Tags { get; set; }
  
  public int CurrentUserId { get; set; }
}

/// <summary>
/// Request model for updating document metadata
/// </summary>
public class DocumentUpdateRequest {
  [StringLength(255)]
  public string Title { get; set; }
  
  [StringLength(2000)]
  public string Description { get; set; }
  
  [RegularExpression("^(Project Documents|Team Resources|Personal Files|Reports|Presentations|Other)$")]
  public string Category { get; set; }
  
  [StringLength(500)]
  public string Tags { get; set; }
}

/// <summary>
/// Paginated result wrapper
/// </summary>
public class PaginatedResult<T> {
  public List<T> Items { get; set; }
  public int TotalCount { get; set; }
  public int PageNumber { get; set; }
  public int PageSize { get; set; }
  public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
}
```

## Implementation Notes

### Authorization Checks (IDOR Protection)

Every method MUST verify user authorization before returning document:

```csharp
// Pattern used in every method:
var document = await _context.Documents.FindAsync(documentId);
if (!await CanUserAccessAsync(documentId, currentUserId)) {
  _logger.LogWarning($"Unauthorized access attempt: User {currentUserId}, Document {documentId}");
  throw new UnauthorizedAccessException("You do not have permission to access this document");
}
```

### Error Handling

- Invalid file size/type: Return HTTP 400 with validation error
- IDOR violation: Return HTTP 403 Forbidden (not 404 to avoid information leakage)
- Document not found: Return HTTP 404
- Unexpected errors: Return HTTP 500; log full exception

### Performance Targets

- GetUserDocuments: ≤500ms for 500 documents (index on UserId, UploadedDate)
- SearchDocuments: ≤2 seconds (LIKE query with collation on Title/Description/Tags)
- GetProjectDocuments: ≤500ms (index on ProjectId, UploadedDate)
