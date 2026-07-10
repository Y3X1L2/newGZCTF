# 审计全部剩余问题修复计划（合并版）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 修复 `2026-05-22-comprehensive-audit-progress.md` 全部剩余问题——合并第二部分（Flag/排行榜/死代码）和第三部分（节点/模板/调度/容器启动）的 P0/P1/P2 级别发现

**Architecture:** 分两阶段——A阶段修可立即修复的安全/数据问题（14个Task），B阶段单独立项处理架构级调度系统集成

**Audit Source:** `docs/superpowers/reviews/2026-05-22-comprehensive-audit-progress.md`

---

## File Map

| 操作 | 文件 | 职责 |
|------|------|------|
| 修改 | `Controllers/NodesController.cs` | Heartbeat AuthToken校验、数据范围校验、Deregister硬删除 |
| 修改 | `Controllers/ImageTemplateController.cs` | LocalImport路径白名单校验 |
| 修改 | `Repositories/GameChallengeRepository.cs` | AddFlags 自动分配 OrderIndex |
| 修改 | `Storage/ImageStorage.cs` | Upload 计算 SHA256 + 检测 OSType |
| 修改 | `Models/Data/GamePhase.cs` | 删除 IREnabled/ScenarioEnabled |
| 修改 | `Services/Fleet/NodeDeployService.cs` | SSH 失败回滚已创建节点 |
| 修改 | `Services/Fleet/ImageDistributionService.cs` | Payload 脱敏 |
| 修改 | `Services/Vm/ArchiveExtractor.cs` | .ova 优先解包再检测 |
| 修改 | `ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/flags/index.tsx` | Flag 编辑模态框 |
| 修改 | `ClientApp/src/pages/game/` 排行榜组件 | 多 Flag 进度展示 |
| 删除 | `ClientApp/src/utils/screenDemoData.ts` | 死代码 |
| 新增 | `Migrations/` | GamePhase 列删除 + TimeSlots DROP |

---

## A 阶段：可立即修复的问题（14 Tasks）

### Task A1: Heartbeat AuthToken 校验 + 数据范围校验

**严重级别:** P0 (NP0-1) + P1 (NP1-2)

**文件:** `Controllers/NodesController.cs:82-94`

**Step 1: 修改 Heartbeat 端点**

```csharp
[HttpPost("{id:guid}/heartbeat")]
[Authorize]
[EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
public async Task<IActionResult> Heartbeat(Guid id, [FromBody] HeartbeatRequest request)
{
    // Validate data ranges
    if (request.CpuLoad < 0 || request.CpuLoad > 100
        || request.MemoryLoad < 0 || request.MemoryLoad > 100
        || request.CurrentContainers < 0 || request.CurrentVms < 0
        || request.UsedPorts < 0)
        return BadRequest(new { message = "Invalid metric values" });

    var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
    if (node is null) return NotFound();

    // Verify AuthToken: only the node's Agent should report its own heartbeat
    var authHeader = HttpContext.Request.Headers.Authorization.ToString();
    if (!authHeader.StartsWith("Bearer ") || authHeader[7..] != node.AuthToken)
        return Unauthorized(new { message = "Invalid node auth token" });

    node.CpuLoad = request.CpuLoad / 100f;
    node.MemoryLoad = request.MemoryLoad / 100f;
    node.CurrentContainers = request.CurrentContainers;
    node.CurrentVms = request.CurrentVms;
    node.UsedPorts = request.UsedPorts;
    node.LastHeartbeat = DateTimeOffset.UtcNow;
    node.Status = NodeStatus.Online;
    await _context.SaveChangesAsync();
    return Ok();
}
```

注：当前 Heartbeat 用 `[Authorize]`（平台用户 cookie 认证），Agent 应当用 Bearer token。如果 Agent 协议尚未实现（架构缺失），此改动可先添加范围校验和注释标记 AuthToken 检查位置，待 Agent 实现后启用。

**Step 2: Commit**

```bash
git commit -m "fix: add heartbeat data range validation and auth token check point"
```

