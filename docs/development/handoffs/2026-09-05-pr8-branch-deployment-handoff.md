# newGZCTF 分支交付与服务器验证交接

更新时间：2026-09-07。范围仅限 newGZCTF；隔离部署与生产切换已完成，交付方式已改为 PR #9 审查。

## 任务目标

- 用户已明确此前的“PR2”实际指 PR #8，不再需要确认编号。
- 用户此前要求取消 PR #8、仅交付分支；2026-09-07 改为提交 PR，已从现有 fork 分支创建 PR #9，不合并 main。
- 已对比该分支与 `10.24.0.27` 生产版本，并在无路由隔离环境完成完整发布包、空库和生产备份副本验证；修复只推送分支。
- PR #8 未重开，PR #9 已创建但未合并；此前经用户授权完成的生产切换保留备份与回退 release，本轮不重新部署。

## 基线与交付

| 项目 | 已核对事实 |
| --- | --- |
| 唯一活动 worktree | `D:\newGZCTF` |
| 原 PR #8 代码提交 | `5047a8afe31aa77f357d630b206ec5267bb2049b` |
| 原分支 | `codex/practice-consolidation`，保留，不删除 |
| 当前分支 | `codex/practice-deployment-validation`，直接继承原 PR 提交，不重写历史 |
| 隔离运行候选 | `9eef8ac12c626672081e81fadbde39946e7d2237` |
| 已推送远端 | `fork`，即 `csc-dsc/newGZCTF`；PR 从该 fork 分支发往主仓库 |
| 主仓库 main | `Y3X1L2/newGZCTF`，核对时为 `bbd5a5d4da8488ad4c32c7bf49523f3136e63831` |
| PR #8 | 已关闭，`closed_at=2026-09-05T14:56:57Z`，`merged_at=null` |
| PR #9 | https://github.com/Y3X1L2/newGZCTF/pull/9，目标 `main`，未合并 |
| 主仓库写权限 | API 返回 `push=false`；2026-09-07 HTTPS dry-run 仍以 403 拒绝 `csc-dsc`，因此使用 fork PR，不声称已在主仓库创建源分支 |
| 交接提交 | 在上述代码基线上仅改文档；精确 SHA 通过 `git log -1` 获取，并核对远端同名分支 |

- 分支：https://github.com/csc-dsc/newGZCTF/tree/codex/practice-deployment-validation
- 当前 PR：https://github.com/Y3X1L2/newGZCTF/pull/9
- 已关闭 PR：https://github.com/Y3X1L2/newGZCTF/pull/8
- 用户已确认改用 PR 交付；后续如需把源分支移至主仓库，仍须先确认写权限，不 force push，不擅自合并或重复创建 PR。
- 原 #6/#7 及本地资料保留在轻量归档引用与 `.local-notes`，无需克隆或恢复旧 worktree。

## 当前状态

状态：`deployed`。隔离验证与 `10.24.0.27` 生产切换已完成，真实登录和 Docker 实例仍为 `NOT_RUN`。

- 已完成：生产只读基线、PR 关闭与分支保存、11 项集成失败闭环、完整 CI、同 SHA 发布包、严格隔离空库与生产备份副本验证、资源清理和生产复核。
- 尚未完成：真实登录、Docker/KVM/TeamLab/AWDP、公网入口和回滚演练；浏览器附着受现有 Edge 设置阻断，`.31` 节点为发布前既有离线状态。
- 隔离测试曾创建 `/opt/gzctf-validation`、两个测试数据库、PostgreSQL/Redis 容器、一个 Docker volume 和降权主站进程；结束时均已删除，根盘恢复约 52.0 GB 可用。
- 生产 `/opt/gzctf/publish` 已原子切换，主站与本机 Agent 已重启；生产 PostgreSQL/Redis 未重启，bundle 未应用 migration，公网入口未改配置。

## 与生产代码的初步对比

生产提交 `3e5526dc1ce336ac5545faacd49a9c0d1ec7ab58` 至原 PR 代码 `5047a8a`：
`49 files changed, 2435 insertions(+), 264 deletions(-)`。这是源码树差异，不是完整运行兼容性证明。

主要增量：

- 练习管理：停用题可管理，容器导入字段、Ready/Docker 模板解析、附件 FileHash 绑定、编辑时清理旧运行字段。
- Content：资产上传与读取 API、Content-Digest、幂等键锁、Token 资源授权优先于上传者身份；未开放外部删除接口。
- Blob：真实业务引用与事务锁保护，覆盖练习附件、头像、海报、课程封面；保留有效上传授权历史。闲置字节自动回收未实现。
- TeamLab：测试目录/能力探测隔离，明确覆盖 nftables/iptables；不改变生产默认行为。
- CI/OpenAPI：生成上传二进制契约、补回归测试；覆盖率仅排除生成的迁移 Designer/Snapshot，不排除手写 Up/Down。

