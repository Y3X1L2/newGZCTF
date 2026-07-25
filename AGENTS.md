# YINYU 安全综合演练平台 — AGENTS 上下文

## 项目概况

| 属性 | 值 |
|------|------|
| 名称 | YINYU 安全综合演练平台（基于 GZCTF 二次开发） |
| 技术栈 | 后端 ASP.NET Core 10 + EF Core + PostgreSQL 16 + Redis 7 + SignalR |
| 前端 | React 19 + TypeScript 5 + Mantine v9 + Vite + pnpm monorepo |
| 架构 | 12 模块 Clean Architecture 插件 + Docker/KVM 节点编排 |
| 分支 | `vnext`（主开发线，全新 UI 重构中） |
| 模式 | CTF / AWDP / Theory / Training / Penetration / Exercise / TeamLab |

## 项目结构

```
D:\newGZCTF/
├── .github/workflows/quality.yml   # CI
├── docs/                            # 产品/技术文档
│   ├── archive/                     # 旧文档归档
│   ├── commercialization/           # 商业化架构（模块边界、API 契约、缓存、数据库）
│   ├── deploy/                      # 部署指南
│   ├── operations/                  # 运维手册
│   └── *.md                         # 设计文档（IAM、AWDP、UI、审计等）
├── scenarios/nebulamind/            # 企业渗透场景（12 服务、21 FLAG、5 安全域）
├── scripts/test-challenge/          # 测试挑战示例
├── src/
│   ├── GZCTF/                       # 主 Web 应用（后端 + 前端 ClientApp）
│   ├── GZCTF.Agent/                 # 节点代理（Docker/KVM 编排）
│   ├── GZCTF.AppHost/               # .NET Aspire 编排
│   ├── GZCTF.Test/                  # 单元测试（20 个子目录）
│   └── GZCTF.Integration.Test/      # 集成测试
├── tests/e2e/                       # Playwright E2E
├── docker-compose.yml               # 生产编排
├── docker-compose.dev.yml           # 开发本地依赖
└── README.md
```

## 后端模块架构（12 模块）

`src/GZCTF/Modules/` 下每个模块遵循 Clean Architecture：

```
Module/
├── Application/    # 服务接口 + 实现
├── Domain/         # 领域模型
└── Infrastructure/ # 基础设施（持久化等）
```

| 模块 | 状态 | 说明 |
|------|------|------|
| Identity | ✅ | 身份认证 + API Token |
| Audit | ✅ | 审计日志 |
| Content | ✅ | 内容管理（公告、帖子） |
| Ctf | ✅ | CTF 竞赛管理 |
| Awdp | ✅ | AWDP 攻防对抗 |
| Theory | ✅ | 理论考试 |
| Training | ✅ | 培训课程 |
| Penetration | ✅ | 渗透测试（开发中） |
| **Exercise** | **✅ 已完成** | **自主练习（后端 + 前端完整实现）** |
| Runtime | ✅ | 容器运行时 |
| TeamLab | ✅ | 团队实验室 |
| Composition | ✅ | 模块注册中心 |

### 基础设施层

| 目录 | 说明 |
|------|------|
| `Infrastructure/Cache/` | Redis 分布式缓存 |
| `Infrastructure/Persistence/` | EF Core DbContext + 迁移 |
| `Infrastructure/Telemetry/` | 遥测监控 |
| `Infrastructure/Concurrency/` | 并发控制 |
| `Services/` | 业务服务（文件、容器、排名） |
| `Hubs/` | SignalR Hub（实时事件、计分板、容器 I/O） |
| `Middlewares/` | 中间件（限流等） |
| `Migrations/` | 数据库迁移 |

## Exercise（自主练习）模块当前状态

**核心发现：整个模块只有空壳**