---

### Task A2: LocalImportRequest 路径白名单校验

**严重级别:** P0 (NP0-2)

**文件:** `Controllers/ImageTemplateController.cs:120`

```csharp
[HttpPost("import-local")]
[RequireAdmin]
public async Task<IActionResult> ImportFromLocal([FromBody] LocalImportRequest request)
{
    // Validate path is within allowed image directories
    var allowedRoots = new[]
    {
        Path.GetFullPath("./images"),
        Path.GetFullPath("/var/lib/gzctf/images"),
        Path.GetFullPath("/var/lib/libvirt/images"),
    };
    var fullPath = Path.GetFullPath(request.LocalPath);
    if (!allowedRoots.Any(r => fullPath.StartsWith(r + Path.DirectorySeparatorChar)
        || fullPath == r))
        return BadRequest(new { message = "Path is not in an allowed directory" });

    // ... rest of method
}
```

**Commit:** `fix: add path traversal protection to LocalImportRequest`

---

### Task A3: ImageStorage Upload 计算 SHA256 + 检测 OSType

**严重级别:** P2 (NP2-4 + NP2-5)

**文件:** `Storage/ImageStorage.cs`

**Step 1: 在 SaveImageAsync 中计算 SHA256**

在文件保存后添加：
```csharp
using var sha = SHA256.Create();
await using var fs = File.OpenRead(filePath);
var hash = Convert.ToHexString(await sha.ComputeHashAsync(fs)).ToLowerInvariant();
imageTemplate.ImageHash = hash;
```

**Step 2: 检测 OSType**

替换硬编码的 `OSType.Windows`：
```csharp
var lowerName = file.FileName.ToLowerInvariant();
imageTemplate.OSType = lowerName.Contains("linux") || lowerName.Contains("ubuntu")
    || lowerName.Contains("centos") || lowerName.Contains("debian")
    ? OSType.Linux : OSType.Windows;
```

**Commit:** `fix: compute SHA256 on upload and detect OS type from filename`

---

### Task A4: NodeDeployService SSH 失败回滚

**严重级别:** P2 (NP2-1)

**文件:** `Services/Fleet/NodeDeployService.cs`

在 `DeployToServerAsync` 的 catch 块中删除已创建的节点：
```csharp
catch (Exception ex)
{
    // Rollback: remove the node record if SSH/probe failed
    var created = await _context.WorkerNodes.FindAsync(node.Id);
    if (created is not null)
    {
        _context.WorkerNodes.Remove(created);
        await _context.SaveChangesAsync(token);
    }
    return new NodeDeployResult { Success = false, Message = ex.Message };
}
```

**Commit:** `fix: rollback worker node on SSH deployment failure`

---

### Task A5: ImageDistributionService Payload 脱敏

**严重级别:** P2 (NP2-7)

**文件:** `Services/Fleet/ImageDistributionService.cs:25`

```csharp
// Before: localPath = template.LocalFilePath  (leaks server path)
// After: use template Id only, Agent looks up path locally
var payload = new { imageId = template.Id, hash = template.ImageHash };
```

**Commit:** `fix: remove server path from ImageDistributionService payload`

---

### Task A6: Flag 编辑功能

**严重级别:** P2 (P2-4)

**文件:** `ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/flags/index.tsx`

在 Flag 列表项中添加编辑按钮 + Modal，发送 PUT 请求至 `/api/edit/Games/{gameId}/Challenges/{challengeId}/Flags/{flagId}`。

**Commit:** `feat(ui): add flag edit modal with PUT API`

---

### Task A7: 排行榜多 Flag 进度

**严重级别:** P2 (P2-5)

找到排行榜前端组件，在挑战项中添加 `solvedCount/totalFlags` 展示。

**Commit:** `feat(ui): show multi-flag progress on leaderboard`

---

### Task A8: GamePhase 残留字段清理

**严重级别:** P3 (P3-4)

**文件:** `Models/Data/GamePhase.cs`

删除 `IREnabled` 和 `ScenarioEnabled` 字段。

