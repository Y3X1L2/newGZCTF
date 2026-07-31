# Open API HTML Reference Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Serve the single external `open-v1` contract through a customer-ready Swagger-style HTML page in production.

**Architecture:** Keep NSwag as the contract generator and Scalar as the HTML renderer. Register and expose only `open-v1` outside Development; keep the internal `v1` document development-only. Add API metadata at controller boundaries so the generated JSON and HTML stay synchronized.

**Tech Stack:** ASP.NET Core 10, NSwag 14.6, Scalar.AspNetCore 2.13, xUnit integration tests

---

### Task 1: Separate external and internal OpenAPI registration

**Files:**
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`

- [x] Register `open-v1` in every environment.
- [x] Register internal `v1` only in Development.
- [x] Preserve the bearer security scheme and contract operation processor.

### Task 2: Map the production HTML and JSON endpoints

**Files:**
- Modify: `src/GZCTF/Extensions/Startup/AppExtensions.cs`

- [x] Map `/openapi/open-v1.json` in every environment.
- [x] Map internal `/openapi/v1.json` only in Development.
- [x] Map Scalar at `/api-docs` with `open-v1` as its only document.
- [x] Enable models, search, request testing, document download, and persistent bearer authentication.

### Task 3: Improve TeamLab contract navigation

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTopologiesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTrafficController.cs`

- [x] Add stable customer-facing tags for topology, runtime, and traffic/capture operations.
- [x] Add concise summaries and descriptions to every TeamLab external operation.
- [x] Regenerate `docs/commercialization/openapi/open-v1.json` from the live generator.

### Task 4: Document and test the customer entry point

**Files:**
- Modify: `docs/commercialization/open-api-v1-guide.md`
- Modify: `src/GZCTF.Integration.Test/Tests/Api/OpenApiTests.cs`

- [x] Document `/api-docs`, the live JSON route, bearer authentication, and the single-contract rule.
- [x] Require the HTML page to return success and reference the external document.
- [x] Require production registration to exclude the internal document.

### Task 5: Verify the large unit

- [x] Run focused OpenAPI integration tests.
- [x] Run `dotnet build src/GZCTF/GZCTF.csproj -c Release --no-restore`.
- [x] Run `git diff --check` for the touched documentation and API configuration files.
