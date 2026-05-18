# Quick Start Guide: Document Upload and Management

**Feature**: Document Upload and Management (001-document-management)  
**Target Audience**: Developers implementing document features  
**Date**: 2026-05-18

## Overview

This guide shows how to implement document management features following the ContosoDashboard Constitution (clean architecture, TDD, security-first).

## Architecture Quick Reference

```
Blazor Page (UI)
    ↓ [Authorize]
    ↓ injects IDocumentService
    ↓
DocumentService (Business Logic)
    ↓ calls IFileStorageService
    ↓ checks authorization
    ↓ logs to DocumentActivityLog
    ↓
Models/Database Layer
    ├─ Document entity
    ├─ DocumentShare entity
    └─ DocumentActivityLog entity
```

**Key Principles**:
1. Services handle ALL business logic and authorization
2. Pages only call services; never access DbContext directly
3. Authorization checks in BOTH service layer AND page level ([Authorize])
4. File storage abstracted via IFileStorageService (local now, Azure later)

---

## Setup: New Document Feature Implementation

### Step 1: Define Tests (TDD First!)

Create a test file: `ContosoDashboard.Tests/Services/DocumentServiceTests.cs`

```csharp
using Xunit;
using FluentAssertions;
using ContosoDashboard.Services;

namespace ContosoDashboard.Tests.Services;

public class DocumentServiceTests {
  private readonly DocumentService _documentService;
  private readonly IFileStorageService _fileStorageService;
  
  public DocumentServiceTests() {
    // Setup in-memory database
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
      .Options;
    
    var context = new ApplicationDbContext(options);
    _fileStorageService = new MockFileStorageService(); // Mock for testing
    _documentService = new DocumentService(context, _fileStorageService, new MockLogger());
  }

  [Fact]
  public async Task CreateAsync_WhenValidRequest_CreatesDocument() {
    // Arrange
    var userId = 1;
    var request = new DocumentUploadRequest {
      Title = "Test Document",
      Category = "Project Documents",
      File = CreateMockFormFile("test.pdf", "application/pdf", 1024)
    };
    
    // Act
    var result = await _documentService.CreateAsync(request);
    
    // Assert
    result.Should().NotBeNull();
    result.Title.Should().Be("Test Document");
    result.DocumentId.Should().BeGreaterThan(0);
  }

  [Fact]
  public async Task GetDocumentAsync_WhenUnauthorized_ThrowsUnauthorizedAccessException() {
    // Arrange
    var documentId = 1;
    var unauthorizedUserId = 999; // User who didn't upload
    
    // Act & Assert
    await _documentService.Invoking(s => s.GetDocumentAsync(documentId, unauthorizedUserId))
      .Should()
      .ThrowAsync<UnauthorizedAccessException>();
  }
  
  // ... more tests covering acceptance scenarios
}
```

### Step 2: Implement the Service

Create: `ContosoDashboard/Services/DocumentService.cs`

