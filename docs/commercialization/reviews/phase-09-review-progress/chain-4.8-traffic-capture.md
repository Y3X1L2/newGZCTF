# 链路 4.8 流量元数据、路径和 PCAP 独立代码审查

- 审查范围：`src/GZCTF/Modules/TeamLab/**`（主站 Application / Infrastructure / Api / Domain）、`src/GZCTF.Agent/Services/Observation/**` 与 `src/GZCTF.Agent/Services/TeamLabNetworkService.cs` 中与流量观测、路径关联、PCAP 抓取/上传/清理相关的代码路径
- 参考规格：`docs/commercialization/phase-09-teamlab-networking-independent-code-review.md` 的 3.8、4.8、5、7、8、9、10、12 节
- 审查日期：2026-07-21
- 审查方式：逐文件打开阅读（30+ 文件），关键不变量与边界通过 Grep/Read 交叉验证

## 一、Findings

### Finding 4.8.1 — P2 — TeamLabPcapService.DeleteAsync 后 MonitorAsync/FinalizeAsync/SaveAsync 重建已删目录与 state.json，违反不变量 #15

- 文件：`src/GZCTF.Agent/Services/Observation/TeamLabPcapService.cs`
- 关键行：
  - `DeleteAsync`（行 215-238）：行 228 `StopOwnedProcess(state)` 仅对进程 `Kill`+`WaitForExit` 同步等待；行 233 `Directory.Delete(directory, true)` 删除整个 segment 目录（含 `capture.pcapng` 与 `state.json`）；行 234-235 释放 `_gates[segmentId]` 的 `SemaphoreSlim`
  - `MonitorAsync`（行 292-308）：行 296 `await process.WaitForExitAsync()`，行 297 调用 `FinalizeAsync(state, CancellationToken.None)`；`finally` 仅做 `TryRemove` 与 `process.Dispose()`
  - `FinalizeAsync`（行 310-335）：行 316 重新读取 `state.FilePath` 文件大小；行 320-322 重新打开文件计算 SHA-256；行 333 调用 `SaveAsync(state, cancellationToken)`
  - `SaveAsync`（行 468-486）：行 471 `Directory.CreateDirectory(Path.GetDirectoryName(path)!)` 无条件重建 segment 目录，再写入 `state.json`
- 现象：`DeleteAsync` 通过 `StopOwnedProcess` 同步等待 dumpcap/tcpdump 退出后删除目录，但 `MonitorAsync` 是在 `StartAsync`（行 117）中以 `_ = MonitorAsync(process, state);` fire-and-forget 启动的。`StopOwnedProcess` 只 `WaitForExit` 进程对象本身，不会让 `MonitorAsync` 的 `await process.WaitForExitAsync()` 提前完成其后续 `FinalizeAsync` 调用。竞态序列：
  1. `DeleteAsync` 行 228 `StopOwnedProcess(state)` → 进程退出
  2. `DeleteAsync` 行 233 `Directory.Delete(directory, true)` → 目录、`state.json`、`capture.pcapng` 全部删除
  3. `MonitorAsync` 行 296 `await process.WaitForExitAsync()` 解除阻塞
  4. `MonitorAsync` 行 297 调用 `FinalizeAsync(state, CancellationToken.None)`
  5. `FinalizeAsync` 行 316 `File.Exists(state.FilePath)` 返回 false（已删除），`bytes=0`
  6. `FinalizeAsync` 行 323-332 将 state 改为 `Status=Failed, CapturedBytes=0, Sha256=null, LastError="Capture process produced no packet file."`
  7. `FinalizeAsync` 行 333 `SaveAsync` → 行 471 `Directory.CreateDirectory` 重建 segment 目录，行 480 `File.Move` 写入新的 `state.json`
