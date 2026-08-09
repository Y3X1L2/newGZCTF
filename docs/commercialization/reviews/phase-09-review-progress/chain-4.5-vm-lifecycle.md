# Phase 9 TeamLab 组网独立代码审查 — 链路 4.5 Linux/Windows VM 创建

- 审查链路：4.5 Linux/Windows VM 创建（VM 生命周期：Create / Destroy / Recover / Replace）
- 审查类型：独立代码审查（sub-agent）
- 审查日期：2026-07-21
- 审查依据：`docs/commercialization/phase-09-teamlab-networking-independent-code-review.md` 第 3.6 节、第 4.5 节、第 5 节（18 条生产级不变量）、第 7 节（适配性反模式）、第 12 节（findings 输出规范）

## 1. 审查范围与覆盖

### 1.1 已实际读取（Read 工具）的代码文件

Agent 端（`src/GZCTF.Agent/`）：

| 文件 | 关键关注点 |
| --- | --- |
| `Services/KvmService.cs` | VM 创建/销毁/恢复状态机、cloud-init network-config v2 生成、endpoint sensor ISO、管理口隔离、domain identity 恢复 |
| `Services/Vm/VmDomainBuilder.cs` | virt-install 参数、stable UUID（SHA-256 派生）、generation metadata 写入 |
| `Services/Vm/VmRuntimeReadinessCoordinator.cs` | BackgroundService、DomainRunning 先于 GuestReady、QGA 轮询窗口 |
| `Services/Vm/VmBootstrapService.cs` | QGA bootstrap 应用、Windows 网络脚本（含 isPrimary 门控）、能力探测 |
| `Services/Vm/VmGuestAgentService.cs` | QGA 客户端、`virsh qemu-agent-command --timeout 30`、reboot 重连 |
| `Services/Vm/AgentOperationReceiptStore.cs` | 幂等操作 receipt（canonical JSON SHA-256 + 文件锁 + 原子写入） |
| `Services/Vm/AgentOciArtifactUploader.cs` | OCI 制品上传、registry/repo/tag 严格校验、annotation 归属校验 |
| `Services/Vm/VmScenarioArtifactService.cs` | scenario 制品提交幂等、sanitize 分支、checkpoint |
| `Services/GuestControl/GuestCertificateAuthority.cs` | RSA 3072 CA + RSA 2048 server cert、ECDSA P-256 CSR、0600 权限 |
| `Services/GuestControl/GuestConfigDriveBuilder.cs` | config drive 构建、OpenStack network_data.json、ValidateInterfaces |
| `Services/GuestControl/GuestEnrollmentStore.cs` | AES-GCM 加密、/16 池按 identity hash 分配、撤销 |
| `Services/GuestControl/GuestEventIngestor.cs` | guestSequence 去重、runtime signal append |
| `Services/GuestControl/GuestManagementNetworkService.cs` | nftables 管理桥隔离 |
| `Controllers/VmController.cs` | Managed/Opaque readiness 分流（GuestSupervisor 是否为 null） |
| `Models/VmModels.cs` | CreateVmRequest 完整契约（含 OperationId/RuntimeId/Generation/Interfaces/GuestSupervisor） |

主站端（`src/GZCTF/`）：

| 文件 | 关键关注点 |
| --- | --- |
| `Services/Fleet/FleetVmService.cs` | 遗留非 TeamLab 路径（不传 OperationId/RuntimeId/Interfaces/GuestSupervisor） |
| `Services/Fleet/VmReadyService.cs` | 遗留非 TeamLab 路径（10 分钟超时销毁） |
| `Services/Fleet/VmArtifactStore.cs` | SHA-256 校验模板文件 |
| `Modules/TeamLab/Application/TeamLabBootstrapOrchestrator.cs` | generation+assetId+profileId+version+stepKey 幂等记录 |
| `Modules/TeamLab/Application/TeamLabScenarioBakeService.cs` | ValidateSources 强制 Managed+Ready+认证输入、能力复制到 Scenario 模板 |
| `Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs` | TeamLab VM 创建主路径、IsCurrentManagedCertification 门控、EndpointSensorChannel 硬编码 false |
| `Modules/Content/Application/BootstrapProfileCompatibilityService.cs` | Opaque 直接拒绝、Managed 严格校验 digest/能力/协议版本 |
| `Modules/Content/Domain/VmPreparedArtifact.cs` | prepared artifact OCI 引用实体 |
| `Modules/Content/Domain/ImageTemplateCapabilityCertification.cs` | 认证实体、能力 ID 常量、ProbeKind 默认 external-evidence |

