<!-- 
  SYNC IMPACT REPORT
  ==================
  
  Constitution: Initial Version 1.0.0
  Ratification Date: 2026-05-18
  Last Amendment: 2026-05-18
  
  PRINCIPLES ESTABLISHED:
  - I. Clean Architecture (Layered Models → Services → Pages)
  - II. Security-First Design (Claims-based auth, IDOR protection)
  - III. Test-Driven Development (TDD for all features)
  - IV. Service-Layer Abstraction (Cloud migration ready)
  - V. Documentation and Training Focus (Code clarity, acceptance tests)
  
  SECTIONS ESTABLISHED:
  - Technology & Architecture Standards (ASP.NET Core 8.0, EF Core, Blazor Server)
  - Development Workflow & Quality Gates (Feature submission, code review, quality gates)
  - Governance (Amendment process, compliance review)
  
  TEMPLATE UPDATES REQUIRED:
  ✅ spec-template.md: Align with architecture principles (no changes needed - template is generic)
  ✅ plan-template.md: Constitution Check section will be auto-populated by plan agent
  ✅ tasks-template.md: Aligned with TDD principle and layered task organization
  ⚠ .github/copilot-instructions.md: Verify alignment with constitution principles (PENDING REVIEW)
  
  FOLLOW-UP ITEMS:
  - [ ] Create runtime guidance in .github/copilot-instructions.md if not present
  - [ ] Add integration test framework (xUnit) to project dependencies
  - [ ] Document security review checklist for PRs
-->

# ContosoDashboard Constitution

## Core Principles

### I. Clean Architecture
Every feature must follow strict layered separation: Models (data contracts), Data (Entity Framework context and repositories), Services (business logic), and Pages (Blazor UI components). Services must not directly reference Pages, and Pages must only consume Services. This enables testability, maintainability, and future cloud migration.

### II. Security-First Design
All user-facing features MUST implement claim-based authorization checks at both the page level (`[Authorize]`) and service level. IDOR (Insecure Direct Object Reference) protection is non-negotiable; services MUST verify the current user's authorization before returning data. Mock authentication is acceptable for training; production deployments require Azure AD, Identity Server, or equivalent identity providers with proper password hashing and MFA.

### III. Test-Driven Development (Non-Negotiable)
New features must follow TDD discipline: unit tests written first → reviewed by stakeholder → tests fail → implementation → tests pass → refactor. Service-layer business logic requires unit tests. Integration tests cover database interactions and cross-service communication.

### IV. Service-Layer Abstraction
All business logic must reside in Services (Models → Services → Pages), never directly in Pages. Service interfaces must be decoupled from infrastructure implementations to maintain cloud migration readiness. Current implementation uses Entity Framework with SQL Server LocalDB; future migrations to Azure SQL or Cosmos DB must not require Page-layer changes.

### V. Documentation and Training Focus
Code MUST include XML comments on public members. Feature implementations MUST include acceptance tests matching spec scenarios. The project serves as a training exemplar for Spec-Driven Development; all decisions must be defensible and replicable by learners.

## Technology & Architecture Standards

**Framework Stack**: ASP.NET Core 8.0 (current) or later, Blazor Server, Entity Framework Core  
**Database**: SQL Server LocalDB for training; Azure SQL or Cosmos DB for cloud deployments  
**Authentication**: Cookie-based mock authentication (training); Azure AD/Microsoft Entra ID for production  
**Testing**: xUnit framework with FluentAssertions for unit and integration tests  
**Package Versioning**: MAJOR.MINOR.BUILD format; MAJOR increments signal breaking changes  

All database queries must use Entity Framework Core with LINQ. Raw SQL is permitted only with justification and security review.

## Development Workflow & Quality Gates

**Feature Submission**: Every feature MUST have:
1. A specification (spec.md) approved by project stakeholders
2. Unit tests demonstrating each acceptance scenario
3. Service-layer implementation with clean architecture adherence
4. Page-layer UI with `[Authorize]` attributes on protected routes

**Code Review Focus**: Reviews MUST verify:
- Architecture compliance (proper separation across Models → Services → Pages)
- Security checks (authorization, IDOR protection, input validation)
- Test coverage (minimum acceptance scenarios covered)
- Documentation (XML comments on public API)

**Quality Gates**:
- All tests MUST pass before merge to main
- Zero security vulnerabilities allowed (manual review mandatory)
- Documentation completeness verified by code reviewer

## Governance

The ContosoDashboard Constitution supersedes all informal practices and serves as the authoritative guide for all technical decisions. All pull requests must verify compliance with these five core principles. Significant deviations require documentation of the rationale and approval from the project lead.

**Amendment Process**: Constitutional changes require:
1. Written proposal documenting the principle addition/modification
2. Stakeholder review and consensus
3. Migration plan for any breaking changes
4. Git commit message: `docs: amend constitution to vX.Y.Z (description)`

**Compliance Review**: Quarterly review of PRs confirms ongoing adherence to principles. Non-compliance is flagged for remediation in the following sprint.

Runtime development guidance is documented in `.github/copilot-instructions.md` and MUST align with this constitution.

**Version**: 1.0.0 | **Ratified**: 2026-05-18 | **Last Amended**: 2026-05-18
