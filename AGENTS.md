# YINYU 项目协作规范

本文件适用于整个仓库。开发者、Codex 和其他 AI 在开始任务前必须先阅读本文件。它只保存长期稳定的规则；会变化的提交、部署和进度信息维护在 `docs/development/current-state.md`。

## 1. 必读顺序

1. `AGENTS.md`
2. `docs/development/current-state.md`
3. `docs/README.md`
4. `README.md`
5. `docs/platform-commercialization-master-plan.md`
6. 与任务直接相关的模块文档

涉及架构、前端或部署时还必须阅读：

- 架构边界：`docs/commercialization/module-boundary-map.md`
- API 标准：`docs/commercialization/external-api-standard.md`
- 前端边界：`docs/commercialization/frontend-component-boundary.md`
- 前端视觉契约：`docs/commercialization/frontend-style-token-contract.md`
- vNext 设计与开发边界：`docs/yinyu-vnext-development-guardrails.md`
- 生产发布：`docs/operations/vnext-maintenance-window-rollout.md`

`docs/archive/` 只保存历史材料，不是当前设计或实现依据。历史计划中的路径、分支、测试数量和部署状态不得直接当作当前事实。

## 2. 事实优先级

发生冲突时按以下顺序判断：

1. 当前运行行为和真实请求/日志
2. 当前 `main` 源码与数据库迁移
3. 当前自动化测试
4. `docs/development/current-state.md`
5. 现行架构、契约和阶段文档
6. README、历史记录和归档文档

不能用旧文档推翻当前运行证据。发现文档过期时，在同一任务中修正文档或明确记录缺口。

## 3. 仓库与技术边界

- 主仓库：`https://github.com/Y3X1L2/newGZCTF.git`
- 稳定分支：`main`
- 后端：`.NET 10 / ASP.NET Core / EF Core / PostgreSQL / Redis`
- 前端：`React 19 / TypeScript / Vite / Mantine / pnpm`
- 执行面：`GZCTF.Agent / Docker / KVM / libvirt / Guacamole`
- 架构形态：模块化单体主站加独立 Agent 执行面

后端调用方向固定为：

```text
HTTP/Frontend -> Contracts -> Application -> Domain -> Infrastructure ports
Business Application -> Runtime Application -> Fleet/VM/TeamLab ports -> AgentClient -> Agent
```

约束：

- Controller 只处理协议、授权、用例调用和 HTTP 映射，不直接编排 Agent 命令。
- 跨模块读取使用公开 query contract，跨模块写入使用 application command。
- PostgreSQL 是业务和运行状态事实源；Redis 只承载缓存、协调和高频缓冲。
- `DeploymentQueueTicket` 是 Docker、VM、培训、AWDP 和 TeamLab 运行任务的统一事实，不建立第二套队列。
- Agent 只执行已经校验的本机操作，不读取比赛、课程、计分或权限实体。
- 运行恢复读取数据库当前事实和 Agent inventory，不从日志文本反推业务状态。
- 具体允许依赖以 `module-boundary-map.md` 为准。

## 4. 前端规则

当前正式前端位于 `src/GZCTF/ClientApp/src/vnext`。新增或重构页面必须：

- 使用 vNext 壳层、语义 Token、CSS Module 和 feature API adapter。
- 遵循 `Route -> feature controller/hook -> feature panel -> foundation component` 依赖方向。
- 页面不直接访问生成 API 文件，不把请求、DTO 转换和视觉状态堆在一个大组件中。
- 通用展示组件不得包含业务请求、路由 ID 或权限逻辑。
- 不新增 `YinyuRefinement.css`、`YinyuTheme.css` 或无作用域全局选择器来覆盖页面。
- 不通过加载旧页面或旧壳层填补未实现路由；接口缺失时显示真实空态或待建设状态。
- 不伪造统计、通知、运行实例、活动或成功结果。
- 支持日间/夜间、键盘操作和 `prefers-reduced-motion`。
- 在 390、1366、1920、2560 像素宽度检查重叠、横向滚动和布局抽动。
- 生成代码不可手工修改；契约变化后重新生成并审核差异。

## 5. 后端与数据规则