域模型：

| 文件 | 关键关注点 |
| --- | --- |
| `Models/Data/ImageTemplate.cs` | VmRuntimeMode 默认 Opaque、VmNetworkMode 枚举 |
| `Models/Data/VmInstance.cs` | RuntimeGeneration/RuntimeNativeId/ConcurrencyToken |

### 1.2 已验证的生产级不变量（第 5 节）

- I1 外部 CI/Image Factory 产出 qcow2 → 流式 SHA-256 → 内部 OCI Registry 不可变制品
- I2 Opaque 模板不被强制 Guest Supervisor / EndpointObservation
- I3 Managed 模板绑定 digest + 能力 + 协议版本
- I4 Scenario baking 输入必须是 Managed+Ready+认证
- I5 外部证据不能提升 Opaque
- I6 主网卡 MAC 匹配不依赖枚举顺序
- I7 无发行版硬编码
- I8 overlay/config drive/domain XML/UUID 持久化先于返回
- I9 domain identity 三层恢复（stable UUID + sidecar generation + virsh desc metadata）
- I10 Managed/Opaque readiness 不互相伪造
- I11 receipt/operation identity 防御
- I12 ShellEscape / ResolveTemplatePath 防注入
- I13 AES-GCM 加密 enrollment state
- I14 nftables 管理桥隔离
- I15 QGA 通信 `virsh qemu-agent-command --timeout 30`
- I16 RSA 3072 CA + ECDSA P-256 CSR + identityDigest 绑定
- I17 Scenario baking sanitize → checkpoint → shutdown 顺序
- I18 Agent 重启后 RDP 代理恢复

## 2. Findings 汇总

共 2 个 finding，均为 P2。

| ID | 等级 | 类别 | 位置 | 标题 |
| --- | --- | --- | --- | --- |
| 4.5.1 | P2 | 多网卡/默认路由 | `KvmService.cs` L773-778；`GuestConfigDriveBuilder.cs` L130-172、L191-206 | Linux cloud-init 与 OpenStack network_data.json 默认路由未按 IsPrimary 门控 |
| 4.5.2 | P2 | 资源销毁 | `KvmService.cs` L870-896 | endpoint sensor ISO 创建销毁 config drive 目录（潜伏 bug） |

---

### Finding 4.5.1 — Linux cloud-init 与 OpenStack network_data.json 默认路由未按 IsPrimary 门控

- **等级**：P2
- **类别**：多网卡 / 默认路由一致性
- **位置**：
  - `src/GZCTF.Agent/Services/KvmService.cs` L773-778（Linux NoCloud cloud-init network-config v2）
  - `src/GZCTF.Agent/Services/GuestControl/GuestConfigDriveBuilder.cs` L130-172（OpenStack network_data.json，关键行 L153-154）
  - `src/GZCTF.Agent/Services/GuestControl/GuestConfigDriveBuilder.cs` L191-206（ValidateInterfaces 未约束非管理接口的 gateway 数量）
- **现象**：

  `KvmService.cs` L773-778 生成 Linux cloud-init network-config v2 时，对每个非 DHCP 接口只要 `iface.Gateway` 非空就写入 `gateway4`，未检查 `iface.IsPrimary`：

  ```csharp
  // KvmService.cs L773-778
  if (!string.IsNullOrWhiteSpace(iface.Gateway))
  {
      if (!IsValidIpv4(iface.Gateway))
          throw new ArgumentException("Invalid VM gateway address.", nameof(iface.Gateway));
      builder.AppendLine($"    gateway4: {iface.Gateway}");
  }
  ```

  `GuestConfigDriveBuilder.cs` L153-154 生成 OpenStack network_data.json（Windows Cloudbase-init 消费）时，同样只要 `item.Gateway` 非空就追加 `0.0.0.0/0` 默认路由，未检查 `IsPrimary`：

  ```csharp
  // GuestConfigDriveBuilder.cs L153-154
  if (!string.IsNullOrWhiteSpace(item.Gateway))
      routes.Add(new { network = "0.0.0.0", netmask = "0.0.0.0", gateway = item.Gateway });
  ```

  `GuestConfigDriveBuilder.cs` L191-206 的 `ValidateInterfaces` 仅校验管理接口（`item.IsManagement`）无 gateway 且无 routes，未校验非管理接口中是否只有一个 `IsPrimary` 接口携带 gateway，也未将 `IsPrimary` 传入 `OpenStackInterface`（L132-140 构造时丢弃了 `IsPrimary` 字段）。

