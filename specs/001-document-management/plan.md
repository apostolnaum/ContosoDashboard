# Implementation Plan: Document Upload and Management

**Branch**: `001-doc-upload-mgmt` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/001-document-management/spec.md`

## Summary

The Document Upload and Management feature adds centralized, secure document storage to ContosoDashboard. Employees can upload work-related documents, organize them by category and project, and share them with team members. The feature follows a phased delivery approach:

**Phase 1 (MVP - P1)**: Upload documents, browse/search documents, download/preview documents (weeks 1-4)  
**Phase 2 (P2)**: Edit/delete documents, share with team members, project document views (weeks 5-8)  
**Phase 3 (P3)**: Task integration, dashboard widgets, reporting (weeks 9-10)

**Technical Approach**: Clean layered architecture (Models → Data → Services → Pages) with `IFileStorageService` abstraction enabling future migration to Azure Blob Storage. Local filesystem storage for training; offline-capable. Service-level IDOR protection. TDD-first implementation.

## Technical Context

**Language/Version**: C# 12 with .NET 8.0 / ASP.NET Core 8.0  
**Primary Dependencies**: Entity Framework Core 8.0, Blazor Server, Bootstrap 5.3  
**Storage**: SQL Server LocalDB (training); Azure SQL (production). Files stored on local filesystem in `AppData/uploads` directory  
**Testing**: xUnit with FluentAssertions for unit tests; Integration tests with in-memory database  
**Target Platform**: Web application (Blazor Server), runs on Windows/macOS/Linux  
**Project Type**: Web application (ASP.NET Core Blazor Server, integrated frontend + backend)  
**Performance Goals**: Upload ≤30 seconds (25 MB), list load ≤2 seconds (500 docs), search ≤2 seconds, preview ≤3 seconds  
**Constraints**: Offline-capable (no cloud services required for training), existing mock authentication system, files outside `wwwroot`  
**Scale/Scope**: 4 user roles (Employee, Team Lead, Project Manager, Administrator), ~20 Razor pages/components, 3 database entities, 7 user stories (P1/P2/P3)  
**Background Processing** (production): Azure Functions (Queue Storage trigger) for async virus scanning; training uses in-process placeholder that bypasses queue

## Constitution Check

**Gate 1: Clean Architecture** ✅ PASS  
Feature requires Models (Document, DocumentShare, ActivityLog), Data (DbContext, migrations), Services (DocumentService, FileStorageService), Pages (DocumentUpload, MyDocuments, ProjectDocuments, etc.). Layering enforced at design phase.

**Gate 2: Security-First Design** ✅ PASS  
Service layer implements `RequireAuthorizationAsync()` checks before returning documents (prevents IDOR). Page layer uses `[Authorize]` attributes. Claims-based identity per existing mock authentication system.

**Gate 3: Test-Driven Development** ✅ PASS  
Phase 1: Write acceptance test → TDD unit tests → service implementation → Blazor components. Test coverage targets 100% of service-layer business logic.

**Gate 4: Service-Layer Abstraction** ✅ PASS  
`IFileStorageService` interface (UploadAsync, DeleteAsync, DownloadAsync, GetUrlAsync) with LocalFileStorageService implementation. Future AzureBlobStorageService swappable via DI, zero UI/schema changes needed.

**Gate 5: Documentation & Training Focus** ✅ PASS  
XML comments on all public service methods. Acceptance tests map to spec scenarios. Code demonstrates abstraction patterns and cloud migration readiness.

**Status**: All gates pass. Feature fully aligned with ContosoDashboard Constitution v1.0.0.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-management/
├── plan.md                    # This file
├── spec.md                    # Feature specification with 7 user stories
├── research.md                # Phase 0 output (TBD)
├── data-model.md              # Phase 1 output (TBD)
├── contracts/                 # Phase 1 output (TBD)
│   ├── document-service.md
│   └── file-storage-service.md
├── quickstart.md              # Phase 1 output (TBD)
├── checklists/
│   └── requirements.md        # Quality validation checklist
└── tasks.md                   # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root) - ASP.NET Core Blazor Web Application

```text
ContosoDashboard/
├── Models/                    # Data contracts
│   ├── Document.cs            # NEW: Document entity
│   ├── DocumentShare.cs       # NEW: Sharing permissions
│   ├── DocumentActivityLog.cs # NEW: Audit trail
│   └── [existing User, Project, Task entities]
│
├── Data/
│   ├── ApplicationDbContext.cs # Updated with DbSet<Document>, migrations
│   └── [existing entity configurations]
│
├── Services/                  # Business logic
│   ├── IFileStorageService.cs # NEW: Interface for file operations
│   ├── LocalFileStorageService.cs # NEW: Local filesystem implementation
│   ├── DocumentService.cs     # NEW: Document CRUD + authorization
│   ├── DocumentShareService.cs # NEW: Sharing logic (P2)
│   ├── DocumentActivityService.cs # NEW: Audit logging
│   ├── IVirusScanQueueService.cs # NEW: Queue producer interface
│   ├── LocalVirusScanQueueService.cs # NEW: In-process placeholder (training)
│   ├── AzureVirusScanQueueService.cs # NEW: Azure Queue Storage producer (production)
│   └── [existing UserService, ProjectService, TaskService]
│
├── Functions/                 # Azure Functions (production; separate deployment unit)
│   └── VirusScanFunction/
│       ├── VirusScanFunction.cs      # Queue-triggered Azure Function
│       ├── VirusScanMessage.cs       # Queue message DTO
│       └── host.json                 # Function host config
│
├── Pages/                     # Blazor Server UI components
│   ├── Documents/             # NEW subdirectory
│   │   ├── MyDocuments.razor
│   │   ├── UploadDocument.razor
│   │   ├── DocumentDetails.razor
│   │   ├── ProjectDocuments.razor
│   │   ├── SharedWithMe.razor  # P2
│   │   └── EditDocument.razor  # P2
│   ├── [existing Index, Tasks, Projects, Team pages]
│   └── Shared/
│       ├── MainLayout.razor    # Updated with Documents nav link
│       └── [existing components]
│
├── wwwroot/
│   ├── css/
│   │   └── documents.css       # NEW: Document page styles
│   └── js/
│       └── file-upload.js      # NEW: Upload progress handling
│
├── Migrations/
│   └── [New migrations for Document entities]
│
└── ContosoDashboard.csproj     # Updated with new package refs if needed
```

**Structure Decision**: ASP.NET Core Blazor Server web application. No separate frontend/backend; UI and services colocated in Pages/Services. Database schema extended with 3 new entities. Files stored in `AppData/uploads/` (outside `wwwroot` for security).

Background virus scanning is handled in-process (synchronous placeholder) during training. In production, upload enqueues a `VirusScanMessage` to Azure Queue Storage; a separate Azure Functions worker dequeues and processes it asynchronously, then updates `Document.ScanStatus`.

## Background Processing: Async Virus Scanning

### Architecture Overview

```
Training (in-process, synchronous)
──────────────────────────────────
Upload Request
  → DocumentService.CreateAsync()
  → PlaceholderVirusScanService.ScanAsync()  [logs + returns IsClean=true]
  → Save file → Insert DB (ScanStatus="Approved")
  → Return DocumentDto to UI


