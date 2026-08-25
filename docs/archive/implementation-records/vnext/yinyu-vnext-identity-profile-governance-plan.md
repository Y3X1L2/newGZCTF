# YINYU vNext 认证、个人主页与通用管理开发计划

更新日期：2026-07-17

## 1. 文档目的

本计划定义 vNext 下一阶段三个纵向切片的产品、接口、前端架构与验收边界：

1. 认证界面：登录、注册、找回、重置、邮箱验证与待激活状态。
2. 公开个人主页：公开身份、个人解题画像、成长趋势与业务经历。
3. 通用管理：用户、战队、学员组和系统设置。

实施时按以下优先级处理冲突：

1. `docs/yinyu-vnext-design-language-draft.md`
2. `docs/yinyu-vnext-page-interaction-api-spec.md`
3. `docs/yinyu-vnext-development-guardrails.md`
4. `docs/commercialization` 已落地契约
5. `D:/Work/newGZCTF-vnext-demo` 的布局比例、视觉层级与动效

Demo 只作为个人主页视觉和信息层级参考，不复制 mock 数据、固定用户 ID、旧 API 适配器或全局 CSS。

本计划保留已经确认的后续设计决策：

- 主题切换继续位于顶层上下文栏，认证页也只提供一个右上角主题入口。
- 账户抽屉不重复放置主题切换，不实现已取消的“语言与区域”模块。
- 未完成页面进入正式待建设状态，不回退旧 Mantine 页面。

## 2. 当前基线与主要缺口

### 2.1 已有基础

- vNext 已有独立 Token、日夜主题、公共壳、管理壳、抽屉、确认对话框、表格、分页与表单基础组件。
- 当前账户会话通过 `useCurrentAccount` 和统一缓存失效逻辑读取。
- 登录、注册、找回、重置、验证、验证码和 Portal SSO 后端接口已经存在。
- 用户、战队、学员组和基础配置管理接口已经存在，且后端已经有 `RolePolicy` 与 `UserManagementGuard`。
- CTF、培训、理论、AWDP 等业务数据已经能为个人统计提供事实来源。

### 2.2 必须补齐的缺口

| 领域 | 当前事实 | 本阶段处理 |
| --- | --- | --- |
| 认证路由 | `/account/*` 未由 vNext 实现，进入 Pending 页 | 完整接管认证路由 |
| 认证能力发现 | 客户端不知道是否开放注册、找回或 Portal 入口 | 新增公开能力 DTO |
| Recovery 隐私 | 服务端会用 404 区分账户不存在或未验证 | 改为统一成功语义，服务端消除枚举泄露 |
| Portal 登录按钮 | 现有 SSO 接口只接受 Portal 生成的 Token，不能作为登录发起页 | 增加可选 Portal 入口地址；未配置时不显示按钮 |
| 账户抽屉 | 当前会显示邮箱，且没有公开个人主页入口 | 去除敏感字段，接入个人摘要和 `/users/me` |
| 公开个人页 | 没有公开 DTO 和跨域统计接口 | 新增只读聚合服务和四组 API |
| 用户详情 | 现有详情 DTO 缺少状态和学员组，列表 DTO 又包含过多敏感字段 | 新增按权限裁剪的管理 DTO |
| 批量用户导入 | 当前接口会直接创建或覆盖账户，没有预检 | 增加预检令牌和原子提交 |
| 用户删除 | 没有关联影响预览，物理删除风险高 | 增加影响接口；封禁/停用作为首选操作 |
| 战队管理 | 列表缺少参赛数和影响信息，无法管理员纠错队长 | 增加管理摘要、详情和影响接口 |
| 学员组 | 缺少恢复归档、成员备注更新和最后管理者保护 | 补齐闭环并保留现有权限规则 |
| 系统设置 | `/api/admin/config` 只覆盖账户、品牌和实例时长 | 增加分组、脱敏、校验和独立保存契约 |
| 管理壳权限 | 当前 vNext 管理壳只允许 Admin，教师无法进入授权用户/学员组页面 | 改为能力驱动的路由与导航 |

## 3. 范围与非目标

### 3.1 本阶段范围

```text
/account/login
/account/register
/account/recovery
/account/reset
/account/verify
/account/confirm
/account/pending

/users/me
/users/:userId

/admin/users
/admin/teams
/admin/student-groups
/admin/settings
```

同时调整：

- 顶层账户入口和 `AccountDrawer`。
- 战队成员、排行榜用户、课程教师等已有用户名入口。
- 管理导航的角色可见性和 `/admin/system` 到 `/admin/settings` 的兼容重定向。

### 3.2 非目标