- 影响：
  - 违反不变量 #15（destroy complete cleanup）的“本地文件清理”要求：destroy / `CleanupGenerationAsync` / `DeleteAsync` 调用后磁盘上仍残留 `runtime-{id}/generation-{gen}/capture-{cid}/segment-{sid}/state.json` 空目录与文件
  - `SnapshotInventoryAsync`（行 260-290）会扫描 `state.json`，被重建的 Failed 状态 segment 会出现在 inventory 中，导致 UI / API 表现 destroy 未生效
  - `CleanupGenerationAsync`（行 240-258）虽在行 257 删除 generation root，但其枚举 state（行 243-256）后到行 257 删除之间仍存在 MonitorAsync 重建目录的窗口；若 destroy 路径在 `pcapService.CleanupGenerationAsync` 之后还有其他子清理（参考 `TeamLabNetworkService.cs` 行 756-761），MonitorAsync 可能复活最后一段 state.json
- 修复方向（任选其一，需与团队确认）：
  - 在 `DeleteAsync` 中显式取消 `MonitorAsync`：在 segment state 上新增 `CancellationTokenSource`，`DeleteAsync` 先 `Cancel` 再 `await` Monitor 任务再删目录
  - 或在 `FinalizeAsync` 入口检查 state 是否已被标记删除（如新增 `state.Deleted` 标记或检查目录是否存在），跳过 `SaveAsync`
  - 或在 `MonitorAsync` 的 `WaitForExitAsync` 之后、`FinalizeAsync` 之前检查 `Directory.Exists(SegmentDirectory(...))`，若不存在则跳过 Finalize
- 级别：P2（功能正确性问题、违反不变量 #15，但仅在 destroy/delete 路径触发，且只残留 state.json 不残留 pcapng，影响限于 inventory/UI 误导）

---

### Finding 4.8.2 — P3 — TeamLabPcapService.DeleteAsync 释放 SemaphoreSlim 与 StartAsync 持锁竞态，可能触发 ObjectDisposedException

- 文件：`src/GZCTF.Agent/Services/Observation/TeamLabPcapService.cs`
- 关键行：
  - `StartAsync`（行 21-131）：行 44 `var gate = _gates.GetOrAdd(request.SegmentId, _ => new SemaphoreSlim(1, 1));`；行 45 `await gate.WaitAsync(cancellationToken);`；行 127-130 `finally { gate.Release(); }`
  - `DeleteAsync`（行 215-238）：行 234 `_gates.TryRemove(request.SegmentId, out var gate);`；行 235 `gate?.Dispose();`
- 现象：`StartAsync` 持锁期间正在执行 `LoadAsync`/`Process.Start`/`SaveAsync` 等可能耗时的操作（行 48-116），与此同时若 destroy 路径并发调用 `DeleteAsync`（`TeamLabNetworkService.cs` 行 756-761 中 `pcapService.CleanupGenerationAsync` 在 destroy 时被调用），`DeleteAsync` 会直接 `Dispose()` SemaphoreSlim。`StartAsync` 退出 `try` 后在 `finally` 行 129 `gate.Release()` 会抛 `ObjectDisposedException`。
- 影响：
  - 异常被 `StartAsync` 行 120-123 的 `catch (Exception exception) when (...)` 捕获，但 `Win32Exception`、`InvalidOperationException` 在 `when` 子句过滤后，`ObjectDisposedException` 不属于过滤集合，会从 `try`/`finally` 越界抛出，向上冒泡到 `TeamLabNodeCaptureExecutionService`，被映射为 capture start 失败。这会污染失败语义（真实原因是 destroy 并发，但表现为 start internal error）。
  - 违反不变量 #16（failure preserves correlation/error codes）：调用方收到的失败原因错误。
- 修复方向：
  - `StartAsync` 的 `finally` 使用 `try { gate.Release(); } catch (ObjectDisposedException) { }`，或
  - 在 `DeleteAsync` 中不直接 `Dispose()` SemaphoreSlim（让 GC 回收），或先标记删除标志再等待 StartAsync 完成
- 级别：P3（窄竞态、仅在 destroy 与 start 并发触发，调用方上层能捕获异常但失败原因不准确）

---

### Finding 4.8.3 — P3 — Capture 上传 token 10 分钟有效期对 10GB 大文件上传不足，触发无谓重试

- 文件：`src/GZCTF/Modules/TeamLab/Application/TeamLabCaptureCoordinator.cs`
- 关键行：
  - 行 117-123：`tokens.Issue(new TeamLabCaptureUploadGrant(...), TimeSpan.FromMinutes(10))` — 上传 token 硬编码 10 分钟有效期
  - 行 124 `segment.MaxBytes` 作为上传 size hint 传给 node，但 token 期限与 MaxBytes 解耦