- **对比**：

  Windows 通过 QGA bootstrap 应用网络的路径 `VmBootstrapService.cs` L491-493 正确实现了 isPrimary 门控：

  ```powershell
  # VmBootstrapService.cs L491-493
  $args = @{ InterfaceIndex=$adapter.ifIndex; IPAddress=$item.ipAddress; PrefixLength=[int]$item.prefixLength }
  if ($item.isPrimary -and $item.gateway) { $args.DefaultGateway = $item.gateway }
  New-NetIPAddress @args | Out-Null
  ```

  Windows 路径仅当 `isPrimary -and gateway` 同时为真时设置 `DefaultGateway`，与 cloud-init/OpenStack 路径行为不一致。

- **影响**：

  当 VM 配置多块非管理网卡且多块都携带 `Gateway` 时（`VmNetworkInterfaceRequest.IsPrimary` 字段存在但未被消费），cloud-init 与 Cloudbase-init 会生成多条 `0.0.0.0/0` 默认路由，导致：
  1. 默认路由抖动 / 非确定性 next-hop，出网路径不可预测；
  2. 主备网关竞争，可能在管理桥或非预期桥上发出默认流量，绕过 `GuestManagementNetworkService` 的 nftables forward drop 隔离语义；
  3. 与 Windows QGA 路径行为分叉，多网卡场景下同一拓扑在不同 OS 模板下表现不一致。

  `VmNetworkInterfaceRequest.IsPrimary`（`Models/VmModels.cs` L83）作为契约字段已存在，但 Linux/OpenStack 网络生成路径未消费它，属于"契约字段存在但实现遗漏"。

- **触发条件**：拓扑中 VM 配置 ≥2 块非管理网卡，且 ≥2 块携带 `Gateway`。当前 TeamLab 主路径 `AgentTeamLabNodeExecutor.BuildVmInterfaces` 通常只给主网卡设 gateway，但 cloud-init/OpenStack 生成路径位于 Agent 侧公共代码，任何调用方（含未来扩展）传入多 gateway 即触发。

- **修复建议**（不破坏稳定功能）：

  1. `KvmService.cs` L773-778：将 `if (!string.IsNullOrWhiteSpace(iface.Gateway))` 收紧为 `if (iface.IsPrimary && !string.IsNullOrWhiteSpace(iface.Gateway))`，仅主网卡写 `gateway4`。
  2. `GuestConfigDriveBuilder.cs` L132-140：`OpenStackInterface` 构造时保留 `IsPrimary` 字段（或复用 `IsManagement` 之外的标志）。
  3. `GuestConfigDriveBuilder.cs` L153-154：仅当 `item.IsPrimary` 时追加 `0.0.0.0/0` 默认路由；非主接口的 gateway 字段对非默认路由场景应通过显式 `Routes` 表达。
  4. `GuestConfigDriveBuilder.cs` L191-206：`ValidateInterfaces` 增加"非管理接口中至多一个携带 gateway，且必须 IsPrimary"的校验，与 Windows 路径行为对齐。

- **风险等级依据**：当前 TeamLab 主路径不会主动构造多 gateway 拓扑，但 Agent 侧公共网络生成代码缺乏防御，属于"潜伏一致性缺陷 + 契约字段未消费"，定为 P2。

---

### Finding 4.5.2 — endpoint sensor ISO 创建销毁 config drive 目录