Production (async, queue-based)
──────────────────────────────────
Upload Request
  → DocumentService.CreateAsync()
  → Save file → Insert DB (ScanStatus="Pending")
  → IVirusScanQueueService.EnqueueAsync(message)  [non-blocking]
  → Return DocumentDto to UI (document visible but marked Pending)

  ... later ...

  Azure Queue Storage receives VirusScanMessage
  → VirusScanFunction (Queue trigger) dequeues message
  → Calls real antivirus API (ClamAV / Defender for Storage)
  → DocumentService.UpdateScanStatusAsync(documentId, scanResult)
  → Document.ScanStatus updated to "Approved" or "Quarantined"
  → Quarantined documents hidden from download/preview
```

### Document.ScanStatus Field

A `ScanStatus` column is added to the `Document` entity:

| Value | Meaning |
|-------|---------|
| `"Pending"` | File uploaded; scan not yet complete |
| `"Approved"` | Scan passed; file accessible for download/preview |
| `"Quarantined"` | Scan flagged threat; file blocked; admin notified |

**Training behavior**: `CreateAsync` sets `ScanStatus = "Approved"` immediately (placeholder bypasses queue).

**UI behavior**: Documents with `ScanStatus = "Pending"` display a "Scanning..." badge; download/preview are disabled until `"Approved"`. Quarantined documents show an admin-only warning.

### Queue Message Schema

```csharp
/// <summary>
/// Enqueued to Azure Queue Storage after successful file upload.
/// Consumed by VirusScanFunction Azure Function.
/// </summary>
public class VirusScanMessage {
  public int DocumentId { get; set; }
  public string FilePath { get; set; }   // Same GUID-based path stored in DB
  public string MimeType { get; set; }
  public DateTime EnqueuedAt { get; set; }
}
```

### Azure Function: VirusScanFunction

```csharp
public class VirusScanFunction {
  private readonly IVirusScanService _scanner;      // ClamAV / Defender for Storage
  private readonly IDocumentService _documentService;
  private readonly ILogger<VirusScanFunction> _logger;