| 组件 | 路径 | 状态 |
|------|------|------|
| Controller | `Controllers/ExerciseController.cs` | **已完成** — 搜索/CRUD/提交Flag/容器/从赛事导入 |
| 服务接口 | `Modules/Exercise/Application/IExerciseService.cs` | **已创建** — 搜索、详情、Flag提交、容器 |
| 服务接口 | `Modules/Exercise/Application/IExerciseManagementService.cs` | **已创建** — CRUD、从赛事导入 |
| 服务实现 | `Modules/Exercise/Application/ExerciseService.cs` | **已创建** — ILike 搜索/多条件筛选/详情/Flag/容器 |
| 服务实现 | `Modules/Exercise/Application/ExerciseManagementService.cs` | **已创建** — 完整的 CRUD + ImportFromGame |
| 模块注册 | `Modules/Exercise/ExerciseModuleRegistration.cs` | **已创建** — DI 注册全部服务 + 仓库 + ImageTemplate |
| 模块注册 | `Composition/ModuleRegistration.cs` | **已注册** — `services.AddExerciseModule()` |
| API Token | `Modules/Identity/Application/ApiTokenScopes.cs` | **已添加** — `ExercisesRead` / `ExercisesWrite` 作用域 + 策略 |
| 请求 DTO | `Models/Request/Exercise/ExerciseFilter.cs` | **已创建** — Search/Category/Difficulty/Tags/Credit 范围 |
| 请求 DTO | `Models/Request/Exercise/ExerciseImportFromGameModel.cs` | **已创建** — fromGameId + challengeIds |
| 请求 DTO | `Models/Request/Exercise/ExerciseCreateModel.cs` | **已创建** — Create/Update 统一 DTO |
| 数据模型 | `Models/Data/ExerciseChallenge.cs` | 已定义（未改动） |
| 数据模型 | `Models/Data/ExerciseInstance.cs` | 已定义（未改动） |
| 仓库 | `Repositories/ExerciseChallengeRepository.cs` + `ExerciseInstanceRepository.cs` | 已存在（未改动，被服务调用） |
| 前端路由 | `ClientApp/.../VNextApp.tsx` | **已添加** — 4 个 practice 路由 + lazy import |
| 前端模块注册 | `ClientApp/.../moduleRegistry.ts` | **已激活** — `implemented: true` |
| 前端 API | `features/practice/api/practiceApi.ts` | **已创建** — useExercises/useExerciseDetail/submitFlag/createContainer |
| 前端首页 | `features/practice/PracticePage.tsx` | **已创建** — 分类网格 + 最近更新 |
| 前端浏览页 | `features/practice/PracticeBrowsePage.tsx` | **已创建** — 搜索 + 分类/难度/标签筛选 + 卡片网格 |
| 前端题目页 | `features/practice/PracticeChallengePage.tsx` | **已创建** — 详情 + FlagSubmission + InstanceControl |
| 前端统计页 | `features/practice/PracticeStatsPage.tsx` | **已创建** — 总数/解答数/正确率 + 分类/难度柱状图 |
| 前端样式 | `features/practice/PracticePage.module.css` | **已创建** — 作用域样式 |
| 关键依赖 | ExerciseChallenge 被 Training 模块引用 | 未改动 |

### 关键依赖关系

ExerciseChallenge 实体已被 Training 模块大量引用，修改需谨慎：
- `TrainingCourseChallenge.ExerciseChallengeId`
- `TrainingChapterChallenge.ExerciseChallengeId`
- `TrainingLabRecord.ExerciseChallengeId`
- `TrainingAnswerRecord.ExerciseChallengeId`
- `FlagContext.ExerciseId`
- `DeploymentQueueTicket.ExerciseContainer`
- `UserInfo.ExerciseVisible`

## 项目计划：自主练习模块

### 目标
对标青少年 CTF 练习平台，实现独立的自主练习子系统。

### 功能特性

| 功能 | 说明 |
|------|------|
| 题库池 | Web/Pwn/Reverse/Crypto/Misc/Forensics 分类 + 难度 + 标签筛选 |
| 题目类型 | 复用 StaticAttachment/DynamicAttachment/StaticContainer/DynamicContainer |
| **关键词搜索** | 按标题/内容关键词匹配题目（模糊搜索，支持中文分词） |
| **标签过滤** | 按 Tags 数组精确匹配（如 "SQL注入","RCE" 等多标签组合） |
| **难度匹配** | 按 Difficulty 枚举筛选（Baby~Insane 8 级），支持多选范围 |
| **从赛事导入题目** | 管理员从现有 CTF 赛事一键导入题目到练习池，保留原分类/标签/难度/附件 |
| 练习模式 | 自由刷题 + 专题训练（如 "SQL注入专项"） |
| 进度追踪 | 已完成/未完成、解题数、正确率、热力图 |
| AI 出题 | 通过 External API + Token 接口让 AI 写入题目 |
| 个人统计 | 解题趋势图、分类雷达图、能力图谱 |

### API + Token 体系

**复用现有标准 External API（Identity 模块已就绪）：**

- Token 格式：`gzctf_pat_{tokenId:N}.{base64url(32 bytes)}`
- 认证方式：`Authorization: Bearer {token}`
- 基础路径：`/api/open/v1/exercises`
- 作用域：需在 `ApiTokenScopes` 注册 `exercises:read` / `exercises:write`
- 速率限制：Redis Token Bucket（`X-RateLimit-*` 头）
- 幂等性：写操作需 `Idempotency-Key` 请求头
- 错误格式：`application/problem+json`

### 实现步骤

