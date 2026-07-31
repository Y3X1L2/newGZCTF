# 链路 4.4 Docker 创建与网络门控 审查结果

## 审查范围与覆盖

本审查覆盖 Phase 9 TeamLab 组网独立审查规格中【链路 4.4 Docker 创建和网络门控】的全部检查项，焦点为设计要求 §3.5（Docker 资产在网络事实就绪前必须处于门控等待状态，不能因镜像默认命令绕过门控）与不变量 #3/#4/#5/#13。

**实际打开并阅读的关键代码文件**：

| 文件 | 关注点 |
| --- | --- |
| `src/GZCTF.Agent/Services/DockerService.cs` | `CreateContainerAsync`、`BuildGatedCommand`、`BuildStartCommand`、镜像就绪检查、幂等容器查找 |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabContainerNetworkFinalizeService.cs` | `FinalizeAsync`、`BuildFinalizeCommand`、信号追加时序、一次性事实校验 |
| `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs` | `CreateContainerAsync`（主站编排：创建→挂接→finalize→等待信号→启动 sensor）、失败补偿 |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs` | `AgentOperationId` 稳定赋值、`RestoreCompletedNodes` 接入 |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabDependencyGraph.cs` | `RestoreCompletedNodes` 跳过已完成阶段 |
| `src/GZCTF/Services/Fleet/ImageDistributionService.cs` | 镜像预分发与重复拉取短路 |
| `src/GZCTF.Agent/Models/TeamLabModels.cs` | `TeamLabContainerNetworkFinalizeRequest/Response` 契约 |
| `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabFleetAdapters.cs` | `EnsureImageAsync` 包装 |
| `src/GZCTF.Agent/Services/RuntimeSignals/AgentRuntimeSignalJournal.cs` | 持久化信号日志（`FileOptions.WriteThrough`） |
| `src/GZCTF.Agent/Services/RuntimeSignals/AgentRuntimeSignalPublisher.cs` | 2s 轮询发布、启动时调度所有已知 operation |

**检查项覆盖**：

- ✅ 镜像预分发、无重复拉取（`ImageDistributionService.cs#L396-L404`）
- ✅ 默认 Entrypoint/Cmd 与显式 StartCommand 均被门控（`DockerService.cs#L132-L149`、`#L535-L554`）
- ✅ 网络 finalize 一次性事实校验：接口/地址/路由/DNS/真实解析（`TeamLabContainerNetworkFinalizeService.cs#L95-L151`）
- ✅ Agent 重启无重复业务命令启动（`DockerService.cs#L151-L177`）
- ✅ 主站重启无重复创建（`TeamLabShardDeploymentService.cs#L105-L107` + `TeamLabDependencyGraph.cs#L117-L135`）
- ✅ 容器创建成功但网络失败时的精确补偿（`AgentTeamLabNodeExecutor.cs#L435-L590`）

## Findings 汇总

| 编号 | 等级 | 标题 | 涉及不变量 |
| --- | --- | --- | --- |
| 4.4.1 | P2 | NetworkReady 信号追加在 marker 释放门控之后，与 30s 等待超时叠加可能销毁正在运行的业务容器 | #5、#13 |
| 4.4.2 | P3 | `BuildStartCommand` 的 `waitForManagedNetwork=true` 分支为不可达死代码 | — |
| 4.4.3 | P3 | 门控激活条件耦合到网络模式而非显式 TeamLab 标志 | — |

### Finding 4.4.1: NetworkReady 信号追加在 marker 释放门控之后，与 30s 等待超时叠加可能销毁正在运行的业务容器

**等级**：P2

**涉及文件与行号**：

- `src/GZCTF.Agent/Services/TeamLab/TeamLabContainerNetworkFinalizeService.cs#L55-L92`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabContainerNetworkFinalizeService.cs#L95-L151`（`BuildFinalizeCommand` 末尾 `touch {marker}` 在 L148）
- `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs#L537-L575`（finalize + 30s `WaitForAsync` + 失败销毁）
- `src/GZCTF.Agent/Services/RuntimeSignals/AgentRuntimeSignalPublisher.cs#L72-L108`（2s 轮询发布器）

**问题陈述**：

`FinalizeAsync` 的执行顺序为：