```csharp
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public class DocumentService : IDocumentService {
  private readonly ApplicationDbContext _context;
  private readonly IFileStorageService _fileStorageService;
  private readonly IVirusScanService _virusScanService;
  private readonly ILogger<DocumentService> _logger;
  private const int MaxFileSizeBytes = 25 * 1024 * 1024; // 25 MB

  public DocumentService(
    ApplicationDbContext context,
    IFileStorageService fileStorageService,
    IVirusScanService virusScanService,
    ILogger<DocumentService> logger) {
    _context = context;
    _fileStorageService = fileStorageService;
    _virusScanService = virusScanService;
    _logger = logger;
  }

  /// <summary>
  /// Core upload logic following TDD and security-first principles
  /// </summary>
  public async Task<DocumentDto> CreateAsync(DocumentUploadRequest request) {
    // 1. Validate input
    ValidateUploadRequest(request);

    // 2. Generate unique file path BEFORE saving to disk
    var fileName = request.File.FileName;
    var fileExtension = Path.GetExtension(fileName);
    var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
    var projectDir = request.ProjectId?.ToString() ?? "personal";
    var filePath = $"{request.CurrentUserId}/{projectDir}/{uniqueFileName}";

    // 3. Scan for viruses
    var scanResult = await _virusScanService.ScanAsync(request.File.OpenReadStream(), fileName);
    if (!scanResult.IsClean) {
      throw new InvalidOperationException($"File failed virus scan: {scanResult.ThreatName}");
    }

    // 4. Save file to disk
    request.File.OpenReadStream().Seek(0, SeekOrigin.Begin);
    await _fileStorageService.UploadAsync(request.File.OpenReadStream(), filePath);

    // 5. Create database record
    var document = new Document {
      UserId = request.CurrentUserId,
      ProjectId = request.ProjectId,
      Title = request.Title,
      Description = request.Description,
      Category = request.Category,
      Tags = request.Tags,
      UploadedDate = DateTime.UtcNow,
      FileSize = request.File.Length,
      MimeType = request.File.ContentType,
      FilePath = filePath,
      CreatedBy = GetCurrentUsername() // From claims
    };

    _context.Documents.Add(document);
    await _context.SaveChangesAsync();

    // 6. Log activity
    await LogActivityAsync(document.DocumentId, request.CurrentUserId, "Upload");

    _logger.LogInformation($"Document created: {document.DocumentId}, Title: {document.Title}");
    return MapToDto(document);
  }

  /// <summary>
  /// IDOR Protection Example: Always verify authorization
  /// </summary>
  public async Task<DocumentDto> GetDocumentAsync(int documentId, int currentUserId) {
    var document = await _context.Documents
      .FirstOrDefaultAsync(d => d.DocumentId == documentId);

    if (document == null) {
      throw new KeyNotFoundException($"Document {documentId} not found");
    }

    // Check authorization
    if (!await CanUserAccessAsync(documentId, currentUserId)) {
      _logger.LogWarning($"Unauthorized access attempt: User {currentUserId}, Document {documentId}");
      throw new UnauthorizedAccessException("You do not have permission to access this document");
    }

    // Log download if this is a preview/download
    await LogActivityAsync(documentId, currentUserId, "Download");

    return MapToDto(document);
  }

  public async Task<bool> CanUserAccessAsync(int documentId, int userId) {
    var document = await _context.Documents
      .AsNoTracking()
      .FirstOrDefaultAsync(d => d.DocumentId == documentId);

    if (document == null) return false;

    // Owner can always access
    if (document.UserId == userId) return true;

    // Project members can access project documents
    if (document.ProjectId.HasValue) {
      var isMember = await _context.ProjectMembers
        .AnyAsync(pm => pm.ProjectId == document.ProjectId && pm.UserId == userId);
      if (isMember) return true;
    }

    // Check if shared (P2 feature)
    var isShared = await _context.DocumentShares
      .AnyAsync(s => s.DocumentId == documentId && s.SharedWithUserId == userId);
    if (isShared) return true;

    // Admins can access everything
    if (await IsAdminAsync(userId)) return true;

    return false;
  }

  private void ValidateUploadRequest(DocumentUploadRequest request) {
    if (string.IsNullOrWhiteSpace(request.Title)) {
      throw new ArgumentException("Title is required");
    }

    if (request.File == null || request.File.Length == 0) {
      throw new ArgumentException("File is required");
    }

    if (request.File.Length > MaxFileSizeBytes) {
      throw new ArgumentException($"File exceeds maximum size of 25 MB");
    }

    var allowedExtensions = new[] { ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".txt", ".jpg", ".jpeg", ".png" };
    var extension = Path.GetExtension(request.File.FileName).ToLower();
    if (!allowedExtensions.Contains(extension)) {
      throw new ArgumentException($"File type {extension} is not supported");
    }

    if (string.IsNullOrWhiteSpace(request.Category)) {
      throw new ArgumentException("Category is required");
    }
  }

  private async Task LogActivityAsync(int documentId, int userId, string activityType) {
    var log = new DocumentActivityLog {
      DocumentId = documentId,
      UserId = userId,
      ActivityType = activityType,
      ActivityDate = DateTime.UtcNow
    };
    _context.DocumentActivityLogs.Add(log);
    await _context.SaveChangesAsync();
  }

  private DocumentDto MapToDto(Document document) {
    return new DocumentDto {
      DocumentId = document.DocumentId,
      Title = document.Title,
      Description = document.Description,
      Category = document.Category,
      Tags = document.Tags,
      UploadedDate = document.UploadedDate,
      FileSize = document.FileSize,
      MimeType = document.MimeType,
      UploadedBy = document.CreatedBy,
      ProjectId = document.ProjectId
    };
  }
  
  // ... implement other interface methods following same patterns
}
```

