# 项目可视化体验增强计划

## 目标

在不推倒现有成熟排行榜和比赛界面的前提下，增强平台的数据观感、实时态势表达和流畅度。重点在 AWDP 赛事大屏新增“互相攻防态势感知 3D 可视化”，并将培训签到表升级为真实日期驱动的 GitHub 风格热力图。所有改动必须统筹全局样式、共享组件和接口契约，避免同类页面出现风格、交互或数据口径不一致。

## 设计原则

1. 证据优先：每个动画、粒子、光束或 3D 标记都必须表达实际数据含义，例如解题、攻击、告警、活跃度、分数变化或队伍状态，禁止只做装饰。
2. 小步增强：排行榜、比分曲线、解题日志等已有成熟界面只做性能优化、共享样式抽取和细节一致性修复，不重写业务逻辑。
3. 接口稳定：优先复用现有 `useCTFScreenData`、`useGameScreenData`、排行榜 API、培训 overview/checkIn API；只有现有字段无法表达真实日期或态势数据时才补充最小字段。
4. 统一风格：大屏沿用当前金属/战术大屏体系，培训沿用管理界面背景和 YINYU 绿色、青绿、薰衣草紫渐变体系；同类状态、进度条、热力图颜色只维护一套语义令牌。
5. 攻防态势独立配色：AWDP 攻击态势只使用红色系流线表达“攻击/风险/入侵方向”，不使用绿色或薰衣草紫；修复态势不改变语义色，用柱体自身材质颜色的发光/呼吸表达“本队服务修复中或修复完成”。
6. 流畅可降级：3D 层必须支持 reduced-motion、WebGL 不可用降级、低性能设备降级、DPR 限制和画布非空检测。

## 当前结构判断

### 大屏与排行榜

- 管理大屏入口：`src/GZCTF/ClientApp/src/components/screen/ScreenDisplayPage.tsx`
- 当前正式大屏主实现：`src/GZCTF/ClientApp/src/components/ctf-screen/CTFScreenPage.tsx`
- 当前大屏数据聚合：`src/GZCTF/ClientApp/src/components/ctf-screen/useCTFScreenData.ts`
- 旧/辅助大屏数据聚合：`src/GZCTF/ClientApp/src/components/screen/useScreenData.ts`
- 当前 3D 分数城市：`src/GZCTF/ClientApp/src/components/ctf-screen/MetalScoreCity.tsx`
- 普通排行榜：`src/GZCTF/ClientApp/src/components/ScoreboardTable.tsx`
- 大屏曲线/热度组件：`src/GZCTF/ClientApp/src/components/ctf-screen/ScoreChart.tsx`、`HeatmapPanel.tsx`

现状：项目已经具备 `three`、`@react-three/fiber`、`@react-three/postprocessing`、`echarts`、`recharts` 和 SignalR。大屏中央已经有 `MetalScoreCity`，但它目前主要表达队伍分数和排名，不足以表达 AWDP 队伍互相攻击和服务修复状态。因此最佳方案是在 AWDP 赛事的大屏中央舞台升级为“分数城市 + AWDP 攻防态势层”，其他比赛类型继续使用现有排行城市/榜单/日志视图，不额外启用攻击态势层。

### WebGPU / WebGL 技术取舍

本轮推荐继续使用 Three.js/WebGL，不把 WebGPU 作为默认实现。

| 对比项 | WebGL | WebGPU |
| --- | --- | --- |
| 成熟度 | 成熟、覆盖广、Three.js 现有路径稳定 | 新标准，能力强但兼容和调试成本更高 |
| API 模型 | 类 OpenGL ES，状态机模型，适合传统 3D 渲染 | 更接近 Vulkan/Metal/D3D12，显式管线、显式资源管理 |
| 性能优势 | 对本项目这种几十到数百节点/流线已足够 | 大规模粒子、GPU compute、复杂后处理和海量实例更有优势 |
| 生态适配 | 项目已有 Three.js/WebGL 代码和依赖 | Three.js WebGPURenderer 仍更适合实验/渐进接入 |
| 降级 | WebGL 不可用时走 2D fallback | WebGPU 必须保留 WebGL fallback，等于维护双渲染路径 |

