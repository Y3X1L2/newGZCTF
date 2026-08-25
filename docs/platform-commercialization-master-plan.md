# YINYU 平台架构与产品总纲

更新时间：2026-08-16

本文是当前平台的架构边界、产品范围和后续建设依据。已经完成的工作以源码、迁移和测试为准；后续目标不得写成已完成能力。阶段计划和审查原文保存在 `docs/archive/implementation-records/`，不作为当前实现依据。

## 1. 产品定位

YINYU 是基于 GZCTF 演进的安全综合演练平台，统一承载：

- CTF 竞赛与动态靶场；
- 理论考试与课程课后测试；
- 培训课程、章节实验和教师学员管理；
- AWDP 攻防演练；
- 自主练习题库；
- Docker、Linux VM、Windows VM 和多节点组网场景；
- 镜像、节点、部署队列、日志、审计和开放 API 管理。

平台的核心价值不是菜单数量，而是同一套用户、内容、运行环境、提交结果和运维事实能够被多个业务场景复用。

## 2. 当前代码结构

```text
src/
├── GZCTF/                  # 主站、模块、数据库迁移和 ClientApp
├── GZCTF.Agent/            # WorkerNode 本机执行面
├── GZCTF.AppHost/          # 本地依赖编排
├── GZCTF.Test/             # 单元与架构测试
└── GZCTF.Integration.Test/ # PostgreSQL、Redis 和 HTTP 集成测试
docs/                       # 现行规范、运维、功能说明和归档记录
```

后端使用模块化单体，执行面由独立 Agent 提供。前端正式代码位于 `src/GZCTF/ClientApp/src/vnext`，不得通过旧页面或全局覆盖补齐新页面。

## 3. 架构边界

业务调用方向固定为：

```text
HTTP / Frontend
  -> Contracts
  -> Application
  -> Domain
  -> Infrastructure ports
  -> Runtime / Fleet / VM / TeamLab ports
  -> AgentClient
  -> GZCTF.Agent
```

### 3.1 事实源

- PostgreSQL 保存用户、比赛、课程、题目、提交、镜像、节点、队列、实例、运行事件和审计事实。
- Redis 只承担缓存、租约、协调和高频缓冲，不能作为业务事实源。
- Agent inventory 是节点本机资源和运行状态的观测来源，主站负责业务归属和状态投影。
- `DeploymentQueueTicket` 是 Docker、VM、培训实验、AWDP 和 TeamLab 运行任务的统一队列事实。

### 3.2 模块依赖

- Controller 不直接编排 Agent 命令或穿透其他模块的 DbSet。
- 跨模块读取使用公开 query contract；跨模块写入使用 application command。
- Agent 不读取比赛、课程、计分和权限实体，只执行主站已校验的本机操作。
- 动态 Flag 必须按实例隔离；管理员预览值不能进入正式实例。
- Docker 和 KVM 能力独立判断，缺少 KVM 不能阻断 Docker 调度。

## 4. 功能分层

| 层 | 现有能力 | 主要事实 |
| --- | --- | --- |
| 内容层 | CTF 题目、附件、Flag、理论题、课程资源、练习题、镜像模板 | 题目、资源、镜像和引用关系 |
| 业务层 | CTF、理论、培训、练习、AWDP、TeamLab 绑定 | 比赛、课程、答卷、练习记录、攻防轮次 |
| 运行层 | Docker/KVM、队列、容量、镜像分发、入口和回收 | 部署任务、实例、节点 inventory、运行事件 |
| 管理层 | 用户、战队、学员组、节点、镜像、日志、系统设置 | 权限、审计、运维操作和配置 |
| 集成层 | Portal SSO、Open API、Token、幂等 operation、Webhook | 外部调用和异步操作状态 |

## 5. 统一运行链路