  [Function(nameof(VirusScanFunction))]
  public async Task Run(
    [QueueTrigger("virus-scan-queue", Connection = "AzureWebJobsStorage")] VirusScanMessage message,
    FunctionContext context) {

    _logger.LogInformation($"Scanning document {message.DocumentId}: {message.FilePath}");

    try {
      var stream = await _fileStorageService.DownloadAsync(message.FilePath);
      var result = await _scanner.ScanAsync(stream, message.FilePath);

      var status = result.IsClean ? "Approved" : "Quarantined";
      await _documentService.UpdateScanStatusAsync(message.DocumentId, status, result.ThreatName);

      if (!result.IsClean) {
        _logger.LogWarning($"Threat detected in document {message.DocumentId}: {result.ThreatName}");
        // Optional: trigger admin notification via NotificationService
      }
    } catch (Exception ex) {
      // Poison-message handling: Azure retries 5 times, then moves to poison queue
      _logger.LogError($"Scan failed for document {message.DocumentId}: {ex.Message}");
      throw; // Re-throw so Azure Functions retries
    }
  }
}
```

### Queue Service Interface

```csharp
public interface IVirusScanQueueService {
  /// <summary>Enqueues a virus scan request; non-blocking.</summary>
  Task EnqueueAsync(VirusScanMessage message, CancellationToken ct = default);
}

// Training: in-process, no actual queue
public class LocalVirusScanQueueService : IVirusScanQueueService {
  public Task EnqueueAsync(VirusScanMessage message, CancellationToken ct = default)
    => Task.CompletedTask; // No-op; DocumentService sets Approved directly
}

// Production: Azure Queue Storage
public class AzureVirusScanQueueService : IVirusScanQueueService {
  private readonly QueueClient _queue;
  public async Task EnqueueAsync(VirusScanMessage message, CancellationToken ct = default) {
    var json = JsonSerializer.Serialize(message);
    await _queue.SendMessageAsync(
      BinaryData.FromString(json),
      visibilityTimeout: TimeSpan.Zero,
      timeToLive: TimeSpan.FromDays(7),
      cancellationToken: ct);
  }
}
```

### Configuration

```json
// appsettings.json (training)
{
  "VirusScan": { "Provider": "Local" },
  "FileStorage": { "Provider": "Local" }
}