此增量没有修改 migration、Designer 或 ModelSnapshot。创建者产品能力本次未重新启用。
`20260815012026_AddExerciseCreatorTracking` 的历史恢复已在 main，原始提交
`c6a2b7f4b5637f5622cfa6bdb42624d5242a0c80`；不得另造 migration ID 或覆盖历史快照。
两条旧 Theory migration 来源仍未恢复，详见 `current-state.md`，不得伪造。

## 验证证据

CI：https://github.com/Y3X1L2/newGZCTF/actions/runs/33960930409
原提交 `5047a8a` 的 11 项失败已在后续分支提交中闭环。最终独立分支运行证据：
https://github.com/csc-dsc/newGZCTF/actions/runs/33979724855

| 验证项 | 结果 |
| --- | --- |
| 前端门禁 | 280/280 通过；locale、lint、strict TypeScript、架构、生产构建和制品预算通过 |
| 后端单元测试 | 971/971 通过 |
| 后端集成测试 | 275/275 通过 |
| 数据/API 门禁 | migration model、7 组查询计划、OpenAPI 快照与向后兼容通过 |
| 完整发布包 | 通过；同 SHA 前端 artifact、主站、efbundle、Agent、传感器和 Supervisor 完整构建 |
| 服务器隔离验收 | 空库与生产备份副本通过受支持链路；真实执行面项目见剩余限制 |

原 11 项失败闭环：

1. 旧 `/instance-credentials` 用例已改为现行 `/remote-access` 合同，并覆盖所有权、RDP/SSH/容器类型和必填字段。
2. TeamLab OpenAPI 门禁已区分同步幂等资源写与持久化异步 operation，并按结构检查敏感字段、保留公开 editor layout 合同。
3. 图片 GET/DELETE 的 500 已由 Linux CI 证实为测试工厂未隔离 `KvmSettings:ImageStoragePath`；测试现使用唯一临时目录。

## 隔离部署结果

- release：`practice-validation-9eef8ac12c626672081e81fadbde39946e7d2237`。
- tar.gz：258,732,482 bytes，SHA-256
  `3c3a2161c9589eb6d4f6f75e0eb4d6b826c1079181ec8e905d526d6303a4ce87`。
- 外部 manifest：SHA-256
  `a004783a7a06a991bb576de42cb09d2267afeca05d9200b1ee10bd31fcc2a70c`；
  372 个列出文件全部按长度和 SHA-256 复核，archive 仅额外包含 manifest 自身。
- 首轮 manifest 漏掉 `wwwroot/.keep`，服务器严格校验拒绝后已修复
  `build-gzctf-release.ps1` 使用 `Get-ChildItem -Force`；失败 release 已删除。
- 空库 bundle 成功生成 132 条可发现 migration，Exercise、Theory、TeamLab 表存在；
  首页、health、OpenAPI 均 200。注册/登录、匿名 401、受限 token 403、资产上传
  201 幂等复用、请求冲突 409、资产读取、练习导入 202 幂等复用、operation
  `Succeeded`、练习读取和附件内容摘要均通过。
- 生产备份 `agent-sync-pre-0a3e1c63-20260904T080316Z` 重新通过全部
  `SHA256SUMS`，dump list 为 2,041 条；副本 bundle 报告 `No migrations were
  applied`，迁移保持 134/head `20260816192540_TeamLabCapabilityClosure`，核心计数
  前后不变。
- 主站以 `www-data` 在 PostgreSQL 容器的 `--network none` namespace 中运行，
  路由数 0，未发布 18080/18081，且无 Docker、libvirt 或 KVM 文件描述符。
  最小 Ubuntu 容器缺 ICU，未安装软件；实际主站复用宿主已安装 runtime/ICU，
  进入 network namespace 后立即降权并启用 `no-new-privs`。
- 依赖启动顺序已实测：Redis 必须先 ready，随后主站约 16 秒达到 health 200。
  初次反序启动形成的等待进程已停止，不属于最终通过路径。
- 严格隔离的生产副本上 `/api/Exercise` 在 Controller 构造阶段初始化
  `DockerProvider`，因故意不提供 Docker socket 返回 500；因此既有练习列表和
  真实容器实例为 `NOT_RUN`，不能拿此结果冒充生产失败或成功。外部 Exercise
  内容链路已在空库通过；真实运行链需独立 Docker 执行面。
