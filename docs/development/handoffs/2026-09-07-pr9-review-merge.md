# PR #9 独立审计、修复与合并交接

更新时间：2026-09-07

## 1. 任务与边界

- 目标：独立审计 `Y3X1L2/newGZCTF#9`，修复阻断问题，完成必要验证，并在候选可信后正常合并到 `main`。
- 授权：允许只读核对生产状态；允许在本独立 worktree 修改、测试、提交、推送和合并。
- 禁止：不重新部署或重启生产，不修改生产数据库、节点、Registry、网关或现有实例，不启用 TeamLab/Fabric。
- 本任务 worktree：`C:\Users\Cloud\.codex\worktrees\611b\newGZCTF`。
- 审查分支：`codex/review-pr9`，从任务开始时最新 `origin/main` 创建。

## 2. 已核验基线

| 对象 | 核验事实 |
| --- | --- |
| `origin/main` | `bbd5a5d4da8488ad4c32c7bf49523f3136e63831` |
| PR #9 head | `bd1e546eba502f4a129eab521b8d7302b55fd326`，固定为本地 `refs/review/pr9` |
| PR 状态 | 2026-09-07 核验为 `OPEN`、`MERGEABLE`，base 为上述 `origin/main` |
| CI | GitHub Actions run `34089009136` 的 `Backend and contract quality` 与 `Frontend quality and artifact` 均针对当前 head 成功 |
| 差异范围 | 相对 `origin/main` 为 56 个文件，约新增 2779 行、删除 373 行 |
| 本机 Docker | Docker Desktop Server 29.5.2 正常响应 |
| 本机代理 | `127.0.0.1:10808` TCP 可达；未修改全局代理 |

## 3. 审计阶段

- [x] 建立独立 worktree、抓取最新主干和 PR head、固定审查引用。
- [x] 核验 PR 元数据、当前 head、base、mergeability 和同候选 CI。
- [x] 读取现行架构、API、前端、TeamLab、发布规范及 PR 专属交接。
- [x] 只读核对生产服务、公开前端制品、OpenAPI 和 Worker 可达性；受 sudo 保护的软链接、manifest、二进制摘要和数据库 migration 未能重新读取，保留为明确限制。
- [x] 审计练习管理、Content 资产授权、Blob 引用安全、Agent/TeamLab、CI 与历史修复保留。
- [x] 完成必要修复与回归测试，形成最终合并候选。
- [x] 合并前重新核对 PR head 与 `origin/main`，推送并确认 GitHub PR 真实状态。
- [x] 更新 `current-state.md`，记录生产运行差异与未验收项。

## 4. 架构与功能结论

- 练习管理新增教师专用列表，停用公共题可管理且学生列表规则不变；批量导入保留容器运行字段，模板必须为 `Ready/Docker` 且解析到 Registry 引用；本地附件以 `FileHash` 绑定，Flag ID 和同 hash 附件更新保持稳定。
- Content 新增 `assets:read/write`、外部上传/描述接口、`Content-Digest` 校验和 `(token, route, key)` 幂等；资源限制先于上传者身份回退，相同键不同内容冲突，相同内容并发重试不重复增加引用。
- Blob 删除不再只信任旧计数，覆盖 Attachment、头像、战队/赛事海报、课程封面、课程资源/视频和 writeup 的真实引用；PostgreSQL hash advisory lock、文件行锁和事务提交顺序避免共享文件误删。物理存储失败只可能留下无引用对象，不会让已提交业务引用指向缺失字节；自动闲置对象 GC 仍未实现。
- Agent 改动只增加可测试的状态根目录和命令能力注入，默认仍为 `/run/gzctf-teamlab`，防火墙仍优先真实 `nft`、否则 `iptables`；没有修改 Agent HTTP contract 或主站协议，未同步远端 Agent 可继续兼容。
- CI 没有跳过测试或排除手写 migration；覆盖率只排除生成的 Designer/Snapshot。旧失效的 `/instance-credentials` 测试改为当前 `/remote-access` 合同，同时保留所有权、类型和必填字段断言。
- PR 相对 `main` 未修改 migration、Designer 或 ModelSnapshot；`2da805c4`、`77ae1757`、`0a3e1c63` 均为 PR head 祖先，队列独立执行、上下文传播、镜像清理退避、Agent 分阶段同步和 Docker inventory 标签修复均保留。

## 5. 审计发现与处理