// appsettings.Production.json
{
  "VirusScan": { "Provider": "Azure" },
  "FileStorage": { "Provider": "Azure" },
  "AzureStorage": {
    "ConnectionString": "<from Key Vault>",
    "ContainerName": "documents",
    "ScanQueueName": "virus-scan-queue"
  }
}
```

### Migration: Add ScanStatus to Document Table

```csharp
protected override void Up(MigrationBuilder migrationBuilder) {
  migrationBuilder.AddColumn<string>(
    name: "ScanStatus",
    table: "Documents",
    maxLength: 20,
    nullable: false,
    defaultValue: "Approved"); // Default keeps existing records accessible
}
```

### Constitution Alignment

- **Clean Architecture**: Queue producer (`IVirusScanQueueService`) lives in Services layer; Azure Function is a separate deployment unit, not coupled to Blazor UI
- **Security-First**: Documents with `ScanStatus = "Pending"` block download/preview; quarantined files permanently blocked
- **Service-Layer Abstraction**: `IVirusScanQueueService` enables swap from in-process (training) to Azure Queue Storage (production) without changing DocumentService
- **TDD**: VirusScanFunction tested with Azure Functions SDK test harness; mock `IVirusScanService` returns clean/threat results

---

## Complexity Tracking

No constitutional violations. Feature fully compliant with all core principles.

---

## Phase 0: Research & Clarification *(In Progress)*

### Research Tasks

1. **File Storage Security Patterns**: Validate GUID-based file naming, path traversal prevention, authorization check patterns in ASP.NET Core
2. **Blazor File Upload UI**: Best practices for upload progress indicators, form validation, error handling in Blazor Server
3. **EF Core Indexing**: Performance targets for searches on (UserId, ProjectId, UploadedDate) with LIKE queries on Title/Description/Tags
4. **Browser Preview APIs**: HTML5 PDF/image preview capabilities; unsupported format fallback strategy
5. **Virus Scanning Integration**: Placeholder antivirus service interface design for future ClamAV/VirusTotal integration

### Deliverable

**research.md** will document findings, best practices, and decisions for each task.

---

## Phase 1: Design & Contracts *(In Progress)*

### 1. Data Model Design

**research.md** prerequisite: ✅ Complete

Entities to define in **data-model.md**:
- **Document**: Fields (DocumentId, UserId, ProjectId, Title, Description, Category, Tags, UploadedDate, FileSize, MimeType, FilePath, CreatedBy)
- **DocumentShare**: Fields (ShareId, DocumentId, SharedWithUserId, SharedDate, SharedBy)
- **DocumentActivityLog**: Fields (ActivityId, DocumentId, UserId, ActivityType, ActivityDate)
- Relationships: User ← Document (one-to-many), Project ← Document (one-to-many, optional), Document → DocumentShare (one-to-many)
- Validation rules: Title required, Category enum (6 values), FileSize > 0, FilePath non-null
- Indexes: (UserId, UploadedDate DESC), (ProjectId, UploadedDate DESC), (DocumentId)

### 2. Service Contracts

**contracts/** directory with interface definitions:

- **IFileStorageService.cs**: Methods (UploadAsync, DeleteAsync, DownloadAsync, GetUrlAsync)
- **DocumentService.cs Interface**: Methods (GetUserDocumentsAsync, SearchAsync, GetDocumentAsync, CreateAsync, UpdateAsync, DeleteAsync)
- **DocumentShareService.cs Interface**: Methods (ShareAsync, UnshareAsync, GetSharedDocumentsAsync)
- **Antivirus Placeholder**: IVirusScanService (ScanAsync) with no-op implementation

### 3. Quick Start Guide

**quickstart.md** will provide:
- How to enable document upload on a new Blazor page
- How to inject DocumentService and call GetUserDocumentsAsync
- How to authorize downloads via service
- How to test locally with sample documents

### 4. Agent Context Update

Update **`.github/copilot-instructions.md`** with reference to `/specs/001-document-management/plan.md` in the SPECKIT section.

---

## Phase 2: Task Generation *(Pending /speckit.tasks)*

Will be generated by `/speckit.tasks` command using:
- User stories and acceptance scenarios from spec.md
- Entity/service/UI structures from data-model.md and contracts/
- Prioritization (P1/P2/P3) to enable phased delivery

---

## Next Steps

1. **Phase 0 Research** (this session): Research file storage, Blazor upload UX, indexing strategy, preview APIs, virus scanning patterns
2. **Phase 1 Design** (this session): Generate data-model.md, contracts/, quickstart.md
3. **Phase 1 Completion**: Update agent context in `.github/copilot-instructions.md`
4. **Phase 2**: Run `/speckit.tasks` to generate actionable task list with dependencies
5. **Implementation**: Teams begin with P1 (upload, search, download) in parallel
