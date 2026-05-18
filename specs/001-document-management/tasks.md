---

description: "Task list for Document Upload and Management feature implementation"
---

# Tasks: Document Upload and Management

**Input**: Design documents from `/specs/001-document-management/`  
**Prerequisites**: plan.md ✅, spec.md ✅, data-model.md ✅, contracts/ ✅, research.md ✅, quickstart.md ✅

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. P1 stories form the MVP; P2 and P3 can be developed in parallel after Phase 2 is complete.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no shared dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US7)
- File paths follow the ASP.NET Core Blazor Server project structure defined in plan.md

---

## Phase 1: Setup

**Purpose**: Project initialization — directory structure, packages, database scaffolding

- [X] T001 Create `ContosoDashboard/Models/Document.cs` with all fields per data-model.md (DocumentId, UserId, ProjectId, Title, Description, Category, Tags, UploadedDate, FileSize, MimeType, FilePath, CreatedBy, ScanStatus)
- [X] T002 [P] Create `ContosoDashboard/Models/DocumentShare.cs` with ShareId, DocumentId, SharedWithUserId, SharedDate, SharedBy fields
- [X] T003 [P] Create `ContosoDashboard/Models/DocumentActivityLog.cs` with ActivityId, DocumentId, UserId, ActivityType, ActivityDate fields
- [X] T004 Add `DbSet<Document>`, `DbSet<DocumentShare>`, `DbSet<DocumentActivityLog>` to `ContosoDashboard/Data/ApplicationDbContext.cs` with FK relationships, unique constraints, and composite indexes per data-model.md
- [X] T005 Create EF Core migration `AddDocumentManagement` and apply to database (`dotnet ef migrations add AddDocumentManagement && dotnet ef database update`)
- [X] T006 [P] Create `ContosoDashboard/Services/IFileStorageService.cs` interface with UploadAsync, DownloadAsync, DeleteAsync, GetUrlAsync per contracts/file-storage-service.md
- [X] T007 [P] Create `ContosoDashboard/Services/IVirusScanQueueService.cs` interface with EnqueueAsync(VirusScanMessage) per plan.md Background Processing section
- [X] T008 [P] Create `ContosoDashboard/Services/VirusScanMessage.cs` DTO (DocumentId, FilePath, MimeType, EnqueuedAt)
- [X] T009 Create `ContosoDashboard/Services/LocalFileStorageService.cs` implementing IFileStorageService using System.IO, storing files in AppData/ContosoDashboard/uploads/{userId}/{projectId|personal}/{guid}.{ext}
- [X] T010 [P] Create `ContosoDashboard/Services/LocalVirusScanQueueService.cs` implementing IVirusScanQueueService as no-op (training placeholder; returns Task.CompletedTask)
- [X] T011 Register IFileStorageService → LocalFileStorageService and IVirusScanQueueService → LocalVirusScanQueueService in `ContosoDashboard/Program.cs` via dependency injection
- [X] T012 Create `ContosoDashboard/Pages/Documents/` subdirectory structure (empty folder placeholder for Razor components)
- [X] T013 [P] Add nav link "Documents" to `ContosoDashboard/Shared/NavMenu.razor` pointing to `/documents`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core DocumentService infrastructure all user stories depend on  
**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T014 Create `ContosoDashboard/Services/DocumentService.cs` with constructor injection of ApplicationDbContext, IFileStorageService, IVirusScanQueueService, ILogger; implement CanUserAccessAsync (owner check, project member check via ProjectMembers table, admin role check)
- [X] T015 Implement `DocumentService.LogActivityAsync` private helper (inserts DocumentActivityLog record for Upload, Download, Delete, Share, Edit activity types)
- [X] T016 [P] Create `ContosoDashboard/Services/DocumentDto.cs` with all DTO fields per contracts/document-service.md (DocumentId, Title, Description, Category, Tags, UploadedDate, FileSize, MimeType, UploadedBy, ProjectId, ProjectName, ScanStatus, CanEdit, CanDelete, CanShare)
- [X] T017 [P] Create `ContosoDashboard/Services/DocumentUploadRequest.cs` with IFormFile File, Title, Description, Category, ProjectId, Tags, CurrentUserId and DataAnnotations validation attributes
- [X] T018 [P] Create `ContosoDashboard/Services/DocumentUpdateRequest.cs` with Title, Description, Category, Tags fields and DataAnnotations
- [X] T019 [P] Create `ContosoDashboard/Services/PaginatedResult.cs` generic wrapper with Items, TotalCount, PageNumber, PageSize, TotalPages
- [X] T020 Implement `DocumentService.MapToDto` private helper mapping Document entity → DocumentDto including CanEdit/CanDelete/CanShare flags based on current user role
- [X] T021 Implement `DocumentService.ValidateUploadRequest` private helper: file size ≤25MB, extension whitelist (.pdf/.docx/.doc/.xlsx/.xls/.pptx/.txt/.jpg/.jpeg/.png), required fields (Title, Category), category enum validation