1. L62：`await runner.RunAsync(command, token)` 运行一次性校验脚本。该脚本（`BuildFinalizeCommand`，L95-L151）在所有事实校验通过后于 L148 执行 `touch {marker}`。marker 一旦被创建，容器内门控脚本 `while [ ! -f /tmp/.gzctf-teamlab-network-ready ]; do sleep 0.05; done; exec "$@"`（`DockerService.cs#L550`）立即退出并 `exec` 业务命令。
2. L63 校验 `result.Success` 后，L72-L85 才将 `NetworkReady` 信号 draft 追加到本地持久化日志。
3. 信号发布器（`AgentRuntimeSignalPublisher`，2s 轮询）将 draft POST 到主站。
4. 主站 `AgentTeamLabNodeExecutor.CreateContainerAsync` 在 L563-L568 以 `TimeSpan.FromSeconds(30)` 等待 `NetworkReady` 信号；L569-L575 在未就绪时调用 `DestroyContainerAsync` 销毁容器。

**违反的不变量**：

- **#5（主机资源已建立但响应丢失时，重放不应造成重复）**：此处不是“重复”而是“已建立的资源被销毁”。业务命令在 marker 被 touch 的瞬间已经开始 `exec`，但主站因 30s 内未观察到 `NetworkReady` 信号而销毁容器。即使重放（依赖图重启）能恢复一致状态，业务进程已被强制中断，违反“主机资源已建立不应被回收”的语义。
- **#13（就绪状态必须基于观测到的事实，禁止固定 sleep/自动重启）**：marker 的释放基于 `BuildFinalizeCommand` 校验的事实（符合 #13），但主站侧的“30s 超时即视为失败并销毁”是一种与事实解耦的固定等待窗口。当信号传递链路（Agent 本地日志 → 发布器轮询 → HTTP POST → 主站 `WaitForAsync`）累计延迟超过 30s 时，主站会无视“业务命令已经在运行”这一事实而销毁容器。

**触发条件**（并非理论，存在现实路径）：

- Agent 主机 IO 负载高，导致 `AgentRuntimeSignalJournal.AppendAsync`（`FileOptions.WriteThrough` 强制 fsync）延迟。
- 发布器 2s 轮询周期叠加网络抖动、主站 GC 暂停或数据库阻塞，使 `WaitForAsync` 在 30s 内未观察到信号。
- Agent 进程在 `runner.RunAsync` 返回后、`signals.AppendAsync` 之前被重启（信号 draft 永远不会写入日志；恢复后发布器也无该 operationId 的待发信号，因为发布器仅在启动时枚举日志中已有的 operationId）。

**影响**：

- 容器被销毁时业务命令可能正在执行写操作或与外部系统交互，留下半完成状态。
- 触发 `RestoreCompletedNodes` 重放，但 `TeamLabShardDeploymentService.cs#L323-L367` 中 `ApplyNodeSuccess` 仅在 Create 成功时设置 `RuntimeResourceId`；若主站未及落库 Create 成功状态就执行了销毁，重放将重新拉镜像+创建容器，造成资源浪费。

**建议修复方向**（任选其一或组合）：

1. **调整时序（首选）**：在 `BuildFinalizeCommand` 中将 `touch {marker}` 改为先写一个临时桩、由 `FinalizeAsync` 在 `signals.AppendAsync` 成功后再通过单独命令 touch 真正 marker；或将 marker 创建与信号 draft 追加合并为一个原子步骤（先追加信号 draft → 再 touch marker）。这样保证只要 marker 存在，信号就一定已持久化。
2. **延长等待与重试**：将 `WaitForAsync` 超时从 30s 提升至与 Agent→主站通信最坏情况匹配的值（如 120s），并在超时后增加一次 `InspectTeamLabContainerAsync` 探测：若容器仍在运行且 marker 已存在，则视为就绪而非销毁。
3. **取消销毁策略**：超时后不直接销毁，而是将节点标记为 `Pending` 并触发 reconcile，由后续探测决定保留或销毁。

**优先级判定依据**：设计要求 §3.5 强调“网络事实就绪前不能进入业务命令”，当前实现满足了“门控释放基于事实”，但破坏了不变量 #5 的“已建立资源不应被回收”语义——这是规格明确的不变量，非可选优化。

---

### Finding 4.4.2: `BuildStartCommand` 的 `waitForManagedNetwork=true` 分支为不可达死代码

**等级**：P3

**涉及文件与行号**：`src/GZCTF.Agent/Services/DockerService.cs#L521-L533`、调用点 `DockerService.cs#L148-L149`