**文件:** `ClientApp/src/pages/admin/games/[id]/Phases.tsx`（已在第3轮修复）

**Commit:** `chore: remove IREnabled/ScenarioEnabled from GamePhase`

---

### Task A9: screenDemoData 死代码删除

**严重级别:** P3 (P3-2)

```bash
git rm src/GZCTF/ClientApp/src/utils/screenDemoData.ts
# 检查并移除所有引用
grep -rn "screenDemoData\|DemoScenario" src/GZCTF/ClientApp/src
```

**Commit:** `chore: remove dead screenDemoData`

---

### Task A10: Flag OrderIndex 自动分配

**严重级别:** P2/P3 (P2-1 + P3-7)

**文件:** `Repositories/GameChallengeRepository.cs`

AddFlags 方法中，如果 model.OrderIndex <= 0，自动使用 `maxOrder + 1`。

**Commit:** `feat: auto-assign OrderIndex for new flags`

---

### Task A11: Deregister 硬删除节点

**严重级别:** P2 (NP2-2)

**文件:** `Controllers/NodesController.cs:72-76`

```csharp
// Before: node.Status = NodeStatus.Offline; await _context.SaveChangesAsync();
// After: _context.WorkerNodes.Remove(node); await _context.SaveChangesAsync();
```

**Commit:** `fix: hard delete node on deregister instead of marking offline`

---

### Task A12: VM 销毁端点

**严重级别:** P1 (NP1-6)

**文件:** `Controllers/NodesController.cs` 或 `Controllers/GameController.cs`

新增端点：
```csharp
[HttpDelete("vms/{instanceId:guid}")]
[RequireUser]
public async Task<IActionResult> DestroyVm(Guid instanceId)
{
    var vm = await _context.VmInstances.FindAsync(instanceId);
    if (vm is null) return NotFound();
    vm.Status = VmInstanceStatus.Destroyed;
    vm.DestroyedAt = DateTimeOffset.UtcNow;
    await _context.SaveChangesAsync();
    return NoContent();
}
```

**Commit:** `feat: add VM instance destroy endpoint`

---

### Task A13: ArchiveExtractor .ova 解包 + 转换逻辑

**严重级别:** P1 (P1-5, 已在第2轮部分修复，确认 .ova 路径完整)

确认此前修复覆盖：.ova 检测 → tar 解包 → 扫描 vmdk → qemu-img 转换。额外添加：解包后再次扫描 qcow2。

**Commit:** `fix: complete OVA extraction pipeline in ArchiveExtractor`

---

### Task A14: 数据库迁移 + 部署

生成最终迁移：GamePhase 列删除 + TimeSlots DROP + HeartbeatRequest 数据范围对应的模型变更。

```bash
dotnet ef migrations add FinalAuditCleanup
dotnet build -c Release && dotnet test && pnpm build
python scripts/deploy.py
```

**Commit:** `chore: final audit cleanup migration and deployment`

---

## B 阶段：架构级问题（单独立项）

以下问题需要系统性的架构设计，不在本次快速修复计划范围内：

| 编号 | 问题 | 需要的设计 |
|------|------|-----------|
| NP0-3~NP0-6 | 调度系统死代码 + 容器不经过调度 | FleetManager/QueueManager 接入业务流程 |
| NP1-1 | sshpass Windows 不可用 | 跨平台 SSH 方案或 Agent 协议替代 |
| NP1-3~NP1-4 | Docker 镜像不预拉取/不验证 | 镜像管理生命周期设计 |
| NP1-5 | AutoTransferService 未调用 | 与调度系统集成 |
| NP1-7 | VM 创建不等待完成 | 后台状态同步服务 |
| NP0-7 | VmInstance 状态同步 | 后台轮询 Agent 状态 |

---

## Execution Order

```
A3(A4+A5) → A1+A2+A11+A12 → A8+A9 → A10+A13 → A6+A7 → A14
```

独立修改（A3+A4+A5）可并行。A6+A7（前端）在前端构建前完成。A14收尾。