### Step 3: Create Razor Component for Upload UI

Create: `ContosoDashboard/Pages/Documents/UploadDocument.razor`

```razor
@page "/documents/upload"
@attribute [Authorize]
@inject IDocumentService DocumentService
@inject NavigationManager Nav
@inject ILogger<UploadDocument> Logger

<PageTitle>Upload Document</PageTitle>

<div class="container mt-4">
  <h3>Upload Document</h3>
  
  <EditForm Model="@uploadModel" OnValidSubmit="@HandleUploadAsync">
    <DataAnnotationsValidator />
    
    <div class="form-group mb-3">
      <label for="title">Document Title *</label>
      <InputText class="form-control" id="title" @bind-Value="uploadModel.Title" />
      <ValidationMessage For="@(() => uploadModel.Title)" class="text-danger" />
    </div>

    <div class="form-group mb-3">
      <label for="category">Category *</label>
      <InputSelect class="form-control" id="category" @bind-Value="uploadModel.Category">
        <option value="">-- Select --</option>
        @foreach(var cat in categories) {
          <option value="@cat">@cat</option>
        }
      </InputSelect>
      <ValidationMessage For="@(() => uploadModel.Category)" class="text-danger" />
    </div>

    <div class="form-group mb-3">
      <label for="description">Description</label>
      <InputTextArea class="form-control" id="description" @bind-Value="uploadModel.Description" />
    </div>

    <div class="form-group mb-3">
      <label for="file">File *</label>
      <InputFile class="form-control" id="file" OnChange="@OnFileSelectedAsync" />
    </div>

    @if (uploadProgress > 0 && uploadProgress < 100) {
      <div class="progress mb-3">
        <div class="progress-bar" role="progressbar" style="width: @uploadProgress%">
          @uploadProgress%
        </div>
      </div>
    }

    @if (!string.IsNullOrEmpty(errorMessage)) {
      <div class="alert alert-danger">@errorMessage</div>
    }

    <button type="submit" class="btn btn-primary" disabled="@isUploading">
      @if (isUploading) {
        <span class="spinner-border spinner-border-sm me-2"></span>
        <span>Uploading...</span>
      } else {
        <span>Upload Document</span>
      }
    </button>
  </EditForm>
</div>

@code {
  private DocumentUploadRequest uploadModel = new();
  private bool isUploading = false;
  private int uploadProgress = 0;
  private string errorMessage = string.Empty;
  private List<string> categories = new() {
    "Project Documents", "Team Resources", "Personal Files",
    "Reports", "Presentations", "Other"
  };

  [CascadingParameter]
  private Task<AuthenticationState> authenticationStateTask { get; set; }

  private async Task OnFileSelectedAsync(InputFileChangeEventArgs e) {
    uploadModel.File = e.File;
  }

  private async Task HandleUploadAsync() {
    try {
      isUploading = true;
      errorMessage = string.Empty;
      uploadProgress = 0;

      var authState = await authenticationStateTask;
      var userId = int.Parse(authState.User.FindFirst("sub")?.Value ?? "0");

      uploadModel.CurrentUserId = userId;
      
      // Simulate progress
      _ = Task.Run(async () => {
        for (int i = 0; i < 100; i += 10) {
          uploadProgress = i;
          await Task.Delay(100);
          StateHasChanged();
        }
      });

      var result = await DocumentService.CreateAsync(uploadModel);
      
      uploadProgress = 100;
      Logger.LogInformation($"Document uploaded: {result.DocumentId}");
      
      // Redirect to documents list
      Nav.NavigateTo("/documents");
    } catch (ArgumentException ex) {
      errorMessage = $"Upload failed: {ex.Message}";
      Logger.LogWarning($"Upload validation error: {ex.Message}");
    } catch (UnauthorizedAccessException ex) {
      errorMessage = "You do not have permission to upload documents";
      Logger.LogWarning($"Unauthorized upload attempt: {ex.Message}");
    } catch (Exception ex) {
      errorMessage = "An unexpected error occurred during upload";
      Logger.LogError($"Upload error: {ex}");
    } finally {
      isUploading = false;
      uploadProgress = 0;
    }
  }
}
```