- 不实现关注、粉丝、私信、举报、自定义封面或虚构的全局排名。
- 不公开真实姓名、邮箱、手机号、学号、IP、IAM 绑定和最近登录时间。
- 不把队伍得分、理论分数、AWDP SLA 或培训进度混入“个人解题”。
- 不在公开个人页展示未结束比赛题目、Flag、答案、理论对错详情或课程作业答案。
- 不重构 TeamLab、渗透、节点、镜像、部署队列和日志页面。
- 不把旧账户页、旧管理弹窗或旧 CSS 包装进 vNext。

## 4. 总体架构

依赖方向固定为：

```text
Route Page -> Controller/Hook -> Feature Adapter -> Generated Client
```

额外约束：

- vNext TSX 不默认导入 `@Api`，不直接使用 `fetch`。
- 生成客户端只由 OpenAPI 生成，不手工编辑。
- DTO 兼容、枚举规范化、分页解包和错误映射只存在于 Adapter。
- 页面只负责路由参数、权限门禁和区块编排。
- 图表计算、统计维度映射、权限展示和表单转换使用可单测纯函数。
- 每个写操作必须等待服务器回读，不能以 HTTP 200 直接修改为最终成功状态。
- 页面覆盖 `loading / ready / empty / stale / error / forbidden`。

### 4.1 前端目录

```text
src/vnext/features/auth/
  api/authApi.ts
  authDomain.ts
  useAuthController.ts
  AuthShell.tsx
  AuthShell.module.css
  LoginPage.tsx
  RegisterPage.tsx
  RecoveryPage.tsx
  ResetPage.tsx
  VerifyPage.tsx
  PendingPage.tsx
  CaptchaField.tsx
  HashPowWorker.ts

src/vnext/features/profile/
  api/userProfileApi.ts
  profileDomain.ts
  skillDimensionRegistry.ts
  useUserProfileController.ts
  UserProfilePage.tsx
  UserProfilePage.module.css
  ProfileIdentity.tsx
  ProfileMetricStrip.tsx
  ProfileActivityHeatmap.tsx
  ProfileSkillMap.tsx
  ProfileGrowthChart.tsx
  ProfileHistory.tsx
  ProfileFacts.tsx

src/vnext/features/admin/users/
src/vnext/features/admin/teams/
src/vnext/features/admin/student-groups/
src/vnext/features/admin/settings/
```

### 4.2 后端边界

- 认证继续由 `AccountController` 处理，补充能力发现与隐私修复。
- 公开个人页进入 Identity 模块的 `UserProfileQueryService`，不让页面并发拼接多个业务控制器。
- 用户和战队管理保留现有 API 路径，复杂查询、影响评估和导入逻辑下沉为应用服务。
- 系统设置新增分组控制器或应用服务，不能把所有配置和秘密继续塞入通用 `ConfigEditModel`。

## 5. 认证界面

### 5.1 壳层与视觉

认证路由不渲染 `PlatformShell` 的全局侧栏，使用独立 `AuthShell`：

```text
┌──────────────────────────────────────────────────────────┐
│ 品牌与返回首页                               主题切换     │
│                                                          │
│ 一次性折面路径                    当前认证表单            │
│ 平台名称与短说明                  420-460px 稳定宽度      │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

- 品牌折面是背景区段，不把表单放入多层悬浮卡片。
- 页面进入时只播放一次 `360-700ms` 路径绘制；表单立即可聚焦和输入。
- 日间使用中性白/灰表面，夜间使用黑色画布和较亮的局部品牌绿，不做整页绿色背景。
- 768px 以下隐藏大面积装饰折面，表单单列展示；390px 下不横向滚动。
- `prefers-reduced-motion` 下直接显示最终折面。

### 5.2 登录 `/account/login`

字段顺序：用户名或邮箱、密码、验证码、找回密码、登录。

交互状态机：

```text
idle -> validating -> captchaPending -> submitting -> refreshingSession -> redirecting
                     \-> captchaError
                                  \-> rejected
                                  \-> networkError
```

- 密码为受控输入，显示/隐藏使用图标按钮。
- 验证码区域预留稳定高度，配置或挑战到达时不推动按钮跳动。
- 登录失败保留用户名、清空密码、刷新失效验证码并把焦点返回密码。
- 登录成功后先刷新账户缓存，确认服务器返回当前用户，再跳转。
- `returnUrl` 只接受站内绝对路径：必须以 `/` 开头，拒绝 `//`、反斜杠、协议和外部主机。
- 已登录用户进入登录页时，在会话确认后跳到安全 `returnUrl` 或首页，不能出现重定向循环。
- 统一身份按钮只有在后端返回 `portalSso.enabled=true` 且配置入口地址时才出现；按钮跳 Portal 发起页，不调用缺少 Token 的回调接口。

### 5.3 注册 `/account/register`

字段：邮箱、用户名、密码、确认密码、验证码。