**问题陈述**：

L34-L36 中：

```csharp
var isolatedHostNetwork = request.UseHostNetworkNone || request.UsePenetrationFabric;
var gateForTeamLabNetwork = isolatedHostNetwork;
```

L148-L149 的 else 分支仅在 `!gateForTeamLabNetwork` 时执行，此时 `isolatedHostNetwork` 必为 `false`，因此传给 `BuildStartCommand` 的 `waitForManagedNetwork` 实参恒为 `false`：

```csharp
else if (!string.IsNullOrWhiteSpace(request.StartCommand))
    createParams.Cmd = BuildStartCommand(request.StartCommand, isolatedHostNetwork);
```

L521-L533 中 `waitForManagedNetwork=true` 分支（含 `teamlab-network-gate` 等待脚本）永远不会被执行。门控逻辑实际上由 L144 的 `BuildGatedCommand` 统一承担。

**影响**：

- 维护负担：门控逻辑分散在 `BuildGatedCommand` 与 `BuildStartCommand` 两处，未来修改门控脚本时容易遗漏其中一处。
- 阅读误导：新维护者可能误以为 `BuildStartCommand` 仍承担门控职责，从而在重构时引入回归。

**建议修复**：

- 删除 `BuildStartCommand` 的 `waitForManagedNetwork` 参数，签名简化为 `BuildStartCommand(string command)`，方法体仅保留 `["sh", "-c", command]`。
- 或保留参数但加 `[Obsolete]` 标记并在注释中说明门控已统一收敛至 `BuildGatedCommand`。

**优先级判定依据**：纯可维护性问题，无运行时风险。但若未来误用此分支（例如调用方误传 `true`），将出现双重门控脚本嵌套，所以应在方便时清理。

---

### Finding 4.4.3: 门控激活条件耦合到网络模式而非显式 TeamLab 标志

**等级**：P3

**涉及文件与行号**：`src/GZCTF.Agent/Services/DockerService.cs#L34-L36`、`#L132-L149`

**问题陈述**：

门控激活判定为 `gateForTeamLabNetwork = isolatedHostNetwork = UseHostNetworkNone || UsePenetrationFabric`。即“只要容器使用 host network none 或穿透 fabric，就启用 TeamLab 门控”。TeamLab 门控语义被绑定到网络模式标志上。

当前唯一启用 `UseHostNetworkNone=true` 的调用方是 `AgentTeamLabNodeExecutor.cs#L467-L468`（TeamLab 容器），因此当前行为正确。但该耦合存在以下风险：

- 未来若有非 TeamLab 场景需要使用 `UseHostNetworkNone`（例如纯隔离容器），会被误启用 TeamLab 门控，导致容器因等待 `/tmp/.gzctf-teamlab-network-ready` 永远阻塞。
- 反过来，若 TeamLab 调用方修改为使用其他网络模式（例如直接使用 bridge），门控会被静默关闭，违反设计要求 §3.5（这是 P1 级回归风险）。

**影响**：

- 当前无错误行为。
- 潜在回归风险：门控静默关闭不会被编译器或测试立即捕获。

**建议修复**：

- 在 `CreateContainerRequest` 增加显式标志 `EnableTeamLabNetworkGate`（或 `RequiresTeamLabNetworkFinalize`），由 `AgentTeamLabNodeExecutor.cs#L451-L475` 显式置 `true`。
- `gateForTeamLabNetwork` 直接取该标志值；`isolatedHostNetwork` 仍由网络模式决定（用于其他网络配置逻辑）。
- 同时在 `BuildGatedCommand` 处增加断言或日志，确保启用门控时 `NetworkMode` 与 TeamLab 期望一致。

**优先级判定依据**：当前无错误，但设计要求 §3.5 是“门控不能因镜像默认命令被绕过”，而当前实现是“门控激活取决于网络模式标志”，二者耦合关系脆弱。属于“当前正确但抗变化能力弱”的可维护性问题，定 P3。

## 启动门控完整性验证

按设计要求 §3.5 与 §4.4 检查项逐一验证：

### 1. 镜像预分发与无重复拉取