- **等级**：P2
- **类别**：资源销毁 / 潜伏 bug
- **位置**：`src/GZCTF.Agent/Services/KvmService.cs` L870-896（关键行 L885-888）
- **现象**：

  `CreateVmAsync` 在 L154-164 先构建 GuestSupervisor config drive，ISO 输出路径为 `GetRuntimeInjectionDirectory(request.VmName)/guest-config/config-drive.iso`（L156 构造 guest-config 目录，L157 创建 ISO）：

  ```csharp
  // KvmService.cs L154-164
  var configDrive = GuestConfigDriveBuilder.Build(
      request,
      Path.Combine(GetRuntimeInjectionDirectory(request.VmName), "guest-config"));
  await CreateIsoAsync(
      configDrive.IsoPath,
      configDrive.VolumeLabel,
      configDrive.Files,
      token);
  mediaArguments.Add(
      $"--disk path={ShellEscape(configDrive.IsoPath)},device=cdrom,readonly=on");
  ```

  随后 L165-166 在 `EndpointSensorChannel` 为 true 时调用 `CreateEndpointSensorInjectionIsoAsync`，该函数 L885-888 对 `GetRuntimeInjectionDirectory(request.VmName)` 整个根目录执行 `Directory.Delete(root, recursive: true)`：

  ```csharp
  // KvmService.cs L885-888
  var root = GetRuntimeInjectionDirectory(request.VmName);
  if (Directory.Exists(root))
      Directory.Delete(root, recursive: true);
  Directory.CreateDirectory(root);
  ```

  `GetRuntimeInjectionDirectory` 的定义（L954-955）：

  ```csharp
  // KvmService.cs L954-955
  private string GetRuntimeInjectionDirectory(string vmName) =>
      Path.Combine(_config.ImageStoragePath, "runtime-injection", vmName);
  ```

  即：config drive 的 ISO 与 endpoint sensor ISO 共享同一个父目录 `runtime-injection/{vmName}/`。`CreateEndpointSensorInjectionIsoAsync` 为了清理自身历史残留，递归删除了整个 `runtime-injection/{vmName}/`，连带销毁了刚生成的 `guest-config/config-drive.iso` 与 `guest-config/` 下所有 config drive 源文件。

  结果：`mediaArguments` 中 L162-163 已 append 的 `config-drive.iso` 路径在 L170 `virt-install` 执行时指向不存在的文件，virt-install 启动失败或以无 config drive 方式启动，GuestSupervisor 入网认证材料丢失。

- **当前是否触发**：

  TeamLab 生产路径 `AgentTeamLabNodeExecutor.cs` L700 硬编码 `EndpointSensorChannel = false`：

  ```csharp
  // AgentTeamLabNodeExecutor.cs L696-702
  GuestControl = new AgentVmGuestControlConfig
  {
      Enabled = requiresGuestControl,
      Required = requiresGuestControl,
      EndpointSensorChannel = false,
      OsType = template.OSType
  },
  ```

  全仓搜索 `EndpointSensorChannel = true` 仅出现在单元测试 `src/GZCTF.Test/UnitTests/Vm/VmGuestControlTests.cs` L81，生产路径未触发。**定为潜伏 bug**：一旦未来启用 endpoint sensor 注入通道，config drive 会被销毁。

- **修复建议**（不破坏稳定功能）：

  将 endpoint sensor ISO 的输出目录与 config drive 目录隔离，例如使用 `runtime-injection/{vmName}/endpoint-sensor/` 子目录，仅清理该子目录而非整个 `{vmName}` 根；或在 `CreateEndpointSensorInjectionIsoAsync` 中只删除 `endpoint-sensor.iso` 自身而不递归删除父目录。

- **风险等级依据**：当前生产路径未触发，但属于"资源销毁范围错误 + 一旦启用即数据丢失"，定为 P2。

## 3. 能力门控完整性验证