- 根据公开能力 DTO 决定是否显示注册入口；关闭注册时显示正式不可用状态。
- 用户名长度、邮箱格式和密码一致性先在客户端校验，服务端错误仍为最终事实。
- 密码通过 `encryptApiData` 使用当前 `apiPublicKey` 加密；无公钥时保持现有明文 HTTPS 契约。
- `LoggedIn`：刷新会话后进入安全返回地址。
- `AdminConfirmationRequired`：进入 `pending?reason=approval`。
- `EmailConfirmationRequired`：进入 `pending?reason=email-verification`。
- Pending 不保存密码；“重新检查”返回登录并保留用户名提示，不伪造自动审批轮询。

### 5.4 找回、重置与验证

`/account/recovery`：

- 无论邮箱是否存在或已验证，成功响应与页面文案均为“若账户存在，将发送邮件”。
- 验证码失败、限流和服务不可用仍可明确提示，但不能泄露账户存在性。
- 该隐私必须在后端实现，不能只靠前端吞掉 404。

`/account/reset?token=&email=`：

- 首先校验参数存在和 Base64 可解码性，不在页面显示完整 Token。
- 输入新密码与确认密码；成功后使用 `replace` 清理 URL 中的 Token 并跳登录。
- 成功后按钮保持终态，不能重复提交同一 Token。

`/account/verify?token=&email=` 与 `/account/confirm?token=&email=`：

- 验证前只显示脱敏邮箱；缺少或损坏参数进入“链接无效”状态。
- 邮箱验证成功后刷新会话并进入安全返回地址。
- 邮箱变更确认要求已有会话；未登录时跳登录并保留原确认 URL。

`/account/pending`：

- 明确区分管理员审核、邮箱验证和未知等待状态。
- 提供重新登录检查、返回首页和已登录状态下的退出操作。
- 不使用无限 loading，不在 URL 中放密码、原始邮箱或身份凭据。

### 5.5 验证码实现

- `None`：不渲染无意义占位文本。
- `HashPow`：使用独立 Worker，Token 格式保持 `${challengeId}:${nonce}`；挑战最多复用 4 分钟，失败后重新获取。
- `CloudflareTurnstile`：使用现有官方组件，主题随平台主题切换，提交后按结果重置。
- 验证码组件只暴露 `getToken()`、`reset()` 和状态，不持有登录或注册文案。
- 不复用旧 Mantine `Captcha`、`HashPow` 或其视觉组件。

### 5.6 认证 API 调整

新增公开接口：

```text
GET /api/account/capabilities
```

建议响应：

```ts
type AccountCapabilities = {
  allowPasswordLogin: boolean
  allowRegister: boolean
  passwordRecoveryAvailable: boolean
  emailConfirmationRequired: boolean
  portalSso: {
    enabled: boolean
    entryUrl?: string
  }
}
```

后端调整：

- `Recovery` 对不存在、未验证账户返回相同的公开响应并保留内部审计结果。
- Portal 配置增加可公开的登录发起地址；回调继续使用 `/api/account/portal-sso`。
- 登录、注册和重置保持现有加密字段语义。
- 能力接口不返回 Portal ProfileEndpoint、密钥、邮箱域白名单明细或其他内部配置。

## 6. 公开个人主页

### 6.1 路由与入口

- `/users/me`：会话确认后 `replace` 到 `/users/{currentUserId}`；未登录则进入登录并设置 returnUrl。
- `/users/:userId?tab=overview|challenges|games|training&window=365d`。
- 战队成员、积分榜用户、课程教师、公开参赛记录和账户抽屉中的用户名接入公开页。
- 教师查看学员作业详情仍走课程权限页，公开主页不能绕过课程授权。

### 6.2 页面结构

个人主页参考 demo 的信息顺序，但改为设计总文档规定的“折域”页面结构：

```text
┌──────────────────────────────────────────────────────────────┐
│ 身份横幅：头像 / 用户名 / 角色 / 简介 / 本人编辑             │
├──────────────────────────────────────────────────────────────┤
│ 个人解题 / 提交 / 正确率 / 参赛 / 课程 / 活跃天数            │
├────────────────────────────────────────┬─────────────────────┤
│ 概览 / 做题 / 赛事 / 培训              │ 身份事实            │
│                                        │ 加入时间            │
│ 52 周活跃热力图                        │ 公开战队            │
│ 分类画像：雷达 + 真实数据表            │ 授课课程/角色信息   │
│ 成长折线                               │                     │
│ 经历时间线                             │                     │
└────────────────────────────────────────┴─────────────────────┘
```

- 页面宽度 `1240-1320px`，主辅栏为 `minmax(0, 1fr) / 300px`。
- 身份横幅是页面区段，不是悬浮大卡片；背景折面由用户 ID 稳定生成。
- 头像和用户名为第一层级，角色使用小型语义标签。
- 简介最多三行；本人显示“编辑资料”，他人不显示关注或私信。
- 不展示 demo 中硬编码的真实姓名、平台排名和虚构百分位。
- Tab 左对齐，只替换主内容；身份区、指标带和右侧事实保持稳定。

