# VM Artifact Store Correctness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make VM template distribution correct and reliable by removing VM qcow2 blobs from the Docker Registry artifact path and using platform-hosted artifact downloads with strict hash validation, resumable Agent downloads, atomic replacement, and accurate errors.

**Architecture:** Docker images continue to use `10.24.0.28:5000` Docker Registry. VM templates use a VM artifact flow: platform validates the template file hash and exposes an authenticated artifact URL, then Agent downloads with Range resume into a `.part` file, verifies sha256, and atomically moves it into `/var/lib/gzctf/images`. VM creation fails before libvirt work if artifact validation or download fails.

**Tech Stack:** ASP.NET Core controllers/services, EF Core models, Agent HTTP API, xUnit unit tests.

---

### Task 1: Platform VM Artifact Validation

**Files:**
- Modify: `src/GZCTF/Services/Fleet/AgentClient.cs`
- Modify: `src/GZCTF/Services/Fleet/FleetVmService.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/FleetVmServiceTests.cs`

- [x] Add tests proving remote VM creation refuses templates whose file hash does not match `ImageTemplate.ImageHash`.
- [x] Implement platform-side file existence, size, and sha256 validation before asking Agent to download.
- [x] Return a template-specific storage error instead of falling through to `No KVM node available`.

### Task 2: Remove VM Registry Artifact Main Path

**Files:**
- Modify: `src/GZCTF/Services/Fleet/AgentClient.cs`
- Modify: `src/GZCTF/Controllers/ImageTemplateController.cs`
- Keep but stop using as main path: `src/GZCTF/Services/Fleet/VmImageRegistryService.cs`

- [x] Stop calling `VmImageRegistryService.EnsureArtifactAsync` from VM deployment.
- [x] Stop eagerly pushing VM qcow2 artifacts to Docker Registry after VM template upload/import.
- [x] Keep Docker Registry behavior unchanged for Docker images.

### Task 3: Agent Resumable VM Download

**Files:**
- Modify: `src/GZCTF.Agent/Controllers/ImageController.cs`
- Modify: `src/GZCTF.Agent/Models/VmModels.cs`

- [x] Add tests or focused validation for `.part` reuse, `Range` requests, hash mismatch cleanup, and atomic replacement.
- [x] Download from platform URL using `Range` when a partial file exists.
- [x] If server does not honor Range, restart from zero safely.
- [x] Verify final sha256 before moving into place.

### Task 4: Accurate Error Propagation

**Files:**
- Modify: `src/GZCTF/Controllers/GameController.cs`
- Modify: `src/GZCTF/Services/Fleet/FleetVmService.cs`

- [x] Persist VM instance error message when storage/download fails.
- [x] `BuildVmCreateFallback` should only say `No KVM node available` when scheduling genuinely failed.
- [x] For storage/download failures, return the concrete VM error message.

### Task 5: Verification and Deployment

**Commands:**
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FleetVmServiceTests|ImageController"`
- `dotnet build src/GZCTF/GZCTF.csproj`
- `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj`

- [x] Deploy to `10.24.0.27`.
- [ ] Sync/update Agent on `10.24.0.31` if the Agent API changed.
- [ ] Retry Windows VM creation and confirm the reported failure, if any, is template-specific rather than KVM-specific.