### Step 4: Create Controller for Downloads/Previews

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase {
  private readonly IDocumentService _documentService;
  private readonly IFileStorageService _fileStorageService;

  [HttpGet("{id}/download")]
  public async Task<IActionResult> DownloadAsync(int id) {
    try {
      var userId = int.Parse(User.FindFirst("sub")?.Value ?? "0");
      var document = await _documentService.GetDocumentAsync(id, userId);
      
      var fileStream = await _fileStorageService.DownloadAsync(document.FilePath);
      
      return File(
        fileStream,
        document.MimeType,
        fileName: $"{document.Title}{Path.GetExtension(document.FilePath)}"
      );
    } catch (UnauthorizedAccessException) {
      return Forbid();
    } catch (KeyNotFoundException) {
      return NotFound();
    }
  }
}
```

---

## Testing Checklist

- [ ] Write unit tests for DocumentService (TDD first)
- [ ] Verify IDOR protection: unauthorized user cannot access document
- [ ] Verify file validation: reject files > 25MB, unsupported types
- [ ] Verify authorization: only owner/PM/admin can delete
- [ ] Test upload progress indicator in browser
- [ ] Test search across title, description, tags
- [ ] Verify audit log entries for all CRUD operations

---

## Common Patterns

### Pattern 1: Service Authorization Check

```csharp
// Every service method that returns user-specific data:
if (!await _documentService.CanUserAccessAsync(documentId, userId)) {
  throw new UnauthorizedAccessException();
}
```

### Pattern 2: File Upload Flow

```csharp
// 1. Validate → 2. Generate path → 3. Scan virus → 4. Save file → 5. Insert DB → 6. Log
```

### Pattern 3: DTOs for UI

```csharp
// Service returns DTOs, never raw entities to Razor pages
var documentDto = await _documentService.GetDocumentAsync(id, userId);
// DTO includes CanEdit, CanDelete flags for conditional UI rendering
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Unauthorized access when downloading | Ensure `CanUserAccessAsync` check in controller |
| File not found after upload | Verify `IFileStorageService.UploadAsync()` returned successful path |
| Large file uploads timeout | Check IIS/Kestrel upload size limits in appsettings |
| Search slow for large document count | Verify indexes on (UserId, UploadedDate) and (ProjectId, UploadedDate) |
| IDOR vulnerability found in review | Add `CanUserAccessAsync()` check before returning document |

---

## References

- [Data Model](data-model.md) - Entity definitions and relationships
- [Document Service Contract](contracts/document-service.md) - Service interface and DTOs
- [File Storage Contract](contracts/file-storage-service.md) - File storage abstraction
- [ContosoDashboard Constitution](../../.specify/memory/constitution.md) - Architecture principles