- `AgentTeamLabNodeExecutor.cs#L446-L447`：调用 `imageDistribution.EnsureDockerImageOnNodeAsync`，`request.ImageReady=true` 时跳过。
- `ImageDistributionService.cs#L304-L329`：`EnsureDockerImageOnNodeAsync` 将请求加入队列并等待 `Ready`。
- `ImageDistributionService.cs#L396-L404`：当 `record.Status == Ready && ImageHash 匹配` 时直接返回，不重新拉取；仅更新 `LastCheckedAt`。
- `DockerService.cs#L132-L143`：门控分支中显式调用 `InspectImageAsync`，若镜像不存在则抛 `image_not_ready`，确保门控分支不会因镜像缺失退化为无门控。

**结论**：✅ 通过。镜像就绪事实被严格检查，无重复拉取路径。

### 2. 默认 Entrypoint/Cmd 与显式 StartCommand 均被门控

- `DockerService.cs#L132-L149`：门控分支中调用 `BuildGatedCommand(image.Config?.Entrypoint, image.Config?.Cmd, request.StartCommand)`，无论 `StartCommand` 是否为空都进入此分支。
- `DockerService.cs#L535-L554`：
  - `payload = (imageEntrypoint ?? []).ToList()`；
  - `if (string.IsNullOrWhiteSpace(startCommand)) payload.AddRange(imageCmd ?? [])`：无 StartCommand 时使用镜像默认 Cmd；
  - `else payload.AddRange(["sh", "-c", startCommand])`：有 StartCommand 时包装为 `sh -c`；
  - 返回 `Entrypoint = ["sh", "-c", "while [ ! -f /tmp/.gzctf-teamlab-network-ready ]; do sleep 0.05; done; exec \"$@\"", "teamlab-network-gate"]`，`Command = payload`。
- 即无论镜像是否提供 Entrypoint/Cmd，是否提供 StartCommand，最终容器启动命令都被外层门控脚本包裹，必须等待 marker 文件才能 `exec` 真正业务命令。

**结论**：✅ 通过。门控覆盖设计要求 §3.5 的两种情形。

### 3. 网络 finalize 一次性事实校验

`TeamLabContainerNetworkFinalizeService.cs#L95-L151` 中 `BuildFinalizeCommand` 按以下顺序执行一次性 shell 校验（任一失败则整体失败、不会 touch marker）：

1. L103-L107：依赖命令存在性（docker/ip/nsenter/dig）。
2. L108：容器 PID 一致性（`docker inspect -f '{{.State.Pid}}'` 与传入 pid 比对）。
3. L109：marker 是否已存在的幂等检查（`already=0/1`）。
4. L112-L121：每个接口校验 `ip link show dev` + `ip -o -4 addr show dev | awk | grep -Fx {address}` + `ip -o link show dev | grep -Fi 'link/ether {mac} '`。
5. L123-L133：每条路由校验 `ip route show exact {cidr} | grep via {gateway} | grep dev {iface}`。
6. L135-L136：`RequireNoDefaultRoute` 时校验 `ip route show default` 为空。
7. L138-L140：每个 DNS server 校验 `grep -Fx "nameserver {server}" /proc/{pid}/root/etc/resolv.conf`。
8. L142-L146：每个 DNS probe 校验 `nsenter -t {pid} -n dig +time=2 +tries=1 +short @{server} {query} A | grep -Fx {expected}`。
9. L148：全部通过后 `touch {marker}`。
10. L149：`printf 'finalized:%s\n' "$already"` 用于返回 `AlreadyFinalized` 标志（`TeamLabContainerNetworkFinalizeService.cs#L91`）。

`FinalizeAsync`（L19-L93）在调用 `BuildFinalizeCommand` 前还做了：

- L42-L53：容器锁 + 容器身份校验（ContainerId/ContainerName/RuntimeId/Generation 一致）+ 容器运行中且 PID 有效。
- L37-L40：active generation 校验（防止 finalize 已被取代的 generation）。

**结论**：✅ 通过。校验基于真实可观测事实（nsenter + ip + dig），无固定 sleep，无自动重启假设，符合 #13。校验一次性原子完成（单条 shell 命令脚本中任一失败即整体失败）。

### 4. Agent 重启无重复业务命令启动

`DockerService.cs#L151-L177`：

- L153：`InspectContainerAsync(containerName, token)` 按名称查现有容器。
- L154-L165：校验现有容器的 image、`GZCTF.Generation` 标签、（门控场景下）`GZCTF.RuntimeId` 标签，任一不一致抛 `runtime_identity_conflict`。
- L168-L170：若容器存在但不运行则 `StartContainerAsync`，不重新创建。
- L170：返回现有容器响应。
- L172-L177：`DockerContainerNotFoundException` 或 404 时才继续创建。

