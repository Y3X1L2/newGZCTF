# YINYU 安全综合演练平台

YINYU 安全综合演练平台基于开源 GZCTF 二次开发，面向 CTF 竞赛、AWDP 攻防演练、理论考试、培训课程和分布式靶场调度等场景。当前仓库是团队后续开发与交付的主仓库，前端、后端、节点调度和课程模块均在此维护。

## 核心模块

- CTF 赛制：支持普通题目、Docker 动态环境、Windows/KVM 靶机、静态 Flag 和动态 Flag。
- AWDP 赛制：支持服务启动、攻击阶段、修补阶段、Checker、Flag 判定和阶段计分。
- 理论考试：支持题库、试卷配置、单选/多选/判断、草稿保存、最终提交和独立榜单。
- 培训课程：支持课程、章节、资源、课程题目、课程环境模板、章节实验、课后理论测试和学习进度。
- 节点与镜像：支持 Docker/KVM 节点注册、镜像模板、镜像上传/注册、内网镜像服务器和多节点调度。
- 渗透演练：正在开发中，重点方向是多网段场景、内网通道、公网入口和节点编排协同。

## 技术栈

- 后端：ASP.NET Core / .NET 10、Entity Framework Core、PostgreSQL、Redis、SignalR。
- 前端：React 19、Vite、Mantine、TypeScript、pnpm。
- 靶场调度：Docker、KVM/libvirt、Guacamole、FRP/WireGuard/Nginx 等网络组件。
- 测试与支撑：xUnit 测试项目、Docker Compose 本地依赖、OpenAPI 生成前端 API 类型。

## 目录结构

```text
.
├── src/
│   ├── GZCTF/                 # 主后端与前端 ClientApp
│   ├── GZCTF.Agent/           # 节点/Agent 相关代码
│   ├── GZCTF.AppHost/         # 本地编排入口
│   ├── GZCTF.Test/            # 单元测试
│   └── GZCTF.Integration.Test/# 集成测试
├── docs/                      # 项目文档
├── scripts/                   # 部署和运维脚本
├── scenarios/                 # 场景/靶场素材
├── tests/                     # 额外测试资料
├── docker-compose.yml         # 生产/演示依赖编排示例
└── docker-compose.dev.yml     # 本地开发依赖
```

## 本地开发

开始开发前请先阅读根目录 `AGENTS.md` 和 `docs/development/current-state.md`。前者定义长期开发规范，后者记录当前主线、已完成阶段、环境和已知缺口。

### 依赖

- .NET 10 SDK
- Node.js 与 pnpm
- Docker / Docker Compose
- PostgreSQL、Redis、guacd 可通过 `docker-compose.dev.yml` 启动

### 启动基础依赖

```bash
docker compose -f docker-compose.dev.yml up -d
```

### 前端开发

```bash
cd src/GZCTF/ClientApp
pnpm install
pnpm dev
```

### 前端构建与类型检查

```bash
cd src/GZCTF/ClientApp
pnpm build
```

`pnpm build` 会先执行 TypeScript 严格检查，再生成生产构建产物。

### 后端构建

```bash
dotnet restore src/GZCTF/GZCTF.csproj
dotnet build src/GZCTF/GZCTF.csproj
```

### 运行测试

```bash
dotnet test src/GZCTF.Test/GZCTF.Test.csproj
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj
```

## 部署说明

典型部署由以下部分组成：

- 平台服务器：运行 GZCTF 后端、前端静态资源、PostgreSQL/Redis 或其连接配置。
- 镜像服务器：保存 Docker 镜像包或镜像仓库，供平台节点拉取。
- 靶机节点：注册到平台后负责启动 Docker 容器或 KVM/Windows 靶机。
- 公网入口：通过 FRP、Nginx、WireGuard 等方式把容器端口或演练入口暴露给外部访问。

生产发布时建议使用 `dotnet publish` 生成后端发布目录，并确保 `ClientApp` 已完成生产构建。已部署环境如果启用了 ASP.NET 静态资源端点缓存，替换前端静态资源后需要重启平台服务，确保 `index.html` 引用最新 hash 资源。

## 镜像与节点约定

- Docker 题目使用镜像模板或课程环境模板绑定镜像。
- 远程节点需要提前安装 Docker/KVM 依赖，并能够访问镜像服务器。
- 多节点环境下，容器可能启动在非平台服务器节点，公网入口应以实际调度节点和转发规则为准。
- 镜像管理服务器建议提供统一上传/注册入口，节点通过内网镜像仓库或受控导入方式拉取。

## 功能测试建议

### CTF 赛制

1. 创建比赛并发布。
2. 添加 Docker 动态 Flag 题目，启动容器并访问入口。
3. 添加 Windows/KVM 靶机题目，验证靶机启动、入口展示和静态 Flag。
4. 以选手身份报名，提交正确 Flag，检查题目分数和总榜。

### AWDP 赛制

1. 创建 AWDP 比赛并配置服务。
2. 启动比赛，确认服务实例、阶段状态和 Checker。
3. 攻击阶段提交 Flag，检查攻击得分。
4. 修补阶段上传补丁包，检查防守状态、修补分和 Checker 结果。

### 理论考试

1. 维护理论题库，导入单选、多选、判断题。
2. 创建理论比赛或章节测试，配置试卷和分值。
3. 选手保存草稿并最终提交。
4. 检查自动判分、榜单和后台统计。

### 培训课程

1. 老师创建课程、章节和课程资源。
2. 配置课程环境模板与课程题目。
3. 学生报名课程，进入章节学习。
4. 启动章节实验容器，提交 Flag，完成课后理论测试。
5. 老师查看学员进度与提交情况。

## 前端维护注意事项

当前正式前端位于 `src/GZCTF/ClientApp/src/vnext`。后续修改遵守：

- 使用 vNext 壳层、语义 Token、CSS Module、feature controller/hook 和 API adapter。
- 不在 `YinyuRefinement.css`、`YinyuTheme.css` 或文件末尾新增跨页面 hard-lock 覆盖。
- 未实现页面显示正式空态或待建设状态，不加载旧页面套壳，不伪造业务数据。
- 修改后检查 390、1366、1920、2560 宽度、日夜主题、滚动行为和页面切换。
- 详细规则见 `docs/yinyu-vnext-development-guardrails.md` 和 `docs/commercialization/frontend-component-boundary.md`。

## 分支与提交

- 主仓库：`Y3X1L2/newGZCTF`
- 主分支：`main`
- 临时实验或队友分支合并前应先本地构建，并在测试服务器完成关键流程验证。

提交前建议执行：

```bash
cd src/GZCTF/ClientApp
pnpm build
```

如涉及后端模型、调度或计分逻辑，还应补充对应 `.NET` 测试或至少完成一轮端到端流程测试。