结论：AWDP 态势层的数据规模主要是队伍柱体、攻击流线、修复发光和最近事件窗口，WebGL 的性能余量足够。WebGPU 的优势在更底层的并行 GPU 计算、更低 CPU 开销和现代图形管线，但会带来浏览器兼容、HTTPS/安全上下文、调试、fallback 和双路径维护成本。当前商业化落地应选择 WebGL；只在后续出现“上千队伍、数万条实时流线、GPU picking/compute 明显成为瓶颈”时，把 WebGPU 作为可选 renderer 研究。

### 培训签到

- 培训首页：`src/GZCTF/ClientApp/src/pages/training/index.tsx`
- 培训 UI 组件：`src/GZCTF/ClientApp/src/components/training/TrainingCourseUI.tsx`
- 培训 API 类型：`src/GZCTF/ClientApp/src/utils/TrainingApi.ts`
- 培训后端 overview：`src/GZCTF/Controllers/TrainingCourseController.cs`
- 后端模型：`src/GZCTF/Models/Request/Training/TrainingModels.cs`

现状：后端已经返回 `TrainingPersonalOverviewModel.activity` 和 `checkIns`，但前端 `TrainingActivityHeatmap` 只是简单映射数组，没有真实月份、星期、日期标签、年份窗口、今日高亮和空日期填充，导致视觉上不像真实日期表。

## 视觉层清单

| 层 | 分析任务 | 数据形态 | 技术路线 | 改动策略 |
| --- | --- | --- | --- | --- |
| AWDP 大屏 3D 攻防态势 | 实时展示队伍互相攻击、服务修复态势、榜单分数变化 | AWDP 攻击日志 + Patch/服务事件 + 排行榜 | Three.js/WebGL，复用现有画布生命周期 | 仅 AWDP 启用，增强 `MetalScoreCity` 或拆出 `AwdpAttackSituationScene` |
| 大屏排行榜 | 排名、分数、解题数对比 | 表格/排序列表 | 现有 React + GSAP 动画 | 仅优化 memo、稳定 key、文字乱码与状态样式 |
| 大屏趋势 | 前 5 队分数趋势 | 时间序列 | 现有 Recharts/ECharts | 保持现状，统一颜色令牌和 tooltip 样式 |
| 培训签到热力图 | 最近一年/月份学习活跃度、签到状态 | 日期序列 | React DOM/CSS grid，必要时 SVG | 重写为真实日期网格，不引入重依赖 |
| 普通排行榜表格 | 队伍解题矩阵 | 宽表格 | 现有 Mantine Table | 只保留横向拖动、分页和性能细节，不重写 |

## 方案一：AWDP 大屏攻防态势感知 3D 可视化

### 产品形态

只在 AWDP 赛事的大屏中央 `metal-city-stage` 中新增“攻防态势”视角，保留现有排行城市视觉基因。Jeopardy、Theory、Penetration、Mixed 中非 AWDP 部分不启用红色攻击流线，避免把普通解题、理论答题或渗透提交误读成队伍互相攻击。

- 队伍节点：按排名/分数排列为城市柱体，节点高度继续表示分数，柱体基础颜色继续按名次/材质体系呈现金、银、冷银等现有风格。
- 攻击流线：每条红色流线表示一次 AWDP 攻击事件，方向为“攻击队伍 -> 被攻击队伍”。流线颜色只能使用红色系，明暗、粗细、粒子速度可以表达攻击强度、近期性和得分，但不能改用绿色、青绿或薰衣草紫。
- 修复态势：修复、Patch 成功、服务恢复等事件不使用独立新颜色，而是在对应队伍柱体上叠加“柱体本身同颜色发光/呼吸/边缘高光”。例如金色柱体发金色修复光，冷银柱体发冷银修复光。这样可以和红色攻击流线形成明确边界。
- 服务异常/宕机：使用低饱和暗红描边或柱体底部红色警戒环，不能覆盖修复态势的柱体本色。
- 右侧现有实时日志继续作为可读明细，3D 只负责“谁在攻击谁、谁正在修复、哪些队伍承压”的态势感知，不替代日志。

