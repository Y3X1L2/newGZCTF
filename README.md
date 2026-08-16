# YINYU 安全综合演练平台

YINYU 是基于 GZCTF 演进的安全综合演练平台，面向 CTF 竞赛、AWDP 攻防、理论考试、培训课程、动态靶场和多节点运行环境。仓库同时维护平台主站、Web 客户端、节点 Agent、数据库迁移、测试和运维资料。

## 平台能力

| 领域 | 主要能力 | 当前状态 |
| --- | --- | --- |
| CTF | 赛事、队伍、题目、附件、静态/动态 Flag、Docker、KVM/Windows VM、计分与榜单 | 已投入使用 |
| 理论考试 | 题库、组卷、单选/多选/判断、草稿、最终提交、成绩与答题回顾 | 已投入使用 |
| 培训课程 | 课程、教师、报名、章节、资源、实验、课后理论、进度与学员详情 | 已投入使用 |
| AWDP | 服务、轮次、Checker、攻击、修补、重置、恢复和态势数据 | 已实现，真实流程按专项手册验收 |
| 节点与镜像 | Agent 注册、Docker/KVM 能力、镜像导入与分发、多节点调度、容量和恢复 | 已投入使用 |
| TeamLab | 拓扑、发布、计划、runtime、组网、流量、抓包与受审计远程操作 | 核心链路已进入主线，规模与故障场景继续验收 |
| 自主练习 | 独立题库、筛选、附件、Flag、容器实例、提交、统计与后台管理 | 已进入主线并完成本地核心验收，待生产发布验收 |

准确的当前提交、环境、阶段和已知缺口见 [当前开发状态](docs/development/current-state.md)。

## 系统组成

```text
Browser
  -> GZCTF 主站（ASP.NET Core + React）
       -> PostgreSQL：业务与运行状态事实
       -> Redis：缓存、租约、协调和高频缓冲
       -> Registry / 镜像存储：Docker 与 VM 模板主副本
       -> GZCTF.Agent：WorkerNode 本机执行面
            -> Docker
            -> KVM / libvirt
            -> TeamLab 网络与流量工具
       -> Guacamole / 公网网关：VM 和动态实例访问入口
```

主站采用模块化单体，Agent 是独立执行面。运行环境统一通过部署队列、容量预留、调度、执行、审计和恢复链路管理。

## 技术栈

- 主站：ASP.NET Core、.NET 10、Entity Framework Core
- Web 客户端：React 19、TypeScript、Vite、pnpm
- 数据：PostgreSQL 16、Redis 7
- 节点执行：GZCTF.Agent、Docker、KVM/libvirt
- 远程访问与网络：Guacamole、Nginx、WireGuard，以及部署环境中的受控转发组件
- 测试：xUnit、Testcontainers、Vitest、前端架构与制品门禁

## 目录结构

```text
.
├── src/
│   ├── GZCTF/                  # 主站、模块和 Web 客户端
│   ├── GZCTF.Agent/            # WorkerNode 执行面
│   ├── GZCTF.AppHost/          # 本地编排入口
│   ├── GZCTF.Test/             # 单元测试
│   └── GZCTF.Integration.Test/ # 集成测试
├── docs/                       # 设计、运维、API、进度与历史记录
├── scripts/                    # 迁移、验证及受控运维脚本
├── scenarios/                  # 场景素材
├── tests/                      # 额外验收资料
├── docker-compose.dev.yml      # 本地依赖
└── docker-compose.yml          # 部署编排示例
```

## 本地开发

### 环境要求

- .NET 10 SDK
- Node.js 与 pnpm
- Docker Desktop 或 Docker Engine

### 启动依赖

```bash
docker compose -f docker-compose.dev.yml up -d
```

### 安装与运行

```bash
dotnet restore src/GZCTF.slnx

cd src/GZCTF/ClientApp
pnpm install
pnpm dev
```

后端可使用 IDE、`dotnet run --project src/GZCTF/GZCTF.csproj` 或 `GZCTF.AppHost` 启动。具体配置以本地开发环境为准，敏感配置不得提交到仓库。

## 构建与测试

```bash
dotnet build src/GZCTF.slnx -c Release
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release

cd src/GZCTF/ClientApp
pnpm build
```

`pnpm build` 包含多语言校验、lint、严格类型检查、架构检查、前端测试、生产构建和制品预算。基础设施功能还必须在真实 Docker、KVM、Registry、节点和网关环境中验收。

## 部署

生产环境不是简单执行 `docker compose up`。正式发布需要数据库备份、迁移检查、独立 release 目录、原子切换、节点与镜像验证以及业务冒烟测试。

- 主站发布：[维护窗口部署手册](docs/operations/vnext-maintenance-window-rollout.md)
- WorkerNode：[节点部署说明](docs/node-deployment/README.md)
- Registry：[镜像仓库部署说明](docs/registry-server/README.md)
- Windows VM：[镜像制作与部署简明指南](docs/operations/windows-vm-quick-deployment-guide.md)

根目录和 `scripts/` 中的历史一键部署脚本不得直接用于生产。

## 文档

- [文档导航与生命周期](docs/README.md)
- [项目协作规范](AGENTS.md)
- [当前开发状态](docs/development/current-state.md)
- [商业化与架构总纲](docs/platform-commercialization-master-plan.md)
- [模块边界](docs/commercialization/module-boundary-map.md)
- [外部 API 标准](docs/commercialization/external-api-standard.md)
- [模块文档覆盖矩阵](docs/modules/README.md)

`docs/archive/` 只用于保存历史证据和旧方案，不作为当前实现依据。

## 协作

开始任务前阅读 `AGENTS.md` 和 `docs/development/current-state.md`，从最新 `origin/main` 创建独立任务分支。代码、测试和必要文档应在同一变更中闭环；部署或项目状态发生变化时同步更新当前状态文档。