| 检查项 | 结论 | 证据 |
| --- | --- | --- |
| Managed 模板必须绑定 digest + 能力 + 协议版本 | ✅ 通过 | `BootstrapProfileCompatibilityService.IsCurrentManagedCertification` L111-129：Opaque 直接 false；Managed 必须 `PreparedArtifactId` 非 null + `ImageHash` 匹配 + `Status=Certified` + `PreparationContractVersion` 匹配 + `GuestProtocolVersion` 匹配 + 能力含 `GuestSupervisor`+`VmPreparedImage` |
| Opaque 模板不被强制 Guest Supervisor / EndpointObservation | ✅ 通过 | `BootstrapProfileCompatibilityService.ValidateReleaseAsync` L45-47：`if (template.VmRuntimeMode == VmRuntimeMode.Opaque) throw Conflict(...)` |
| Opaque 模板不被 QGA 轮询伪造 readiness | ✅ 通过 | `VmController.cs` L34：`if (result is not null && request.GuestSupervisor is null) await readiness.TrackAsync(...)` —— Managed VM（有 GuestSupervisor）不进 QGA 轮询；`VmRuntimeReadinessCoordinator.TrackAsync` L26-60 先 emit `DomainRunning` 再调度 QGA，Opaque VM 的 `DomainRunning` 信号先于任何 QGA 探测发出 |
| Scenario baking 输入必须是 Managed+Ready+认证 | ✅ 通过 | `TeamLabScenarioBakeService.ValidateSources` L440-458：强制 `VmRuntimeMode.Managed` + `VmArtifactStatus.Ready` + 当前认证；`CommitArtifactAsync` L298-380 从 sourceCertification 复制能力到新 Scenario 模板的 certification，`ImageHash` 绑定 `artifact.ArtifactDigest` |
| 外部证据不能提升 Opaque | ✅ 通过 | `IsCurrentManagedCertification` L115：`if (template.VmRuntimeMode == VmRuntimeMode.Opaque ... ) return false;` —— 任何外部认证记录对 Opaque 模板直接返回 false |
| TeamLab VM 创建主路径门控 | ✅ 通过 | `AgentTeamLabNodeExecutor.CreateVmAsync` L603-608：`requiresGuestControl` 为真时调用 `IsCurrentManagedCertification` 严格校验，Opaque/未 Ready/无认证均返回 Failed |
| Scenario 模板用 ImageHash，Managed 用 PreparedArtifact.ArtifactDigest | ✅ 通过 | `AgentTeamLabNodeExecutor.cs` L638-640：`template.VmRuntimeMode == VmRuntimeMode.Scenario ? template.ImageHash! : template.PreparedArtifact!.ArtifactDigest` |

**能力门控完整性结论**：5 项核心检查全部通过，Opaque 提升路径在 `IsCurrentManagedCertification`、`ValidateReleaseAsync`、`ValidateSources` 三处独立锁死，无绕过路径。

## 4. 多网卡与默认路由验证

| 检查项 | 结论 | 证据 |
| --- | --- | --- |
| 主网卡 MAC 匹配不依赖枚举顺序（Linux） | ✅ 通过 | `KvmService.cs` L755-757：cloud-init network-config v2 使用 `match.macaddress` + `set-name`，由 guest 按 MAC 匹配接口，不依赖 PCI 枚举顺序 |
| 主网卡 MAC 匹配不依赖枚举顺序（Windows） | ✅ 通过 | `VmBootstrapService.cs` L486：`Get-NetAdapter \| Where-Object { $_.MacAddress -eq $mac } \| Select-Object -First 1`，按 MAC 匹配 |
| 管理接口无 gateway | ✅ 通过 | `GuestConfigDriveBuilder.cs` L202：`item.IsManagement && (!string.IsNullOrWhiteSpace(item.Gateway) \|\| item.Routes.Count > 0)` 抛异常；`KvmService.cs` BuildCloudInitNetworkConfig 对管理接口同样不写 gateway |
| 默认路由按 IsPrimary 门控（Windows QGA 路径） | ✅ 通过 | `VmBootstrapService.cs` L491-493：`if ($item.isPrimary -and $item.gateway) { $args.DefaultGateway = $item.gateway }` |
| 默认路由按 IsPrimary 门控（Linux cloud-init 路径） | ❌ 失败 | 见 Finding 4.5.1 — `KvmService.cs` L773-778 未检查 `iface.IsPrimary` |
| 默认路由按 IsPrimary 门控（OpenStack network_data 路径） | ❌ 失败 | 见 Finding 4.5.1 — `GuestConfigDriveBuilder.cs` L153-154 未检查 `IsPrimary`；`ValidateInterfaces` L191-206 未约束非管理接口 gateway 数量 |

**多网卡与默认路由结论**：MAC 匹配三路径（Linux cloud-init / Windows QGA / OpenStack）均正确，不依赖枚举顺序；但默认路由的 IsPrimary 门控仅 Windows QGA 路径正确，Linux cloud-init 与 OpenStack 路径存在 Finding 4.5.1。

## 5. 适配性反模式检查（第 7 节）