- 关联文件：`src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs`
  - 行 201：`model.MaxBytes is < 1024 or > 10L * 1024 * 1024 * 1024` — 单次 capture 总 MaxBytes 上限 10GB
  - 行 319-340 `AssignSegmentBudgets`：MaxBytes 按 segment 数量平均分配，单 segment 最大可达 10GB
- 现象：10GB 数据在 1Gbps 链路上理论需要约 80 秒，但在实际生产网络（跨节点、TLS、对象存储后端）下，10 分钟可能不够，特别是当 worker 节点出口带宽有限或对象存储写入有重试时。token 过期后 `TeamLabCaptureUploadService`（行约 60-90）会拒绝上传，coordinator 会重试 upload，重新分配 token，重新上传整个 segment。
- 影响：
  - 数据无丢失（segment 在 Agent 端已 Captured，PostgreSQL 中 segment 状态可回退到 Captured 重试）
  - 但带宽浪费严重：10GB 重传一次浪费一次 IO 与网络；同时上传 token 频繁过期会被 `TeamLabEventRecorder` 记为失败事件，污染监控指标
  - 违反不变量 #16（失败原因不准确）：表面是 upload 失败，根因是 token 寿命不足
- 修复方向：
  - token 有效期基于 `segment.MaxBytes` 计算（如 `Math.Max(10, MaxBytes / 100MB) min`），或
  - 提供续期接口（refresh token），或
  - 提升至 30 分钟（参考 `TeamLabCaptureUploadService.cs` 中 `teamlab:capture-upload:{segmentId}` 租约 15 分钟，token 应不短于租约）
- 级别：P3（无数据丢失，但带宽/监控指标受影响，仅在 ≥1GB 大 segment 上传场景触发）

---

## 二、不变量验证

### 不变量 #12 — PostgreSQL 是事实来源，Redis 仅用于唤醒/缓冲
- **验证通过**。
- `RedisTeamLabTrafficIngestor.cs` 行 21-24 `ProtectedTrimScript`：使用 `XPENDING` 检查 pending 消息数，若为 0 用 `XTRIM MAXLEN`，否则用 `XTRIM MINID` 保留 pending 中最早 ID 之前的消息。保证未消费的消息不会被裁剪。
- `PostgresTeamLabTrafficBatchWriter.cs` 使用临时 staging 表 + binary COPY + `INSERT ... ON CONFLICT DO NOTHING`：
  - observations 唯一键：`(RuntimeId, Generation, ObservationPointId, SourceSequence)`
  - flows 唯一键：`(CapturedAt, RuntimeId, Generation, Fingerprint)`
  - 重放安全：重复写入被忽略，`inserted` 计数为新插入数
- `TeamLabTrafficApplicationService.cs` 的 cursor 推进基于 PostgreSQL 中的 `LastSequence`（`CollectNodeObservationsAsync` 行 610-614），即使本地 buffer 丢样本也推进游标，丢样本计数通过 `DroppedCount` 透传到 Redis 与监控，符合“PostgreSQL 是事实来源、Redis 仅缓冲”的设计
- `TeamLabTrafficPersistenceWorker.cs` 双任务（collect + persist）配合指数退避（最大 5s），持久化失败时不会推进 cursor
- 结论：PostgreSQL 始终为事实来源；Redis Stream 与 LocalBuffer 均为缓冲，不参与事实判定

