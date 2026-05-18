# Data Model: Document Upload and Management

**Feature**: Document Upload and Management (001-document-management)  
**Date**: 2026-05-18  
**Phase**: 1 (Design)  
**Status**: Complete

## Entity Relationship Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        DOCUMENT MANAGEMENT                      │
└─────────────────────────────────────────────────────────────────┘

┌──────────────┐              ┌────────────────────┐
│     User     │              │    Project         │
├──────────────┤              ├────────────────────┤
│ UserId (PK)  │◄─────────────│ ProjectId (PK)     │
│ Email        │  1:N         │ Name               │
│ FullName     │              │ Description        │
│ Department   │              └────────────────────┘
│ Role         │                      ▲
└──────────────┘                      │
       ▲                              │ 1:N (optional)
       │ 1:N                          │
       │                    ┌─────────────────────────────┐
       │ (uploader)         │      DOCUMENT              │
       └────────────────────┤─────────────────────────────┤
                            │ DocumentId (PK)             │
                            │ UserId (FK) - uploader      │
                            │ ProjectId (FK, nullable)    │
                            │ Title (string, required)    │
                            │ Description (string)        │
                            │ Category (string, enum-like)│
                            │ Tags (string, comma-sep)    │
                            │ UploadedDate (datetime)     │
                            │ FileSize (long)             │
                            │ MimeType (string, max 255)  │
                            │ FilePath (string)           │
                            │ CreatedBy (string)          │
                            └─────────────────────────────┘
                                     ▲
                                     │ 1:N
                      ┌──────────────┴──────────────┐
                      │                             │
          ┌───────────────────────────┐   ┌──────────────────────┐
          │   DOCUMENTSHARE           │   │ DOCUMENTACTIVITYLOG  │
          ├───────────────────────────┤   ├──────────────────────┤
          │ ShareId (PK)              │   │ ActivityId (PK)      │
          │ DocumentId (FK)           │   │ DocumentId (FK)      │
          │ SharedWithUserId (FK)     │   │ UserId (FK)          │
          │ SharedDate (datetime)     │   │ ActivityType (string)│
          │ SharedBy (string)         │   │ ActivityDate (datetime)
          └───────────────────────────┘   └──────────────────────┘
                      ▲
                      │ 1:N (recipient)
                      └──────────────────────┐
                                             │
                                        ┌──────────────┐
                                        │     User     │
                                        │  (recipient) │
                                        └──────────────┘
```

---

## Core Entities

### Document

**Purpose**: Store document metadata and file references  
**Lifecycle**: Created on upload, updated on file replacement, deleted on user/admin deletion

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| DocumentId | int | PK, Identity | Auto-increment, never exposed to UI |
| UserId | int | FK → User | Document uploader (immutable) |
| ProjectId | int | FK → Project, nullable | Associated project; null for personal docs |
| Title | string (255) | NOT NULL | User-supplied document name |
| Description | string (2000) | NULL | Optional detailed description |
| Category | string (50) | NOT NULL | "Project Documents" \| "Team Resources" \| "Personal Files" \| "Reports" \| "Presentations" \| "Other" |
| Tags | string (500) | NULL | Comma-separated tags (e.g., "budget,2026,approval") |
| UploadedDate | datetime | NOT NULL | UTC timestamp when file uploaded |
| FileSize | long | NOT NULL | Bytes; validate ≤25MB (26,214,400) |
| MimeType | string (255) | NOT NULL | MIME type (e.g., "application/pdf", "image/jpeg") |
| FilePath | string (500) | NOT NULL | Internal path: {userId}/{projectId or personal}/{guid}.{ext} |
| CreatedBy | string (255) | NOT NULL | Username of uploader (for audit) |
| ScanStatus | string (20) | NOT NULL, default "Approved" | `"Pending"` \| `"Approved"` \| `"Quarantined"`. Training sets Approved on upload; production set asynchronously by VirusScanFunction |

**Indexes**:
```csharp
// Primary query index
.HasIndex(d => new { d.UserId, d.UploadedDate })
  .IsDescending(false, true) // UserId ASC, UploadedDate DESC

// Project documents index  
.HasIndex(d => new { d.ProjectId, d.UploadedDate })
  .IsDescending(false, true)