1. 业务模块提交创建、重置或销毁 command。
2. Application service 校验权限、资源归属、镜像能力和业务状态。
3. Runtime service 创建 `DeploymentQueueTicket` 并进行容量预留。
4. 调度器选择满足 Docker/KVM、镜像、CPU、内存、存储和端口池条件的节点。
5. Agent 执行节点本地网络、容器或虚拟机操作并回报结构化事件。
6. 主站根据队列、节点 inventory 和入口 ACK 推进状态；入口未确认时不向用户展示可用地址。
7. 销毁、重置和恢复按运行 ID、代次和资源归属精确清理，失败时保留可继续处理的状态。

## 6. 现行模块边界

- **CTF / Content**：赛事、题目、附件、Flag、镜像引用和提交判定；不直接管理节点本地资源。
- **Theory**：题库、试卷、答卷快照、判分和答案回顾；不把练习提交或课程进度当作答卷事实。
- **Training**：课程、章节、资源、报名、教师、实验、课后理论和学习进度；运行实例通过统一运行底座创建。
- **Exercise**：公共练习题库、来源导入、标签/难度、附件、Flag、容器实例、提交和统计；不共享比赛实例或比赛 Flag 状态。
- **AWDP**：服务、轮次、Checker、攻击/修补、阶段计分和人工验收；运行资源仍走统一队列。
- **TeamLab / Penetration**：TeamLab 管理拓扑、发布和 runtime；Penetration 只负责比赛绑定、目标、提交、得分和重置策略。
- **Identity**：本地登录、Portal SSO、角色和组织关系；统一认证对接方源码不属于本仓库。

## 7. 前端开发契约

- 依赖方向为 `Route -> feature controller/hook -> feature panel -> foundation component`。
- 请求、DTO 转换和视觉状态分开；页面不直接访问生成 API 文件。
- 业务请求只能存在于 feature API adapter 或 controller/hook，通用展示组件不包含权限和路由 ID。
- 使用 vNext 壳层、语义 Token、CSS Module；不得新增无作用域全局覆盖。
- 页面必须支持日间/夜间、键盘操作、减少动效偏好，并在 390、1366、1920、2560 宽度检查布局。
- 未实现接口显示真实空态或待建设状态，不使用 mock 结果冒充业务成功。

## 8. 当前产品缺口

1. 自主练习需要完成目标生产数据库迁移、真实容器实例、发布、回滚和内容运营验收。
2. TeamLab 需要继续完成双 Worker 故障、规模并发、长期流量、服务注入和完整跨节点现场验收。
3. Windows VM 目前只承诺比赛场景；认证镜像、双实例 RDP/Guacamole、原生 mstsc 剪贴板、隔离和销毁清理仍需实机签收。
4. AWDP 真实攻击、修补和异常恢复继续由授权测试人员手工验收。
5. 统一内容资产和出题 API 仍需按现有模块边界逐项补齐，不能新建第二套运行队列或提交事实。

## 9. 建设顺序

后续任务按以下顺序推进：

1. 优先修复线上可复现的稳定性问题，不以新页面掩盖运行底座缺陷。
2. 完成练习生产验收和内容治理，确认它与 CTF、培训、AWDP 的来源关系不污染事实。
3. 完成 TeamLab 和 Windows VM 的真实基础设施验收，再扩大容量和故障测试。
4. 补齐 AWDP、Identity、Theory、Training、Content 和 Exercise 的现行模块说明。
5. 只有在现有边界稳定后，才继续新增赛制或复杂组网能力。

## 10. 质量与发布门禁

文档和代码提交至少执行 `git diff --check`。前端改动执行 locale、lint、TypeScript、架构检查、测试和 build；后端改动执行 Release build、单元测试和受影响的集成测试。迁移、调度、镜像、Agent、VM、TeamLab、AWDP 和公网入口必须补充真实环境验收，不能只以 mock 通过为完成。

生产发布必须使用备份、独立 release 目录、迁移前检查、原子切换、业务冒烟和明确回退路径。详细步骤见 [生产发布与回滚手册](operations/vnext-maintenance-window-rollout.md)。