**Checkpoint**: Foundation complete — all user story services can be implemented

---

## Phase 3: User Story 1 — Upload Personal Documents (Priority: P1) 🎯 MVP

**Goal**: Employees can upload documents with metadata, with validation and virus scan placeholder  
**Independent Test**: Upload a PDF via the form, verify it appears in My Documents with correct metadata and ScanStatus="Approved"

### Implementation for User Story 1

- [X] T022 [US1] Implement `DocumentService.CreateAsync`: validate request → generate GUID file path → enqueue virus scan message (IVirusScanQueueService) → save file (IFileStorageService.UploadAsync) → insert Document record with ScanStatus="Approved" (training) → call LogActivityAsync("Upload") → return DocumentDto
- [X] T023 [US1] Create `ContosoDashboard/Pages/Documents/UploadDocument.razor` (`@page "/documents/upload"`, `[Authorize]`): EditForm with Title (InputText), Category (InputSelect from 6 values), Description (InputTextArea), Tags (InputText), ProjectId (InputSelect from user's projects), InputFile for file selection
- [X] T024 [US1] Add upload progress bar (Bootstrap progress element) and loading spinner to UploadDocument.razor; disable submit button while isUploading=true to prevent duplicate submissions
- [X] T025 [US1] Add client-side file validation to UploadDocument.razor: display inline error if file exceeds 25 MB or has unsupported extension before form submission
- [X] T026 [US1] Add success toast and error alert to UploadDocument.razor; redirect to `/documents` on success
- [X] T027 [US1] Add `wwwroot/js/file-upload.js` with JS interop helper for tracking upload progress via Blazor InputFile events

**Checkpoint**: User Story 1 complete — upload works end-to-end, metadata captured, ScanStatus shown

---

## Phase 4: User Story 2 — Browse and Search Documents (Priority: P1)

**Goal**: Employees can view their documents in a sortable, filterable list and search across title/description/tags  
**Independent Test**: Seed 10 documents across 3 categories; verify filter by category, sort by date, and search by tag each return correct results within 2 seconds

### Implementation for User Story 2

- [X] T028 [US2] Implement `DocumentService.GetUserDocumentsAsync`: query Documents where UserId == currentUserId, ordered by UploadedDate DESC, with pagination; project to DocumentDto including ProjectName via Include(d => d.Project)
- [X] T029 [US2] Implement `DocumentService.SearchDocumentsAsync`: filter Documents accessible to currentUserId (owner OR project member OR shared OR admin) where Title.Contains OR Description.Contains OR Tags.Contains search term; order by UploadedDate DESC; paginate
- [X] T030 [US2] Create `ContosoDashboard/Pages/Documents/MyDocuments.razor` (`@page "/documents"`, `[Authorize]`): display documents table with columns Title, Category, Upload Date, File Size, Project; default sort = UploadedDate DESC
- [X] T031 [US2] Add column header click sorting to MyDocuments.razor (Title ASC/DESC, UploadedDate ASC/DESC, Category ASC/DESC, FileSize ASC/DESC); show sort indicator arrow on active column
- [X] T032 [US2] Add filter controls to MyDocuments.razor: Category dropdown (all 6 values + "All"), Project dropdown (user's projects + "All"), Date Range (from/to date pickers); filters apply immediately on change
- [X] T033 [US2] Add search input to MyDocuments.razor; debounce 300ms; call SearchDocumentsAsync; show result count ("X documents found"); show "No results" empty state with clear-search link
- [X] T034 [US2] Add pagination controls to MyDocuments.razor (Previous/Next/page numbers); page size 20; show total document count

**Checkpoint**: User Story 2 complete — list, filter, sort, search all functional independently

---

## Phase 5: User Story 3 — Download and Preview Documents (Priority: P1)

**Goal**: Employees can download any accessible document and preview PDFs/images in-browser  
**Independent Test**: Upload a PDF and a JPEG; verify PDF previews inline, JPEG previews inline, and DOCX shows "Preview unavailable" with download fallback

### Implementation for User Story 3

- [X] T035 [US3] Implement `DocumentService.GetDocumentAsync`: fetch document, call CanUserAccessAsync (throw UnauthorizedAccessException if false), call LogActivityAsync("Download"), return DocumentDto
- [X] T036 [US3] Create `ContosoDashboard/Controllers/DocumentsController.cs` (`[ApiController]`, `[Authorize]`, route `/api/documents`): GET `{id}/download` endpoint — call DocumentService.GetDocumentAsync, stream file via IFileStorageService.DownloadAsync, return File() with original filename; return 403 for UnauthorizedAccessException, 404 for KeyNotFoundException
- [X] T037 [US3] Add GET `{id}/preview` endpoint to DocumentsController: same authorization check; return file stream with enableRangeProcessing=true for PDFs; return 415 Unsupported Media Type for non-previewable MIME types
- [X] T038 [US3] Create `ContosoDashboard/Pages/Documents/DocumentDetails.razor` (`@page "/documents/{Id:int}"`, `[Authorize]`): show document metadata (title, description, category, tags, uploader, date, size, ScanStatus badge); Download button; Preview panel using `<embed>` for PDFs and `<img>` for images; "Preview unavailable" message with Download fallback for other types
- [X] T039 [US3] Add Download button to each row in MyDocuments.razor linking to `/api/documents/{id}/download`
- [X] T040 [US3] Show "Scanning..." badge (Bootstrap warning badge) on documents with ScanStatus="Pending"; disable Download and Preview buttons until ScanStatus="Approved"; show "Quarantined" error badge for ScanStatus="Quarantined"

**Checkpoint**: User Story 1 + 2 + 3 complete — full P1 MVP is independently functional and testable

---

## Phase 6: User Story 4 — Edit and Delete Documents (Priority: P2)

**Goal**: Document owners (and PMs) can update metadata, replace files, and permanently delete documents  
**Independent Test**: Edit title/category, verify changes persist; delete with confirmation, verify file removed from disk and DB record gone

### Implementation for User Story 4

- [X] T041 [P] [US4] Implement `DocumentService.UpdateAsync`: verify currentUserId == document.UserId (throw UnauthorizedAccessException otherwise); update Title/Description/Category/Tags; save; call LogActivityAsync("Edit"); return updated DocumentDto
- [X] T042 [P] [US4] Implement `DocumentService.ReplaceFileAsync`: verify owner or PM; validate new file (size, type); generate new GUID path; save new file; delete old file via IFileStorageService.DeleteAsync; update FilePath, FileSize, MimeType, ScanStatus="Approved"; save; LogActivityAsync("Edit")
- [X] T043 [US4] Implement `DocumentService.DeleteAsync`: check permission (owner, PM of associated project, or admin); delete file via IFileStorageService.DeleteAsync; remove Document from DB (cascades to DocumentShares); LogActivityAsync("Delete"); return true
- [X] T044 [US4] Create `ContosoDashboard/Pages/Documents/EditDocument.razor` (`@page "/documents/{Id:int}/edit"`, `[Authorize]`): pre-populated EditForm with Title, Description, Category, Tags fields; Save button calls DocumentService.UpdateAsync; Cancel returns to DocumentDetails
- [X] T045 [US4] Add "Replace File" section to EditDocument.razor: InputFile component; on submit calls DocumentService.ReplaceFileAsync; shows validation errors for size/type
- [ ] T046 [US4] Add Delete button to DocumentDetails.razor: visible only when CanDelete=true on DocumentDto; shows Bootstrap confirm modal ("Are you sure you want to permanently delete [Title]?"); on confirm calls DocumentService.DeleteAsync then redirects to `/documents`
- [ ] T047 [US4] Add Edit button to DocumentDetails.razor and MyDocuments.razor row: visible only when CanEdit=true; navigates to `/documents/{id}/edit`

**Checkpoint**: User Story 4 complete — full document lifecycle management functional

---

## Phase 7: User Story 5 — Share Documents with Team Members (Priority: P2)

**Goal**: Document owners can share with specific users; recipients receive notifications and see shared documents in "Shared with Me"  
**Independent Test**: Share a document with User B; verify User B receives notification and document appears in their "Shared with Me"; verify User B can download it

### Implementation for User Story 5

- [ ] T048 [P] [US5] Create `ContosoDashboard/Services/DocumentShareService.cs` implementing ShareAsync (insert DocumentShare, call NotificationService to notify recipient), UnshareAsync (delete DocumentShare), GetSharedDocumentsAsync (query DocumentShares where SharedWithUserId == currentUserId, join Document, order by SharedDate DESC)
- [ ] T049 [P] [US5] Update `DocumentService.CanUserAccessAsync` to include DocumentShare check: `db.DocumentShares.AnyAsync(s => s.DocumentId == documentId && s.SharedWithUserId == userId)`
- [ ] T050 [US5] Create share UI on DocumentDetails.razor: "Share" button (visible when CanShare=true); modal with user search/select dropdown (users from UserService); confirm triggers DocumentShareService.ShareAsync
- [ ] T051 [US5] Create `ContosoDashboard/Pages/Documents/SharedWithMe.razor` (`@page "/documents/shared"`, `[Authorize]`): list of documents shared with current user via DocumentShareService.GetSharedDocumentsAsync; columns: Title, Shared By, Shared Date, Category; Download button per row
- [ ] T052 [US5] Add "Shared with Me" nav link to NavMenu.razor or Documents section sidebar

**Checkpoint**: User Story 5 complete — sharing, notifications, and Shared with Me view all functional

---

## Phase 8: User Story 6 — View Project Documents (Priority: P2)

**Goal**: Project team members can view all documents linked to a project from the project detail page  
**Independent Test**: Associate two documents with Project Alpha; verify all project members can see them in ProjectDetails; verify PM can upload directly from project page

### Implementation for User Story 6

- [ ] T053 [P] [US6] Implement `DocumentService.GetProjectDocumentsAsync`: verify currentUserId is project member (UnauthorizedAccessException if not); query Documents where ProjectId == projectId, ScanStatus != "Quarantined"; order by UploadedDate DESC; include uploader name
- [ ] T054 [US6] Add "Project Documents" collapsible section to `ContosoDashboard/Pages/ProjectDetails.razor`: list documents with Title, Category, Uploaded By, Date, Download button; lazy-load on section expand to preserve existing page performance
- [ ] T055 [US6] Add "Upload Document to Project" button in the Project Documents section (visible to Project Managers only): navigates to `/documents/upload?projectId={id}` (pre-selects project in UploadDocument.razor form)
- [ ] T056 [US6] Update UploadDocument.razor to read optional `projectId` query parameter and pre-select the matching project in the ProjectId InputSelect

**Checkpoint**: User Story 6 complete — project-level document access functional for all team members

---

## Phase 9: User Story 7 — Integrate Documents with Tasks (Priority: P3)

**Goal**: Task detail pages show attached documents; users can attach existing or upload new documents from task context  
**Independent Test**: Attach a document to Task #5 (in Project Alpha); verify document appears in task view and also in Project Alpha documents

### Implementation for User Story 7

- [ ] T057 [P] [US7] Create `ContosoDashboard/Models/TaskDocument.cs` join entity: TaskId (FK → TaskItem), DocumentId (FK → Document), AttachedDate, AttachedBy
- [ ] T058 [P] [US7] Add `DbSet<TaskDocument>` to ApplicationDbContext; create EF Core migration `AddTaskDocument`
- [ ] T059 [P] [US7] Create `ContosoDashboard/Services/TaskDocumentService.cs` with AttachDocumentAsync (insert TaskDocument, associate document's ProjectId with task's project if not already set), DetachDocumentAsync, GetTaskDocumentsAsync (query TaskDocuments where TaskId == taskId, join Document with auth check)
- [ ] T060 [US7] Add "Attached Documents" section to `ContosoDashboard/Pages/Tasks.razor` (or TaskDetails component): list documents attached to selected task; Download button per row; "Attach Document" button opens modal with two tabs: "Select Existing" (search user's documents) and "Upload New" (inline UploadDocument form)
- [ ] T061 [US7] Update `DocumentService.CreateAsync` to accept optional TaskId; when provided, call TaskDocumentService.AttachDocumentAsync after document creation to link document to task and inherit task's ProjectId

**Checkpoint**: User Story 7 complete — task-document correlation fully functional

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Dashboard integration, accessibility, audit reporting, and final quality hardening

- [ ] T062 Add "Recent Documents" widget to `ContosoDashboard/Pages/Index.razor`: call DocumentService.GetUserDocumentsAsync(userId, pageSize: 5); display last 5 uploaded documents with title, date, download link; show "No documents yet — Upload your first document" empty state
- [ ] T063 Add document count to dashboard summary cards in Index.razor: query total Documents count for current user and display alongside existing task/project counts
- [ ] T064 Create `ContosoDashboard/wwwroot/css/documents.css` with styles for: document list table, ScanStatus badges (Pending=warning, Approved=success, Quarantined=danger), upload progress bar, preview panel, empty state illustrations
- [ ] T065 [P] Add ARIA labels and `for` attributes to all form labels in UploadDocument.razor and EditDocument.razor (basic accessibility per clarification session)
- [ ] T066 [P] Add keyboard navigation support to document list: ensure all action buttons (Download, Edit, Delete, Share) are reachable via Tab key and activated via Enter/Space
- [ ] T067 [P] Add file-type icon display to document list: map MIME type to Bootstrap Icons (pdf → `bi-file-pdf`, word → `bi-file-word`, excel → `bi-file-excel`, image → `bi-file-image`, other → `bi-file-earmark`)
- [ ] T068 [P] Create `ContosoDashboard/Services/AzureVirusScanQueueService.cs` implementing IVirusScanQueueService using Azure.Storage.Queues QueueClient; register conditionally in Program.cs based on `VirusScan:Provider` config value ("Local" vs "Azure")
- [ ] T069 [P] Create `ContosoDashboard/Services/AzureBlobStorageService.cs` implementing IFileStorageService using Azure.Storage.Blobs BlobContainerClient; register conditionally in Program.cs based on `FileStorage:Provider` config value
- [ ] T070 [P] Create `ContosoDashboard/Functions/VirusScanFunction/VirusScanFunction.cs` Azure Functions Queue trigger: dequeue VirusScanMessage, call IVirusScanService.ScanAsync, update Document.ScanStatus via DocumentService.UpdateScanStatusAsync; log threats; re-throw on failure for auto-retry

---

## Dependencies

```
Phase 1 (Setup: T001–T013)
  └─ Phase 2 (Foundational: T014–T021)
       ├─ Phase 3 (US1 Upload: T022–T027)      ← MVP start
       ├─ Phase 4 (US2 Browse/Search: T028–T034)
       └─ Phase 5 (US3 Download/Preview: T035–T040)  ← MVP complete
            ├─ Phase 6 (US4 Edit/Delete: T041–T047)
            ├─ Phase 7 (US5 Share: T048–T052)
            └─ Phase 8 (US6 Project Docs: T053–T056)
                 └─ Phase 9 (US7 Task Integration: T057–T061)
                      └─ Phase 10 (Polish: T062–T070)
```

**Cross-phase parallelism after Phase 2**:
- US1 + US2 + US3 can be developed in parallel (different files)
- US4 + US5 + US6 can be developed in parallel after Phase 2 (different files)
- US7 depends on US6 (ProjectId propagation)
- Phase 10 tasks are all independent of each other [P]

---

## Parallel Execution Examples

### Sprint 1 (Phases 1–2, sequential — blocking)
Developer A: T001 → T004 → T005 (models + migrations)  
Developer B: T006 → T007 → T008 → T009 → T010 → T011 (services + DI)  
Developer C: T012 → T013 → T016 → T017 → T018 → T019 (DTOs + nav)

### Sprint 2 (Phases 3–5, parallel per US after Phase 2)
Developer A: T014 → T015 → T020 → T021 → T022 → T023 → T024 → T025 → T026 → T027 (US1)  
Developer B: T028 → T029 → T030 → T031 → T032 → T033 → T034 (US2)  
Developer C: T035 → T036 → T037 → T038 → T039 → T040 (US3)

### Sprint 3 (Phases 6–8, parallel)
Developer A: T041 → T042 → T043 → T044 → T045 → T046 → T047 (US4)  
Developer B: T048 → T049 → T050 → T051 → T052 (US5)  
Developer C: T053 → T054 → T055 → T056 (US6)

---

## Implementation Strategy

**MVP Scope (Phases 1–5)**: Implement US1 + US2 + US3 first. This delivers the complete upload-search-download cycle, which is the minimum viable feature. All P1 acceptance scenarios pass after Phase 5.

**Incremental Delivery**:
1. After Phase 3 (US1): Demo upload form to stakeholders — early feedback on metadata form UX
2. After Phase 4 (US2): Demo browsing and search — validate filter/sort meets expectations
3. After Phase 5 (US3): **MVP complete** — all P1 user stories independently testable
4. After Phases 6–8 (US4/5/6): P2 feature-complete — editing, sharing, project integration
5. After Phase 9 (US7): P3 task integration complete
6. After Phase 10: Production-ready — Azure abstractions, accessibility, dashboard widgets

**Testing Approach**:
- Each Phase 3–9 should be manually verified against its Independent Test before merging
- Unit tests for all DocumentService methods (TDD: write test → run failing → implement → pass)
- Integration tests using in-memory database for repository queries