### 6.3 六项指标口径

1. 个人解题：用户亲自提交并正确的不同规范题目数。
2. 提交：可计入个人统计的可判定提交总数。
3. 正确率：正确提交数 / 可判定提交数，并显示样本数。
4. 参赛场次：有效且允许公开的参与记录数。
5. 课程：本人显示审核通过与完成数；公开访客只显示安全汇总，教师显示授课课程。
6. 活跃天数：当前时间窗内存在有效学习或竞赛事件的自然日数。

以下内容不得计入个人解题：

- 队友成功提交带来的队伍解题。
- 只存在队伍关系、不能证明提交人的 `FirstSolve`。
- 理论分数、签到次数、AWDP SLA/修补/服务分。
- 无法审计到个人的历史数据。

### 6.4 活跃度、画像、趋势和经历

活跃热力图：

- 默认最近 52 周，可切自然年；使用 CSS Grid 和真实日期，不用持续 Canvas。
- 单元格保持接近正方形，按容器宽度决定完整周数，不补多余孤立方块。
- Tooltip 展示日期及 CTF、培训、理论、AWDP 和渗透活动分类。
- 首页只保留短期摘要；个人页提供更长窗口和分类明细，避免完全重复。

分类解题画像：

- `SkillDimensionRegistry` 统一映射 Web、Pwn、Reverse、Crypto、Forensics/IR、Pentest/OSINT、Misc/AI/PPC 和 Other。
- 每个维度返回 `solved / attempted / submissions / acceptedSubmissions / successRate / benchmarkP90 / radarValue`。
- 雷达按平台 P90 基准归一化，不按用户自身最大值归一化。
- 雷达与真实数据表 Hover/键盘焦点联动；样本少于 3 道标记“样本不足”。
- 移动端和 200% 缩放使用条形维度列表，不要求横向拖动雷达。

成长趋势：

- 使用轻量 SVG 折线，不使用柱状图。
- 默认累计个人解题；可切最近 90 天，一次最多显示四个分类。
- 数据更新时局部插值，不在每次 Tab 切换后重放完整动画。

经历时间线：

- 显示公开赛事、结课、授课和可验证里程碑。
- 比赛项明确写“团队成绩”，包含比赛、赛制、队伍、结束时间和随队结果。
- 理论分数、培训作业答案和详细课程进度默认不公开。

### 6.5 个人主页 API

```text
GET /api/users/{userId}
GET /api/users/{userId}/overview?window=365d
GET /api/users/{userId}/activity?from=&to=
GET /api/users/{userId}/history?type=&cursor=&count=
GET /api/users/me/private-overview
GET /api/account/summary
```

首屏只请求身份与 overview；activity 和 history 在进入视口后延迟加载。

核心 DTO：

```ts
type PublicUserProfile = {
  id: string
  userName: string
  role: Role
  bio?: string
  avatar?: string
  registeredAt: number
  publicTeam?: { id: number; name: string; avatar?: string }
  taughtCourses?: Array<{ id: number; title: string }>
}

type UserProfileOverview = {
  window: string
  generatedAt: number
  metrics: {
    solved: number
    submissions: number
    acceptedSubmissions: number
    successRate: number
    gameCount: number
    courseCount?: number
    activeDays: number
  }
  dimensions: UserSkillDimension[]
  trend: Array<{ date: string; cumulativeSolved: number; delta: number }>
}
```

隐私由服务端裁剪，公开 DTO 不定义邮箱、手机号、真实姓名、学号、IP、登录时间和 IAM 字段。

### 6.6 聚合与性能

- 新建 `UserProfileQueryService`，按 CTF、培训、AWDP 和渗透数据源执行数据库聚合后合并，不加载完整提交实体。
- CTF 个人解题只读取 `Submission.UserId` 对应事实，并排除测试、隐藏和未结束比赛敏感明细。
- 课程实例题使用 `TrainingCourseSubmission`；详细课程进度只在本人私有接口返回。
- AWDP 只把 `AwdpFlag.SubmittedByUserId` 的攻击命中作为个人活动，团队 SLA 不进入个人画像。
- 渗透提交保持独立经历，不与普通 CTF 正确率混算。
- 公共身份与 overview 缓存 5 分钟并支持 ETag；平台分类 P90 基准缓存 1 小时。
- 私有 overview 不进入公共缓存。
- history 使用 `(occurredAt, type, id)` 稳定游标，不使用大 offset。

建议补充索引：

```text
Submission(UserId, SubmitTimeUtc, ChallengeId, Status)
PenetrationSubmission(UserId, SubmittedAt)
AwdpFlag(SubmittedByUserId, FirstSubmittedAt) WHERE SubmittedByUserId IS NOT NULL
```