- 优先扩展现有模块 contract 和 application service，不从 Controller 直接穿透其他模块的 DbSet。
- EF 实体、迁移、模型快照和读写路径必须在同一变更中闭环。
- 不修改或删除历史 migration 来掩盖当前模型问题；新迁移必须支持生产备份后的前向升级。
- Flag、token、密码、Cookie、WireGuard 私钥、Registry 凭据和完整 user-data 不得写入日志、测试快照、文档或提交。
- 动态 Flag 必须按实例隔离；管理员预览值不能进入正式实例。
- 镜像模板主状态和节点分发状态必须分开表达，失败重试必须遵守 `Retryable`。
- Docker 与 KVM 能力独立判断；缺少 KVM 不能阻断 Docker 调度。

## 6. Git 工作流

任务开始时执行：

```powershell
git status --short --branch
git fetch origin --prune
git log -5 --oneline --decorate
```

规则：

- 从最新 `origin/main` 创建 `codex/<task-name>` 分支；小型紧急修复也要保持提交边界清晰。
- 不覆盖或回滚他人的未提交改动，不使用 force push，不改写已共享历史。
- 一个提交只表达一个可验证意图；源码、测试和必要文档一起提交。
- 合并前检查远端是否前进，使用快进、正常 merge 或经过审查的 rebase，不覆盖远端。
- 禁止把本地归档、发布制品、数据库副本、镜像、凭据和测试 Cookie 提交到仓库。

## 7. 验证门禁

根据影响范围选择最小充分测试；共享契约、调度、计分、迁移和认证变更需要扩大验证。

文档与通用门禁：

```powershell
git diff --check
```

前端：

```powershell
cd src/GZCTF/ClientApp
pnpm validate:locales
pnpm lint:check
pnpm check
pnpm check:architecture
pnpm test
pnpm build
```

后端：

```powershell
dotnet build src/GZCTF.slnx -c Release
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release
```

说明：

- 小改动可以先跑定向测试，但最终报告必须明确哪些全量测试未执行。
- 数据迁移必须在 PostgreSQL Testcontainers 或生产数据库副本上验证升级和恢复。
- Docker、VM、镜像、Agent、TeamLab、AWDP 和公网入口不能只靠 mock 判断完成；真实基础设施验收单独记录。
- AWDP 自动操作可能触发本机安全软件时，保留自动化代码门禁，把真实攻击/修补流程交给授权测试人员按 `docs/yinyu-awdp-manual-acceptance.md` 执行。

## 8. 部署规则

- 未收到明确部署指令时，不修改生产服务器、数据库、节点、Registry 或公网网关。
- 发布前确认提交已推送、数据库已备份、发布物可识别、回滚目录存在。
- 使用独立 release 目录和原子软链接切换，不原地覆盖运行目录。
- 禁止使用 `scripts/deploy.sh`、`scripts/deploy-server.py` 和 `scripts/one-click-deploy.*` 进行生产发布；这些是历史脚本。
- 生产步骤以 `docs/operations/vnext-maintenance-window-rollout.md` 为准。
- 部署后检查服务状态、首页、关键 API、日志、数据库迁移、节点、队列、镜像和至少一条真实业务链路。
- 不在仓库中保存服务器密码、IAM token、浏览器 Cookie 或私钥。

## 9. 会话与交接协议

新任务开始：

1. 读取本文件和 `current-state.md`。
2. 核对 Git、远端和用户最新要求，不延续聊天中的过时假设。
3. 对照当前代码验证任务涉及的真实入口、API 和测试。
4. 大任务先建立可落盘、可更新的计划文档；小修复直接实现并验证。

任务结束：

1. 记录最终提交、改动范围和测试结果。
2. 如部署，记录环境、发布物身份、备份和冒烟结果。
3. 如项目状态发生变化，更新 `docs/development/current-state.md`。
4. 未完成事项写入现有缺口文档或任务交接记录，不只留在聊天中。
5. 使用 `docs/development/task-handoff-template.md` 交接跨会话或跨人员工作。

`current-state.md` 只记录已经证实的当前事实。不要写计划中的功能为“已完成”，不要复制整段聊天记录，也不要保存凭据。