### 数据契约

优先从 `useCTFScreenData` 已经拉取的 AWDP 数据中派生攻防事件模型，不新建接口：

```ts
export type AttackSituationEventKind =
  | 'awdp-attack'
  | 'awdp-patch'
  | 'awdp-service-up'
  | 'awdp-service-down'

export interface AttackSituationEvent {
  id: string
  kind: AttackSituationEventKind
  time: number
  sourceTeamId: number
  sourceTeamName: string
  targetTeamId?: number
  targetTeamName?: string
  serviceName: string
  points: number
  status: 'attack' | 'patched' | 'up' | 'down'
}
```

落点：

- 在 `src/GZCTF/ClientApp/src/components/ctf-screen/useCTFScreenData.ts` 中新增派生字段 `awdpSituationEvents`，只在 `isAwdpScoreGame` 时生成。
- 不改变现有 `teams`、`solveEvents`、`scoreHistory` 字段，避免破坏已有大屏和排行榜。
- 若 AWDP 数据接口失败，保留上一次可用数据，并给大屏显示 `partial` 状态，而不是清空 3D 场景。
- 非 AWDP 赛事的 `awdpSituationEvents` 必须为空，3D 层不得绘制攻击流线。

### 3D 组件边界

建议新增：

- `src/GZCTF/ClientApp/src/components/ctf-screen/AwdpAttackSituationScene.tsx`
- `src/GZCTF/ClientApp/src/components/ctf-screen/useAwdpAttackSituationModel.ts`

职责：

- `useAwdpAttackSituationModel`：将 `teams`、`awdpSituationEvents`、`awdpServices` 转换成稳定的 3D 布局模型，输出队伍柱体、红色攻击边、修复发光状态、服务异常状态。
- `AwdpAttackSituationScene`：只负责 Three.js 场景生命周期、渲染、交互、降级。
- `CTFScreenPage`：控制模式切换和数据传入，不承担 3D 细节。

### 交互

- 鼠标拖拽：旋转视角。
- 滚轮：缩放视角，限制范围避免穿模。
- 点击队伍节点：右侧/弹层显示该队最近 AWDP 攻击、被攻击、修复和服务状态。
- 点击红色攻击流线：高亮攻击队、被攻击队、服务名和得分。
- 空白双击或按钮：重置视角。
- 大屏自动播放：无人操作 12 秒后恢复缓慢巡航。
- 移动端或小屏：保留静态 2D 摘要，不强制加载 WebGL。

### 视觉编码

| 数据 | 视觉通道 |
| --- | --- |
| 队伍分数 | 节点高度/柱体高度 |
| 排名 | 金/银/铜/冷银色材质 |
| AWDP 攻击 | 红色方向流线，攻击队伍 -> 被攻击队伍 |
| 攻击得分/强度 | 红色流线粗细、亮度、运动速度 |
| 近期攻击 | 红色流线透明度更高、尾迹更长 |
| 修复/Patch 成功 | 队伍柱体本色发光，不改变柱体所属颜色 |
| 服务恢复 | 柱体本色短脉冲，持续时间短于修复中呼吸光 |
| 服务宕机/异常 | 柱体底部暗红警戒环或红色细描边 |
| 最近 10 分钟承压 | 被攻击队伍柱体底部红色压力环大小 |
| 过期数据 | 降低透明度并显示 stale 标签 |

颜色边界：

- 攻击只能是红色系：建议 `#ff3b3b`、`#ff5a4f`、`#b8142a`，用透明度和亮度区分状态。
- 修复只能是柱体同色发光：金柱发金光，银柱发银白冷光，普通冷银柱发冷银光。
- 绿色、青绿、薰衣草紫不进入 AWDP 攻击态势层；这些颜色继续留给平台其他状态、培训热力图或普通成功状态。
- 攻击和修复同时发生时，红色攻击流线优先绘制在空中，柱体同色修复光只贴附柱体表面和边缘，两者不互相覆盖。