已有 `TrainingCourseSubmission(UserId, SubmittedAt)`、训练进度和 `UserParticipation(UserId, GameId)` 索引继续复用。

### 6.7 账户抽屉联动

- 顶部只显示头像、用户名、角色和简介，不显示邮箱、手机号或学号。
- 接入 `GET /api/account/summary`，展示个人解题、活跃天数和运行实例/待审核摘要。
- “继续进行”最多三项，按即将结束的考试/比赛、运行实例、最近课程排序。
- 增加“个人主页”，保留“账户设置”和按权限显示的管理入口。
- 主题切换仍在顶栏；抽屉底部只保留退出登录。
- 抽屉继续使用固定头尾、中部独立滚动和可恢复焦点的 vNext 基础实现。

## 7. 通用管理

### 7.1 管理壳与权限模型

管理壳改为“教师及以上可进入，具体页面按能力开放”：

| 页面 | 教师 | 管理员 | 超级管理员 |
| --- | --- | --- | --- |
| 用户管理 | 仅授权学员组内学生 | 学生、教师 | 全部角色 |
| 学员组 | 自己管理的组和成员 | 全部组、教师分配 | 全部 |
| 战队管理 | 不可见 | 可见 | 可见 |
| 系统设置 | 不可见 | 品牌和普通策略 | 全部含危险设置 |

- 前端可见性只改善体验，服务端继续使用 `RolePolicy` 和 `UserManagementGuard` 作为最终门禁。
- 导航项定义 `minimumRole/capability`，不再只用 `implemented`。
- 教师访问无权限管理 URL 时显示明确 Forbidden，不跳到首页或 404。
- 桌面保留分组侧栏；窄屏使用现有左侧导航抽屉。

### 7.2 用户管理 `/admin/users`

URL 状态：

```text
?q=&role=&group=&status=&page=&user=
```

页面结构：

1. 紧凑页头：用户总数、待激活、已停用、当前可管理范围。
2. 工具栏：搜索、角色、学员组、状态、批量导入。
3. 表格：用户、角色、真实姓名、学号、组、注册时间、最后访问和状态。
4. 右侧宽抽屉：身份、组织、账户状态和危险操作。

隐私：

- 教师只获取用户名、头像、真实姓名、学号、授权组和学习所需状态。
- 邮箱、手机号、IP 和最近登录只返回给管理员以上。
- 超级管理员信息只允许超级管理员读取或修改。

写操作：

- 编辑用户名、邮箱、资料、角色和学员组后，重新读取详情与当前列表行。
- 角色修改前显示将获得或失去的能力；最后一个超级管理员不能降级。
- 重置密码只显示一次，离开对话框后不保存在页面状态、URL 或日志。
- “停用/解除停用”作为主安全操作；物理删除放入危险区。
- 删除前读取关联影响，展示队长身份、队伍、参赛、提交、课程、实例和 IAM 绑定数量；有阻断项时禁止删除。

批量导入：

```text
上传 CSV/JSON -> 浏览器格式解析 -> 服务端预检 -> 错误表 -> 确认提交 -> 服务器回读
```

- 预检用户名、邮箱、学号、角色、组和队伍冲突。
- 服务端返回预检令牌和逐行错误；提交必须原子化，不能沿用当前“重复用户即覆盖并重置密码”的行为。
- 页面只显示服务端确认创建/更新的数量和失败原因。

建议新增 API：

```text
GET  /api/admin/users/{id}/detail
GET  /api/admin/users/{id}/impact
POST /api/admin/users/{id}/suspension
DELETE /api/admin/users/{id}/suspension
POST /api/admin/users/import/preview
POST /api/admin/users/import/commit
GET  /api/admin/users/options?role=&keyword=&count=20
```

停用优先使用 Identity Lockout 和安全戳失效，不再通过覆盖角色丢失原权限信息；现有 `Role.Banned` 作为兼容状态读取。

### 7.3 学员组管理 `/admin/student-groups`

URL 状态：

```text
?q=&archived=&group=
```

桌面布局：左侧 `280-320px` 组列表，右侧为组详情；移动端先显示列表，选择后进入详情。

详情区域：

- 组名称、说明、状态和更新时间。
- 成员 Tab：学生、真实姓名、学号、备注、加入时间、移除。
- 教师 Tab：所有者、协作者和角色；只有管理员以上可分配教师。
- 操作：编辑、归档、恢复归档、添加成员、更新备注。

规则：

- 添加学生或教师使用搜索选择，不手填 GUID。
- 教师只能管理自己负责的组；管理员可管理全部组。
- 一个学生可进入多个组，但同一组内不可重复。
- 归档不删除成员关系，默认列表隐藏归档组。
- 不能移除最后一个所有者或让组失去管理人。
- 成员和教师变更后回读组详情与列表计数。

需补 API：

