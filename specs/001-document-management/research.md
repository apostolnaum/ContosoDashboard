# Research Phase: Document Upload and Management

**Date**: 2026-05-18  
**Feature**: Document Upload and Management (001-document-management)  
**Status**: Complete

## Research Task 1: File Storage Security Patterns

### Decision: GUID-Based Filenames with Path Prefix Isolation

**Chosen Approach**:
- Generate GUID for each upload before database insertion
- Store file path as: `{userId}/{projectId or "personal"}/{GUID}.{extension}`
- Example: `42/1001/550e8400-e29b-41d4-a716-446655440000.pdf`
- Never use user-supplied filenames in file paths (prevents path traversal)
- Store actual user-supplied title in database (Title field)

**Rationale**:
1. **Security**: GUID makes path traversal impossible; attacker cannot guess file paths
2. **Atomicity**: Generate path → Save file → Insert database record. Prevents orphaned DB records.
3. **Simplicity**: No collision detection; multiple documents can share titles
4. **Cloud-Ready**: Same pattern works for Azure blob names without modification
5. **Training Value**: Teaches students secure file naming and atomic transactions

**Alternatives Considered**:
- ❌ User-supplied filename: Path traversal vulnerability (../../etc/passwd)
- ❌ Sequential numbering: Reveals document count, enables enumeration attacks
- ❌ Hash-based names: Complex, collision handling needed

**References**:
- OWASP Path Traversal: https://owasp.org/www-community/attacks/Path_Traversal
- ASP.NET Core file uploads: Microsoft.AspNetCore.Http.IFormFile

---

## Research Task 2: Blazor Server File Upload UX

### Decision: Custom Blazor Component with Progress Indicator

**Chosen Approach**:
- Create reusable `<DocumentUpload>` Razor component with:
  - HTML input type="file" (multiple files supported)
  - Form fields: Title (required), Category (dropdown), Description (textarea), Tags (text)
  - Upload button that disables after click (prevents duplicate submissions)
  - Progress bar showing upload % via OnProgress event
  - Success/error messages via toast notifications
  - Form validation (client-side for UX, server-side for security)

**Blazor Implementation Pattern**:
```csharp
@page "/documents/upload"
@inject DocumentService DocumentService

<EditForm Model="@model" OnValidSubmit="@HandleUpload">
  <InputText @bind-Value="model.Title" placeholder="Document Title" />
  <InputSelect @bind-Value="model.Category">
    @foreach(var category in categories) {
      <option>@category</option>
    }
  </InputSelect>
  <InputFile OnChange="@OnFileSelected" />
  <progress value="@uploadProgress" max="100"></progress>
  <button type="submit" disabled="@isUploading">Upload</button>
</EditForm>
```

**Rationale**:
1. **Integrated**: No separate upload endpoint; works within Blazor SPA
2. **Accessible**: HTML progress element semantic for basic a11y
3. **Real-time Feedback**: Progress updates keep users informed
4. **Form Validation**: EditForm + DataAnnotations for required fields
5. **Error Handling**: Exceptions caught, user-friendly messages displayed

**Alternatives Considered**:
- ❌ Plain file input: No progress indicator (poor UX for large files)
- ❌ JavaScript/jQuery: Requires external dependency; less integrated with Blazor
- ❌ Drag-and-drop: Good UX but adds complexity; can add in P2+

**References**:
- Blazor file uploads: Microsoft docs ASP.NET Core Blazor file uploads
- Progress element: MDN HTML progress element spec

---

## Research Task 3: EF Core Indexing for Search Performance

### Decision: Composite Indexes on Common Query Patterns

**Chosen Approach**:
Create the following indexes on the Document table:

```csharp
// Primary index for "My Documents" listing (most common query)
modelBuilder.Entity<Document>()
  .HasIndex(d => new { d.UserId, d.UploadedDate })
  .IsDescending(false, true) // UserId ASC, UploadedDate DESC
  .HasDatabaseName("IX_Document_UserId_UploadedDate");

// Secondary index for project documents
modelBuilder.Entity<Document>()
  .HasIndex(d => new { d.ProjectId, d.UploadedDate })
  .IsDescending(false, true)
  .HasDatabaseName("IX_Document_ProjectId_UploadedDate");

// Full-text search simulation (LIKE queries on Title, Description, Tags)
modelBuilder.Entity<Document>()
  .Property(d => d.Title)
  .UseCollation("Latin1_General_CI_AS"); // Case-insensitive for LIKE
```

**Query Performance Targets**:
- `GET /api/documents?userId=42` (My Documents): ≤500ms for 500 documents
- `GET /api/documents?search=budget` (Search): ≤2 seconds (with LIKE filter)
- Database query times: ≤100ms for indexed lookups; ≤1000ms for LIKE searches