### 性能边界

- 单页只允许一个 WebGL 主场景。
- `renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5))`，低端设备降至 1.0。
- 队伍节点上限：默认渲染前 64 队，超过后折叠为“其他队伍云团”。
- 流线事件上限：最近 80 条，超过按时间衰减移除。
- 粒子数量：桌面 600 以内，低性能/移动 160 以内。
- 文本标签使用 CanvasTexture 缓存，key 只包含队伍名、排名、分数桶，避免每帧重建。
- 鼠标位置、相机状态等高频状态用 `useRef`，不进入 React state。
- WebGL context loss 时显示静态排行榜城市截图式 DOM fallback。

### 降级

- `prefers-reduced-motion: reduce`：关闭巡航、流线运动和脉冲，只保留静态节点与最近事件列表。
- WebGL 不可用：显示 2D 态势摘要卡，包括 Top 队伍、热点类别、最近攻击流。
- 大屏接口失败：保留 last-known-good 数据，并显示“数据延迟/部分数据”状态。

## 方案二：培训签到 GitHub 风格真实日期热力图

### 产品形态

将 `TrainingActivityHeatmap` 从简单 42 格数组升级为真实日期组件：

- 默认展示最近 12 个月，按周为列、星期为行。
- 顶部显示月份标签，如 `Jul Aug Sep ... Jun`。
- 左侧显示 `Mon Wed Fri` 或中文 `一 三 五`，保持紧凑。
- 每个格子对应真实日期，即使没有数据也渲染为空强度格。
- 今日显示细边框或内发光。
- 已签到日使用绿色基础强度，学习行为/完成章节/容器实验提交叠加强度。
- tooltip 显示：日期、签到状态、学习动作、完成章节、实验提交。
- 右侧或底部显示 Less -> More 强度图例。

### 数据契约

现有 `overview.activity` 已包含：

```ts
interface TrainingActivityPointModel {
  date: string
  studyActions: number
  completedChapters: number
  acceptedChallenges: number
  checkedIn: boolean
}
```

最小后端增强建议：

- 当前后端只返回 42 天。为了 GitHub 年视图，`BuildOverview` 的 `since` 从 `today.AddDays(-41)` 改为最近 371 天起点。
- 如担心 payload，接口可接受 `?days=371`，默认仍 371；前端训练首页使用 371，侧栏小卡用最近 42 天截取。
- `checkIns` 已有真实日期，可作为签到真实性校验；前端强度以 `activity` 为主，缺失日补 0。

### 组件边界

建议新增：

- `src/GZCTF/ClientApp/src/components/training/TrainingContributionCalendar.tsx`
- `src/GZCTF/ClientApp/src/components/training/trainingActivity.ts`

职责：

- `trainingActivity.ts`：生成日期区间、按周分桶、计算月份标签、计算强度等级。
- `TrainingContributionCalendar`：渲染日历格、tooltip、图例、今日标记。
- `TrainingCourseUI.tsx`：删除旧 `TrainingActivityHeatmap` 直接数组渲染，改调用新组件；保留同名 wrapper 可减少调用处改动。

### 强度算法

```ts
const score =
  (checkedIn ? 1 : 0) +
  Math.min(studyActions, 4) +
  completedChapters * 2 +
  acceptedChallenges * 3

level =
  score <= 0 ? 0 :
  score <= 1 ? 1 :
  score <= 3 ? 2 :
  score <= 6 ? 3 : 4
```

颜色：

- level 0：深色空格，与背景拉开对比。
- level 1：低透明松石绿。
- level 2：YINYU 绿色。
- level 3：高亮青绿。
- level 4：绿色 + 薰衣草紫边缘高光。

### 布局

- 培训首页概览卡展示 12 个月热力图。
- 侧栏签到卡展示最近 6 周压缩版，但仍使用同一组件的 `range="compact"` 模式。
- 宽度不足时不挤压单元格：横向滚动或显示最近 6 个月，不能变形为不规则网格。