### 不变量 #15 — destroy 完整清理
- **部分违反** — 见 Finding 4.8.1。
- 主体清理路径正确：`TeamLabNetworkService.cs` 行 756-761 destroy 时依次调用 `bootstrapService.CleanupGenerationAsync`、`endpointSensors.Remove`、`observationRegistry.RemoveAsync`、`observationSpool.Remove`、`pcapService.CleanupGenerationAsync`，覆盖各子系统的 generation 资源
- `ObservationBatchSpool.cs` `Remove`（行 147-164）使用 epoch 递增 + mutation gate 等待，删除目录前确保 inflight 写入完成，无残留
- `ObservationPointRegistry.cs` `RemoveAsync`（行 55-61）删除 `observation-points.json` 文件
- `EndpointSensorChannelService.cs` `Remove`（行 85-104）`StopHostProcess` + 取消 CTS + 删除 Unix socket 文件 + `ContinueWith` 中 `ZeroMemory` HMAC key
- `TeamLabPcapService.cs` `CleanupGenerationAsync`（行 240-258）删除 generation root
- `TeamLabCaptureCoordinator.cs` `ExpireAsync`（行 160-232）清理 agent captures + object storage，失败回退到 `CleanupPending` 重试
- 唯一漏洞：Finding 4.8.1 中 `DeleteAsync`/`CleanupGenerationAsync` 后 `MonitorAsync` 异步重建 state.json

### 不变量 #16 — 失败保留相关性/错误码
- **基本验证通过**，但 Finding 4.8.2 / 4.8.3 中存在错误码失真场景。
- `TeamLabPcapService.cs` 失败时写入 `state.LastError` 并保持 segment Id、capture Id、observation point Id 不变
- `TeamLabCaptureCoordinator.cs` `ExpireAsync`（行 197-208）根据 agent/object 清理结果区分 `LastError`：`"Agent capture cleanup is pending."` / `"Object-storage capture cleanup is pending."` / `"Agent and object-storage capture cleanup are pending."`
- `TeamLabTrafficApplicationService.cs` `ApplyNodeResult`（行 884-923）segment 状态转换保留 `LastError`、`CorrelationCursor` 等
- `TeamLabTrafficPathCorrelator.cs` `CreatePath`（行 275-300）`EvidenceFingerprint` 包含 confidence 字节 + observation IDs，确保相关性证据可追溯
- Finding 4.8.2 中 `ObjectDisposedException` 会让 start 失败原因错误；Finding 4.8.3 中 token 过期会让 upload 失败原因错误

### 不变量 #17 — secrets 不进入 PCAP 元数据
- **验证通过**。
- `TeamLabCaptureArtifactStore.cs` `WriteArchiveAsync`（行 142-200）写入 `manifest.json`，内容仅包含：segment PublicId、observation point Id、SHA-256 hex digest、Bytes、StartedAt、CompletedAt、interfaces 列表
- `WriteSegmentAsync`（行 100-134）使用 `DigestingReadStream` 计算 SHA-256 校验 segment 内容，不包含 HMAC key、token、runtime secret
- `InternalTeamLabCaptureUploadController.cs` 匿名内部端点使用 bearer token + worker node header + SHA-256 header，token 不写入 manifest
- `PcapSegmentUploader.cs` HTTP PUT 使用 bearer token，token 不写入 PCAP 文件本身
- `TeamLabCaptureUploadGrant` 内容（job.PublicId, segment.PublicId, workerNodeId, CapturedBytes, MaxBytes, Sha256）均为非敏感数据
- `EndpointSensorChannelService.cs` `Remove`（行 94-102）`ContinueWith` 中 `CryptographicOperations.ZeroMemory(registration.Key)` 显式清零 HMAC key
- 结论：HMAC key、upload token、ASP.NET Core Data Protection key 均不会出现在 PCAP / manifest / state.json 中

## 三、链路功能验证

### 3.1 A→B / B→C / C→B / B→A 四段方向保留
- **验证通过**。
- `PacketFingerprint.cs` `FlowDigest`（行 144-150）：`{source}|{sourcePort}|{destination}|{destinationPort}|{protocol}`，方向性由源/目的顺序编码，A→B 与 B→A 产生不同 FlowFingerprint
- `EndpointSensorAuthenticator.cs` `Digest`（行 85-86）：相同方向性编码，与 packet 侧一致
- `TeamLabTrafficPathCorrelator.cs` `BuildPacketPaths`（行 213-222）按 PacketFingerprint 聚类，方向隐含在 fingerprint 中
- `BuildTemporalProcessPaths`（行 224-245）按 ProcessIdentityHash + FlowFingerprint 聚类，`HasDirectedProcessTransition`（行 247-261）检查 inbound→outbound 序列，确认方向性
- `IsInboundProcessEvent` / `IsOutboundProcessEvent`（行 263-273）覆盖 `accept/accepted/inbound/received` 与 `connect/connected/outbound/opened`，对应 A→B（B 端 accept）→ B→C（B 端 connect）的链条