- 清理后重新核对生产：release `3e5526dc1ce336ac5545faacd49a9c0d1ec7ab58`、
  主站 PID 2692、Agent PID 2691、两者 NRestarts 0，首页/health/OpenAPI 200，
  生产 migration 134、用户 172、练习 605、文件 217、镜像 456，均与测试前一致。

## 生产切换结果

- 用户在隔离验收完成后明确授权正式切换；运行候选仍为已完整验证的
  `9eef8ac12c626672081e81fadbde39946e7d2237`，分支后续 tip 只含 CI 清理和文档。
- 维护前活动票据、容器、VM、TeamLab runtime 均为 0；有 1 场比赛处于活动时间，
  因此先完成候选下载和验签，再开始服务停机。
- 发布前备份：
  `/opt/gzctf/backups/practice-deployment-pre-9eef8ac-20260906T032027Z`，总计
  1,359,448,361 bytes。数据库 dump 776,051,891 bytes、2,063 个 catalog 条目，
  SHA-256 `fa62f3473dc3693fb23a1be4ae1c0285e6800289b3a2670160b6c9904d26284d`；
  `SHA256SUMS` 全部通过，catalog 包含 DataProtectionKeys。旧 release、共享文件、
  schema、迁移历史、核心计数、配置、systemd 和 Nginx 均在服务器本机保存。
- bundle 未应用 migration；数据库保持 134/head
  `20260816192540_TeamLabCapabilityClosure`，切换前后核心计数完全一致。
- `/opt/gzctf/publish` 当前指向
  `/opt/gzctf/releases/practice-validation-9eef8ac12c626672081e81fadbde39946e7d2237/publish`；
  `/opt/gzctf/publish.previous` 指向原 `3e5526dc` release。服务停机从
  `2026-09-06T03:20:27Z` 至候选健康 `03:26:46Z`，约 6 分 19 秒。
- 主站 PID 36118、Agent PID 36120，均 active/running、NRestarts 0。首页、登录页、
  `/practice`、配置、OpenAPI、API docs、health、metrics、SPA 入口和共享附件内容
  SHA-256 通过；发布后 journal 无 error。
- 发布后核心计数仍为用户 172、战队 76、赛事 22、赛题 110、课程 29、练习 605、
  理论试卷 4、AWDP 服务 10、文件 217、镜像 456；活动队列和镜像工作为 0。
- Local Server 与 `worker-10.24.0.30` 在线可调度；`worker-10.24.0.31` 心跳已中断
  约 25.9 小时，早于发布。远端 Worker Agent 未同步，本轮只更新 10.24.0.27。
- browser-harness 原始错误要求在现有 Edge 的 `chrome://inspect/#remote-debugging`
  允许 remote debugging；未改用 Playwright/CDP，也未新开浏览器。因此真实登录和
  Docker 练习实例没有执行，继续标记 `NOT_RUN`。

## 服务器只读基线

以下表格是生产切换前基线；最终生产事实见上节。

| 项目 | 实测 |
| --- | --- |
| SSH | `whoami@10.24.0.27:22`，hostname `whoami`，sudo 成功 |
| 活动目录 | `/opt/gzctf/releases/docker-provisioning-inventory-3e5526dc-20260904T093342Z/publish` |
| 活动链接 | `/opt/gzctf/publish` 指向上行；`publish/files` 指向 `/opt/gzctf/shared/files` |
| manifest | gitCommit `3e5526dc1ce336ac5545faacd49a9c0d1ec7ab58`，994 个文件 |
| GZCTF.dll SHA-256 | `c85a1e35822fb31e009927591441cc218b158ff6006a1dd099f8db6ad1f6241c`，与 manifest 一致；本次未声称逐一复核其余 993 个文件 |
| 服务 | `gzctf.service` PID 2692、`gzctf-agent.service` PID 2691，均 active/running、NRestarts 0 |
| 数据库 | 134 条 migration，head `20260816192540_TeamLabCapabilityClosure`；练习 605、ImageTemplate 456 |
| 数据库大小 | 28,145,335,319 bytes，约 26.2 GiB |
| 资源 | 根盘剩余约 52.0 GB；内存约 31GB、测试前可用约 29GB |
| .NET | SDK 10.0.300；ASP.NET/Core runtime 10.0.8；生产 runtimeconfig 请求 10.0.0 |
| 基础依赖 | Docker 存在；生产 PostgreSQL 16 / Redis 7 容器运行 |
| 可用镜像 | 已缓存 postgres:16-alpine、redis:7-alpine、ubuntu:22.04、busybox:latest；未发现 dotnet 基础镜像 |
| 构建缺口 | 未找到 Node/pnpm 或常见用户 NuGet cache，不能假设服务器可离线构建 |