// Full-text search support
.Property(d => d.Title).UseCollation("Latin1_General_CI_AS")
.Property(d => d.Description).UseCollation("Latin1_General_CI_AS")
.Property(d => d.Tags).UseCollation("Latin1_General_CI_AS")
```

**Validation Rules**:
- Title: Required, 1-255 characters, no leading/trailing whitespace
- Description: Optional, 0-2000 characters
- Category: Required, must be one of 6 enum values
- Tags: Optional, comma-separated, max 500 characters
- FileSize: Required, > 0 and ≤ 26,214,400 bytes (25 MB)
- MimeType: Required, whitelist: "application/pdf", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.ms-powerpoint", "application/vnd.openxmlformats-officedocument.presentationml.presentation", "text/plain", "image/jpeg", "image/png"
- FilePath: Required, non-null, matches pattern `\d+/(personal|\d+)/[a-f0-9-]+\.\w+`

**Access Patterns**:
1. Get user's documents (indexed): `db.Documents.Where(d => d.UserId == userId).OrderByDescending(d => d.UploadedDate)`
2. Get project documents (indexed): `db.Documents.Where(d => d.ProjectId == projectId && d.UserId != null).OrderByDescending(d => d.UploadedDate)`
3. Search (LIKE on indexed columns): `db.Documents.Where(d => d.Title.Contains(searchTerm) || d.Description.Contains(searchTerm) || d.Tags.Contains(searchTerm))`
4. Get single document: `db.Documents.FirstOrDefaultAsync(d => d.DocumentId == id)`

---

### DocumentShare (P2 Feature)

**Purpose**: Track document sharing permissions and notify recipients  
**Lifecycle**: Created when owner shares, deleted when owner unshares

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| ShareId | int | PK, Identity | Auto-increment |
| DocumentId | int | FK → Document | Shared document (must exist) |
| SharedWithUserId | int | FK → User | Recipient of shared document |
| SharedDate | datetime | NOT NULL | UTC timestamp when shared |
| SharedBy | string (255) | NOT NULL | Username of sharer (for audit) |

**Constraints**:
- Unique constraint: (DocumentId, SharedWithUserId) - cannot share same doc with same user twice
- Cascade delete: If Document deleted, all shares deleted

**Relationships**:
- One Document can have many DocumentShare records (one-to-many)
- One User can receive many DocumentShare records (one-to-many)

**Access Patterns**:
1. Get documents shared with user: `db.DocumentShares.Where(s => s.SharedWithUserId == userId).Include(s => s.Document).OrderByDescending(s => s.SharedDate)`
2. Get users a document is shared with: `db.DocumentShares.Where(s => s.DocumentId == docId).Include(s => s.SharedWithUser)`

---

### DocumentActivityLog (Audit Trail)

**Purpose**: Track all document operations for compliance and reporting  
**Lifecycle**: Appended on every document operation (no updates/deletes)

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| ActivityId | int | PK, Identity | Auto-increment |
| DocumentId | int | FK → Document | Associated document (nullable for deleted docs) |
| UserId | int | FK → User | User performing action |
| ActivityType | string (50) | NOT NULL | "Upload" \| "Download" \| "Delete" \| "Share" \| "Unshare" \| "Edit" |
| ActivityDate | datetime | NOT NULL | UTC timestamp of activity |

**Constraints**:
- No cascade delete on DocumentId - log persists even if doc deleted
- Index on (DocumentId, ActivityDate) for audit reports

**Access Patterns**:
1. Get document's audit trail: `db.DocumentActivityLogs.Where(a => a.DocumentId == docId).OrderByDescending(a => a.ActivityDate)`
2. Get user's activities: `db.DocumentActivityLogs.Where(a => a.UserId == userId).OrderByDescending(a => a.ActivityDate)`
3. Admin reporting: `db.DocumentActivityLogs.Where(a => a.ActivityDate >= startDate && a.ActivityDate <= endDate)`

---

## Database Migrations

### Migration 1: Create Document Tables

```csharp
protected override void Up(MigrationBuilder migrationBuilder) {
  migrationBuilder.CreateTable(
    name: "Documents",
    columns: table => new {
      DocumentId = table.Column<int>(nullable: false)
        .Annotation("SqlServer:Identity", "1, 1"),
      UserId = table.Column<int>(nullable: false),
      ProjectId = table.Column<int>(nullable: true),
      Title = table.Column<string>(maxLength: 255, nullable: false),
      Description = table.Column<string>(maxLength: 2000, nullable: true),
      Category = table.Column<string>(maxLength: 50, nullable: false),
      Tags = table.Column<string>(maxLength: 500, nullable: true),
      UploadedDate = table.Column<DateTime>(nullable: false),
      FileSize = table.Column<long>(nullable: false),
      MimeType = table.Column<string>(maxLength: 255, nullable: false),
      FilePath = table.Column<string>(maxLength: 500, nullable: false),
      CreatedBy = table.Column<string>(maxLength: 255, nullable: false)
    },
    constraints: table => {
      table.PrimaryKey("PK_Documents", x => x.DocumentId);
      table.ForeignKey("FK_Documents_User_UserId", x => x.UserId, "Users", "UserId", onDelete: ReferentialAction.Restrict);
      table.ForeignKey("FK_Documents_Project_ProjectId", x => x.ProjectId, "Projects", "ProjectId", onDelete: ReferentialAction.Restrict);
    });

  migrationBuilder.CreateIndex(
    name: "IX_Document_UserId_UploadedDate",
    table: "Documents",
    columns: new[] { "UserId", "UploadedDate" },
    descending: new[] { false, true });

  migrationBuilder.CreateIndex(
    name: "IX_Document_ProjectId_UploadedDate",
    table: "Documents",
    columns: new[] { "ProjectId", "UploadedDate" },
    descending: new[] { false, true });
}
```

### Migration 2: Create DocumentShare Table (P2)

```csharp
protected override void Up(MigrationBuilder migrationBuilder) {
  migrationBuilder.CreateTable(
    name: "DocumentShares",
    columns: table => new {
      ShareId = table.Column<int>(nullable: false)
        .Annotation("SqlServer:Identity", "1, 1"),
      DocumentId = table.Column<int>(nullable: false),
      SharedWithUserId = table.Column<int>(nullable: false),
      SharedDate = table.Column<DateTime>(nullable: false),
      SharedBy = table.Column<string>(maxLength: 255, nullable: false)
    },
    constraints: table => {
      table.PrimaryKey("PK_DocumentShares", x => x.ShareId);
      table.UniqueConstraint("UQ_DocumentShare_Document_User", x => new { x.DocumentId, x.SharedWithUserId });
      table.ForeignKey("FK_DocumentShare_Document", x => x.DocumentId, "Documents", "DocumentId", onDelete: ReferentialAction.Cascade);
      table.ForeignKey("FK_DocumentShare_User", x => x.SharedWithUserId, "Users", "UserId", onDelete: ReferentialAction.Restrict);
    });
}
```

### Migration 3: Create DocumentActivityLog Table

```csharp
protected override void Up(MigrationBuilder migrationBuilder) {
  migrationBuilder.CreateTable(
    name: "DocumentActivityLogs",
    columns: table => new {
      ActivityId = table.Column<int>(nullable: false)
        .Annotation("SqlServer:Identity", "1, 1"),
      DocumentId = table.Column<int>(nullable: true),
      UserId = table.Column<int>(nullable: false),
      ActivityType = table.Column<string>(maxLength: 50, nullable: false),
      ActivityDate = table.Column<DateTime>(nullable: false)
    },
    constraints: table => {
      table.PrimaryKey("PK_DocumentActivityLogs", x => x.ActivityId);
      table.ForeignKey("FK_ActivityLog_Document", x => x.DocumentId, "Documents", "DocumentId", onDelete: ReferentialAction.SetNull);
      table.ForeignKey("FK_ActivityLog_User", x => x.UserId, "Users", "UserId", onDelete: ReferentialAction.Restrict);
    });

  migrationBuilder.CreateIndex(
    name: "IX_ActivityLog_DocumentId_ActivityDate",
    table: "DocumentActivityLogs",
    columns: new[] { "DocumentId", "ActivityDate" },
    descending: new[] { false, true });
}
```

---

## Summary

- **3 new entities**: Document (core), DocumentShare (P2), DocumentActivityLog (audit)
- **Relationships**: User:Document (1:N), Project:Document (1:N optional), Document:DocumentShare (1:N)
- **Indexes**: (UserId, UploadedDate DESC), (ProjectId, UploadedDate DESC), (DocumentId, ActivityDate DESC)
- **Validation**: All constraints mapped to acceptance scenarios in spec.md
- **Audit Trail**: All CRUD operations logged for compliance
- **Cloud Migration**: Schema requires no changes for Azure SQL; file paths work as-is for Azure Blob names