### 3.2 fingerprint 去重
- **验证通过**。
- `TeamLabTrafficPathCorrelator.cs` `BuildTemporalProcessPaths`（行 232-237）：按 `FlowFingerprint` groupby 后 `Select(item => item.First())`，确保同一 flow 在同一时间窗口内只产生一条 observation，避免重复路径
- `PacketFingerprint.cs`（行 49-83）canonical form 包含 IP header 4..8（identification+flags+fragment offset）、protocol、IP addresses、TCP 头 16 字节 + options、UDP 头 6 字节、ICMP 头 2+4 字节、payload，确保同一 packet 产生相同 fingerprint
- `PostgresTeamLabTrafficBatchWriter.cs` observations 唯一键包含 `SourceSequence`，flows 唯一键包含 `Fingerprint`，避免重复写入

### 3.3 cursor / Redis / PostgreSQL 幂等性
- **验证通过**。
- cursor：`TeamLabTrafficApplicationService.cs` `CollectNodeObservationsAsync`（行 610-614）使用 `LastSequence` 推进，重放安全
- Redis：`RedisTeamLabTrafficIngestor.cs` `ProtectedTrimScript`（行 21-24）保证 pending 消息不被裁剪；`StreamAutoClaimAsync`（30s idle）回收卡住的消费者
- PostgreSQL：`PostgresTeamLabTrafficBatchWriter.cs` `ON CONFLICT DO NOTHING` 保证重复写入幂等；使用 staging table + binary COPY 高效导入
- `TeamLabCaptureUploadService.cs` 上传完成后若 segment 已 Uploaded 且对象存在，返回 200 + `AlreadyExists=true`，幂等

### 3.4 路径关联置信度
- **验证通过**。
- `TeamLabTrafficPath.cs` `TeamLabPathConfidence` 枚举：`PacketExact=0`、`ProcessCorrelated=1`、`TemporallyRelated=2`
- `TeamLabTrafficPathCorrelator.cs` `BuildPacketPaths`（行 220）使用 `PacketExact`：相同 PacketFingerprint 在 5s PacketWindow 内至少 2 条 observation
- `BuildTemporalProcessPaths`（行 240-243）：
  - `HasDirectedProcessTransition` 为 true → `ProcessCorrelated`（有 inbound→outbound 序列证据）
  - 否则 → `TemporallyRelated`（仅时间窗口内同 ProcessIdentityHash）
- `CreatePath`（行 280）`evidence = confidence byte + observations IDs`，`EvidenceFingerprint = SHA256(evidence)`，确保证据可追溯

### 3.5 capture 预算分配
- **验证通过**。
- `TeamLabTrafficApplicationService.cs` `AssignSegmentBudgets`（行 319-340）：
  - `MaxBytes < Segments.Count` 时抛 `capture_budget_too_small`
  - 按 segment PublicId 排序，`baseline = MaxBytes / count`、`remainder = MaxBytes % count`，前 `remainder` 个 segment 各 +1 字节，保证总和等于 MaxBytes
- `TeamLabPcapService.cs` 行 28 校验 `MaxBytes is < 1024 or > 10L * 1024 * 1024 * 1024`
- `BuildStartInfo`（行 337-380）将 MaxBytes 转换为 dumpcap `-b filesize:` 或 tcpdump `-C` 参数，确保单文件不超过预算

### 3.6 清理完整性
- **部分违反** — 见 Finding 4.8.1。
- 主体清理路径正确（见不变量 #15 部分）
- `TeamLabCaptureCoordinator.cs` `ExpireAsync`（行 160-232）正确处理 agent / object 双侧清理，失败时进入 `CleanupPending` 状态重试
- `TeamLabPcapService.cs` `CleanupGenerationAsync`（行 240-258）正确删除 generation root
- `ObservationBatchSpool.cs` `Remove`（行 147-164）正确使用 epoch + mutation gate
- 唯一漏洞：`DeleteAsync` 后 `MonitorAsync` 异步重建 state.json