```text
POST /api/admin/student-groups/{id}/restore
PUT  /api/admin/student-groups/{id}/members/{studentId}
```

并为现有 manager 删除增加最后所有者保护。

### 7.4 战队管理 `/admin/teams`

URL 状态：

```text
?q=&locked=&page=&team=
```

表格列：战队、队长、成员数、锁定状态、参赛数、更新时间和操作。

右侧抽屉只提供管理员纠错能力：

- 修改名称、简介和锁定状态。
- 查看成员并纠正队长。
- 在明确影响后移除错误成员。
- 查看参与比赛和运行实例摘要。
- 删除战队前展示成员、参赛、实例、提交和附件/Writeup 影响。

不在管理抽屉复制普通 `/teams` 的申请、邀请和成员日常协作界面。

建议新增 API：

```text
GET /api/admin/teams?count=&skip=&keyword=&locked=
GET /api/admin/teams/{id}/detail
GET /api/admin/teams/{id}/impact
PUT /api/admin/teams/{id}/captain
DELETE /api/admin/teams/{id}/members/{userId}
```

### 7.5 系统设置 `/admin/settings`

设置分为六个稳定区段：

1. 品牌：标题、副标题、描述、页脚、Logo。
2. 注册与认证：注册策略、邮箱确认、验证码、Portal SSO。
3. 实例与网络：实例时长、公网入口、端口池和代理模式。
4. Registry：镜像地址、命名空间、认证和上传限制。
5. 邮件：SMTP、发件人和连接测试。
6. 高级：API 加密、调度和需要重启的设置。

页面使用左侧区段导航和单一编辑区，不把六组字段放入一个超长卡片。URL 使用：

```text
/admin/settings?section=brand|auth|runtime|registry|mail|advanced
```

保存规则：

- 每组独立读取、校验和保存，不共用一个提交按钮。
- 表单使用版本号或 ETag；后台配置被其他人修改时返回 409，页面显示差异并要求重新加载。
- 密码、Registry Auth、SMTP 密码和 SSO 内部地址不回传原值，只返回 `configured` 状态；空输入表示保持不变，显式清除需要二次确认。
- 每个字段显示 `Immediate / WorkerRefresh / RestartRequired` 生效方式。
- 公网地址、端口池、Registry、Portal SSO 和 API 加密保存前展示影响模块。
- Logo 先本地预览，上传后回读 `logoUrl`；重置需要确认。
- Registry、SMTP 和公网入口提供独立测试动作，测试结果不等于配置已保存。
- 所有设置变更写结构化审计日志，秘密字段只记录“已更新”，不记录值。

建议 API：

```text
GET/PUT /api/admin/settings/brand
GET/PUT /api/admin/settings/auth
GET/PUT /api/admin/settings/runtime
GET/PUT /api/admin/settings/registry
GET/PUT /api/admin/settings/mail
GET/PUT /api/admin/settings/advanced
POST    /api/admin/settings/registry/test
POST    /api/admin/settings/mail/test
POST    /api/admin/settings/runtime/validate
```

现有 `/api/admin/config` 和 Logo API 保持兼容；vNext Adapter 可在迁移期组合旧接口，但页面不感知来源。`/admin/system` 使用 replace 重定向到 `/admin/settings`。

## 8. 状态、交互与响应式统一规则

- 搜索输入使用 250-350ms debounce；翻页、筛选和选中实体写入 URL。
- 打开抽屉不清空列表滚动和筛选；浏览器后退先关闭抽屉。
- 抽屉宽度由共享语义尺寸控制，头尾固定，中部独立纵向滚动，不产生水平滚动条。
- 表格在 1366px 保持核心列；次要字段按 `desktop/wide` 层级隐藏并进入详情。
- 390px 下管理表格降级为紧凑实体列表，不缩小到不可读字体。
- 主按钮、危险按钮、状态文字和焦点不能只靠颜色区分。
- 抽屉、Tab 和页面切换使用现有 vNext motion token；禁止 `transition: all` 和持续装饰动画。
- 个人页身份路径、雷达和折线每个视图最多一个表达性动效焦点。
- 主题切换不改变组件尺寸、行高和列数，避免日夜模式布局抽动。

## 9. 实施顺序

### A. 契约与测试基线

- [ ] 固化认证、个人主页和通用管理的 OpenAPI 差异清单。
- [ ] 建立学生、教师、管理员、超级管理员和被停用用户测试数据。
- [ ] 建立个人/团队提交混合数据，验证统计归属。
- [ ] 为四类管理路由建立权限矩阵集成测试。
- [ ] 记录当前首屏请求数、数据库查询耗时和 bundle 基线。

退出条件：接口名、DTO、权限和隐私规则有测试支撑，后续页面不需要猜测契约。

### B. 认证闭环，P0