由于门控脚本以 `exec "$@"` 执行业务命令，容器重启时若 marker 已存在则立即执行业务命令；若 marker 不存在则继续等待。Agent 重启不会创建重复容器。

**结论**：✅ 通过。Agent 重启不会因幂等查找遗漏而创建重复业务容器。

### 5. 主站重启无重复创建

- `TeamLabShardDeploymentService.cs#L105-L107`：`AgentOperationId` 仅在为 null 时赋值（`Guid.CreateVersion7()`），跨重启保持稳定，符合 #3“操作身份稳定”。
- `TeamLabShardDeploymentService.cs#L124-L125`：每次部署循环都调用 `TeamLabDependencyGraph.RestoreCompletedNodes(runtimeAssets)` 重建已完成节点集合。
- `TeamLabDependencyGraph.cs#L117-L135`：
  - 若 `RuntimeResourceId` 已设置且 `ExecutionStage < Failed` → 标记 `Create` 节点为完成；
  - VM 的 `GuestReady`/`Bootstrap`/`Health` 节点按 `ExecutionStage` 区间判定；
  - 已完成节点会被 `TryTakeReadyBatch` 跳过（L101），不会被重新调度。

**结论**：✅ 通过。主站重启后已成功的 Create 阶段不会被重新执行，符合 #5“主机资源已存在时重放不重复”。

### 6. 容器创建成功但网络失败时的精确补偿

`AgentTeamLabNodeExecutor.cs#L435-L590`：

- L490：`CreateContainerOrThrowAsync` 成功创建容器（业务命令在门控中等待）。
- L491-L515：每个接口挂接失败 → L511-L513 销毁容器并返回 `Failed`。
- L516-L521：`OperationId` 缺失 → L518-L520 销毁容器并返回 `Failed`。
- L537-L562：`FinalizeTeamLabContainerNetworkAsync` 失败或 DryRun → L558-L560 销毁容器并返回 `Failed`。
- L563-L575：30s 内未观察到 `NetworkReady` 信号 → L571-L573 销毁容器并返回 `Failed`。
- L576-L588：`StartEndpointSensorAsync` 失败且 `EndpointObservation == Required` → L585-L587 销毁容器并返回 `Failed`。
- L137-L138 / L146-L147：异常路径下若 sensor 已注册则 `RemoveEndpointSensorAsync` 回滚 sensor。

**结论**：✅ 通过（补偿覆盖度）。补偿触发条件精确：仅在“容器已创建”之后的失败路径触发销毁，且每个失败点独立补偿，不会遗漏。注：30s 超时销毁的合理性见 Finding 4.4.1，那是补偿“是否应触发”的问题，而非“补偿是否覆盖”的问题。

## 已检查但确认不是问题的高风险点

1. **marker 文件路径使用 `/proc/{pid}/root/tmp/.gzctf-teamlab-network-ready`**（`TeamLabContainerNetworkFinalizeService.cs#L99`）：
   - 风险点：PID 复用导致 touch 错容器。
   - 验证：pid 来自 L44 `InspectTeamLabContainerAsync` 的实时 inspect，且 L108 在脚本内再次校验 `docker inspect -f '{{.State.Pid}}' {containerId} = {pid}`。若 PID 已变化，脚本会因 PID 不匹配而失败，不会 touch 错误容器的 marker。
   - 结论：✅ 不是问题。

2. **现有容器直接 `StartContainerAsync` 不重建**（`DockerService.cs#L168-L170`）：
   - 风险点：若旧容器来自历史 generation，会误启动。
   - 验证：L162-L165 在启动前严格校验 image / `GZCTF.Generation` / `GZCTF.RuntimeId` 标签一致性，任一不匹配抛 `runtime_identity_conflict`。门控场景下 `RuntimeId` 校验额外保证不会跨 runtime 复用容器。
   - 结论：✅ 不是问题。

3. **`AlreadyFinalized` 判定基于 `result.Output.Contains("finalized:1")`**（`TeamLabContainerNetworkFinalizeService.cs#L91`）：
   - 风险点：输出解析脆弱。
   - 验证：`printf 'finalized:%s\n' "$already"` 输出格式固定为 `finalized:0` 或 `finalized:1`，且脚本以 `set -eu` 执行（L103），任一前置校验失败时根本不会执行到 printf。无歧义解析风险。
   - 结论：✅ 不是问题。