## 方案三：排行榜与图表全局一致性和轻量性能优化

### 不做的事

- 不重写普通 CTF 排行榜表格。
- 不改变现有排行榜接口。
- 不改变积分计算口径。
- 不把所有图表统一迁移到一个库。

### 要做的事

1. 建立共享可视化令牌：
   - `--yy-viz-success`
   - `--yy-viz-info`
   - `--yy-viz-warning`
   - `--yy-viz-critical`
   - `--yy-viz-neutral`
   - `--yy-viz-grid`
   - `--yy-viz-tooltip-bg`
2. 统一进度条风格：
   - 培训进度条、CTF 题目进度条、大屏热度条统一改成 AWDP 风格：干净轨道、渐变填充、无噪点。
3. 统一 tooltip：
   - 背景、边框、字体、日期格式统一。
4. 排行榜性能守护：
   - `ScoreboardTable` 保持分页 30 条。
   - 宽表格保留顶部横向拖动条。
   - 表头和行组件继续 `React.memo`。
   - 重复查找改为 `Map`/`Set`，避免每格循环扫描时可优化。
5. 大屏动画守护：
   - 排行榜行位移动画只在排名或分数签名变化时触发。
   - 3D 和 GSAP 动画不共享 React state 高频更新。

## 实施阶段

### Phase 1：可视化基础整理

目标：不改变页面结构，先统一数据和样式基础。

任务：

- 梳理 `useCTFScreenData` 的数据口径，新增 `awdpSituationEvents` 派生字段。
- 提取可视化颜色令牌到现有 YINYU 样式文件。
- 给大屏数据 hook 增加 last-known-good / partial 状态字段。
- 修复大屏文件中的编码态文案，确保所有大屏状态可读。

验收：

- 普通排行榜、CTF 大屏、培训首页功能不变。
- `pnpm --dir src/GZCTF/ClientApp check` 通过。
- 接口失败时大屏不清空为白屏或空场景。

### Phase 2：培训真实日期签到热力图

目标：完成 GitHub 风格真实日期热力图。

任务：

- 后端 overview 返回最近 371 天活动数据，或新增 `days` 参数。
- 新建 `TrainingContributionCalendar` 和日期分桶工具。
- 替换培训首页概览卡和签到卡的旧 heatmap。
- 加入月份、星期、今日、图例、tooltip 和紧凑模式。

验收：

- 日期格数量与真实日历一致。
- 月份标签与当前年份/月份一致。
- 今日签到后当天格子立即点亮。
- 不同屏幕不会出现格子错位或异常换行。

### Phase 3：AWDP 大屏攻防态势 3D 层

目标：只在 AWDP 赛事的现有大屏中央加入互相攻防态势感知，不破坏排行榜，不影响 Jeopardy、Theory、Penetration 的大屏表达。

任务：

- 新建 `useAwdpAttackSituationModel`，将 AWDP 攻击日志、Patch 结果、服务上下线事件转为队伍节点、红色攻击边、修复发光状态和服务异常状态。
- 新建 `AwdpAttackSituationScene`，复用/迁移 `MetalScoreCity` 的渲染生命周期和交互。
- 在 `CTFScreenPage` 中根据 `game.gameType` 或 `isAwdpScoreGame` 条件启用 AWDP 态势层；非 AWDP 赛事继续使用现有 `MetalScoreCity`。
- 加入拖拽、缩放、点击队伍/热点、重置视角。
- 加入 WebGL fallback、reduced-motion、低性能降级。

验收：

- AWDP 攻击日志能显示为攻击队伍到被攻击队伍的红色方向流线。
- AWDP Patch/服务恢复能显示为对应队伍柱体本色发光，不出现绿色或薰衣草紫攻击态势色。
- 非 AWDP 赛事不出现红色攻击流线或 AWDP 攻防态势层。
- 拖拽、滚轮缩放、点击选择都可用。
- 关闭/重开页面没有 WebGL context 泄漏。
- 3D canvas 非空，窗口 resize 后比例正确。