### 3.7 PCAP manifest 字节可验证性
- **验证通过**。
- `TeamLabCaptureArtifactStore.cs`：
  - `WriteSegmentAsync`（行 100-134）使用 `DigestingReadStream`（行 205-275）逐块计算 SHA-256，与 manifest 中声明的 SHA-256 比对，不匹配则删除 object 并抛异常
  - `WriteArchiveAsync`（行 142-200）写入 tar archive，`manifest.json` 包含每个 segment 的 SHA-256、Bytes、PublicId、observation point Id
  - `KnownLengthReadStream`（行 277-315）限制 segment 读取不超过 `segment.Bytes`，防止 manifest 与实际数据不一致
- `InternalTeamLabCaptureUploadController.cs` 接收时校验 `X-TeamLab-Segment-Sha256` header 与实际计算 SHA-256 匹配
- 下载侧 `OpenTeamLabTrafficController.cs` `DownloadCapture`（行 120-144）流式输出 tar archive，客户端可逐 segment 校验 SHA-256

## 四、适配性与安全验证

### 4.1 适配性 — Linux 特定调用、平台分支、文件路径硬编码
- **验证通过**。
- `TeamLabPcapService.cs` `BuildStartInfo`（行 337-380）：优先 `dumpcap`，回退 `tcpdump`（仅单接口）；`CommandExists` 检测 PATH；路径 `/var/lib/gzctf/captures`（行 16）符合 Linux FHS
- `EndpointSensorChannelService.cs`：Docker 模式用 `nsenter` 进入容器 PID namespace（行 306-310），VM 模式 Linux 用 systemd，Windows VM 用 schtasks（行 25）；Unix domain socket 路径通过 `VmSocketPath` / `DockerSocketPath` 生成
- `ObservationPointRegistry.cs` `LoadAsync`（行 63-85）从 `/run/gzctf-teamlab` 恢复（tmpfs，重启后丢失合理）
- `ObservationBatchSpool.cs` `_root = "/var/lib/gzctf/observations"`（行 31）
- `TeamLabPacketObserver.cs` 使用 SharpPcap，支持 Linux+Windows 多平台；`snap length` 可配 96-65535
- 无平台特定假设泄漏到抽象层

### 4.2 适配性 — 热路径有界、背压、缓存上限
- **验证通过**。
- `ObservationBatchSpool.cs`：32,768-item bounded channel（行 20-26），`FullMode = Wait` 提供背压；`LocalBuffer` 10,000 容量 FIFO；epoch 失效机制
- `RedisTeamLabTrafficIngestor.cs`：`MaxStreamLength = 250,000`；`AppendBatchAsync` 使用 capacity lock + length check；`localBuffer` 在 Redis 失败时回退
- `FlowAccumulator.cs`：`PriorityQueue` 驱逐，容量 128-1,000,000
- `TeamLabTrafficLocalBuffer.cs`：10,000 容量 FIFO，drop 计数
- `EndpointSensorChannelService.cs`：`MaxEventBytes = 16 * 1024` 绑定单行长度
- `TeamLabEventRecorder.cs`：trim 消息到 1024 字符
- 热路径全部有界，无 unbounded growth

### 4.3 安全边界 — HMAC、重放保护、token、lease
- **验证通过**。
- `EndpointSensorAuthenticator.cs`：
  - 行 60-61：`value.Sequence <= previousSequence` 重放拒绝
  - 行 62：`ObservedAt < now.AddMinutes(-10) || > now.AddMinutes(2)` 时间窗口
  - 行 80-82：`HMACSHA256.HashData` + `CryptographicOperations.FixedTimeEquals` 防时序攻击
  - 行 69-74：signature 长度 + hex 字符校验
- `TeamLabCaptureUploadService.cs`：
  - `FixedTimeEquals` 校验 upload token
  - Redis lease `teamlab:capture-upload:{segmentId}` 15 分钟
  - 校验 segment 状态 + SHA-256