| 反模式 | 结论 | 证据 |
| --- | --- | --- |
| 发行版硬编码（Ubuntu/Server 等具体版本判断） | ✅ 无问题 | 全部 OS 分支基于 `VmInitOsType` 枚举（`Linux=0`/`Windows=1`，见 `VmModels.cs` L5-9），无 Ubuntu/CentOS/Server 2022 等具体发行版字符串判断 |
| 镜像名 / 模板 ID / 节点名 特殊分支 | ✅ 无问题 | `KvmService` / `AgentTeamLabNodeExecutor` / `BootstrapProfileCompatibilityService` 均按 `VmRuntimeMode` / `VmArtifactStatus` / 能力 ID 分支，无 `if (template.Name == "...")` 或 `if (vmName.Contains("xxx"))` 模式 |
| 单模板特殊分支 | ✅ 无问题 | 无 `if (templateId == 42)` 类硬编码；所有模板走统一认证 + 能力过滤路径 |
| 路径穿越 | ✅ 无问题 | `AgentOciArtifactUploader` 的 `NormalizeRegistry`/`NormalizeRepository`/`NormalizeTag` 严格校验防路径注入；`KvmService.ShellEscape` 对所有嵌入 shell 的路径转义；`ResolveTemplatePath` 校验模板路径不越界 |
| Shell 注入 | ✅ 无问题 | `KvmService` 所有 `virsh`/`qemu-img`/`virt-install` 命令的动态参数均经 `ShellEscape`；`VmDomainBuilder.BuildVirtInstallArguments` 构造的参数同样经转义 |
| 镜像名/标签注入 OCI registry | ✅ 无问题 | `AgentOciArtifactUploader.TryResolveAsync` L97-150 通过 annotation 验证 registry 目标归属，防止跨 operation 污染 |

**适配性反模式结论**：无发行版硬编码、无镜像名/模板 ID/节点名特殊分支、无单模板特殊分支，路径穿越与 Shell 注入防御到位。

## 6. 持久化与恢复验证

| 检查项 | 结论 | 证据 |
| --- | --- | --- |
| overlay/config drive/domain XML/UUID 持久化先于返回 | ✅ 通过 | `KvmService.CreateVmAsync` L170 `virt-install` 成功 → L175 写 generation sidecar 文件 → L176-177 读 `virsh domuuid` → L180 读 `virsh desc` 中的 `gzctf-generation=` → L181 读 sidecar → L182-188 `GetIdentityConflict` 校验 nativeId+generation 一致 → L191 配置管理口隔离 → L193 返回。任何 identity 不一致抛 `runtime_identity_conflict` |
| domain identity 三层恢复 | ✅ 通过 | (1) stable UUID：`VmDomainBuilder.BuildStableDomainId` L53-61 由 SHA-256 派生，`virt-install` 首次创建写入；(2) sidecar generation 文件：`KvmService` L114-116 写、L181 读；(3) `virsh desc` metadata `gzctf-generation={generation}`：`ParseDomainGeneration` L312-322 解析。Agent/主站重启后 `EvaluateCreateDisposition` L327-358 + `GetIdentityConflict` L360-386 用三层事实恢复 disposition |
| 幂等创建（Create/Reuse/Replace/Conflict 状态机） | ✅ 通过 | `EvaluateCreateDisposition` L327-358 根据 nativeId 是否存在、generation 是否匹配、sidecar 是否一致决定 Create/Reuse/Replace/Conflict；`CreateVmAsync` L48-214 完整实现四态 |
| receipt 幂等 | ✅ 通过 | `AgentOperationReceiptStore.ExecuteAsync` L16-53：canonical JSON SHA-256 + 文件锁 + 原子写入，相同 `operationId+requestHash` 返回缓存结果；`VmScenarioArtifactService.CommitAsync` L20-31、`TeamLabBootstrapOrchestrator.RecordSuccess/RecordFailure` L9-74 均通过 receipt store 实现幂等 |
| Scenario baking sanitize → checkpoint → shutdown 顺序 | ✅ 通过 | `VmScenarioArtifactService` L66 sanitize 写 checkpoint 再 shutdown，支持恢复；`SanitizeAsync` L134-159 按 OsType 分支（Windows PowerShell / Linux systemctl）清理 GuestSupervisor 状态 |
| Agent 重启后 RDP 代理恢复 | ✅ 通过 | `KvmService.RestoreRdpProxiesAsync` L454-469 在 Agent 启动时恢复 RDP 代理 |
| enrollment state 加密持久化 | ✅ 通过 | `GuestEnrollmentStore` AES-GCM 加密 intent/token/secrets；`AllocateLeaseAsync` L271-304 在 /16 池中按 identity hash 分配管理 IP+MAC；`RevokeVmAsync` L247-269 按 vmName+generation+nativeVmId 撤销 |
| 管理桥 nftables 隔离 | ✅ 通过 | `GuestManagementNetworkService` input 链只放行 established/DHCP/listenPort，forward 链 drop；`KvmService.BuildManagementPortIsolationCommand` L661-669 通过 `virsh domiflist` + awk 匹配 bridge 名设置 `isolated on` |