4. **Agent 在 `runner.RunAsync` 与 `signals.AppendAsync` 之间崩溃**：
   - 风险点：marker 已 touch、业务命令已启动、但信号永远丢失。
   - 验证：这本质上是 Finding 4.4.1 描述的问题的一个极端路径，已在 4.4.1 中作为触发条件之一记录。重放时 `RestoreCompletedNodes` 会因 `RuntimeResourceId` 未设置而重新调度 Create 节点，触发 `DockerService.CreateContainerAsync` 的幂等分支返回现有容器，然后再次执行 finalize + 等待信号，最终能恢复一致状态。这一恢复路径本身是正确的，但 4.4.1 中描述的 30s 销毁会破坏该恢复路径——这就是 4.4.1 定为 P2 而非 P3 的原因。
   - 结论：✅ 不是独立问题，归入 4.4.1。

5. **`AgentRuntimeSignalPublisher` 启动时仅枚举日志中已有 operationId**（`AgentRuntimeSignalPublisher.cs#L72-L108`）：
   - 风险点：若信号 draft 未写入日志，恢复后不会重试发布。
   - 验证：这同样是 4.4.1 的触发条件之一。在 `signals.AppendAsync` 成功后崩溃的情况下，下次启动时枚举到该 operationId，会重发未 ack 的信号。在 `runner.RunAsync` 后、`signals.AppendAsync` 前崩溃的情况下，信号确实永远丢失，但 marker 已存在——此时容器仍在运行业务命令，重放会通过 `DockerService` 幂等分支返回现有容器，重新 finalize 时 `BuildFinalizeCommand` L109 检测到 marker 已存在（`already=1`）、所有事实校验仍会通过、最终重新追加信号 draft，主站能恢复。该恢复路径成立的前提是主站不先销毁容器，因此仍是 4.4.1。
   - 结论：✅ 不是独立问题，归入 4.4.1。

6. **`useHostNetworkNone` 与 `usePenetrationFabric` 同时为 true 的组合**：
   - 验证：`AgentTeamLabNodeExecutor.cs#L467-L468` 中 `UseHostNetworkNone = true`、`UsePenetrationFabric = false`；其他调用方（非 TeamLab）通过 `FleetContainerManager` 设置，二者不会同时为 true。
   - 结论：✅ 不是问题。

## 链路覆盖结论

**链路 4.4 Docker 创建与网络门控** 的所有规格检查项均已实际打开对应代码文件验证，结论如下：

- **门控完整性**：✅ 设计要求 §3.5 完全满足。镜像默认 Entrypoint/Cmd 与显式 StartCommand 两种路径均被 `BuildGatedCommand` 的等待脚本包裹，业务命令在 marker 文件出现前无法执行。
- **网络 finalize**：✅ 一次性原子校验接口/地址/MAC/路由/默认路由/DNS 配置/DNS 真实解析（dig +short + grep expected），无固定 sleep、无自动重启假设，符合 #13。
- **镜像预分发**：✅ 无重复拉取。
- **幂等性**：✅ Agent 侧（容器名 + 标签校验）与主站侧（`AgentOperationId` 稳定 + `RestoreCompletedNodes`）双重保证，符合 #3、#5。
- **DB 状态时序**：✅ `ApplyNodeSuccess` 仅在 Create 成功（含 finalize + 信号确认）后才设置 `RuntimeResourceId`，符合 #4。
- **补偿覆盖**：✅ 容器创建后所有失败路径均有销毁补偿。

**Findings 总览**：3 个 findings，P2×1（4.4.1，门控释放与信号持久化的时序问题，需在设计层面调整时序或延长等待策略），P3×2（4.4.2 死代码、4.4.3 门控条件耦合，均为可维护性改进，无运行时风险）。

**最严重问题**：4.4.1 在 Agent IO 高负载或主站响应延迟场景下可能销毁正在运行业务命令的容器，破坏不变量 #5。建议优先修复 4.4.1，修复路径明确（调整 `FinalizeAsync` 中 `signals.AppendAsync` 与 `touch marker` 的相对时序，或将主站等待超时延长并对超时场景增加“容器是否仍在运行且 marker 已存在”的探测）。

**未覆盖项**：无。所有规格 §4.4 列出的检查项均已实际打开代码并验证。
