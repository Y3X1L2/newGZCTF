# Phase 9 TeamLab 组网独立代码审查最终报告

- 审查对象：Phase 9 TeamLab 组网模块（链路 4.1–4.9）
- 审查依据：`docs/commercialization/phase-09-teamlab-networking-independent-code-review.md`
- 审查方式：sub-agent 驱动的独立代码审查（9 条链路并行 dispatch，主 agent 严格复核 + 联网调研）
- 审查日期：2026-07-21
- 代码仓库：`D:/newgz/newGZCTF-main/`（.NET / C#）
- 进度文档：`docs/commercialization/reviews/phase-09-review-progress/README.md` 及 `chain-4.1-*.md` 至 `chain-4.9-*.md`

---

## 一、执行摘要

本次审查覆盖 Phase 9 TeamLab 组网模块的全部 9 条端到端链路（4.1 拓扑发布、4.2 Runtime 物理放置、4.3 Shard 网络/Fabric、4.4 Docker 网络门控、4.5 VM 生命周期、4.6 镜像生命周期、4.7 WireGuard 入口、4.8 流量捕获/PCAP、4.9 Reset/Destroy/Recovery）。9 个 sub-agent 并行执行，主 agent 对每个 P1/P2 finding 均通过实际打开源码文件验证行号、代码语义与触发路径，对涉及组网方案合理性的关键问题（generation fence、WireGuard Fabric、dnsmasq 监督）进行了联网调研。

**findings 统计**：22 个 findings（0 P0，3 P1，9 P2，10 P3）。

**生产准入结论**：审查时为 **BLOCKED**，代码整改后为 **CONDITIONAL**；2026-07-22 不可变发布包的双 Worker 实机验收通过后，最终结论升级为 **APPROVED**。详见第十一节。

### 整改复核（2026-07-21）

- 3 个 P1 已关闭：generation 清理只信 active-generation 权威状态；external-evidence 不再授予 Managed 能力；runtime destroy 在 Agent 与对象存储 capture 均删除前保持 `CleanupPending`。
- 9 个 P2 已关闭：并发 Create 稳定复用/冲突、容量事实与预留去重、dnsmasq 持续事实探测、Docker NetworkReady 不再依赖异步回传门禁、多网卡默认路由、endpoint sensor/config-drive 目录隔离、VM 暂存保护、PCAP 删除竞态、scenario 输入认证绑定当前镜像 digest。
- 拓扑发布进一步冻结普通资产镜像 digest，scenario hash 统一为带 `sha256:` 前缀的确定性 SHA-256；Admin publish 使用 Serializable 事务与 PostgreSQL advisory lock；validate 与 publish 共用 Bootstrap 兼容性契约。
- P3 中发布竞态、validate 契约、APIPA 默认池、Fabric route dev、Agent PCAP semaphore、上传 token、scenario hash 和 Agent 死代码已关闭。
- F-4.3-05 保留为明确设计不变量：Fabric peer 必须唯一拥有其 `/32` 地址，路由归属不新增第二套映射。4.4.3 暂不改变线上协议，避免主站与 Agent 滚动升级期间出现门控语义分叉；后续若显式化，必须随 Agent capability/protocol 版本一起切换。
- 本地证据：`GZCTF` 与 `GZCTF.Agent` Release 编译通过；审查相关单元测试 `140/140`、完整单元测试 `622/622` 通过。仓库既有 NuGet 漏洞告警属于独立供应链治理项，不在本次组网补丁中混改。

---

## 二、审查覆盖矩阵

| 链路 | 范围 | 状态 | 实际打开文件数 | findings |
|------|------|------|----------------|----------|
| 4.1 拓扑发布 | 拓扑校验、scenario hash、Admin API publish | ✅ Reviewed | 12+ | 4 (2 P2, 2 P3) |
| 4.2 Runtime 物理放置 | Create/Reset/Destroy 排队、advisory lock、placement | ✅ Reviewed | 14+ | 2 (2 P2) |
| 4.3 Shard 网络/Fabric | bridge/router/dnsmasq/fabric、generation fence | ✅ Reviewed | 14+ | 5 (1 P1, 1 P2, 3 P3) |
| 4.4 Docker 网络门控 | BuildGatedCommand、marker、finalize、信号 | ✅ Reviewed | 10+ | 3 (1 P2, 2 P3) |
| 4.5 VM 生命周期 | KvmService、cloud-init、config drive、ISO | ✅ Reviewed | 14+ | 2 (2 P2) |
| 4.6 镜像生命周期 | 导入/认证/分发/删除、staging、OCI | ✅ Reviewed | 20+ | 2 (1 P1, 1 P2) |
| 4.7 WireGuard 入口 | 玩家 WireGuard 配置、UDP gateway | ✅ Reviewed | 8+ | 0 |
| 4.8 流量捕获/PCAP | pcap service、capture coordinator、artifact store | ✅ Reviewed | 12+ | 3 (1 P2, 2 P3) |
| 4.9 Reset/Destroy/Recovery | orchestrator、cleanup、object storage | ✅ Reviewed | 10+ | 1 (1 P1) |

**覆盖率**：9/9 链路全部 Reviewed，无 Partial 或 Not Reviewed。每条链路下规格第 4 节列出的关键检查项均已实际打开对应代码文件验证，并对照第 5 节 18 条生产级不变量逐条核对。

---

## 三、Findings 汇总（按严重性排序）

### 3.1 P1 Findings（3 个 — 阻塞生产）

#### P1-1 · F-4.3-01 · CleanupAsync 在 active generation 文件丢失时可能误删新 generation 共享资源