**持久化与恢复结论**：8 项全部通过。domain identity 三层恢复（stable UUID + sidecar + virsh desc metadata）设计完备，Agent/主站重启后能正确恢复 disposition 状态；持久化严格先于返回，identity 冲突会抛异常回滚。

## 7. 已检查但确认不是问题的高风险点

> 以下检查项在初筛阶段被标记为高风险，经实际读取代码后确认**不是问题**，列出以避免重复审查。

| 高风险点 | 确认结论 | 证据 |
| --- | --- | --- |
| `AgentOperationReceiptStore` 幂等是否真的原子 | ✅ 不是问题 | canonical JSON SHA-256 + 文件锁 + 原子写入（先写临时文件再 rename），相同 `operationId+requestHash` 返回缓存结果，跨进程安全 |
| `GuestEnrollmentStore` AES-GCM 加密是否正确 | ✅ 不是问题 | intent/token/secrets 均 AES-GCM 加密后持久化，nonce 随机生成，密钥由 KDF 派生；`RevokeVmAsync` 按 vmName+generation+nativeVmId 三元组撤销，防止误撤销 |
| `KvmService.ShellEscape` 是否覆盖所有动态参数 | ✅ 不是问题 | 所有 `virsh`/`qemu-img`/`virt-install` 命令的动态参数（vmName/templatePath/vmPath/isoPath/bridgeName）均经 `ShellEscape`；`VmDomainBuilder.BuildVirtInstallArguments` 构造的参数同样经转义 |
| `ResolveTemplatePath` 是否防路径穿越 | ✅ 不是问题 | 模板路径校验不越界，`AgentOciArtifactUploader` 的 `NormalizeRegistry`/`NormalizeRepository`/`NormalizeTag` 严格校验防路径注入 |
| `GuestConfigDriveBuilder.ValidateInterfaces` management 接口 gateway 检查 | ✅ 不是问题 | L202 `item.IsManagement && (!string.IsNullOrWhiteSpace(item.Gateway) \|\| item.Routes.Count > 0)` 抛异常，管理接口严格无 gateway 无 routes（但注意：非管理接口的 gateway 数量未校验，属于 Finding 4.5.1 的一部分） |
| `VmRuntimeReadinessCoordinator` 对 Opaque VM 是否无限重试 | ✅ 不是问题 | `AdvanceAsync` L104-172 对无 QGA 的 Opaque VM 会重试，但 `TrackAsync` L26-60 先 emit `DomainRunning` 信号，Opaque VM 的 readiness 由 `DomainRunning` 推进而非 `GuestReady`；`ProbeWindow=8s` + `RetryDelay=2s` + `warningAfterSeconds` 在 30-3600s 范围内，有上限告警 |
| `VmController` Managed/Opaque readiness 分流是否正确 | ✅ 不是问题 | L34 `if (result is not null && request.GuestSupervisor is null)` 仅 Opaque VM（无 GuestSupervisor）进 QGA 轮询；Managed VM（有 GuestSupervisor）由 GuestSupervisor 事件推进 readiness |
| `FleetVmService`/`VmReadyService` 遗留非 TeamLab 路径是否干扰 | ✅ 不是问题 | `FleetVmService.CreateVmAsync` L48-136 不传 `OperationId`/`RuntimeId`/`Interfaces`/`GuestSupervisor`，仅传 `TemplateId`/`VmName`/`Memory`/`Cpu`，走简化路径；`VmReadyService` 为 BackgroundService 轮询 `VmInstance` 表，10 分钟超时自动销毁，与 TeamLab 路径（由 `AgentTeamLabNodeExecutor` + `VmRuntimeReadinessCoordinator` 推进）隔离 |
| `GuestCertificateAuthority` 证书签发是否绑定 identity | ✅ 不是问题 | `IssueClientCertificate` L40-85 强制 ECDSA P-256 CSR，绑定 `identityDigest`；CA RSA 3072 + server cert RSA 2048，PEM/PFX 原子写入，`RestrictFile` 设置 0600 |
| `VmGuestAgentService` QGA 超时是否合理 | ✅ 不是问题 | `virsh qemu-agent-command --timeout 30`，30 秒超时；`WaitReadyAsync` L16-47 轮询 `guest-ping` + `guest-info`；`RebootAndWaitAsync` L191-221 发送 `guest-shutdown reboot` 后等待 QGA 断连重连 |
| `TeamLabScenarioBakeService.CommitArtifactAsync` 能力复制是否正确 | ✅ 不是问题 | L298-380 从 sourceCertification 复制能力到新 Scenario 模板的 certification，`ImageHash` 绑定 `artifact.ArtifactDigest`，防止 digest 漂移 |
| `GuestEventIngestor` 序列号去重 | ✅ 不是问题 | `JournalOnceAsync` L22-58 按 `guestSequence` 去重后 append runtime signal，防止重复入网事件 |
| `AgentTeamLabNodeExecutor` EndpointSensorChannel 硬编码 false | ✅ 不是问题（当前） | L700 硬编码 `EndpointSensorChannel = false`，当前生产路径不触发 endpoint sensor 注入；但该硬编码同时使 Finding 4.5.2 成为潜伏 bug，未来启用时需同步修复 |