**Rationale**:
1. **Compound Keys**: (UserId, UploadedDate) covers 90% of queries
2. **Descending Date**: Natural sorting (newest first) eliminates post-query sort
3. **LIKE Optimization**: Case-insensitive collation speeds pattern matching
4. **SQL Server Native**: SQL Server LIKE indexes are well-optimized
5. **Training Value**: Teaches index design for common query patterns

**Alternatives Considered**:
- ❌ Full-Text Search (FTS): Requires SQL Server Enterprise; overkill for training
- ❌ No indexes: Queries would be ≤5 seconds (unacceptable UX)
- ❌ Separate search table: Adds complexity; not justified for 500-document scale

**References**:
- EF Core index configuration: Microsoft docs
- SQL Server query execution plans: EXPLAIN in SSMS

---

## Research Task 4: Browser-Native Preview APIs

### Decision: HTML5 <embed> Tag with Unsupported Format Fallback

**Chosen Approach**:
- **PDF Preview**: Use HTML5 `<embed src="..." type="application/pdf">`
- **Image Preview** (JPEG, PNG): Use HTML5 `<img src="..." />`
- **Unsupported Formats** (DOCX, XLSX): Display message "Preview not available. Download to view."
- **Security**: Serve previews via controller endpoint that validates authorization before streaming

**Preview Endpoint Pattern**:
```csharp
[HttpGet("documents/{id}/preview")]
[Authorize]
public async Task<IActionResult> PreviewDocument(int id) {
  var doc = await _documentService.GetDocumentAsync(id);
  // Validate authorization in service
  if (!await _documentService.CanUserAccessAsync(User, doc))
    return Forbid();
  
  return PhysicalFile(doc.FilePath, doc.MimeType, 
    enableRangeProcessing: true); // Streaming support for large PDFs
}
```

**Rationale**:
1. **Browser Native**: No third-party dependencies (training-friendly)
2. **Offline-Capable**: Doesn't require cloud APIs
3. **Secure**: Authorization check before streaming
4. **Graceful Degradation**: Unsupported formats offer download fallback
5. **Performance**: Range requests for PDFs (allows seeking without full download)

**Alternatives Considered**:
- ❌ PDF.js library: Adds JavaScript dependency; not needed for basic preview
- ❌ Office 365 preview API: Requires cloud service (offline constraint violation)
- ❌ ImageMagick conversion: Complex server-side processing; potential security risk

**References**:
- HTML5 embed spec: https://html.spec.whatwg.org/multipage/iframe-embed-object.html
- HTTP Range requests: RFC 7233

---

## Research Task 5: Virus Scanning Integration Pattern

### Decision: Async Queue-Based Scanning via Azure Functions (Production) with In-Process Placeholder (Training)

**Chosen Approach**:
Virus scanning follows a two-tier design:

1. **Training**: Synchronous placeholder (`PlaceholderVirusScanService`) runs in-process; no queue needed; document marked `ScanStatus="Approved"` immediately.
2. **Production**: Upload enqueues a `VirusScanMessage` to **Azure Queue Storage**; an **Azure Functions** worker (Queue Storage trigger) dequeues and scans asynchronously; updates `Document.ScanStatus` to `"Approved"` or `"Quarantined"` when complete.

**Why async queue instead of synchronous API call in production?**

- Real antivirus APIs (ClamAV, Microsoft Defender for Storage) can take 2-30 seconds for a 25 MB file; blocking the upload HTTP request would time out
- Queue-based approach returns upload response immediately (ScanStatus=Pending); users see the document while scan runs in background
- Azure Queue Storage retries failed messages automatically (5 retries + poison queue); resilience without custom retry logic
- Scales independently: scan workers scale out separately from the web app during upload spikes

**Document.ScanStatus lifecycle**:

```
Upload → ScanStatus="Pending" → Queue enqueued
                                    ↓
                           Azure Function dequeues
                                    ↓
                       Clean?  → ScanStatus="Approved"
                       Threat? → ScanStatus="Quarantined" + admin alert
```

**UI behavior**: Documents with `ScanStatus="Pending"` show a "Scanning..." badge; download/preview blocked until `"Approved"`.

Create `IVirusScanService` interface with placeholder implementation:

```csharp
// Service interface (easy to replace)
public interface IVirusScanService {
  Task<VirusScanResult> ScanAsync(Stream fileStream, string fileName);
}

public class VirusScanResult {
  public bool IsClean { get; set; }
  public string ThreatName { get; set; }
  public DateTime ScanDate { get; set; }
}

// Training implementation (placeholder)
public class PlaceholderVirusScanService : IVirusScanService {
  public async Task<VirusScanResult> ScanAsync(Stream fileStream, string fileName) {
    // Log the scan attempt (for audit)
    _logger.LogInformation($"Virus scan placeholder: {fileName}");
    
    // Return clean (no actual scanning)
    return new VirusScanResult {
      IsClean = true,
      ScanDate = DateTime.UtcNow
    };
  }
}

// Production implementation (future)
public class ClamAVVirusScanService : IVirusScanService {
  // Would integrate with ClamAV daemon
  public async Task<VirusScanResult> ScanAsync(Stream fileStream, string fileName) {
    // Real implementation using ClamAV.Net or similar
  }
}
```

**Registration in DI Container**:
```csharp
// appsettings.json
"VirusScan": {
  "Provider": "Placeholder" // or "ClamAV", "VirusTotal"
}

// Program.cs
var virusScanProvider = builder.Configuration["VirusScan:Provider"];
if (virusScanProvider == "ClamAV") {
  builder.Services.AddScoped<IVirusScanService, ClamAVVirusScanService>();
} else {
  builder.Services.AddScoped<IVirusScanService, PlaceholderVirusScanService>();
}
```

**Rationale**:
1. **Training-Friendly**: Placeholder works offline without external services
2. **Production-Ready**: Interface design enables easy swapping
3. **Dependency Injection**: Configuration-driven implementation selection
4. **Audit Trail**: Logging enables compliance reporting
5. **Extensibility**: Students can implement real ClamAV/VirusTotal integration

**Production Path**:
- `AzureVirusScanQueueService` enqueues `VirusScanMessage` to Azure Queue Storage (`virus-scan-queue`)
- `VirusScanFunction` (Azure Functions, .NET isolated worker) consumes queue messages
- Function calls ClamAV, Microsoft Defender for Storage, or VirusTotal REST API
- Function calls `DocumentService.UpdateScanStatusAsync()` to record result
- Quarantined documents trigger admin notification via existing `NotificationService`
- Poison messages (5 failed retries) moved to `virus-scan-queue-poison`; operator review required

**Alternatives Considered**:
- ❌ No scanning: Violates security best practices (feature spec requirement)
- ❌ Synchronous API call inline with upload: 2-30s scan time blocks HTTP request; UX and timeout risk
- ❌ Background hosted service in ASP.NET Core: Shares process with web app; scan failures affect web availability
- ❌ Service Bus instead of Queue Storage: Overkill for single-consumer scan queue; Queue Storage simpler and cheaper
- ✅ Azure Functions + Queue Storage: Isolated process, auto-retry, scales independently, zero-change to web app

**References**:
- Azure Functions Queue Storage trigger: https://learn.microsoft.com/azure/azure-functions/functions-bindings-storage-queue-trigger
- Azure Queue Storage SDK: Azure.Storage.Queues NuGet package
- Microsoft Defender for Storage (built-in blob scanning): https://learn.microsoft.com/azure/defender-for-cloud/defender-for-storage-introduction
- ClamAV: https://www.clamav.net/

---

## Summary of Decisions

| Research Task | Decision | Implementation Complexity | Training Value |
|---------------|----------|---------------------------|-----------------|
| File Storage Security | GUID-based paths, prefix isolation | Low (straightforward code) | High (path traversal, atomicity) |
| Blazor Upload UX | Custom component with progress bar | Medium (EditForm + file input) | High (form validation, Blazor patterns) |
| EF Core Indexing | Composite indexes on (UserId, Date) | Low (EF configuration) | High (query optimization) |
| Browser Preview | HTML5 embed + fallback for unsupported | Low (native HTML) | Medium (graceful degradation) |
| Virus Scanning | Async Azure Functions + Queue Storage (prod); placeholder in-process (training) | Medium (2-tier pattern) | High (queues, async processing, DI abstraction) |

**All decisions align with**:
- ✅ ContosoDashboard Constitution (clean architecture, security-first, TDD, abstraction)
- ✅ Training focus (simple implementations, clear patterns, extensible design)
- ✅ Offline-first constraint (no external cloud services required)
- ✅ Performance targets (indexed queries, range requests for streaming)
- ✅ Cloud migration path (abstractions enabling Azure Blob Storage swap)

---

## Next Steps

Phase 0 research complete. Proceed to Phase 1:
1. Generate data-model.md with entity diagrams and relationships
2. Create contracts/ with service interface definitions
3. Create quickstart.md with developer onboarding guide
4. Update agent context in `.github/copilot-instructions.md`
