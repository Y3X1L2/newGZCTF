# 2026-05-22 全链路侦察与修复报告

## 概述

对 `http://<test-server-ip>:8080/` 进行深度侦察，追踪了从 Flag 管理到部署队列的全链路，发现了多个 P0/P1 级别的致命问题并完成修复。

---

## 一、问题发现（按模块）

### 1. 多 Flag 管理（CRUD 全链路断裂）

| # | 严重程度 | 问题描述 | 根因 |
|---|---------|---------|------|
| F1 | **P0** | 创建 Flag 后前端不可见 | 前端 `load()` 调用 `GET /api/edit/Games/{id}/Challenges`（列表API），而 `ChallengeInfoModel` 没有 `flags` 字段，永远取不到数据 |
| F2 | **P1** | 后端 `AddFlags` 丢弃 7 个扩展字段 | 只保存了 `Flag + Challenge + Attachment`，`OrderIndex`、`ScoreMode`、`FixedScore`、`MaxAttempts`、`AnswerType`、`CustomName`、`Description` 全部丢失 |
| F3 | **P1** | `FlagInfoModel` 只返回 3 个字段 | `FromFlagContext` 映射缺失，前端需要展示的扩展字段全部为默认值 |

### 2. 环境模板（前端完全缺失操作入口）

| # | 严重程度 | 问题描述 |
|---|---------|---------|
| T1 | **P0** | 前端只有列表展示，无任何操作按钮（注册 Docker、上传压缩包、从本地导入、删除均无入口） |
| T2 | **P1** | `DockerRegisterRequest` 缺少 `RegistryAuth` 字段，无法支持私有仓库认证 |
| T3 | **P1** | `Delete` 接口不检查模板引用关系，可能误删正在被题目使用的镜像 |

### 3. 节点管理（多处虚假实现）

| # | 严重程度 | 问题描述 | 根因 |
|---|---------|---------|------|
| N1 | **P0** | `CleanupButton` 调用 `DELETE /api/v1/nodes`（无ID），必然 405/404 | 后端 DELETE 端点是 `/api/v1/nodes/{id:guid}`，需要一个 Guid 参数 |
| N2 | **P0** | `DeployButton` 发送空 POST body `{}`，必然 400 | 后端 `NodeDeployRequest` 要求 `HostAddress`、`Username`、`Password` 全为 `[Required]` |
| N3 | **P0** | `NodesController.Detail` 泄露 `AuthToken` | 直接 `return Ok(node)` 返回完整实体，包含敏感字段 |
| N4 | **P1** | 节点列表和详情页无轮询刷新，状态永远不更新 | 只有挂载时加载一次，无 `setInterval` 轮询 |

### 4. 部署队列（前后端完全脱节）

| # | 严重程度 | 问题描述 |
|---|---------|---------|
| Q1 | **P0** | 前端页面是纯静态占位符，无任何数据获取逻辑，永远显示"暂无排队请求" |
| Q2 | **P1** | 后端没有提供查询 `DeploymentTarget` 列表的 API 端点 |
| Q3 | **P1** | `ImageDistributionService` 未注册到 DI 容器，永远不会被实例化 |

### 5. 架构级问题（已在报告中记录，本次未修复）

- QueueManager 只分配节点，不执行实际部署
- GameController 创建 `DeploymentTarget` 后不经过队列系统
- FleetManager/调度器未被业务代码调用
- 没有 Agent 程序向心跳端点上报数据

---

## 二、修复内容

### 修复 1：Flag CRUD 全链路

**文件 1**: `src/GZCTF/ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/flags/index.tsx`

- `load()` 函数从 `GET /api/edit/Games/{gameId}/Challenges`（列表API）改为 `GET /api/edit/Games/{gameId}/Challenges/{challengeId}`（详情API）
- 直接从 `data.flags` 获取 Flag 数组

**文件 2**: `src/GZCTF/Models/Request/Edit/FlagInfoModel.cs`

- 补全所有扩展字段：`OrderIndex`、`Description`、`ScoreMode`、`FixedScore`、`MaxAttempts`、`AnswerType`、`CustomName`、`AttachmentHash`
- `FromFlagContext` 方法补全完整字段映射

**文件 3**: `src/GZCTF/Repositories/GameChallengeRepository.cs`

- `AddFlags` 方法补全所有扩展字段的赋值

### 修复 2：环境模板前端补全

**文件**: `src/GZCTF/ClientApp/src/pages/admin/images/Index.tsx`（完全重写）

- 添加 `RegisterDockerModal`：注册 Docker 镜像（支持私有仓库）
- 添加 `ImportLocalModal`：从服务器本地路径导入镜像
- 添加上传压缩包功能（调用 `POST /api/v1/image-templates/upload`）
- 添加删除功能（带确认对话框）
- 添加刷新按钮
- 状态 Badges 完整支持 3 种状态（Ready/Importing/Error）

**文件**: `src/GZCTF/Controllers/ImageTemplateController.cs`

- `DockerRegisterRequest` 添加 `RegistryAuth` 字段（支持私有仓库密码）
- `Delete` 方法添加引用检查，题目正在使用的镜像不允许删除

### 修复 3：节点管理前端修复

**文件**: `src/GZCTF/ClientApp/src/components/admin/CleanupButton.tsx`（完全重写）