### Phase 4：排行榜和图表一致性收口

目标：不重构成熟排行榜，只修复一致性和性能边界。

任务：

- 将大屏、培训、普通比赛中的进度条改为同一视觉规则。
- 大屏 tooltip、排行榜 tooltip、培训 heatmap tooltip 使用同一背景/边框/日期格式。
- 对 `ScoreboardTable` 中明显重复计算处做 Map 化优化。
- 对大屏 `ScoreChart` 和 `HeatmapPanel` 做静态 props/memo 检查，避免重复动画。

验收：

- 普通排行榜样式不被推倒。
- 同类进度条视觉一致。
- 大数据量排行榜保持分页和横向拖动。
- 无新增 TypeScript 错误。

## 质量与测试计划

### 静态检查

```bash
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp build
git diff --check
```

### 视觉检查

- 大屏桌面 1920x1080：3D 态势层非空，边栏榜单不遮挡。
- 大屏 1366x768：3D 画布不溢出，日志和榜单保持可读。
- 培训首页桌面：12 个月日期热力图月份/星期标签正确。
- 培训首页窄屏：热力图使用紧凑或横向滚动，不压缩成畸形。

### 交互检查

- 3D 场景拖拽旋转、滚轮缩放、点击队伍、重置视角。
- reduced-motion 模式下场景不持续巡航，数据仍可读。
- 培训签到成功后 overview 刷新，今日格点亮，按钮进入已签到态。
- 排行榜横向拖动条仍可控制宽表格。

### 数据不变量

- 大屏总队伍数来自 accepted participations 或 scoreboard items，不重复计算。
- 总分仍以现有 scoreboard 接口为准。
- AWDP 态势事件只用于 AWDP 大屏攻防可视化，不覆盖排行榜主分数口径。
- CTF Accepted、Theory 提交、Penetration Accepted 不进入 AWDP 攻击流线。
- 培训热力图每个日期最多一个格子，缺失日期补 0，不因接口缺数据造成日期错位。

### 性能检查

- 大屏 3D 场景桌面目标 50-60 FPS。
- 低性能或后台标签页暂停非必要动画。
- WebGL DPR 上限 1.5。
- 场景卸载后释放 geometry、material、texture、event listener、animation frame。
- 热力图 DOM 格子约 371 个，禁止每秒重算日期布局。

## 风险与处理

1. WebGL 与现有 GSAP 动画竞争主线程。
   - 处理：3D 场景状态用 ref，React 只在数据批量变化时同步；事件流限长。
2. AWDP 攻击日志缺少攻击队或被攻击队字段。
   - 处理：该事件不绘制红色队伍间流线，只进入右侧日志和统计；禁止伪造目标队伍或画成泛化热点。
3. Patch/服务恢复与攻击事件同时发生导致颜色混乱。
   - 处理：红色攻击流线在空中层，柱体同色修复光在柱体表面层；用渲染层级和透明度分开，禁止把修复显示成绿色成功光。
4. 签到热力图返回 371 天导致 payload 增大。
   - 处理：activity 是小整数模型，体量可控；如后续需要可加 `days` 参数。
5. 视觉统一误伤成熟排行榜。
   - 处理：排行榜只改共享 token 和局部细节，不改布局结构和数据流。
6. 编码态文字影响验收。
   - 处理：所有被触及的大屏/培训组件顺手修复可见文案，避免新视觉层承载乱码。

## 推荐执行顺序

推荐按 Phase 1 -> Phase 2 -> Phase 3 -> Phase 4 执行。原因是 Phase 1 建立数据与样式边界，Phase 2 风险低且能快速改善培训体验，Phase 3 是核心增量但风险最高，Phase 4 做全局一致性收口，避免一开始就改动成熟排行榜造成回归。

## 不纳入本轮

- 不重做全部大屏信息架构。
- 不替换普通比赛排行榜为 3D 或 WebGL。
- 不改变现有积分计算后端。
- 不新增复杂报表导出。
- 不引入新的重型图表库。