## 8. 链路覆盖结论

### 8.1 正确链路覆盖

外部 CI/Image Factory 产出 qcow2 → API 导入 → 流式 SHA-256 校验（`VmArtifactStore.ValidateAndBuildDownloadAsync` L18-50）→ 内部 OCI Registry 不可变制品（`AgentOciArtifactUploader`）→ Opaque 模板（`ImageTemplate.VmRuntimeMode` 默认 Opaque）→ 平台受控认证（`ImageTemplateCapabilityCertification`，`ProbeKind` 默认 `external-evidence`）→ Managed 模板（`IsCurrentManagedCertification` 严格门控）→ 能力过滤后的节点分发（`BootstrapProfileCompatibilityService.ValidateReleaseAsync`）→ runtime overlay + config drive（`GuestConfigDriveBuilder.Build`）+ domain start（`KvmService.CreateVmAsync` → `VmDomainBuilder.BuildVirtInstallArguments` → `virt-install`）

**链路完整覆盖，无断点。**

### 8.2 三种运行模式覆盖

| 模式 | 创建路径 | readiness 推进 | 销毁 |
| --- | --- | --- | --- |
| Managed | `AgentTeamLabNodeExecutor.CreateVmAsync` L603-608 门控 → `KvmService.CreateVmAsync` L154-164 config drive → `virt-install` | `VmRuntimeReadinessCoordinator` 由 GuestSupervisor 事件推进（不进 QGA 轮询） | `KvmService.DestroyVmAsync` L216-264 |
| Opaque | `BootstrapProfileCompatibilityService.ValidateReleaseAsync` L45-47 拒绝 bootstrap/EndpointObservation → 仅基础 VM 创建 | `VmController` L34 `GuestSupervisor is null` → QGA 轮询，`DomainRunning` 先于 `GuestReady` | 同上 |
| Scenario | `TeamLabScenarioBakeService.ValidateSources` L440-458 强制 Managed+认证输入 → `CommitArtifactAsync` 复制能力 → `AgentTeamLabNodeExecutor` L638-640 用 `template.ImageHash` | 同 Managed | 同上 |

**三种模式覆盖完整，门控互不绕过。**

### 8.3 Findings 汇总

- 共 2 个 P2 finding，无 P0/P1。
- Finding 4.5.1：Linux cloud-init 与 OpenStack network_data.json 默认路由未按 IsPrimary 门控（多网卡一致性缺陷，契约字段未消费）。
- Finding 4.5.2：endpoint sensor ISO 创建销毁 config drive 目录（潜伏 bug，当前 `EndpointSensorChannel = false` 未触发）。

### 8.4 总体结论

链路 4.5 Linux/Windows VM 创建的核心不变量（能力门控、identity 恢复、持久化先于返回、MAC 匹配、适配性反模式）全部通过，无破坏稳定功能的缺陷。2 个 P2 finding 均为"潜伏一致性缺陷"，当前生产路径未触发，但应在启用多 gateway 拓扑或 endpoint sensor 注入通道前修复。建议按 Finding 4.5.1 修复建议将 Linux/OpenStack 网络生成路径与 Windows QGA 路径的 IsPrimary 门控对齐，按 Finding 4.5.2 修复建议将 endpoint sensor ISO 输出目录与 config drive 目录隔离。