- 改为先获取节点列表，再逐个对 `status === 'Offline'` 的节点调用 `DELETE /api/v1/nodes/{id}`
- 添加 `onCleanup` 回调，清理完成后通知父组件刷新

**文件**: `src/GZCTF/ClientApp/src/components/admin/DeployButton.tsx`（完全重写）

- 改为点击后展开内联表单（IP、用户名、密码），填写完整后发送正确结构的 POST 请求

**文件**: `src/GZCTF/ClientApp/src/pages/admin/nodes/Index.tsx`（完全重写）

- 添加 15 秒轮询刷新（`setInterval(loadNodes, 15000)`）
- 添加节点删除按钮（调用 `DELETE /api/v1/nodes/{id}`）
- 添加刷新按钮
- `CleanupButton` 和 `DeployButton` 传入 `onCleanup`/`onDeployed` 回调

**文件**: `src/GZCTF/ClientApp/src/pages/admin/nodes/[id]/Detail.tsx`（完全重写）

- 添加 15 秒轮询刷新
- 修复 `AuthToken` 泄露：解构时排除 `authToken` 字段
- 添加删除按钮（跳回列表页）

**文件**: `src/GZCTF/ClientApp/src/pages/admin/Dashboard/Index.tsx`（部分重写）

- 添加 15 秒轮询刷新
- 刷新按钮传入回调
- `DeployButton`/`CleanupButton` 传入 `onDeployed`/`onCleanup` 回调

**文件**: `src/GZCTF/Controllers/NodesController.cs`

- `Detail` 端点改为返回匿名对象投影，排除 `AuthToken` 字段

### 修复 4：部署队列前后端对接

**文件**: `src/GZCTF/Controllers/NodesController.cs`（新增 Controller）

- 新增 `DeploymentTargetsController`，提供以下端点：
  - `GET /api/v1/deployment-targets` - 列表查询（支持状态过滤、分页）
  - `GET /api/v1/deployment-targets/{id}` - 单条查询
  - `DELETE /api/v1/deployment-targets/{id}` - 取消任务（只能取消 Pending/Running 状态）

**文件**: `src/GZCTF/ClientApp/src/pages/admin/queue/Index.tsx`（完全重写）

- 对接 `GET /api/v1/deployment-targets` API
- 使用 SWR 10 秒自动轮询
- 状态筛选下拉框
- 刷新按钮
- 取消任务按钮（只对 Pending/Running 显示）
- 错误信息 Tooltip 展示
- 总记录数展示

### 修复 5：ImageDistributionService DI 注册

**文件**: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`

- 添加 `builder.Services.AddScoped<ImageDistributionService>();`

### 图标修复（构建错误修复）

所有新增/修改的前端文件统一使用项目原有图标方案 `@mdi/js` + `@mdi/react`，不得引入新的图标库：

| 文件 | 图标映射 |
|------|---------|
| Dashboard/Index.tsx | `mdiRefresh` |
| images/Index.tsx | `mdiRefresh`, `mdiDeleteOutline` |
| nodes/[id]/Detail.tsx | `mdiRefresh`, `mdiDeleteOutline`, `mdiArrowLeft` |
| nodes/Index.tsx | `mdiRefresh`, `mdiDeleteOutline` |
| queue/Index.tsx | `mdiRefresh`, `mdiClose` |

---

## 三、涉及文件清单

### 后端（.cs）

| 文件 | 修改类型 |
|------|---------|
| `Repositories/GameChallengeRepository.cs` | 修改 |
| `Models/Request/Edit/FlagInfoModel.cs` | 修改 |
| `Controllers/NodesController.cs` | 大量修改（新增 DeploymentTargetsController） |
| `Controllers/ImageTemplateController.cs` | 修改（RegistryAuth + Delete引用检查） |
| `Extensions/Startup/ServicesExtension.cs` | 修改（ImageDistributionService注册） |

### 前端（.tsx）

| 文件 | 修改类型 |
|------|---------|
| `pages/admin/games/[id]/challenges/[challengeId]/flags/index.tsx` | 修改 |
| `pages/admin/images/Index.tsx` | 完全重写 |
| `pages/admin/nodes/Index.tsx` | 完全重写 |
| `pages/admin/nodes/[id]/Detail.tsx` | 完全重写 |
| `pages/admin/queue/Index.tsx` | 完全重写 |
| `pages/admin/Dashboard/Index.tsx` | 部分重写 |
| `components/admin/CleanupButton.tsx` | 完全重写 |
| `components/admin/DeployButton.tsx` | 完全重写 |

---

## 四、构建验证

- **后端**: `dotnet build --no-restore` → 成功，1 个警告（已知：`VmManager` 已过时）
- **前端**: `npm run build` → 成功，0 错误

---

## 五、未解决问题（架构级，需单独规划）

以下问题在本次修复中确认存在，但需要较大的架构调整，不适合在本次修复范围内处理：

1. **没有 Agent 程序**：心跳端点存在但无人调用，节点 CPU/内存等指标永远为 0
2. **QueueManager 只分配节点不执行部署**：需要后台服务从数据库读取 Pending 记录并实际创建容器
3. **GameController 创建 DeploymentTarget 后不经过队列**：VM 部署路径完全断裂
4. **FleetManager/调度器未被任何 Controller 调用**：调度体系已编码但未接入业务流程
5. **sshpass 在 Windows 环境不可用**：`NodeDeployService` 依赖的部署方式在非 Linux 服务器上会失败