- [x] 新增账户能力接口并修复 Recovery 枚举泄露。
- [x] 实现 `authApi`、安全 returnUrl 和验证码领域测试。
- [x] 实现 `AuthShell`、登录、注册、找回、重置、验证和 Pending。
- [x] 接入 API 加密、会话刷新和账户缓存失效。
- [x] 验证 None、HashPow、Turnstile 三种验证码状态。
- [x] 验证 Portal 发起页、回调和站内安全跳转。

退出条件：未登录用户可在 vNext 完成所有认证流程，受限页面不会进入无效 Pending 页。

进度记录（2026-07-17）：认证纵向切片已完成自动化和本地浏览器验收。既有
`/api/account/portal-sso`、Portal Profile 解析、稳定外部身份绑定、自动注册和登录逻辑未改动；
相关单元回归 7 项、认证集成测试 18 项和前端全量 110 项通过。浏览器已验证注册、会话回读、
安全跳转、退出、重新登录、错误登录、无效链接、Pending、日夜主题及移动/桌面布局。
`192.168` 网段暂不可达，因此未重复执行真实 IAM Token 现场联调；此前已验证可用的旧 SSO
链路继续作为部署基线，网络恢复后只需做一次回归，不阻塞后续个人主页开发。

### C. 公开个人主页与账户抽屉

- [x] 建立 `UserProfileQueryService`、公开 DTO、索引和缓存策略。
- [x] 完成个人解题、团队成绩、培训、AWDP 和渗透数据归属测试。
- [x] 实现身份、overview、activity、history 和 private-overview API。
- [x] 实现个人页 Controller、身份区、指标、热力图、画像、折线和时间线。
- [x] 实现四个 Tab、时间窗、延迟加载和移动端条形画像。
- [x] 更新账户抽屉并接入 `/users/me`。
- [x] 接通战队成员和课程教师用户名入口；确认现有积分榜以战队为最小实体，不伪造缺少用户 ID 的个人入口。

退出条件：公开访问不泄露敏感信息；本人和他人页面差异正确；所有统计可由原始事实复算。

进度记录（2026-07-17）：C 阶段已完成自动化与本地真实数据验收。个人主页使用公开身份、六项指标、52 周/90 天活动热力图、八维分类画像、累计解题折线和游标分页经历；本人额外显示私有学习摘要。账户抽屉不再显示邮箱，改为真实统计、继续事项和个人主页入口。`/users/me` 在账户解析后使用 replace 规范化为用户 GUID 路由。

统计归属由后端统一保证：CTF 仅统计 `Submission.UserId` 对应的本人事实；课程实例题计入个人解题与分类画像；理论、AWDP 和渗透保持独立活动类型；隐藏、测试和未结束赛事不公开。集成测试 3 项通过，前端全量 114 项测试、strict TypeScript、lint、架构检查、生产构建和 bundle 预算通过；浏览器已验证登录回跳、日夜主题、延迟加载、账户抽屉及 390/1366/1920 宽度无页面级横向溢出。

### D. 管理壳、用户与学员组

- [ ] 将管理壳改为能力驱动，并验证教师边界。
- [ ] 实现用户列表、筛选、分页和详情抽屉。
- [ ] 实现用户编辑、停用、密码重置和删除影响。
- [ ] 实现批量导入预检与原子提交。
- [ ] 实现学员组分栏页、成员和教师管理。
- [ ] 补齐归档恢复、备注更新和最后管理者保护。

退出条件：教师只能操作授权学员；管理员写操作均有服务器回读和审计记录。

### E. 战队管理

- [ ] 增加战队管理摘要、筛选、详情和影响接口。
- [ ] 实现表格、详情抽屉、锁定、队长纠错和成员纠错。
- [ ] 实现删除影响确认和阻断展示。

退出条件：管理员可完成必要纠错，但日常战队操作仍留在普通战队页。

### F. 系统设置

- [ ] 建立六组脱敏 DTO、权限、版本冲突和生效方式。
- [ ] 实现分组 Adapter、Controller 和独立保存表单。
- [ ] 实现 Logo、Registry、邮件和运行网络测试动作。
- [ ] 实现危险变更影响提示与结构化审计。
- [ ] 添加 `/admin/system` 兼容重定向。

退出条件：普通配置与危险配置互不覆盖，秘密不回显，写入后显示服务器真实配置版本。

### G. 全量回归与收尾

- [ ] 删除本阶段对应的 Pending 导航和失效兼容代码。
- [ ] 更新页面/API 规范与延后缺口文档。
- [ ] 执行前后端自动化、生产构建和 bundle 预算。
- [ ] 在真实服务器执行认证、个人页和四个管理页面的浏览器验收。
- [ ] 保留日间/夜间及 390、1366、1920、2560 宽度关键截图。

## 10. 测试与验收

### 10.1 后端自动化