- `InternalTeamLabCaptureUploadController.cs`：`[AllowAnonymous]` + bearer token + worker node header + SHA-256 header
- `OpenTeamLabTrafficController.cs`：`RequireRuntimeOwnerAsync` 鉴权运行时归属
- ASP.NET Core Data Protection：upload token 10min lifetime（Finding 4.8.3 中讨论）
- HMAC key 在 `EndpointSensorChannelService.Remove` 中显式 `ZeroMemory`（行 98）
- 无 secret 进入日志或 PCAP manifest（见不变量 #17）

## 五、链路覆盖结论

链路 4.8 审查覆盖了规格 3.8 节定义的全部功能模块：

| 模块 | 文件 | 覆盖 |
| --- | --- | --- |
| 流量采集 | TeamLabPcapService.cs, TeamLabPacketObserver.cs | ✓ |
| 路径关联 | TeamLabTrafficPathCorrelator.cs, TeamLabTrafficPath.cs | ✓ |
| fingerprint | PacketFingerprint.cs, EndpointSensorAuthenticator.cs | ✓ |
| Redis ingest | RedisTeamLabTrafficIngestor.cs, ITeamLabTrafficIngestor.cs | ✓ |
| PostgreSQL batch write | PostgresTeamLabTrafficBatchWriter.cs | ✓ |
| cursor 推进 | TeamLabTrafficApplicationService.cs, TeamLabTrafficPersistenceWorker.cs | ✓ |
| 本地缓冲 | TeamLabTrafficLocalBuffer.cs, ObservationBatchSpool.cs | ✓ |
| endpoint sensor | EndpointSensorChannelService.cs | ✓ |
| 观察点注册 | ObservationPointRegistry.cs, TeamLabRuntimeObservation.cs | ✓ |
| flow 聚合 | TeamLabTrafficFlowAggregate.cs, FlowAccumulator.cs | ✓ |
| PCAP 上传/工件存储 | TeamLabCaptureArtifactStore.cs, TeamLabCaptureUploadService.cs, PcapSegmentUploader.cs | ✓ |
| 抓取协调 | TeamLabCaptureCoordinator.cs, TeamLabCaptureCoordinatorWorker.cs | ✓ |
| API 端点 | OpenTeamLabTrafficController.cs, InternalTeamLabCaptureUploadController.cs | ✓ |
| 实体 | TeamLabTrafficCapture.cs, TeamLabTrafficObservation.cs, TeamLabRuntimeTraffic.cs | ✓ |
| 运行时操作 | TeamLabRuntimeOperationApplicationService.cs（SubmitCaptureStart/Stop） | ✓ |
| destroy 清理 | TeamLabNetworkService.cs（行 756-761） | ✓ |
| 事件记录 | TeamLabEventRecorder.cs | ✓ |

**findings 汇总**：3 项
- P0：0 项
- P1：0 项
- P2：1 项（Finding 4.8.1 — DeleteAsync 后 MonitorAsync 重建 state.json）
- P3：2 项（Finding 4.8.2 — SemaphoreSlim 释放竞态；Finding 4.8.3 — 上传 token 10 分钟对大文件不足）

**不变量验证**：
- #12 PostgreSQL 事实来源：通过
- #15 destroy 完整清理：部分违反（Finding 4.8.1）
- #16 失败保留相关性/错误码：通过（Finding 4.8.2 / 4.8.3 存在错误码失真场景，但属次要）
- #17 secrets 不进入 PCAP 元数据：通过

**链路功能验证**：7 项全部通过（方向保留、fingerprint 去重、cursor/Redis/PostgreSQL 幂等、路径关联置信度、capture 预算分配、PCAP manifest 字节可验证性）；清理完整性存在 1 项漏洞（Finding 4.8.1）。

**适配性与安全验证**：3 项全部通过（Linux 特定调用与平台分支、热路径有界与背压、HMAC/重放/token/lease 安全边界）。

**链路覆盖结论**：链路 4.8 功能完整、设计合理，主体实现符合规格要求；3 项 findings 均为局部缺陷，未发现阻塞性问题。建议优先处理 Finding 4.8.1（P2）以恢复不变量 #15 的完整性，Finding 4.8.2 与 4.8.3 可纳入下一迭代修复。
