# YINYU vNext Phase 6 AWDP 开发计划

更新日期：2026-07-16

## 1. 阶段目标

本阶段在 vNext 前端内完成 AWDP 选手端与管理端闭环，不加载或包装旧 Mantine 页面。

完成后应支持：

- 管理员配置服务、启动和停止 AWDP、查看实例、补丁、攻击日志与榜单。
- 选手查看所有队伍服务入口，在攻击阶段提交 Flag，在修补阶段上传补丁。
- 选手管理本队实例，明确区分重置和恢复，并看到剩余次数与操作结果。
- REST 快照与 Monitor Hub 协同；断线重连、刷新和重新进入页面后恢复服务器真实状态。

## 2. 范围与非目标

### 2.1 本阶段范围

- `/games/:gameId/awdp`
- `/admin/games/:gameId/awdp-services`
- AWDP 领域 Adapter、规范化模型、Controller、状态展示与自动化测试。
- `GameWorkspaceShell` 和 `GameAdminShell` 的 AWDP 条件导航。

### 2.2 非目标

- 不重构渗透演练或 TeamLab。
- 不修改 AWDP 后端计分、轮次和调度规则。
- 不从容器存在、页面倒计时或 SignalR 单条事件推断比赛事实。
- 不把旧 `Awd.tsx`、`AwdServices.tsx`、`AwdpWidgets.tsx` 引入 vNext。

## 3. 分层与目录

```text
features/games/awdp/
  api/awdpPlayerApi.ts
  awdpDomain.ts
  awdpDomain.test.ts
  useAwdpWorkspaceController.ts
  AwdpWorkspacePage.tsx
  AwdpStageHeader.tsx
  AwdpServiceTable.tsx
  AwdpActionPanel.tsx
  AwdpActivityPanels.tsx

features/admin/awdp/
  api/awdpAdminApi.ts
  useAdminAwdpController.ts
  AdminAwdpPage.tsx
  AwdpServicePanel.tsx
  AwdpRuntimePanel.tsx
  AwdpPatchLogPanel.tsx
  AwdpAttackLogPanel.tsx
```

依赖方向固定为：

```text
Route Page -> Controller/Hook -> Feature Adapter -> Generated Client
```

TSX 不得默认导入 `@Api` 客户端。兼容旧响应、枚举映射、入口 URL 和分页解包只允许存在于 Adapter 或领域函数。

## 4. 选手端设计

### 4.1 页面结构

1. 阶段栏：当前轮次、攻击/修补状态、剩余时间、刷新时间。
2. 摘要：本队 AWDP 得分、排名、运行服务、剩余重置/恢复次数、防守状态。
3. 阶段操作：攻击阶段展示 Flag；修补阶段展示服务和补丁上传。
4. 服务列表：展示所有队伍的服务、入口、运行和 Checker 状态；本队行增加明确文本标记。
5. 本队管理：重置与恢复通过确认对话框执行，并说明影响和剩余次数。
6. 活动区域：榜单、攻击日志、补丁状态按可切换视图展示，避免超长页面。

### 4.2 状态规则

- `NotStarted/Stopped/Finished`：只读，不展示可执行攻击或修补操作。
- `Attack`：允许提交 Flag；补丁上传禁用。
- `Patch`：允许为本队服务上传 `.tgz` 或 `.tar.gz`；Flag 提交禁用。
- 未知状态按只读处理，不映射为健康或进行中。
- Checker 与补丁结果分别展示，不能把“漏洞已阻断”解释为“服务完全正常”。

### 4.3 实时规则

- 初次进入并行读取 status、instances、scoreboard、attack logs 和 patch status。
- `/hub/monitor?game={gameId}` 监听轮次、服务和补丁事件。
- Hub 事件只触发有节流的快照重读；不直接拼接不完整业务状态。
- 重连后立即读取全量快照，离线时保留最后快照并显示连接状态。

## 5. 管理端设计

### 5.1 页面结构

管理端使用五个 Tab：

- 服务配置：服务列表和右侧编辑抽屉，按基础、校验脚本、计分、轮次限制分区。
- 轮次与实例：开始/停止控制、阶段摘要、实例矩阵和实例重置/恢复。
- 补丁记录：队伍、服务、轮次、Checker、Exp、最终状态和消息。
- 攻击日志：攻击方、目标方、服务、分数和时间。
- AWDP 榜单：攻击、SLA、修补、扣分和总分。

### 5.2 高风险操作

- 启动前展示服务数量、队伍可见事实和配置缺失；后端未提供的容量不由前端推断。
- 停止、删除服务、重置和恢复必须确认。
- 写操作完成后以服务器回读为成功依据。
- 服务编辑在保存前校验名称、镜像、端口、时长、轮数、分数和次数边界。

## 6. 性能与响应式

- 服务与实例按服务、队伍搜索；大数据量只渲染当前筛选或分页结果。
- 实例状态使用稳定 key 和领域规范化结果，实时刷新不重建无关组件。
- 390、1366、1920 和 2560 宽度不产生页面级横向滚动。
- 小屏将服务表转换为紧凑列表，管理编辑器和确认操作使用可滚动抽屉。
- 动效只用于阶段变化、列表增量和抽屉；支持 `prefers-reduced-motion`。

## 7. 实施顺序

1. 建立 AWDP 领域模型、状态映射、倒计时和入口规范化测试。
2. 建立 Player/Admin Adapter，覆盖读取、Flag、补丁、服务和实例操作。
3. 完成选手 Controller、Monitor Hub 和页面组合。
4. 完成管理 Controller、服务编辑、轮次控制和审计视图。
5. 接入路由和条件导航，完成样式与响应式。
6. 执行完整构建和真实浏览器验收。

## 8. 验收退出条件

- strict TypeScript、lint、架构检查、测试、生产构建和 bundle 预算全部通过。
- AWDP 比赛未开始、攻击、修补、结束四种状态显示正确。
- 两支队伍可以看到彼此服务；只有本队实例可重置或恢复。
- 正确和错误 Flag 都得到真实服务器结果，正确结果刷新榜单。
- 补丁上传经历 Checker、Exp 和最终状态，页面不混淆三个结果。
- 重置、恢复、启动、停止均等待终态回读，错误信息可见。
- Hub 断线重连和刷新页面后状态不回退、不重复追加日志。
- 日间、夜间和四种目标视口无遮挡、溢出和不可访问操作。

## 9. 开发进度

- [x] 固化前端治理基线。
- [x] 建立 AWDP 领域模型、状态语义和安全入口规范化。
- [x] 建立 Player/Admin Adapter 和 Monitor Hub 快照校准。
- [x] 完成选手端阶段、攻击、补丁、服务、榜单和日志页面。
- [x] 完成管理端服务、轮次实例、补丁、攻击日志和榜单页面。
- [x] 接入 AWDP/Mixed 条件路由与导航。
- [x] 通过完整前端构建和自动化测试。
- [x] 使用 `10.24.0.27` 真实 AWDP 数据验证管理、选手和历史结果页面。
- [ ] 在可销毁专用比赛中执行启动、攻击、修补、重置、恢复和停止写流程。