1. **阻断，已修复**：容器题改为附件题时，原候选只在前端清除部分字段，服务端仍可接受并持久化 `ContainerImage`、资源限制、端口、`NetworkMode` 和 `FlagTemplate` 等残留。提交 `3e4bd99f` 在 application service 统一归一化全部运行绑定，并补内部管理与外部预校验回归；前端同步清理所有隐藏字段。
2. **中等，已修复**：TeamLab OpenAPI 敏感字段测试从整段 schema 扫描弱化为顶层 properties 检查，嵌套泄漏可能绕过。提交 `51c2884f` 恢复递归覆盖 `runtimeResourceId`、`protectedDownloadToken`、`protectedSecret`，同时允许公开合同中有意提供的脱敏 `lastError`。
3. **低，已修复**：重命名后的 `ImageTemplateRemoteAccessTests.cs` 混入 CR 字符，最终差异无法通过 `git diff --check`。提交 `51c2884f` 规范化行尾。
4. **非阻断既有风险**：`SSH.NET 2025.1.0` 命中高危公告 `GHSA-q939-rpr3-3284`，修复版本为 `2026.0.0`。漏洞影响 `ScpClient` 递归下载；当前主站只搜索到 `SshClient` 使用，未调用 `ScpClient`，且该依赖版本不由 PR #9 引入。仍应以独立依赖升级任务处理。
5. **非阻断既有缺口**：Blob 安全策略会保留已解除引用的对象字节，尚无自动 GC；这避免事务回滚后附件 404，但需要后续实现可审计、按真实引用复核的闲置对象回收。

## 6. 验证记录

| 验证项 | 结果 |
| --- | --- |
| PR 原 head CI | run `34089009136`：后端 build、971 单元、275 集成、migration model、7 组查询计划、OpenAPI 向后兼容通过；前端 87 文件/280 项及制品通过 |
| 定向单元 | 114/114，通过；覆盖练习、资产、Blob、TeamLab nftables/iptables 和发布脚本 |
| 定向 PostgreSQL/OpenAPI | 16/16，通过；含 4 路并发同键资产上传，只有 1 个 operation、1 个写入者和 `ReferenceCount=1` |
| Release build | `dotnet build src/GZCTF.slnx -c Release`：0 error，通过 |
| 全量单元 | 973/973，0 skipped，通过 |
| 全量集成 | 276/276，0 skipped，通过；测试后无 Testcontainers 容器残留 |
| 前端完整门禁 | locale、lint、strict TypeScript、架构、87 文件/280 项测试、Vite build、220 文件制品预算全部通过 |
| 差异检查 | `git diff --check origin/main..HEAD` 通过；无 migration/Designer/Snapshot 差异 |
| main CI | push run `34094573626` 针对 `51c2884f` 完整成功：后端、单元、集成、migration model、7 组查询计划、OpenAPI 和前端 artifact 均通过 |

## 7. 合并与生产关系

- GitHub 于 `2026-09-07T07:15:18Z` 将 PR #9 识别为 `MERGED`，merge commit 为 `c615e61d8f1461dcf6d199ff00d61507bc76a29f`；修复提交 `3e4bd99f`、门禁提交 `51c2884f` 随同推入 `main`。
- 生产没有重新部署或重启。`10.24.0.27` 的主站 PID 36118、本机 Agent PID 36120 均从 2026-09-06 发布窗口持续 `active/running`、`NRestarts=0`。
- 生产首页引用的主 bundle、CSS、API client 和图标 SHA-256 与 `9eef8ac` 的既有 CI artifact 全部一致；运行 OpenAPI 为 JSON、83 条路径，包含 assets 和 exercises import，规范化后与 `9eef8ac` 快照完全相同。PR 从 `9eef8ac` 到原 head 没有运行源码差异，因此未发现公开制品热替换证据。
- `/healthz` 返回 HTTP 200、`text/plain`，但正文为 `Degraded`，不能写成健康。`/api/Exercise` 返回 401 JSON，排除了 SPA fallback 假阳性。由于无非交互 sudo，活动软链接、release manifest、主站/Agent 二进制摘要和数据库 migration history 未在本任务重新读取；2026-09-06 的特权核验仅作为历史证据保留。
- `worker-10.24.0.30:5001/api/status` 返回 401 JSON，证明 Agent HTTP 端点可达；`.31:5001` 连接失败，延续发布前既有离线状态。未登录远端节点、未同步 Agent、未启用 TeamLab/Fabric。
- 最终 `main` 比生产运行代码多 `3e4bd99f` 的练习服务端/前端修复及 `51c2884f` 的测试门禁，因此源码已合并不等于服务器已修复。若发布，必须从最终 `main` 构建新 release，按维护窗口手册重新做备份、manifest/二进制校验、原子切换和业务冒烟。

## 8. 未验收项

- 未执行生产登录、练习写操作、真实 Docker 练习实例创建/入口/Flag/销毁、KVM、TeamLab、AWDP、公网入口或回滚演练。
- 未读取或修改生产数据库、Redis、Registry、节点、现有实例和 203 网关；未访问 9091/18080。
- `.31` Worker 的离线根因和远端 Agent 版本仍需独立处理；不能归因于 PR #9。
- 主站 `Degraded` health 的具体组件需有 sudo/受控诊断权限后从结构化 health 或日志确认；结合已知 134 条历史记录与 132 条可发现 migration，数据库 historical migration 降级是可能原因，但本任务没有把推断写成事实。