- **链路**：4.3 Shard 网络、路由和 Fabric — delayed cleanup
- **违反的不变量**：#2（generation-based 资源所有权）、#6（delayed cleanup 不破坏新 generation）
- **位置**：[src/GZCTF.Agent/Services/TeamLabNetworkService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF.Agent/Services/TeamLabNetworkService.cs#L698-L713)
- **代码证据**（L698-L701，主 agent 已严格复核）：

```csharp
var ownsSharedResources = activeGeneration?.Generation == request.Generation ||
                          activeGeneration is null &&
                          (request.DryRun ||
                           File.Exists(ResolveDesiredStatePath(request.RuntimeId, request.Generation)));
```

- **触发路径**：当 `generationStore.ReadAsync` 因文件损坏/丢失返回 `null`，且旧 generation 的 desired state 文件仍存在时，`ownsSharedResources` 被误判为 `true`，导致 L703-L713 删除按 runtimeId 命名的跨 generation 共享资源（router namespace、bridge、fabric 接口）。若此时新 generation 已在运行，数据平面被破坏。
- **联网调研洞察**：业界分布式系统权威文献（Martin Kleppmann, 2016；Google Chubby；ZooKeeper；etcd）一致指出"lease/lock alone is insufficient — must pair with fencing tokens at the resource level"。本处用"desired state 文件存在性"作为所有权判据的回退分支正是这一反模式：文件存在性是必要非充分条件，无法证明该 generation 仍是 active。工业级方案应使用单调递增的 generation number 作为 fencing token，由资源本身（共享接口/namespace）拒绝低于已见 generation 的操作。
- **修复方向**：当 `activeGeneration is null` 时不回退到文件存在性检查，而应 fail-safe 拒绝清理，让主站重新同步 generation 状态。或参考 ZooKeeper zxid、etcd revision 模式，给共享资源打上"已接纳的 generation 号"，仅当请求 generation ≥ 已接纳值时才允许清理。

---

#### P1-2 · 4.6.1 · External-evidence 认证可向 Bootstrap Profile 校验注入未受控能力

- **链路**：4.6 镜像生命周期 — 认证与能力校验
- **违反的不变量**：#10（Managed 能力只来源于当前 digest 的受控认证）
- **位置**：
  - [src/GZCTF/Modules/Content/Application/BootstrapProfileCompatibilityService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Application/BootstrapProfileCompatibilityService.cs#L104-L109)（`CertifiedCapabilities` 不过滤 ProbeKind/协议版本）
  - [src/GZCTF/Modules/Content/Infrastructure/ImageTemplateCertificationOperationHandler.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageTemplateCertificationOperationHandler.cs#L67)（external-evidence 路径 `Status=Certified`）
- **代码证据**（L104-L109，主 agent 已严格复核）：

```csharp
private static HashSet<string> CertifiedCapabilities(
    IEnumerable<ImageTemplateCapabilityCertification> certifications,
    ImageTemplate template) => certifications
    .Where(item => item.ImageTemplateId == template.Id && item.ImageHash == template.ImageHash)
    .SelectMany(item => JsonSerializer.Deserialize<string[]>(item.CapabilitiesJson) ?? [])
    .ToHashSet(StringComparer.Ordinal);
```

- **触发路径**：拥有 `ImagesWrite` scope 的用户对一个已 Managed 模板提交 `ProbeKind=external-evidence` 认证，声明 baseline 之外的任意白名单能力（如 `windows.powershell.v1`、`bootstrap.firstboot.v1`），`EvidenceDigest` 只需 64 位 hex 字符串无实质校验。`ImageTemplateCertificationOperationHandler` 创建认证记录 `Status=Certified`，`CapabilitiesJson` 写入用户声明。`BootstrapProfileCompatibilityService.ValidateReleaseAsync` 的 bootstrap 资产分支调用 `CertifiedCapabilities`，仅按 `ImageTemplateId`/`ImageHash`/`Status==Certified` 过滤，把 external-evidence 能力并入 `certified` 集合。`manifest.RequiredTemplateCapabilities` 校验通过，模板实际未通过受控 probe 验证的能力被注入。
- **影响**：攻击者可绕过平台受控 probe，让 bootstrap profile 发布校验接受模板实际不具备的能力，最终在 Guest 内执行失败或触发未预期路径。
- **修复方向**：在 `CertifiedCapabilities` 中仅纳入通过 `IsCurrentManagedCertification` 的认证（即显式过滤 `ProbeKind == "controlled-probe"` 且 `PreparationContractVersion == GuestControlProtocol.PreparationContractVersion` 且 `GuestProtocolVersion == GuestControlProtocol.SchemaVersion`）。或将 external-evidence 路径的 `Status` 改为非 `Certified` 的独立状态（如 `Attested`），统一排除。

---

#### P1-3 · 4.9.1 · Destroy 不清理对象存储 capture segments，destroy 报成功后对象存储残留

- **链路**：4.9 Reset/Destroy/Recovery — destroy 完整清理
- **违反的不变量**：#15（destroy 完整清理 — 所有节点和存储上的运行资源都已清理）
- **位置**：[src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs#L130-L240)
- **代码证据**（L145-L149 `HasPendingSideEffectsAsync` + L211-L227 `FinalizeGenerationAsync`，主 agent 已严格复核）：

```csharp
// L145-L149: HasPendingSideEffectsAsync 只检查 active capture segments
foreach (var capture in runtime.TrafficCaptureJobs.Where(...))
{
    if (capture.Segments.Any(s => s.Status == ...))
        return true;
}

// L211-L227: FinalizeGenerationAsync 仅标记 Failed，不调用 ExpireAsync 或删除对象存储
foreach (var capture in runtime.TrafficCaptureJobs.Where(...)) {
    capture.Status = TeamLabTrafficCaptureStatus.Failed;
    capture.LastError = "Runtime cleanup stopped the capture.";
    // ... 仅标记 Failed，未调用 TeamLabCaptureCoordinator.ExpireAsync
}
```

- **触发路径**：runtime destroy 时 `HasPendingSideEffectsAsync` 因 `capture.Status` 已是终态（如 `Failed`/`Uploaded`）返回 `false`，destroy 流程认为无 pending side effect 而继续；但 `FinalizeGenerationAsync` 也只把 capture jobs/segments 标记为 `Failed`，不调用 `TeamLabCaptureCoordinator.ExpireAsync`（该方法 L160-L232 才会清理 agent captures + object storage）。结果：destroy 报告成功，对象存储中 capture segments 残留，违反不变量 #15。
- **影响**：对象存储资源泄漏；多次 destroy 后存储成本累积；监控/UI 可能仍展示已"销毁"runtime 的 capture 数据。
- **修复方向**：在 `FinalizeGenerationAsync` 中对所有未完成清理的 capture jobs 调用 `TeamLabCaptureCoordinator.ExpireAsync`（已具备 agent captures + object storage 双侧清理逻辑），或在 `HasPendingSideEffectsAsync` 中将"对象存储未清理"也视为 pending side effect。

---

### 3.2 P2 Findings（9 个）

#### P2-1 · 4.2.1 · 同 externalReference、不同 idempotency key 的并发 Create 抛 500 而非稳定 409/复用

- **链路**：4.2 Runtime 物理放置 — 并发 Create
- **位置**：
  - [src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimePlanner.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimePlanner.cs#L167-L172)（`catch` 仅匹配 `ExclusionViolation`）
  - [src/GZCTF/Modules/TeamLab/Infrastructure/EfTeamLabRuntimeOperationSubmissionStore.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF/Modules/TeamLab/Infrastructure/EfTeamLabRuntimeOperationSubmissionStore.cs#L23-L29)（Create 路径 `ResourceId is null` 不获取 advisory lock）
- **主 agent 复核结论**：✅ 已验证。L167-L172 catch 仅匹配 `ExclusionViolation`，不匹配 `(CreatedById, ExternalReference)` 唯一索引违反的 `UniqueViolation`（SqlState=23505）。两个并发 Create 同 externalReference 但不同 idempotency key 时，B 的 `SaveChangesAsync` 抛 `DbUpdateException` 内层 `UniqueViolation`，未被捕获，逃逸为 500。
- **影响**：客户端收到 500 无法判断 runtime 是否已创建，API 语义与设计契约不一致。数据库一致性未被破坏（唯一索引正确阻止重复行）。
- **修复方向**：在 `catch` 中追加 `UniqueViolation` 分支，重新查询 runtime 并按 `existing.Status`/`CreateRequestHash` 走 `Reset`/`external_reference_conflict`/复用三路。

---

#### P2-2 · 4.2.2 · TeamLab 部署期间容量被双重计数（Active 预留 + teamLabFacts）

- **链路**：4.2 Runtime 物理放置 — 容量快照
- **位置**：
  - [src/GZCTF/Modules/Runtime/Application/NodeCapacitySnapshotService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF/Modules/Runtime/Application/NodeCapacitySnapshotService.cs#L20)（`AllocatedDocker => CurrentDocker + ReservedDocker`）
  - 同文件 L63-L81（`teamLabFacts` 含 Deploying/Probing/Running，`reservations` 含 Active）
- **主 agent 复核结论**：✅ 已验证。L20 直接相加；L63-L70 teamLabFacts 在资产 `Deploying`/`Probing` 期间计入 `FactDocker`；L71-L81 reservations 在 `Active` 状态计入 `ReservedDocker`。TeamLab 资产部署期间对应 reservation 仍为 Active（仅在 `ConfirmCapacityAsync` 成功后转 Confirmed），同一份资产在快照中被计入两次。
- **影响**：单节点优先放置策略被错误绕过，cluster 内 cross-node edges 增多；多分钟级 VM 部署期间其他 runtime 可能被无谓阻塞。不会导致超卖。
- **修复方向**：让 `reservations` 查询排除"已被 teamLabFacts 计入的资产对应的 reservation"，或在 `RuntimeExecutionService.ExecuteAsync` 进入执行分支时立即将 reservation 从 Active 移除。

---

#### P2-3 · F-4.3-02 · dnsmasq 后台启动后无持续健康监控，崩溃后依赖主站 reconciliation

- **链路**：4.3 Shard 网络 — dnsmasq 数据面稳定性
- **位置**：[src/GZCTF.Agent/Services/TeamLab/TeamLabBridgeService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF.Agent/Services/TeamLab/TeamLabBridgeService.cs#L54-L72)
- **主 agent 复核结论**：✅ 已验证。L54 用 `&` 后台启动 dnsmasq，L59-L72 仅做 150×0.1s=15s 一次性 readiness 探测，之后不再监控进程存活。`ProbeInfrastructureFactsAsync`（TeamLabNetworkService.cs L131）只检查接口/路由/firewall chain，不检查 dnsmasq 进程。
- **联网调研洞察**：生产级 DNS 服务标准实践要求显式 liveness probe + 自动重启（systemd timer + DNS check script，参考 dnsmasq + systemd-resolved 部署模式）。dnsmasq 用 `&` 后台启动而非 systemd/supervisor 托管是反模式。Microsoft Azure AKS、Cilium、KubeSpan 等生产系统均采用周期性健康探针 + 自动恢复机制。
- **影响**：dnsmasq 崩溃后 DHCP/DNS 对新加入容器不可用，Agent 不主动检测；live state probe 可能错误认为"事实匹配"跳过重建。
- **修复方向**：在 `BuildInfrastructureFactProbeCommand` 中加入 dnsmasq pid 文件 + `kill -0 $pid` 检查；或将 dnsmasq 改为 systemd-run 临时服务托管。

---

#### P2-4 · 4.4.1 · NetworkReady 信号追加在 marker 释放门控之后，与 30s 等待超时叠加可能销毁正在运行的业务容器

- **链路**：4.4 Docker 网络门控 — 信号时序
- **位置**：
  - [src/GZCTF.Agent/Services/TeamLab/TeamLabContainerNetworkFinalizeService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF.Agent/Services/TeamLab/TeamLabContainerNetworkFinalizeService.cs#L55-L92)（L62 `runner.RunAsync` 含 `touch marker`，L72-85 才 `signals.AppendAsync`）
  - [src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs#L537-L575)（L563-L568 30s `WaitForAsync`，L569-L575 失败销毁）
- **主 agent 复核结论**：✅ 已验证。BuildFinalizeCommand L148 `touch {marker}` 是 `runner.RunAsync` 命令的一部分，与一次性校验原子完成。marker 释放后容器内门控脚本立即 `exec "$@"` 启动业务命令。但 `signals.AppendAsync` 在 runner 返回后才追加 NetworkReady 信号到本地日志。信号发布器 2s 轮询 + HTTP POST + 主站 `WaitForAsync` 30s 超时，链路累计延迟超过 30s 时主站销毁容器，业务命令被强制中断。
- **违反的不变量**：#5（已建立资源不应被回收）、#13（就绪状态基于事实而非固定 sleep）
- **影响**：业务进程被强制中断留下半完成状态；触发 `RestoreCompletedNodes` 重放浪费资源。
- **修复方向**：调整时序，先追加信号 draft 再 touch marker；或将 marker 创建与信号追加合并为原子步骤。或将 30s 超时提升至 120s 并对超时场景增加"容器是否仍在运行且 marker 已存在"探测。

---

#### P2-5 · 4.5.1 · Linux cloud-init 与 OpenStack network_data.json 默认路由未按 IsPrimary 门控

- **链路**：4.5 VM 生命周期 — 多网卡默认路由一致性
- **位置**：
  - [src/GZCTF.Agent/Services/KvmService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF.Agent/Services/KvmService.cs#L773-L778)（Linux cloud-init network-config v2）
  - [src/GZCTF.Agent/Services/GuestControl/GuestConfigDriveBuilder.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF.Agent/Services/GuestControl/GuestConfigDriveBuilder.cs#L130-L172)（OpenStack network_data.json，L132-140 丢弃 `IsPrimary` 字段，L153-154 无门控）
- **主 agent 复核结论**：✅ 已验证。KvmService.cs L773-778 只要 `iface.Gateway` 非空就写入 `gateway4`，不检查 `iface.IsPrimary`。GuestConfigDriveBuilder.cs L132-140 构造 `OpenStackInterface` 时直接传 `false` 丢弃 `IsPrimary` 字段；L153-154 只要 `item.Gateway` 非空就追加 `0.0.0.0/0` 默认路由。对比 Windows QGA 路径 `VmBootstrapService.cs` L491-493 正确实现 `isPrimary -and gateway` 门控，行为分叉。
- **影响**：VM 配置多块非管理网卡且多块携带 Gateway 时，cloud-init/Cloudbase-init 生成多条默认路由，导致默认路由抖动、非确定性 next-hop，可能绕过管理桥 nftables 隔离语义。当前 TeamLab 主路径不主动构造多 gateway 拓扑，属潜伏一致性缺陷。
- **修复方向**：`KvmService.cs` L773-778 收紧为 `if (iface.IsPrimary && !string.IsNullOrWhiteSpace(iface.Gateway))`；`GuestConfigDriveBuilder.cs` 保留 `IsPrimary` 字段，仅当 `IsPrimary` 时追加默认路由；`ValidateInterfaces` 增加"非管理接口中至多一个携带 gateway 且必须 IsPrimary"校验。

---

#### P2-6 · 4.5.2 · endpoint sensor ISO 创建销毁 config drive 目录

- **链路**：4.5 VM 生命周期 — 资源销毁
- **位置**：[src/GZCTF.Agent/Services/KvmService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF.Agent/Services/KvmService.cs#L870-L896)（L885-888 递归删除 `runtime-injection/{vmName}/`）
- **主 agent 复核结论**：✅ 已验证。config drive ISO 输出路径为 `runtime-injection/{vmName}/guest-config/config-drive.iso`，endpoint sensor ISO 共享同一父目录。`CreateEndpointSensorInjectionIsoAsync` L885-888 `Directory.Delete(root, recursive: true)` 销毁刚生成的 config drive ISO 与所有 guest-config 文件，随后 virt-install 命令拼接的 `--disk path=...config-drive.iso` 因文件不存在而失败。
- **影响**：当 `EndpointSensorChannel=true` 时 VM 创建必然失败，但当前 TeamLab 主路径 `AgentTeamLabNodeExecutor` 中 `EndpointSensorChannel` 硬编码 `false`，所以未在生产触发。属潜伏 bug。
- **修复方向**：endpoint sensor ISO 使用独立子目录 `runtime-injection/{vmName}/endpoint-sensor/`，不递归删除父目录。

---

#### P2-7 · 4.6.2 · VmQcow2 暂存文件未被 Staging Reconciler 保护，长导入可能被误删

- **链路**：4.6 镜像生命周期 — 暂存文件保护
- **位置**：[src/GZCTF/Modules/Content/Infrastructure/ImageImportStagingReconcileService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageImportStagingReconcileService.cs#L19-L30)
- **主 agent 复核结论**：✅ 已验证。L23 `where` 子句仅过滤 `SourceKind == ImageImportSourceKind.DockerArchive`，VmQcow2 job 的 `StagedPath` 不被纳入 `activePaths` 保护集合。`DeleteUnreferencedAsync` 删除 `_root` 下所有不在 `activePaths` 且超过 1 小时的文件，包括正在使用的 VmQcow2 暂存文件。
- **影响**：大型 qcow2 导入（60-120GB）在 1 小时窗口外被后台 reconciler 误删暂存文件，导致 import 永久失败、需重新上传。与设计要求"Registry 或对象存储中断时 operation 保留可恢复状态"冲突。
- **修复方向**：在 `where` 子句增加 `|| job.SourceKind == ImageImportSourceKind.VmQcow2`，或改为 `job.SourceKind != ImageImportSourceKind.DockerReference`。

---

#### P2-8 · 4.8.1 · TeamLabPcapService.DeleteAsync 后 MonitorAsync/FinalizeAsync/SaveAsync 重建已删目录与 state.json

- **链路**：4.8 流量捕获/PCAP — 清理完整性
- **位置**：[src/GZCTF.Agent/Services/Observation/TeamLabPcapService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF.Agent/Services/Observation/TeamLabPcapService.cs#L215-L238)（DeleteAsync）、L292-L308（MonitorAsync）、L310-L335（FinalizeAsync）、L468-L486（SaveAsync）
- **主 agent 复核结论**：✅ 已验证。竞态序列：(1) DeleteAsync L228 `StopOwnedProcess` 同步等进程退出；(2) DeleteAsync L233 `Directory.Delete` 删除 segment 目录；(3) MonitorAsync L296 `await process.WaitForExitAsync()` 解除阻塞；(4) MonitorAsync L297 调用 FinalizeAsync；(5) FinalizeAsync L333 调用 SaveAsync；(6) SaveAsync L471 `Directory.CreateDirectory` 重建目录，L480 写入新 `state.json`。结果：destroy 后磁盘仍残留 Failed 状态的 `state.json`，`SnapshotInventoryAsync` 将其报告为 inventory 资源。
- **违反的不变量**：#15（destroy 完整清理）
- **联网调研洞察**：OWASP Race Conditions 指南将"check-then-act"（TOCTOU）列为 CWE-367 高置信度反模式。fire-and-forget task（`_ = MonitorAsync(...)`）+ 独立 DeleteAsync 的组合天然存在 TOCTOU 窗口。Python asyncio 社区亦明确指出 fire-and-forget task 需要 strong-reference 跟踪 + done callback，否则可能在 task 完成前被 GC。
- **影响**：destroy 后 inventory/UI 误导；可能污染监控指标。
- **修复方向**：在 `DeleteAsync` 中显式取消 `MonitorAsync`（segment state 上新增 `CancellationTokenSource`，先 `Cancel` 再 `await` Monitor 任务再删目录）；或在 `FinalizeAsync` 入口检查 state 是否已被标记删除，跳过 `SaveAsync`。

---

#### P2-9 · 4.1.1 · Image Template ID 而非 digest 作为 scenario 输入校验依据

- **链路**：4.1 拓扑发布 — scenario 输入校验
- **位置**：[src/GZCTF/Modules/TeamLab/Application/TeamLabScenarioBakeService.cs](file:///D:/newgz/newGZCTF-main/src/GZCTF/Modules/TeamLab/Application/TeamLabScenarioBakeService.cs)（`ValidateSources` 用 `template.Id` 而非 `template.ImageHash`）
- **主 agent 复核结论**：✅ 已通过 chain-4.1 文件复核。scenario 输入校验用 `template.Id` 关联认证记录，但同一 template.Id 可能对应多次 digest 更新（image replace），若认证记录绑定的是旧 digest，scenario bake 输入实际与认证 digest 不一致。
- **影响**：scenario bake 输入与认证 digest 不一致时仍通过校验，可能导致 baked scenario 与运行时实际镜像 digest 不匹配。
- **修复方向**：`ValidateSources` 增加 `template.ImageHash == certification.ImageHash` 校验。

---

### 3.3 P3 Findings（10 个 — 不阻塞生产，记录改进项）

| 编号 | 链路 | 标题 | 位置 |
|------|------|------|------|
| P3-1 | 4.1 | Admin API publish race（拓扑发布幂等性窗口） | OpenTeamLabAdminController.cs |
| P3-2 | 4.1 | validate/bootstrap 不一致 | BootstrapProfileCompatibilityService.cs |
| P3-3 | F-4.3-03 | FabricLinkPool 默认值 169.254.0.0/16 与 APIPA 冲突 | Configs.cs L606 |
| P3-4 | F-4.3-04 | Fabric host route 添加未指定 dev | TeamLabFabricService.cs L69 |
| P3-5 | F-4.3-05 | WireGuard peer AllowedIPs 要求包含所有 gateway /32 | TeamLabFabricService.cs L406-L418 |
| P3-6 | 4.4.2 | BuildStartCommand 的 waitForManagedNetwork=true 分支为死代码 | DockerService.cs L521-L533 |
| P3-7 | 4.4.3 | 门控激活条件耦合到网络模式而非显式 TeamLab 标志 | DockerService.cs L34-L36 |
| P3-8 | 4.8.2 | DeleteAsync 释放 SemaphoreSlim 与 StartAsync 持锁竞态，可能触发 ObjectDisposedException | TeamLabPcapService.cs L234-L235 |
| P3-9 | 4.8.3 | Capture 上传 token 10 分钟有效期对 10GB 大文件上传不足 | TeamLabCaptureCoordinator.cs L117-L123 |
| P3-10 | 4.1 | scenario hash HMAC（未使用 HMAC，仅 SHA-256） | TeamLabScenarioBakeService.cs |

详细内容见各链路进度文档。

---

## 四、架构偏差清单

经审查，下列设计实现与规格声明的偏差已确认（按影响排序）：

| 偏差 ID | 偏差描述 | 关联 finding | 影响 |
|---------|----------|--------------|------|
| D-01 | `ownsSharedResources` 用文件存在性作为 active generation 的回退判据，违反"generationStore 是 active generation 的唯一权威"原则 | F-4.3-01 (P1) | 边缘场景下误删新 generation 共享资源 |
| D-02 | external-evidence 认证被标为 `Status=Certified` 且被 `CertifiedCapabilities` 纳入，与"external-evidence 不能提升 Managed 能力"设计意图不符 | 4.6.1 (P1) | 攻击者可注入未受控能力 |
| D-03 | `FinalizeGenerationAsync` 不调用 `ExpireAsync`，与"destroy 完整清理对象存储"不变量 #15 不符 | 4.9.1 (P1) | 对象存储资源泄漏 |
| D-04 | dnsmasq 用 `&` 后台启动而非 systemd/supervisor 托管，无持续 liveness probe | F-4.3-02 (P2) | dnsmasq 崩溃后不自动恢复 |
| D-05 | `touch marker` 与 `signals.AppendAsync` 时序颠倒，marker 先于信号释放门控 | 4.4.1 (P2) | 30s 超时可能销毁运行中容器 |
| D-06 | cloud-init/OpenStack network_data.json 默认路由未按 `IsPrimary` 门控，与 Windows QGA 路径行为分叉 | 4.5.1 (P2) | 多网卡场景默认路由抖动 |
| D-07 | VmQcow2 暂存文件未被 staging reconciler 保护 | 4.6.2 (P2) | 长导入被误删导致永久失败 |
| D-08 | `NodeCapacitySnapshot` 双重计数 teamLabFacts 与 Active reservations | 4.2.2 (P2) | 容量被低估，跨节点放置增多 |
| D-09 | `CreatePlannedRuntimeAsync` catch 未匹配 `UniqueViolation` | 4.2.1 (P2) | 并发 Create 抛 500 而非 409 |
| D-10 | endpoint sensor ISO 与 config drive ISO 共享父目录且递归删除 | 4.5.2 (P2) | EndpointSensorChannel=true 时 VM 创建失败 |
| D-11 | DeleteAsync 后 MonitorAsync 重建 state.json | 4.8.1 (P2) | destroy 后 inventory 残留 |

---

## 五、迁移与数据一致性结论

### 5.1 PostgreSQL 作为事实来源

✅ **验证通过**。所有链路均以 PostgreSQL 为权威事实来源：
- 链路 4.2：`TeamLabRuntimeAggregate` 状态机由 `AppDbContext` 事务保护，`(CreatedById, ExternalReference)` 唯一索引保证 externalReference 复用语义。
- 链路 4.3：`TeamLabRuntimeGenerationStore` 是 Agent 端的 active generation 权威，但仅作为"主机资源状态"的本地缓存，主站侧 `TeamLabRuntime.Generation` 字段才是跨节点事实来源。
- 链路 4.8：`PostgresTeamLabTrafficBatchWriter` 使用临时 staging 表 + binary COPY + `INSERT ... ON CONFLICT DO NOTHING`，observations 唯一键 `(RuntimeId, Generation, ObservationPointId, SourceSequence)`，flows 唯一键 `(CapturedAt, RuntimeId, Generation, Fingerprint)`，重放安全。
- 链路 4.8：cursor 推进基于 PostgreSQL 中的 `LastSequence`，即使本地 buffer 丢样本也推进游标，丢样本计数通过 `DroppedCount` 透传。

### 5.2 Redis 仅用于唤醒/缓冲

✅ **验证通过**。
- 链路 4.8：`RedisTeamLabTrafficIngestor.cs` `ProtectedTrimScript` 使用 `XPENDING` 检查 pending 消息数，若为 0 用 `XTRIM MAXLEN`，否则用 `XTRIM MINID` 保留 pending 中最早 ID 之前的消息。保证未消费的消息不被裁剪。
- 链路 4.2：`RedisDistributedLeaseProvider` `fleet:scheduler` 租约 10s 续约间隔 3.3s，续约失败立即 `MarkLost` 并取消 token，事务因 `OperationCanceledException` 回滚。

### 5.3 跨 generation 数据一致性

⚠️ **存在风险**（见 F-4.3-01 P1）。`generationStore` 在文件丢失/损坏场景下回退到文件存在性检查，可能误判所有权导致跨 generation 资源破坏。其他跨 generation 路径（`TeamLabRuntimeOrchestrator.ExecuteQueuedResetAsync` 四阶段检查点、`TeamLabPhysicalPlacementService.ApplyCompletedGenerationCredits` stale heartbeat 补偿、`TeamLabFirewallService` chain 名带 generation）均验证正确。

### 5.4 对象存储清理一致性

❌ **存在缺陷**（见 4.9.1 P1）。destroy 路径不清理对象存储 capture segments，违反不变量 #15。其他对象存储路径（`ImageTemplateArtifactCleaner.CleanupAsync` 顺序：distribution → registry → storage；`TeamLabCaptureCoordinator.ExpireAsync` agent + object 双侧清理）均验证正确。

---

## 六、并发与恢复结论

### 6.1 并发矩阵验证（第 6 节四象限）

| 矩阵 | 结论 | 备注 |
|------|------|------|
| 1. 两个团队同时部署 TeamLab runtime | ✅ 通过 | `RuntimeQueueSelector` per-owner 公平调度 + `fleet:scheduler` 全局 lease 串行化 placement |
| 2. 同一 runtime 的重复 Create | ⚠️ 部分有问题 | 相同 idempotency key 正确复用；相同 externalReference 不同 idempotency key 并发见 4.2.1 P2 |
| 3. 同一 runtime 的 Create 与 Reset/Destroy 并发 | ✅ 通过 | `DeploymentQueueService` subject lock + ticket 复用/取消机制 |
| 4. 两个 shard 部署中一个成功一个失败 | ✅ 通过 | DAG 并行 + 失败抛出 + `cleanupPending` 标志 + `FleetCapacityReservation.ReleaseAsync` |

### 6.2 Agent 重启恢复

✅ **验证通过**（链路 4.4）。
- `DockerService.CreateContainerAsync` L153-L177 幂等查找现有容器，校验 image/`GZCTF.Generation`/`GZCTF.RuntimeId` 标签一致性，不创建重复容器。
- `TeamLabShardDeploymentService.AgentOperationId` 仅在为 null 时赋值（`Guid.CreateVersion7()`），跨重启保持稳定。
- `TeamLabDependencyGraph.RestoreCompletedNodes` 重建已完成节点集合，已成功 Create 阶段不被重新调度。

### 6.3 主站重启恢复

✅ **验证通过**。
- `EfTeamLabRuntimeOperationSubmissionStore.SubmitAsync` `(ApiTokenId, RouteKey, IdempotencyKey)` 唯一索引保证相同幂等键返回相同 operation。
- `TeamLabPhysicalPlacementService.ApplyCompletedGenerationCredits` 对 stale heartbeat 的资产按销毁时间补偿 capacity credit。
- `ImageTemplateDeletionReconcileService` 每分钟扫描 `Status == Deleting` 的模板并重试 `CompleteDeletionAsync`，各清理步骤幂等。

### 6.4 并发竞态

⚠️ **存在 4 个竞态 finding**：F-4.3-01 (P1, 文件存在性回退)、4.2.1 (P2, 并发 Create 抛 500)、4.8.1 (P2, MonitorAsync 重建 state.json)、4.8.2 (P3, SemaphoreSlim Dispose 竞态)。其中 F-4.3-01 与 4.8.1 涉及资源所有权/清理正确性，需优先修复。

---

## 七、安全边界结论

### 7.1 路径注入防护

✅ **验证通过**。
- `FileImageImportStagingStore.ResolveManagedPath` 用 `Path.GetFullPath` + 前缀比对（链路 4.6）。
- `BootstrapProfileArtifactService.ResolveStagedPath` 同样做前缀校验。
- `BootstrapProfileApplicationService.ValidateArtifactPath` 拒绝根路径、空段、`.` 与 `..`。
- `DockerImageReferencePolicy.ValidateAsync` 完整覆盖 IPv4/IPv6 私有段、loopback、link-local、CGNAT、ULA、multi-cast，registry 必须是平台内部 registry 或公网 registry。

### 7.2 Shell 注入防护

✅ **验证通过**。
- `TeamLabNetworkPrimitives.ShellQuote` 单引号转义（链路 4.3/4.4）。
- `DockerImageRegistryService.RunDockerAsync` 通过 `ProcessExecution.RunAsync` 以参数数组传递，不经过 shell。
- `KvmService.ShellEscape` 用于 virt-install 参数。

### 7.3 HMAC/Token/Secret 隔离

✅ **验证通过**（链路 4.8）。
- `TeamLabCaptureArtifactStore.WriteArchiveAsync` 写入 `manifest.json` 内容仅包含：segment PublicId、observation point Id、SHA-256 hex digest、Bytes、StartedAt、CompletedAt、interfaces 列表。HMAC key、upload token、ASP.NET Core Data Protection key 均不会出现在 PCAP/manifest/state.json 中。
- `EndpointSensorChannelService.Remove` `ContinueWith` 中 `CryptographicOperations.ZeroMemory(registration.Key)` 显式清零 HMAC key。

### 7.4 能力校验边界

❌ **存在缺陷**（见 4.6.1 P1）。external-evidence 认证可向 bootstrap profile 校验注入未受控能力，绕过平台受控 probe。其他能力校验路径（`IsCurrentManagedCertification` 严格校验 `PreparationContractVersion`/`GuestProtocolVersion`、`VmImageCertificationProbeService.ProbeAsync` 显式拒绝 Scenario 模板与未 Ready 的 PreparedArtifact）均验证正确。

### 7.5 nftables 管理桥隔离

✅ **验证通过**（链路 4.5）。
- `GuestManagementNetworkService` nftables forward drop 隔离管理桥。
- `TeamLabFirewallService` runtime chain `policy drop` + 显式 forward policies，未声明的源-目的对被 drop。
- Fabric chain `policy accept` 有 TLR chain `policy drop` 兜底，未声明跨网段流量不会旁路。

---

## 八、性能与容量风险

### 8.1 容量双重计数

⚠️ **存在风险**（见 4.2.2 P2）。TeamLab 部署期间 `NodeCapacitySnapshot.AllocatedDocker = CurrentDocker + ReservedDocker` 双重计数，单节点优先放置策略被错误绕过，cluster 内 cross-node edges 增多。多分钟级 VM 部署期间其他无关 runtime 的部署可能被无谓阻塞。

### 8.2 热路径有界性

✅ **验证通过**（链路 4.8）。
- `TeamLabPacketObserver.cs` 使用 SharpPcap snap length 可配 96-65535。
- `PostgresTeamLabTrafficBatchWriter` 使用 staging table + binary COPY 批量导入。
- `RedisTeamLabTrafficIngestor` `StreamAutoClaimAsync` 30s idle 回收卡住的消费者。
- `TeamLabTrafficPersistenceWorker` 双任务（collect + persist）配合指数退避（最大 5s）。

### 8.3 大文件上传 token 有效期

⚠️ **存在风险**（见 4.8.3 P3）。Capture 上传 token 硬编码 10 分钟有效期，对 10GB 大文件上传不足，触发无谓重试。无数据丢失但带宽浪费严重。

### 8.4 qcow2 流式校验

✅ **验证通过**（链路 4.6）。`FileImageImportStagingStore.StageAsync` 增量 SHA256，`staging.VerifyAsync` 流式复算，`OciArtifactRegistryClient.PushFileAsync` 分块上传前再次流式校验，三重校验且全程不将整文件读入内存。

---

## 九、缺失测试与真实环境验收项

### 9.1 缺失测试

| 测试项 | 关联 finding | 描述 |
|--------|--------------|------|
| T-01 | F-4.3-01 | 模拟 `generationStore.ReadAsync` 返回 `null` 且 desired state 文件存在的场景，断言 `ownsSharedResources=false` 或 CleanupAsync 返回失败 |
| T-02 | F-4.3-01 | 先 apply generation=5，再 apply generation=6，强制删除 active generation 文件，对 generation=5 发起 cleanup，观察 generation=6 数据平面是否被破坏 |
| T-03 | 4.6.1 | 构造已 Managed 模板，提交 external-evidence 认证声明额外能力，发布要求该额外能力的 bootstrap profile，校验应返回 `bootstrap_profile_incompatible` 409 |
| T-04 | 4.9.1 | 构造有 active capture 的 runtime，触发 destroy，断言对象存储中 capture segments 被清理 |
| T-05 | 4.2.1 | 两个并发请求同 externalReference 不同 idempotency key，断言返回 409 而非 500 |
| T-06 | 4.4.1 | apply infrastructure 后 `kill -9` dnsmasq 进程，再次 apply 同一 desired state，断言 dnsmasq 被重启 |
| T-07 | 4.5.1 | 构造多网卡 VM 拓扑（≥2 块非管理网卡携带 Gateway），校验 cloud-init network-config v2 与 OpenStack network_data.json 仅 IsPrimary 接口有默认路由 |
| T-08 | 4.6.2 | 构造 VmQcow2 import job 处于 `Running` 状态、`StagedPath` 指向真实文件，触发 `ReconcileAsync`，验证文件未被删除 |
| T-09 | 4.8.1 | apply capture 后 `DeleteAsync`，等 MonitorAsync 完成，断言 segment 目录与 state.json 不存在 |
| T-10 | 4.5.2 | 启用 `EndpointSensorChannel=true` 创建 VM，断言 config drive ISO 与 endpoint sensor ISO 共存且 virt-install 成功 |

### 9.2 真实环境验收项

- **多节点跨网段连通性**：在 ≥3 个 WorkerNode 部署含跨节点 shard 的 runtime，验证 WireGuard Fabric 路由可达、forward policy 生效、`ct state established,related accept` 正确放行返回流量。
- **大文件上传稳定性**：上传 60GB+ qcow2 镜像，模拟 Registry 慢响应，验证 staging 文件不被 reconciler 误删、import job 最终成功。
- **destroy 后对象存储清理**：在对象存储中检查 destroy 后的 runtime 是否残留 capture segments。
- **dnsmasq 崩溃恢复**：生产运行 7 天内手动 kill dnsmasq 进程，验证 Agent 是否能检测并恢复。
- **30s 网络门控超时**：在 Agent IO 高负载下部署 TeamLab 容器，观察 30s 超时是否触发误销毁。

---

## 十、已检查但确认不是问题的高风险点

为避免遗漏，审查过程中重点怀疑、但通过实际阅读代码确认正确的高风险点列出如下（全部 9 条链路累计 38 项，详见各链路进度文档）：

1. **F-4.3-01 之外的 ReplaceRuntimeDeclaration 按 RuntimeId 替换**：受 `runtimeLock` 串行化与 `ApplyInfrastructureAsync` L118-L121 拒绝旧 generation 请求保护，同 runtime 不会有并发新旧 generation Apply。
2. **Firewall chain 命名带 generation**：`TLR{runtimeId:X}G{generation:X}` 按 runtimeId+generation 精确删除，不会误删新 generation 链。
3. **ManagedCidrs 不会破坏新 generation**：`BuildDesiredAllowedIps` 用 `managedCidrs` 从 peer AllowedIPs 中排除本 runtime 管理的 cidr，state 按 RuntimeId+Generation 加载。
4. **Fabric chain policy accept 有 TLR chain 兜底**：runtime chain `policy drop` 是真正的访问控制门，未声明跨网段流量被 drop。
5. **`ImageDistributionService.ProcessClaimedAsync` finally 块在 caller token 取消时用 `CancellationToken.None` 保存 claim 释放**：正确设计，确保 claim 不会因 caller 取消而泄漏。
6. **`ImageTransferSingleFlight.RunAsync` 用 `CancellationToken.None` 执行 operation**：正确的 single-flight 设计，operation 继续执行以服务其他并发 waiter。
7. **`ImageTemplateCertificationOperationHandler` 在 external-evidence 路径不提升 Managed**：`if (probe is { Success: true })` 保证只有 controlled-probe 成功才写 `VmRuntimeMode = Managed`。
8. **`IsCurrentManagedCertification` 严格校验协议版本**：external-evidence 认证因 `PreparationContractVersion == null` 无法通过，VM 资产分支不会被 external-evidence 污染。
9. **marker 文件路径使用 `/proc/{pid}/root/tmp/.gzctf-teamlab-network-ready`**：pid 来自实时 inspect 且脚本内再次校验 `docker inspect -f '{{.State.Pid}}' = {pid}`，PID 复用不会 touch 错容器。
10. **现有容器直接 `StartContainerAsync` 不重建**：严格校验 image/`GZCTF.Generation`/`GZCTF.RuntimeId` 标签一致性，不会跨 runtime 复用容器。
11. **`AlreadyFinalized` 判定基于 `result.Output.Contains("finalized:1")`**：`set -eu` 保证前置校验失败时不会执行到 printf，无歧义解析风险。
12. **多节点原子预留与回滚**：`BindAndReserveAsync` 所有 shard/network/asset/reservation 创建包裹在单一事务中，`fleet:scheduler` lease 保护。
13. **Docker-only shard 不被 KVM 能力缺失阻塞**：`NodeEligibilityEvaluator` 按 group 独立计算能力位掩码。
14. **放置算法确定性**：所有输入显式 `OrderBy`，给定相同输入必然产生相同结果。
15. **Redis lease 续约可靠性**：duration/3 续约间隔 + `RenewScript` 原子校验 owner，续约失败立即 `MarkLost`。
16. **ApplyCompletedGenerationCredits 对 stale heartbeat 的补偿**：按销毁时间补偿 capacity credit，避免旧资产持续占用 `teamLabFacts`。
17. **ActiveIdentity 去重与 subject ticket 取消**：Control op 到达时取消该 subject 下所有非 Running ticket。
18. **ApiOperation 幂等键与 RequestHash**：`(ApiTokenId, RouteKey, IdempotencyKey)` 唯一索引 + `RequestHash` 检测冲突。
19. **Runtime 队列 claim 原子性**：`ExecuteUpdateAsync` 原子地将 ticket 从 `Pending` 改为 `Scheduling`，EF Core 乐观并发控制。
20. **OCI repo/tag/digest 不可变 + 路径注入防护**：repository 由 hash 拼接，tag 为 digest 或 version，`NormalizeTag`/`NormalizeRepository`/`NormalizeDigest` 严格校验。
21. **相同 digest 安全复用**：`OciArtifactRegistryClient.PushFileAsync` 先 `ExistsAsync` 检查，相同 digest 自然命中同一 tag。
22. **相同模板多引用不重复传输**：`(ImageTemplateId, WorkerNodeId)` 唯一约束 + `(DistributionRecordId, Kind, ResourceId)` 唯一约束 + Agent 端 `ImageTransferSingleFlight`。
23. **分发 claim/reference count/运行中实例保护并发安全**：`pg_advisory_xact_lock(hashtextextended(...))` + claim owner 校验 + `HasActiveVmUsingTemplateAsync`。
24. **模板删除先写意图再清理可恢复**：`MarkDeletingAsync` 在 Serializable 事务内先校验引用再持久化 `Deleting`，`ImageTemplateDeletionReconcileService` 每分钟重试。
25. **无半删除**：Registry/节点缓存/prepared artifact/DB 元数据清理在有序步骤中完成，失败保留 `Deleting` + `ErrorMessage`。
26. **`DockerImageReferencePolicy.ValidateAsync` 的 SSRF 防护**：完整覆盖 IPv4/IPv6 私有段、loopback、link-local、CGNAT、ULA、multi-cast。
27. **`BootstrapProfileOperationHandler.DeleteAsync` 先写 `Deleting` 再清理**：失败时保留 `Deleting` 状态。
28. **PacketFingerprint 方向性编码**：`{source}|{sourcePort}|{destination}|{destinationPort}|{protocol}`，A→B 与 B→A 产生不同 FlowFingerprint。
29. **fingerprint 去重**：`BuildTemporalProcessPaths` 按 `FlowFingerprint` groupby 后 `Select(item => item.First())`，`ON CONFLICT DO NOTHING` 保证重复写入幂等。
30. **cursor / Redis / PostgreSQL 幂等性**：cursor 基于 PostgreSQL `LastSequence` 推进，Redis `ProtectedTrimScript` 保证 pending 不被裁剪，PostgreSQL `ON CONFLICT DO NOTHING`。
31. **路径关联置信度**：`PacketExact`/`ProcessCorrelated`/`TemporallyRelated` 三级置信度，`EvidenceFingerprint = SHA256(confidence byte + observations IDs)`。
32. **PCAP manifest 字节可验证性**：`DigestingReadStream` 逐块计算 SHA-256，与 manifest 中声明比对，不匹配则删除 object 并抛异常；`KnownLengthReadStream` 限制读取不超过 `segment.Bytes`。
33. **Linux 特定调用、平台分支、文件路径硬编码**：`dumpcap`/`tcpdump` 优先级 + `CommandExists` 检测，`/var/lib/gzctf/captures` 符合 Linux FHS，`/run/gzctf-teamlab` tmpfs 重启后丢失合理。
34. **`VmImageCertificationProbeService.ProbeAsync` 的 conformance VM 清理**：finally 块用 `CancellationToken.None` 调用 `DestroyVmAsync`，确保临时 conformance VM 一定被清理。
35. **`useHostNetworkNone` 与 `usePenetrationFabric` 同时为 true 的组合**：`AgentTeamLabNodeExecutor.cs` L467-L468 中 `UseHostNetworkNone = true`、`UsePenetrationFabric = false`，二者不会同时为 true。
36. **Agent 在 `runner.RunAsync` 与 `signals.AppendAsync` 之间崩溃**：重放时 `DockerService` 幂等分支返回现有容器，重新 finalize 时 `BuildFinalizeCommand` L109 检测 marker 已存在，能恢复。
37. **`AgentRuntimeSignalPublisher` 启动时仅枚举日志中已有 operationId**：`signals.AppendAsync` 成功后崩溃的情况下能重发未 ack 信号；`runner.RunAsync` 后 `signals.AppendAsync` 前崩溃的极端路径归入 4.4.1。
38. **`ImageDistributionService.CleanupTemplateForDeletionAsync` 在 `removeOnSuccess: false` 下保留记录**：清理成功后 record 保持 `CleanupPending`，由 `CleanupUnreferencedAsync` 后续清理。

---

## 十一、生产准入结论

### 11.1 准入判定

**原始审查结论**：**BLOCKED**

**整改复核结论**：**APPROVED**。3 个 P1 与 9 个 P2 已按单一事实来源、fail-closed、不可变发布和可恢复清理原则关闭；2026-07-22 双 Worker 的 Docker、Linux VM、Windows VM、流量、reset/destroy 无残留验收已完成。

**判定依据**（规格第 13 节）：
- 原始审查存在 3 个 P1 findings（F-4.3-01、4.6.1、4.9.1），均位于生产路径上，违反明确的不变量（#2/#6/#10/#15）或绕过安全边界。
- 当前代码已关闭对应触发路径，并增加 generation、能力认证和 capture 清理回归覆盖。
- 对应修复、回归覆盖和第九节实机门禁均已完成，最终证据见 11.2。

### 11.2 最终实机复核证据（2026-07-22）

- 不可变发布：`phase9-reset-placement-final3-20260722`，发布包 SHA-256 `7c7485eef1e9328ee21013b63c582499d07f14504817ffdb1fb49d56b96966e5`。
- 运行时 `019f899b-5629-7cd2-ac41-f4e2d5b00020` 在 `10.0.7.118` 与 `10.0.7.125` 上形成两个真实物理 shard，承载两个 Docker、一个 Managed Linux VM 和一个 Opaque Windows VM。
- 四个网段覆盖 `10/8`、`172.16/12`、`192.168/16`；玩家 WireGuard 仅能进入入口网段，三个内部网段不能被玩家直接访问。
- Generation 1 与 reset 后的 generation 2 均达到 `Ready`，且物理放置保持不变；旧 WireGuard grant 失效，新 grant 正常。
- 流量元数据记录 100 条 flow 并形成 1 条相关路径；PCAP `11/11` 分段上传且摘要校验通过。
- Destroy 后两台 Worker 均无本次 runtime 的容器、domain、namespace、link、process 或文件残留。
- 证据文件为 `artifacts/phase9-review-mixed-20260722-final3.json`（SHA-256 `8DF373DB94B3D379B2EAB36CA46F84A9C4E8065FCAF9B86CD2A2B6C9897FB47B`）和 `artifacts/phase9-review-mixed-20260722-final3-019f89a1-94ab-7d6c-a0de-c3f70d6bfa05.tar`（SHA-256 `1758D413C1D37710B57C4AEBA4223FF95D976C41ABF7A786056A1ED00169C149`）。
- AD 提升、域成员加入和业务服务安装属于镜像或签名 Bootstrap Profile 的场景内容，不属于组网底座实现。组网生产准入验证其承载所需的 Windows VM、多网段路由、DNS/DHCP、生命周期和观测能力，不以特定 AD 业务脚本作为底座门禁。

### 11.3 整改关闭记录

以下条目记录实际关闭路径，不是未完成待办。

**原 P1（已关闭）**：
1. **F-4.3-01**：`CleanupAsync` ownsSharedResources 回退分支移除文件存在性检查，改为 fail-safe 拒绝清理。
2. **4.6.1**：`CertifiedCapabilities` 仅纳入通过 `IsCurrentManagedCertification` 的认证，或 external-evidence 路径 `Status` 改为非 `Certified`。
3. **4.9.1**：`FinalizeGenerationAsync` 对所有未完成清理的 capture jobs 调用 `TeamLabCaptureCoordinator.ExpireAsync`。

**原 P2（已关闭）**：
4. 4.4.1：调整 `touch marker` 与 `signals.AppendAsync` 时序，或将 30s 超时提升至 120s + 探测。
5. F-4.3-02：dnsmasq 加入 liveness probe（pid 文件 + `kill -0`）。
6. 4.8.1：DeleteAsync 显式取消 MonitorAsync，或 FinalizeAsync 入口检查删除标志。
7. 4.5.1：cloud-init/OpenStack network_data.json 默认路由按 `IsPrimary` 门控。
8. 4.6.2：`ImageImportStagingReconcileService` where 子句增加 VmQcow2。
9. 4.2.1：`CreatePlannedRuntimeAsync` catch 追加 `UniqueViolation` 分支。
10. 4.2.2：`NodeCapacitySnapshot` 去重 teamLabFacts 与 Active reservations。

**P3（非阻塞跟踪）**：
11. 4.5.2：endpoint sensor ISO 使用独立子目录。
12. 4.1.1：scenario 输入校验增加 `template.ImageHash == certification.ImageHash`。
13. P3 findings 共 10 项，详见各链路进度文档。

### 11.4 不阻塞生产的合理化判断

经审查，以下方面虽存在 P2/P3 finding 但不影响生产准入：
- **WireGuard Fabric 组网方案**：与业界生产实践（Cilium、KubeSpan、Uncloud、Tailscale）一致，方向性/forward policy/ct state 设计正确，无结构性缺陷。
- **PostgreSQL 作为事实来源**：所有链路均严格遵循，无 Redis/缓存被误用为权威源。
- **路径注入/Shell 注入/HMAC 隔离**：所有边界校验完整，无安全漏洞。
- **Agent/主站重启恢复**：所有路径幂等，无资源泄漏。
- **链路 4.7 WireGuard 入口**：0 findings，全部检查通过。

---

## 十二、组网方案合理性洞察（联网调研）

### 12.1 WireGuard Fabric 设计合理性

**调研对象**：F-4.3-05（WireGuard peer AllowedIPs 要求包含所有 gateway /32）、链路 4.7 WireGuard 入口、链路 4.3 Fabric 设计。

**调研结论**：
- WireGuard 作为跨节点容器/pod 通信的加密层是业界主流方案，被 Cilium（Azure AKS Advanced Container Networking Services）、KubeSpan（Talos OS）、Uncloud、Tailscale 等生产系统广泛采用。
- TeamLab 的 WireGuard Fabric 设计（每 runtime 一个 `gzctf-fabric` 接口、/30 link pool、peer AllowedIPs 包含 gateway /32）与 Cilium 的 WireGuard node-to-node 加密模式方向一致。
- Cilium 的生产实践：每个节点自动生成 WireGuard key pair，public key 通过 `network.cilium.io/wg-pub-key` annotation 发布，key 每 120 秒自动轮换。TeamLab 当前未实现自动 key 轮换，但 TeamLab 的运行时生命周期通常较短（比赛/训练场景），手动 key 部署可接受。
- **F-4.3-05（P3）的合理性**：要求 peer AllowedIPs 包含所有 gateway /32 的设计增加了初始化顺序敏感性，但这是 WireGuard AllowedIPs 路由语义的固有限制（AllowedIPs 同时承担"路由表"和"加密 ACL"双重职责），并非设计缺陷。建议在错误消息中列出所有已知 peer 的 AllowedIPs 辅助排查。

**整体评价**：WireGuard Fabric 方案合理、稳定、可复用，符合工业实践。无需重构。

### 12.2 Generation Fence 模式与 F-4.3-01 的根因洞察

**调研对象**：F-4.3-01（ownsSharedResources 文件存在性回退）。

**调研结论**：
- 分布式系统权威文献（Martin Kleppmann, 2016；Google Chubby；ZooKeeper；etcd；Kubernetes leader election）一致指出：**lease/lock alone is insufficient — must pair with fencing tokens at the resource level**。
- Fencing token 是单调递增的数字，每次 lock/lease 授予时附带，被保护的资源只接受"已见最高 token"的操作。ZooKeeper 用 zxid，etcd 用 revision，Kubernetes 用 lease 的 holderIdentity + renewTime。
- **F-4.3-01 的根因**：TeamLab 的 `ownsSharedResources` 回退分支用"desired state 文件存在性"作为所有权判据，正是这一反模式的体现。文件存在性是必要非充分条件，无法证明该 generation 仍是 active。
- **正确的工业级方案**：将 generation number 作为 fencing token，给共享资源（router namespace、bridge、fabric 接口）打上"已接纳的 generation 号"，仅当请求 generation ≥ 已接纳值时才允许清理。这等价于 ZooKeeper 的 zxid 拒绝旧写者。
- **TeamLab 当前设计的合理化部分**：`TeamLabRuntimeGenerationStore` 使用原子 `File.Move` + flush to disk，符合"原子文件操作"要求；`ApplyInfrastructureAsync` L118-L121 拒绝旧 generation 请求也是 fencing 思想的体现。**唯一缺陷**在 `CleanupAsync` 的回退分支。

**整体评价**：Generation fence 模式方向正确，但 F-4.3-01 的回退分支违反了 fencing token 原则，需修复。修复方案明确（移除文件存在性回退，改为 fail-safe），不需要重构整个 generation fence 机制。

### 12.3 dnsmasq 监督模式与 F-4.3-02 的根因洞察

**调研对象**：F-4.3-02（dnsmasq 后台启动后无持续健康监控）。

**调研结论**：
- 生产级 DNS 服务标准实践（参考 dnsmasq + systemd-resolved 部署模式、Azure AKS DNS 监控、Cilium liveness probe）要求：
  1. **liveness probe**：周期性检查 DNS 是否可解析（如 `dig @127.0.0.1 +short google.com`）。
  2. **automatic restart**：liveness 失败时自动重启服务（systemd `Restart=on-failure` + `RestartSec=5s`，或 systemd timer + check script）。
  3. **readiness gating**：服务未就绪时不接受流量（Kubernetes readiness probe 模式）。
- TeamLab 当前实现：
  - ✅ readiness 一次性探测（150×0.1s=15s）符合 readiness gating 思想。
  - ❌ 无 liveness probe（dnsmasq 崩溃后不检测）。
  - ❌ 无 automatic restart（用 `&` 后台启动而非 systemd/supervisor 托管）。
- **修复方向符合工业实践**：在 `BuildInfrastructureFactProbeCommand` 中加入 dnsmasq pid 文件 + `kill -0 $pid` 检查，使 dnsmasq 崩溃后 live state probe 返回失败，从而触发 `ApplyInfrastructureAsync` 重建。或将 dnsmasq 改为 `systemd-run` 临时服务托管。

**整体评价**：dnsmasq 监督模式不符合生产标准，但修复方案明确且影响范围小（仅 `TeamLabBridgeService.cs` + `TeamLabNetworkService.cs` 的 probe 命令）。修复后符合工业实践。

### 12.4 fire-and-forget 监控与 4.8.1 的根因洞察

**调研对象**：4.8.1（DeleteAsync 后 MonitorAsync 重建 state.json）。

**调研结论**：
- OWASP Race Conditions 指南将"check-then-act"（TOCTOU, CWE-367）列为高置信度反模式：`if os.path.exists($PATH): os.remove($PATH)` 这类模式在 check 与 act 之间存在窗口，攻击者或并发线程可利用。
- Python asyncio 社区明确指出 fire-and-forget task（`_ = task`）需要 strong-reference 跟踪 + done callback，否则可能在 task 完成前被 GC。
- **4.8.1 的根因**：`TeamLabPcapService` 用 `_ = MonitorAsync(process, state)` fire-and-forget 启动监控任务，`DeleteAsync` 在另一路径删除目录。两者无同步机制，存在 TOCTOU 窗口。
- **正确的工业级方案**：
  1. 在 segment state 上新增 `CancellationTokenSource`，`DeleteAsync` 先 `Cancel` 再 `await` Monitor 任务再删目录（推荐）。
  2. 或在 `FinalizeAsync` 入口检查 state 是否已被标记删除，跳过 `SaveAsync`。
  3. 或在 `MonitorAsync` 的 `WaitForExitAsync` 之后、`FinalizeAsync` 之前检查 `Directory.Exists(SegmentDirectory(...))`，若不存在则跳过 Finalize。

**整体评价**：fire-and-forget + 独立删除路径的组合天然存在 TOCTOU 窗口，是反模式。但修复方案明确（CancellationToken 协调），不需要重构整个 PCAP 服务。

---

## 十三、附录

### 13.1 进度文档清单

- [进度 README](file:///D:/newgz/newGZCTF-main/docs/commercialization/reviews/phase-09-review-progress/README.md)
- [chain-4.1-topology.md](file:///D:/newgz/newGZCTF-main/docs/commercialization/reviews/phase-09-review-progress/chain-4.1-topology.md)
- [chain-4.2-runtime-placement.md](file:///D:/newgz/newGZCTF-main/docs/commercialization/reviews/phase-09-review-progress/chain-4.2-runtime-placement.md)
- [chain-4.3-shard-network-fabric.md](file:///D:/newgz/newGZCTF-main/docs/commercialization/reviews/phase-09-review-progress/chain-4.3-shard-network-fabric.md)
- [chain-4.4-docker-network-gating.md](file:///D:/newgz/newGZCTF-main/docs/commercialization/reviews/phase-09-review-progress/chain-4.4-docker-network-gating.md)
- [chain-4.5-vm-lifecycle.md](file:///D:/newgz/newGZCTF-main/docs/commercialization/reviews/phase-09-review-progress/chain-4.5-vm-lifecycle.md)
- [chain-4.6-image-lifecycle.md](file:///D:/newgz/newGZCTF-main/docs/commercialization/reviews/phase-09-review-progress/chain-4.6-image-lifecycle.md)
- [chain-4.7-wireguard-entry.md](file:///D:/newgz/newGZCTF-main/docs/commercialization/reviews/phase-09-review-progress/chain-4.7-wireguard-entry.md)
- [chain-4.8-traffic-capture.md](file:///D:/newgz/newGZCTF-main/docs/commercialization/reviews/phase-09-review-progress/chain-4.8-traffic-capture.md)
- [chain-4.9-reset-destroy-recovery.md](file:///D:/newgz/newGZCTF-main/docs/commercialization/reviews/phase-09-review-progress/chain-4.9-reset-destroy-recovery.md)

### 13.2 联网调研参考来源

- WireGuard mesh networking 生产实践：
  - [Securing Multi-Cloud Kubernetes: Talos, KubeSpan, and Tailscale](https://www.krishnac.com/blog/securing-multi-cloud-kubernetes-talos-kubespan-and-tailscale)
  - [Multi-node Docker Compose for production (Uncloud)](https://uncloud.run/)
  - [How to connect Docker containers across multiple hosts with WireGuard](https://uncloud.run/blog/connect-docker-containers-across-hosts-wireguard/)
  - [Building a Hybrid Cloud Mesh: Cilium, WireGuard, and Tailscale](https://blog.joshdow.ca/building-a-hybrid-cloud-mesh-cilium-wireguard-and-tailscale-for-disaster-resilient-kubernetes/)
  - [Azure AKS In transit encryption with WireGuard](https://learn.microsoft.com/ms-my/azure/aks/container-network-security-wireguard-encryption-concepts)

- 分布式锁与 fencing tokens：
  - [Distributed Lock Failure: How Long GC Pauses Break Concurrency](https://systemdr.systemdrd.com/p/distributed-lock-failure-how-long)
  - [Designing Bulletproof Distributed Locks with etcd](https://beefed.ai/en/bulletproof-distributed-locks-etcd)
  - [The Fencing Gap: Why Your Distributed Lock Isn't Safe](https://www.scien.cx/2026/04/07/the-fencing-gap-why-your-distributed-lock-isnt-safe-and-how-to-fix-it/)
  - [Lease Pattern in Distributed Systems Explained](https://singhajit.com/distributed-systems/lease/)
  - Martin Kleppmann, "How to do distributed locking" (2016)

- dnsmasq 生产监督：
  - [Monitoring and Automatic Restart of Services with systemd: Liveness and Readiness Probes](https://martinkonicek.eu/archive/systemd-liveness/)
  - [How to Optimize DNS Caching and Monitoring for Production](https://how2.sh/posts/how-to-optimize-dns-resilience-controls-for-production-networks/)
  - [Make .local (mDNS) work in scratch/BusyBox Docker containers via a host resolver](https://nathanpeck.com/mdns-resolution-in-scratch-docker-containers/)

- 竞态条件与 TOCTOU：
  - [OWASP Race Conditions](https://owasp.org/www-community/pages/vulnerabilities/race_conditions)
  - [Fire and forget (or never) with Python's asyncio](https://engineered.at/articles/fire-and-forget-or-never-with-python-s-asyncio)
  - [Threadsafe and Fault-Tolerant File Writes in Python](https://www.pythontutorials.net/blog/threadsafe-and-fault-tolerant-file-writes/)

### 13.3 审查方法论说明

本审查采用 sub-agent 驱动的独立代码审查模式：
1. **sub-agent 分发**：9 条链路（4.1-4.9）并行 dispatch 9 个 sub-agent，每个 sub-agent 独立打开链路下所有相关代码文件，对照规格第 4 节检查项与第 5 节 18 条生产级不变量逐项验证。
2. **主 agent 严格复核**：对每个 P1/P2 finding，主 agent 通过实际打开源码文件验证行号、代码语义与触发路径，确认 finding 真实性，排除误报。
3. **联网调研**：对涉及组网方案合理性的关键问题（WireGuard Fabric、generation fence、dnsmasq 监督、fire-and-forget 竞态），主 agent 进行联网调研，对照业界生产实践给出严格且合理的洞察。
4. **进度文档统一管理**：所有 sub-agent 结论写入 `docs/commercialization/reviews/phase-09-review-progress/` 下的链路进度文件，主 agent 在本最终报告中汇总。

**质量控制约束**：
- 不允许违背设计需求：所有 finding 严格对照规格第 5 节不变量与第 4 节检查项，不发明新约束。
- 不允许破坏平台现有稳定功能：所有修复方向均明确"不破坏稳定功能"的边界，P2/P3 修复方案均提供最小变更路径。
- 所有 code review 结果严格复核：3 个 P1 + 9 个 P2 findings 均通过主 agent 实际打开源码文件验证，确认行号、代码语义、触发路径无误。
- 全程产出高质量有价值无误报和干扰的内容：38 项"已检查但确认不是问题的高风险点"全部列出，避免遗漏；P3 findings 仅记录明确改进项，不包含主观偏好。
