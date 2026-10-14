# 旧 VM 来源迁移候选（2026-10-09）

基线 `origin/main 240ea370`，分支 `codex/image-source-migration`，工作树 `C:/Users/Cloud/.codex/worktrees/image-agent-storage-guard/newGZCTF`。状态为 **IMPLEMENTED / VERIFIED 本地定向门禁 / NOT_RUN 生产迁移**。用户授权保留模板 1 与题目、练习绑定，迁移到独立镜像节点后移除旧 local 来源；其他旧模板退休由独立任务负责。本提交没有操作服务器、删除生产文件或改变 PVE 源机。

## 范围与流程

管理员使用 `POST /api/v1/image-templates/{templateId}/source-migrations`，带 `Idempotency-Key`，获得 202 operation；同一路径下 `GET /{operationId}` 读取真实阶段。Controller 只做授权和协议映射；Content service/store/handler 复用既有 `ApiOperation`、`ImageImportJob`、worker 租约和恢复，不新增业务表、迁移或队列。

1. 捕获原绝对路径、设备/文件身份、SHA-256、大小、固定 Registry 地址/仓库/tag 与 cleanup ID。只允许配置镜像根目录内的非数字 `.qcow2` 普通文件；禁止目录、numeric Agent cache、文件软链接或根目录外路径。`StagedPath=null`，不会进入旧上传暂存清理。
2. 校验原文件；如 Registry 尚无内容，使用既有上传能力补齐。完整读取 manifest 与 blob，核对 manifest 本体 SHA-256、单 layer 摘要/大小、blob 全量 SHA-256/大小，并检查前后 manifest 一致。
3. 在 PostgreSQL 有界事务和模板锁内，重新读取模板身份，创建既有 `VmPreparedArtifact` 固定来源记录，设置来源 Ready、清 `LocalFilePath`。仅改变来源字段，模板 ID、`Opaque/Managed`、DHCP/静态模式、实例凭据能力、远程访问与题目/练习绑定保持。`EvidenceDigest=null`：来源校验不能伪装为 Windows 来宾认证或 factory 认证。
4. 再次确认 DB 固定来源、Registry 完整字节、其他模板不再引用该文件、实际 qcow2 backing 与全部 libvirt 域不使用它。匹配捕获文件身份后先原子重命名到固定 cleanup checkpoint，再核对身份、字节与实际依赖，才删除。只处理这个捕获文件，保留 numeric cache、XML、其他文件和目录。
5. 崩溃或清理失败后，DB 和持久 job 是事实源；已切换来源也可使用新幂等键再提交，沿用原 cleanup ID。不会因新 operation ID 遗失隔离文件，也不会重新导入 Windows 或重写绑定。

## 删除前的观测要求

主站必须声明 `ImageSourceMigration:LocalAgentConfigurationPath` 为当前本机 Agent 实际 `appsettings.json`，`LocalAgentServiceName` 默认为 `gzctf-agent`。该字段是本机配置路径，不能填密码或 token。

检查实际运行 PID/cwd、配置文件身份与启动前时间，并从本机 Agent 配置读取明确的 `Kvm:ImageStoragePath` 和 `TeamLab:RuntimeStateRoot`。未知环境/命令行/额外配置覆盖、不可访问目录、目录别名越界、无法读取完整 backing/libvirt inventory 都保留旧文件。bind mount 的同设备/inode 别名参与使用检查；不会把硬编码默认目录当作实际运行事实。该能力是受限旧来源清理，不是通用主站执行面。

HTTP 408/429/5xx、连接/验证超时或暂时无法证明文件未使用，保留具体错误码并沿用既有最多 5 次重试。401/403、TLS 身份、字节/路径/文件身份不一致等拒绝自动重试。新的 `ApiOperationRetryableException` 只让现有 worker 保留用例错误码；其他 handler 的 generic/terminal/cancel 路径保持。

## 已执行验证

| 项目 | 结果 |
| --- | --- |
| Release 编译 | 定向测试构建主站与测试项目成功，0 error；既有依赖与测试分析器告警保留 |
| 来源/worker/固定来源下载单测 | **18/18**，含同字节替换文件保护、坏 hash/manifest/blob、重启 checkpoint、管理员边界、Opaque 与 Managed 冷缓存走固定 Registry、原 5 次重试和 lease 取消回归 |
| PostgreSQL/Testcontainers | **3/3**，含 5 题/4 练习绑定及 Windows 模式/凭据/远程访问保持，来源已提交但 backing 拒绝后的重启和新幂等键恢复，其他模板共享源时拒删，错误 Registry blob 不提交来源 |
| 真实 TestServer 内部 OpenAPI | 导出 **1/1**；随后 OpenAPI documentation **2/2**；新增内部路由已生成 `Api.ts`，未手写生成代码 |
| 生成前端类型 | `pnpm check` 通过 |
| 通用门禁 | `git diff --check` 通过 |

首次 PostgreSQL 尝试被未运行的 Docker 阻断；Daemon 恢复后，测试进程使用 `DOCKER_HOST=npipe://./pipe/dockerDesktopLinuxEngine` 完成真实 PostgreSQL 校验。没有用 mock 代替 PostgreSQL。全量后端、全量前端与真实 Linux 文件身份/Agent 根目录/普通 Windows 创建、联网、销毁验收交给组合候选与服务器发布任务；本地通过不能称生产迁移完成。

交叉审查发现 Linux 中源文件直接位于 image 根目录时，原子重命名后的缺失路径会解析成父目录本身，原边界条件误把 `parent == root` 拒绝为越界。已最小修正：只有缺失文件允许其实际父目录等于配置根目录，存在文件仍须实际位于根目录之下。新用例执行隔离文件恢复、删除完成后的重复删除；在现有 `aspnet:10.0` Linux 容器内以真实 libc `statx/realpath` 跑来源测试 **12/12**（0 skip），仓库和 runner 只读挂载、容器断网，所有文件操作仅在容器 `/tmp`。实际服务器 Agent 配置/backing 清理仍待生产验收。

## 发布与接手

生产写者在组合候选审查、完整备份与原子 release 切换后，先核对模板 1 的实际模式/凭据能力和全部绑定，再声明实际 Agent 配置路径，通过正常管理员 API 提交来源迁移。必须报告固定来源的真实 digest/size、旧源与 numeric cache 区别、完整 Registry 核验、清理结果和实际磁盘回收。完成普通 Windows 在无该模板 cache 节点上的正常创建/访问/销毁；不能仅以 operation succeeded 或进程 running 签收。

回退源码不应把 `LocalFilePath` 指回已删除文件；固定 Registry 来源与既有 `VmPreparedArtifact` 可由基线既有读取路径继续使用。如需要恢复旧文件，只能从已完整核验的固定 Registry 内容恢复并复核，再审查指针变更。新来源登记与旧模板退休分别进行；生产原始证据、发布物和受保护脚本保存在仓库外。