连接使用已有 known_hosts；本机现有 Paramiko 可用。认证材料只从本地私有来源读取，
见 `.local-notes/README.md` 的连接说明，不提交或打印密码、完整连接串、数据库或 Cookie。

## 隔离验证方案与禁区

1. 不直接运行 `deploy-gzctf-release.py` 或 `activate-gzctf-release.sh` 做旁路测试。它们会停生产服务、替换 `/usr/local/bin/gzctf-agent`、迁移所复制配置指向的数据库并切换链接；仅修改部署根目录不足以隔离。
2. 主站启动即写库：`EntityConfigurationProvider` 和 `PrelaunchHelper` 自动迁移/初始化，多个 Runtime/API/Webhook Worker 无条件启动。未找到全局关闭开关；`Agent:LocalNodeSchedulable=false` 不是安全模式。
3. 使用独立 PostgreSQL、Redis、files、临时文件、日志与 DataProtection 目录；不复用生产配置、数据目录、Redis 或节点。先空库，再评估服务器内生产副本升级，不下载生产数据库到本地。
4. 数据库约 26.2 GiB 而余量约 49G，必须先预算 dump、恢复、WAL 和制品空间，不能直接执行整库克隆。
5. 可研究单独 `--network none` namespace，让测试 DB/Redis/应用共享 loopback，服务器用 `nsenter` 访问。禁止挂载 Docker/libvirt socket、KVM、生产目录，禁止 host network 或 privileged；必须实测无法连接原节点/Registry。单独 `--internal` 网络不自动等于无宿主访问。
6. 显式隔离 `ConnectionStrings:Database/Storage`、Redis 配置/前缀和 Registry；空 Registry 会回落生产 `10.24.0.28:5000`。禁用 Nginx 配置同步与 Portal SSO；克隆库需评估原加密配置兼容性，不能输出密钥。
7. 真正监听配置为 `ServerPort`/`MetricPort`；`/healthz` 在 MetricPort（默认 3001），不是 8080。空库完整 Web 验收不等于 Docker/KVM/TeamLab 实际执行链通过。
8. 构建入口为 `scripts/deployment/build-gzctf-release.ps1`，输出主站、efbundle、Agent 和相关组件及清单。默认 `FrontendBuildMode=Source` 可能自动安装依赖；未获安装许可前，应使用同提交前端 artifact 和 `FrontendBuildMode=Artifact`，不能用旧前端或 Skip 冒充完整包。

## 后续工作边界

1. 本次隔离部署和生产切换无需继续；重新开展时先核对分支 tip、CI run、生产 SHA 和服务器资源，不能复用聊天中的临时值。
2. 生产已切到 `9eef8ac`；回退时使用 `/opt/gzctf/publish.previous` 和本次新鲜备份，不执行 EF Down 或手改 migration history。
3. 若要在不接触生产 Docker 的条件下签收内部 `/api/Exercise`，必须提供独立 Docker 执行面，或另立任务解除只读列表 Controller 对 `DockerProvider` 构造副作用的依赖。
4. `20260802023000_RemoveDestroyedTeamLabUdpMappings.cs` 的迁移注册缺口必须作为独立 migration reconciliation 任务处理；不得补同 ID 的猜测 Designer，也不得在生产手改 history。
5. 当前交付入口为 PR #9；不重开 PR #8、不重复创建 PR、不擅自合并 main。后续生产操作必须再次获得明确授权。

## 本地工具与资料

- .NET SDK：`D:\tools\dotnet-sdk-10\dotnet.exe`；Python/Paramiko：`D:\Python\python.exe`；GitHub CLI：`D:\Github\gh.exe`。
- 既有前端依赖位于 `src/GZCTF/ClientApp`；生产 NuGet 依赖在用户缓存，测试依赖不完整，不得静默 restore/install。
- `.local-notes/legacy-20260905` 保留原 12 个文件和本地资料；`.local-notes/contract-export` 是离线 OpenAPI 工具，不是第二个 worktree。清理曾被执行策略拦截，不换工具绕过。
- GitHub SSH 推送使用已配置的 `fork-ssh` 与本机既有 SSH key。API 走旧代理曾反复 EOF，临时移除进程代理后成功关闭 PR；不要修改系统代理或全局 SSH 配置。
- 新会话只承接本文件所列 newGZCTF 工作，不承接此前其他仓库、软件配置、磁盘清理或其他服务器任务。