```
阶段 1 — 后端模块骨架        ✅ 已完成
├── 补全 Modules/Exercise/Application/
├── 创建 ExerciseModuleRegistration.cs
├── 在 ModuleRegistration.cs 注册
├── 扩展 ApiTokenScopes（ExercisesRead / ExercisesWrite）
└── 实现 ExerciseController（CRUD + 搜索/筛选 + Flag提交 + 容器 + 从赛事导入）

阶段 2 — External API        ⏳ 待处理
├── 实现 ExerciseOpenApiController（/api/open/v1/exercises）
├── 集成 Idempotency + Rate Limit
└── 实现 POST /api/open/v1/exercises/generate（AI 出题）

阶段 3 — 前端页面            ✅ 已完成
├── PracticePage.tsx（首页 + 分类导航）
├── PracticeBrowsePage.tsx（题库浏览 + 筛选 + 搜索）
├── PracticeChallengePage.tsx（题目详情 + Flag提交 + 容器）
├── PracticeStatsPage.tsx（个人统计柱状图）
├── VNextApp.tsx 路由 + API hooks + moduleRegistry 激活
└── vite build 通过

阶段 4 — AI 出题             ⏳ 待处理
├── 定义 AI 出题请求/响应契约
├── 异步出题队列
└── Webhook 回调
```

## 团队协作规范

### Git 分支策略
```
main（稳定）
 └── vnext（主开发线）
      ├── feature/exercise-backend
      ├── feature/exercise-frontend
      ├── feature/xxx（队友）
      └── ...
```
- 禁止直接 push main 和 vnext
- 每个功能建 feature 分支
- 合并前必须 `pnpm build` + `dotnet test`

### 提交前验证
```bash
dotnet build src/GZCTF/GZCTF.csproj
dotnet test src/GZCTF.Test/GZCTF.Test.csproj
cd src/GZCTF/ClientApp
pnpm build        # TypeScript 严格检查 + 构建
pnpm lint
```
- 提交格式：`feat(scope): msg` / `fix(scope): msg` / `refactor(scope): msg`
- 不提交：密码、密钥、连接字符串、`appsettings.json`（有 Template）

### 职责边界

| 你（练习模块） | 队友 |
|----------------|------|
| Exercise 后端模块 | Training 模块完善 |
| 练习前端全部页面 | AWDP 功能 |
| External API + Token 对接 | Penetration 渗透模块 |
| AI 出题接口 | 运维/部署 |
| 题库管理 | 系统管理功能 |

### 架构约束（来自 docs/commercialization/module-boundary-map.md）
- Controller → Contract (DTO) → Application Service → Domain → Infrastructure
- Controller 不得直接访问 AppDbContext、Docker、libvirt
- 所有 write 操作需要 Idempotency-Key

### 前端注意事项（来自 README）
- 样式用作用域 class（如 `.yy-practice-page`），写在 `.module.css` 中
- 优先复用 Mantine 组件，避免 hard-lock 覆盖
- 使用 `@Api` 自动生成的 TypeScript 类型（OpenAPI → TypeScript）
- 修改后检查桌面 + 笔记本宽度滚动行为

## 关键参考文件

### 后端（架构模板 — Training 模块）
- `src/GZCTF/Modules/Training/Application/`
- `src/GZCTF/Modules/Training/Domain/`
- `src/GZCTF/Modules/Training/Infrastructure/`

### 后端（API Token 体系）
- `src/GZCTF/Modules/Identity/Domain/ApiToken.cs`
- `src/GZCTF/Modules/Identity/Application/ApiTokenIssuer.cs`
- `src/GZCTF/Modules/Identity/Application/ApiTokenValidator.cs`
- `src/GZCTF/Modules/Identity/Application/ApiTokenScopes.cs`
- `src/GZCTF/Modules/Identity/Infrastructure/ApiTokenAuthenticationHandler.cs`

### 后端（External API）
- `docs/commercialization/external-api-standard.md`
- `docs/commercialization/open-api-v1-guide.md`
- `Controllers/VNextController.cs`

### 后端（模块注册）
- `src/GZCTF/Modules/Composition/ModuleRegistration.cs`

### 前端（架构模板 — Training UI）
- `src/GZCTF/ClientApp/src/vnext/features/training/catalog/`
- `src/GZCTF/ClientApp/src/vnext/features/training/chapter/`
- `src/GZCTF/ClientApp/src/vnext/features/training/course/`

### 前端（挑战运行时 — Flag 提交）
- `src/GZCTF/ClientApp/src/vnext/features/challenge-runtime/`

### 前端（模块注册）
- `src/GZCTF/ClientApp/src/vnext/app/shell/moduleRegistry.ts`

## 开发环境命令
```bash
# 启动本地依赖（PostgreSQL + Redis + guacd）
docker compose -f docker-compose.dev.yml up -d

# 后端构建
dotnet restore src/GZCTF/GZCTF.csproj
dotnet build src/GZCTF/GZCTF.csproj

# 前端
cd src/GZCTF/ClientApp
pnpm install
pnpm dev      # 开发服务器
pnpm build    # 生产构建 + 类型检查

# 测试
dotnet test src/GZCTF.Test/GZCTF.Test.csproj
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj

# 数据库迁移
dotnet ef migrations add MigrationName
dotnet ef database update
```