- 认证：安全 returnUrl、注册三种状态、验证码、密码加密、Recovery 防枚举、Portal 禁用/拒绝/成功。
- 个人页：本人/他人隐私裁剪、队友提交不计个人解题、隐藏和未结束比赛不泄露、游标稳定、ETag。
- 用户：教师组边界、角色升级限制、最后超级管理员保护、停用使会话失效、删除影响阻断。
- 学员组：可见范围、重复成员、归档恢复、最后所有者保护。
- 战队：锁定、队长纠错、参与关系影响和删除阻断。
- 设置：分组权限、秘密脱敏、409 版本冲突、校验失败不保存、审计日志不含秘密。

### 10.2 前端自动化

- Adapter 响应解析、错误映射和 URL 查询状态。
- 登录/注册/重置状态机与重复提交保护。
- returnUrl、Base64 参数和外部 URL 拒绝测试。
- 动态技能维度、雷达归一化、热力图日期网格和趋势数据纯函数。
- 角色导航、表格列裁剪、抽屉焦点和关闭后焦点恢复。
- 批量导入预检、设置脏状态和服务器版本冲突。

### 10.3 真实浏览器流程

1. 错误密码、正确密码、退出、返回受限路由。
2. 开放/关闭注册；三种注册结果；邮箱验证和密码重置。
3. HashPow、Turnstile 和无验证码登录。
4. Portal 用户首次自动创建、再次登录绑定同一账户和安全 returnUrl。
5. 查看本人、其他学生、教师和不存在用户主页；确认敏感字段不出现在响应和 DOM。
6. 教师只看到授权组学生，并能修改允许字段但不能提升角色。
7. 管理员创建测试用户、停用、恢复、重置密码并验证旧会话失效。
8. 学员组添加/移除学生、分配教师、归档和恢复。
9. 战队锁定、队长纠错和删除影响检查。
10. 分组修改一个可回滚设置，刷新后确认服务器回读；并执行 Registry/SMTP 测试。

### 10.4 视觉与可访问性

- 390、768、1024、1366、1920、2560 宽度无页面级横向滚动、遮挡和文字截断。
- 浏览器 200% 缩放可完成所有表单和抽屉操作。
- 日间、夜间颜色语义一致，夜间不出现大面积刺眼绿色。
- 键盘可完成认证、Tab、热力图信息读取、表格行、抽屉和确认操作。
- 错误提示与字段建立 `aria-describedby`；Dialog/Drawer 有标题、焦点圈和 Esc 行为。
- reduced motion 下停止路径绘制、雷达展开和抽屉位移动画，但状态变化仍可识别。

### 10.5 性能门槛

在 10,000 用户、1,000,000 条 CTF 提交的基准数据上：

- 公开身份与 overview 首屏不超过 2 个业务请求，不产生跨用户 N+1。
- overview 数据库聚合目标 p95 小于 500ms；公共缓存命中目标 p95 小于 100ms。
- 用户列表 50 条分页和常用筛选目标 p95 小于 300ms。
- activity/history 不阻塞首屏，离开视口后不重复请求。
- 个人页图表不使用逐帧 React state；滚动过程中不触发持续全页重渲染。
- 新路由继续通过既有 bundle 预算，图表优先使用轻量 SVG/CSS，不为个人页引入新的大型图表运行时。

性能目标以基准测试结果为准；未达到时先修查询、索引和渲染边界，不通过提高超时掩盖。

## 11. 提交拆分

建议保持以下可独立回退的提交顺序：

1. `docs: define identity profile and governance phase`
2. `feat(auth-api): expose account capabilities and harden recovery`
3. `feat(auth-ui): implement vnext authentication flows`
4. `feat(profile-api): add public user profile projections`
5. `feat(profile-ui): implement public profile and account summary`
6. `feat(admin-users): add user governance and import preview`
7. `feat(admin-groups): complete student group governance`
8. `feat(admin-teams): implement team correction workspace`
9. `feat(admin-settings): add versioned grouped settings`
10. `test: complete identity and governance acceptance`

每个提交在进入下一项前必须通过对应自动化测试；每个纵向切片完成后更新本文第 9 节进度，避免长任务上下文丢失。

## 12. 完成定义

本阶段只有同时满足以下条件才算完成：

- 所有列出的正式路由均由 vNext 实现，不加载旧页面。
- 权限、隐私和统计归属由后端保证，前端没有敏感字段隐藏式“保护”。
- 认证、个人主页和通用管理均覆盖完整状态与真实写流程。
- strict TypeScript、lint、架构检查、单元测试、集成测试、生产构建和 bundle 预算全部通过。
- 日间、夜间、四档桌面/移动视口和 200% 缩放验收通过。
- 浏览器控制台无运行错误、未处理 Promise、布局溢出和持续无意义请求。
- 关键写操作通过服务器回读确认，审计日志能定位操作者、对象、结果和关联 ID。
