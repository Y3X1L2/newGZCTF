# Agent 镜像下载容量守卫

## 目标与基线

节点下载前必须确认实际写入目录有空间，并让并发下载共享一份容量账本。基线为 `origin/main 2ba6e8d8eafac9f381a59e701357863db717754c`，任务分支 `codex/image-agent-storage-guard`，独立 worktree `C:/Users/Cloud/.codex/worktrees/image-agent-storage-guard/newGZCTF`。本任务不部署、不连接服务器、不修改来宾网络或主站调度。

## 实现边界

- `AgentStorageProbe` 在 Linux 64 位进程使用 `statvfs`，读取目标目录的有效可用字节和 filesystem ID。符号链接及 bind mount 指向同一文件系统时共用账本，images/runtime/Docker 不因目录不同而重复获得空间。未建立目录取最近现存父目录，测量失败明确拒绝，不能回退根盘的容量。Windows fallback 只服务开发测试；正式执行面为 Linux。
- `AgentImageStorageBudget` 在本机锁内测量、检查、登记将来新增的字节。写入完成后减少未来承诺，已落盘部分由真实剩余空间反映；退出和失败在实际 writer 的 `Dispose` 归还余量。预算没有持久化表或额外队列，重启后文件占用由文件系统重新测量。
- VM 和 bootstrap blob 使用同一个 `AgentImageDownloadWriter`。必须先得到预期大小或合法响应大小，校验续传范围；每个 80 KiB 数据块写前复核容量和大小上限。`.part` 续传只承诺尚未写入的字节，完成后的 rename 不申请第二份镜像空间。短响应保留可续传部分；长响应不能越过批准大小。body stream 有两小时期限，避免仅收到 HTTP headers 后无限等待。
- VM 缓存命中要求实际文件和正确 SHA-256，已声明大小也必须相符；完整且摘要正确的 `.part` 可以直接校验后 rename。不同 template ID 仍对应不同物理文件，singleflight key 加入目的文件身份，不能把另一 ID 的完成响应误当本 ID 已经存在。
- 复用现有 `ImageTransferSingleFlight`、`AgentResourceLock` 与 operation gate。HTTP caller 取消等待不会提前归还共享 writer 的空间；共享 writer 结束才归还。没有改变既有取消消费者语义，也没有实现自动删除残留 `.part`；残留占用始终包含在真实磁盘事实中。
- 反馈使用现有 Agent 错误 envelope：`Storage`、`image.storage_capacity_insufficient`、HTTP 507、`Retryable=true`，受控消息说明阶段、所需新增字节、实测可用量、并发承诺和安全余量。不打印地址中的 token、认证信息或文件内容。测量失败和文件系统变化分别是稳定错误码。

## Docker 是保守准入，VM 是分块守卫

Docker daemon 控制层下载和解包，当前接口不能预先给出可信的展开峰值，也没有活动 pull inventory。因此本期默认为每次缺失 Docker 镜像保留非零 8 GiB，节点内串行拉取，使用 daemon 返回的实际 data-root 测量，与同文件系统 VM/blob 共享账本，开始准入和成功后的实际余量检查；不可变 `@sha256:` 镜像已能 Inspect 时可跳过新下载预算，普通 tag 仍需拉取确认。

配置位于 `Agent:ImageStorage`：`SafetyMarginBytes` 默认 `1073741824`（1 GiB），`DockerPullBudgetBytes` 默认 `8589934592`（8 GiB），两者必须正数。该 8 GiB 是可调整的保守允许量，**不是层的精确展开上界、磁盘 quota 或实例增长预算**。本期没有逐字节拦截 Docker daemon 的写入，不能声称未知大镜像解包或外部 Docker 写入绝不会填满硬盘。

共享 pull 不因等待者断开而主动取消 daemon 请求，租约保持到 pull await 完成，再核验实际 image 存在。未知 Engine 传输失败或进程退出后，不能单凭 HTTP task 失败证明 daemon 的所有后台写者都已停止；强保证仍需后续的层预算或 daemon 存储 quota/可靠终态对账。不得把这项限定实现写成完备 Docker 峰值容量保证。

本机账本只包含下载 writer，**不包含主站尚未启动的 VM/Docker 长期增长承诺**；该部分由主站已有运行资源预留负责。目录保护也不能代替 PVE thin pool 的物理池容量门禁。

## 验证与接手

Agent Release 构建通过，0 警告/0 错误。定向 41/41 测试通过，覆盖并发不能超卖、同盘 VM/Docker 共账、分盘独立、已写字节结算、容量测量失败、换盘、取消 waiter 不提前释放、低空间拒绝、续传、错误范围、短/长响应、缓存命中、完整 `.part` 提升和 Docker daemon 错误响应不能被旧 tag 掩盖。未运行全量单测、集成与前端门禁，由集成分支统一执行。

另用本机隔离 Linux 容器真实验证：32 MiB tmpfs 的 images/runtime/docker/符号链接 fsid 相同，rootfs 不同，余量正确；同盘 20 MiB Docker 预算存在时另一个 20 MiB VM/blob 下载在写文件前拒绝，释放后 1 MiB 流实际写入成功。隔离 Docker 29.8.0 daemon 的 `/docker-data` 与 Agent 共享卷，超实测容量 pull 先拒绝且无本地镜像；保守预算允许的极小测试镜像实际 pull 和 Inspect 完成，正确 digest 缓存命中无需再次准入。

测试 harness、日志、极小 Registry fixture 只保存到仓库外 `D:/Work/YINYU-TeamLab-Image-Storage-Guard-20261008/`；本任务创建的容器、卷、网络已清理，原有 Docker 资源保留。未做平台双环境验收，也未修改 `.27/.31`。

无 EF migration、公开 HTTP DTO 或前端生成 API 改动。下一步将本提交与需求分发、引用回收提交集成，执行全量门禁，并在得到部署批准后记录两套环境新建/通信/销毁再建及低空间未选节点不写入的真实证据。

## 主站 VM 下载错误接线

第二个提交修正 `AgentClient.DownloadVmImageAsync` 与 `DownloadPreparedVmImageAsync`：非成功 HTTP 响应抛既有 `AgentClientException`，完整保留 Agent 的错误类别、代码、是否可重试、节点和 HTTP 状态。先前返回仅含 message 的 failed result，导致分发层再次包装为普通下载错误。节点不存在仍返回原来的 failed result，不改变该既有行为。

实际 HTTP fake handler 驱动两条方法，8/8 定向测试通过：507 容量错误和永久 size mismatch 的分类/重试策略都保留，节点缺失不发请求，成功响应的大小/摘要/验证事实保持。直接调用方为 `ImageDistributionService.ProcessClaimedAsync` 和 `AgentClient.CreateVmAsync`；前者已有 typed exception 分发记录接线，后者直接传播。

第三个提交在正常合并需求分发/引用回收分支后，补上 `EnsureVmTemplateOnNodeAsync` 和 `EnsureDockerImageOnNodeAsync` 等待失败分发记录的接线，抛既有 `AgentClientException` 并保留 record 的类别、代码、Retryable、Worker 和原错误消息；旧记录缺少类别/代码时仅使用保守的普通镜像传输失败 fallback，不能擅自宣布可重试。所选节点测试同时检查 runtime ticket 分类及 TeamLab failure projection 的代码与重试策略。相关分发、所选需求及 Agent HTTP 映射定向测试 38/38 通过。
