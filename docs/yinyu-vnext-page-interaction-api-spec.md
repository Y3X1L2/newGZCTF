# YINYU vNext 页面交互与 API 规格

本文描述当前正式 vNext 前端的页面边界、核心交互和 API 适配位置。它用于开发导航和评审，不替代实时 OpenAPI；具体请求方法、字段和状态码以运行中的 `/api/open/v1`、生成客户端和后端 Controller 为准。

## 1. 事实来源

- 路由：`src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx`
- 导航：`src/GZCTF/ClientApp/src/vnext/app/shell/moduleRegistry.ts`
- 页面实现：`src/GZCTF/ClientApp/src/vnext/features/`
- 生成客户端：`src/GZCTF/ClientApp/src/api/`
- 外部 API：[Open API v1 使用指南](./commercialization/open-api-v1-guide.md)
- 已知缺口：[vNext 已知契约与验收缺口](./yinyu-vnext-deferred-contract-gaps.md)

页面不得绕过 feature adapter 直接依赖生成客户端。文档与源码冲突时先验证运行行为，再修正文档。

## 2. 全局交互契约

### 2.1 路由和状态

- 可分享的筛选、标签、页码和当前题目使用 URL 表达；临时弹窗、悬停和输入中状态留在组件内部。
- 路由参数先解析和校验，再进入 feature controller。无效 ID、无效标签和无权限资源必须显示明确错误或回到合法默认状态。
- 页面切换保留壳层尺寸和滚动条槽位，禁止通过整页重新挂载制造抽动。
- 返回操作优先回到业务上下文；没有有效来源时回到所属列表，而不是依赖硬编码浏览器历史长度。

### 2.2 读取状态

所有远程读取统一表达：

| 状态 | 页面行为 |
| --- | --- |
| 首次加载 | 使用与最终布局同尺寸的骨架或安静加载态 |
| 空数据 | 说明真实原因并仅提供当前用户可执行的下一步 |
| 失败 | 显示可读错误、关联信息和重试入口，不把空数组当成功 |
| 刷新 | 尽量保留已成功数据，局部标记刷新状态 |
| 权限拒绝 | 不泄露受限内容，显示统一无权限结果 |

### 2.3 写操作

- 创建、保存、提交、启动、停止和删除在请求期间禁用重复触发。
- 危险操作使用确认对话框，并明确对象、影响和是否可恢复。
- 异步操作使用服务器返回的 operation、ticket 或领域状态轮询/推送；禁止固定等待若干秒后推断完成。
- 成功后按服务端回读刷新，不能仅修改本地数字制造成功状态。
- 表单先从输入事件复制值，再进行异步校验或状态更新，避免读取已失效 DOM 引用。

### 2.4 抽屉、弹窗和工作台

- 抽屉用于不离开上下文的详情和辅助管理；全页编辑用于字段多、需要预览或存在复杂工作流的场景。
- 抽屉宽度使用响应式上限，正文独立纵向滚动，遮罩与位移动画同一时序完成。
- 弹窗只承载短任务，不把大型编辑器、导入器或长表格塞入不可滚动容器。
- 工作台的主操作位于内容流中可见位置，不能因侧栏、固定头部或视口高度而不可达。

## 3. 页面路由

### 3.1 认证与个人

| 路由 | 页面职责 | 主要交互 |
| --- | --- | --- |
| `/account/login` | 本地登录与 Portal SSO 入口 | 登录、保留合法返回地址、跳转 SSO |
| `/account/register` | 本地注册 | 校验账号资料、提交、处理审核状态 |
| `/account/recovery` | 发起账号恢复 | 提交邮箱并显示非枚举式结果 |
| `/account/reset` | 重置凭据 | 校验令牌、设置新密码 |
| `/account/verify`、`/account/confirm` | 邮箱验证与变更确认 | 显示真实确认结果 |
| `/account/pending` | 等待审核 | 刷新账号状态 |
| `/settings/:section?` | 个人资料、安全与偏好设置 | 分区保存、密码与账号安全操作 |
| `/users/:userId` | 公开或授权范围内的用户画像 | 查看统计、经历与可见资料 |

认证请求集中在 `features/auth/api/authApi.ts`，个人设置和用户页分别使用 `settingsApi.ts` 与 `profile/api/userProfileApi.ts`。Portal SSO 由后端验证 token 并建立本平台会话，前端不能信任 URL 中的用户字段。

### 3.2 首页、公告、战队

| 路由 | 页面职责 | 主要交互 |
| --- | --- | --- |
| `/` | 平台概览和主要入口 | 读取真实首页聚合、进入赛事/培训/练习 |
| `/posts` | 公告列表 | 分页和进入详情 |
| `/posts/:postId` | 公告正文 | 渲染可信 Markdown、返回列表 |
| `/teams` | 战队浏览和当前队伍 | 搜索、分页、查看队伍详情 |

对应适配器为 `home/homeApi.ts`、`posts/postsApi.ts` 和 `teams/teamApi.ts`。列表容器宽度固定一致，空简介和超长文本不得改变相邻卡片结构。

### 3.3 赛事与竞赛工作台

| 路由 | 页面职责 | 主要交互 |
| --- | --- | --- |
| `/games` | 赛事发现 | 状态筛选、搜索、进入赛事 |
| `/games/:gameId` | 赛事详情 | 报名/加入、查看规则与状态、进入可用赛制 |
| `/games/:gameId/challenges` | CTF 解题工作台 | 展开多分类、选择题目、附件、实例、Flag 提交 |
| `/games/:gameId/scoreboard` | CTF 排名 | 刷新排名、查看趋势与队伍结果 |
| `/games/:gameId/theory` | 理论考试 | 草稿保存、最终提交、按规则重试 |
| `/games/:gameId/theory-scoreboard` | 理论排名 | 查看个人或队伍理论成绩 |
| `/games/:gameId/awdp` | AWDP 选手工作台 | 服务状态、攻击、补丁、重置与恢复 |
| `/games/:gameId/pentest` | TeamLab/渗透场景 | 启动运行、访问资产、提交目标、查看状态 |

赛事通用读取和 CTF 操作经 `games/gamePlayerApi.ts`；理论、AWDP 和 TeamLab 分别经各自 feature 下的 API adapter。CTF 中题目正文、附件、实例控制和 Flag 提交位于中央任务流，右栏只放比赛上下文和辅助信息。动态实例入口必须等待服务器报告 Ready。

### 3.4 自主练习

| 路由 | 页面职责 | 主要交互 |
| --- | --- | --- |
| `/practice` | 练习概览 | 查看进度、推荐和最近活动 |
| `/practice/browse` | 练习题库 | 分类、标签、状态筛选与分页 |
| `/practice/challenge/:id` | 单题练习 | 附件、实例、Flag 提交和结果回读 |
| `/practice/stats` | 练习统计 | 查看分类分布、趋势和完成情况 |

练习模块已实现，前端统一使用 `features/practice/api/practiceApi.ts`，后端开放接口位于 Exercise 模块。练习事实不能从比赛页面的临时状态推导；统计和历史只展示接口真实返回值。

### 3.5 培训

| 路由 | 页面职责 | 主要交互 |
| --- | --- | --- |
| `/training` | 课程目录与学习活动 | 浏览课程、查看自己的学习活动 |
| `/training/courses/new` | 创建课程 | 编辑基础信息、海报和报名策略 |
| `/training/courses/:courseId` | 课程详情 | 课程介绍、章节、资源、进度、学员、教师、题库和环境 |
| `/training/courses/:courseId/edit` | 编辑课程 | 更新课程级配置 |
| `/training/courses/:courseId/chapters/new` | 新建章节 | Markdown 编辑与预览 |
| `/training/courses/:courseId/chapters/:chapterId/edit` | 编辑章节 | 回读并修改章节内容 |
| `/training/courses/:courseId/challenges/new` | 新建课程实例题 | 配置题目、附件、环境和 Flag |
| `/training/courses/:courseId/challenges/:challengeId/edit` | 编辑课程实例题 | 回读全部配置并再次保存 |
| `/training/courses/:courseId/chapters/:chapterId` | 学习章节 | 章节树、正文、实验、练习、目录和完成条件 |
| `/training/courses/:courseId/chapters/:chapterId/theory` | 章节理论作业 | 草稿、提交、结果和按规则重试 |
| `/training/courses/:courseId/chapters/:chapterId/theory-edit` | 配置章节理论作业 | 选题、规则配置、保存与回读 |

学习端使用 `training/api/trainingLearnerApi.ts`、`training/chapter/trainingChapterApi.ts` 和 `training/api/courseEnvironmentApi.ts`；课程管理写操作集中在 `training/admin/trainingAdminApi.ts`。课程详情的标签状态必须规范化，返回编辑页后不能让遗留查询参数锁死其他标签。学员学习详情使用可滚动的右侧抽屉，并按服务端分页读取。

### 3.6 平台管理

| 路由 | 页面职责 |
| --- | --- |
| `/admin/dashboard` | 节点、队列、实例、镜像和日志概览 |
| `/admin/images` | 镜像模板、上传、分发和引用状态 |
| `/admin/nodes`、`/admin/nodes/:nodeId` | 节点能力、健康、容量和详情 |
| `/admin/queue` | 部署任务状态、阻塞原因和控制 |
| `/admin/instances` | Docker/VM 运行实例管理 |
| `/admin/logs` | 系统和操作日志查询 |
| `/admin/users` | 用户、角色和账号状态管理 |
| `/admin/teams` | 战队管理 |
| `/admin/student-groups` | 学员组与成员管理 |
| `/admin/system` | 系统配置管理 |
| `/admin/exercises` | 公共练习题管理与导入 |
| `/admin/theory-bank` | 理论题库管理 |

各页面通过 `features/admin/api/` 与对应 feature adapter 访问后端。管理首页可以由 adapter 聚合多个接口，但页面组件不能知道聚合细节。镜像、队列和实例操作必须展示异步终态与失败原因。

### 3.7 赛事管理

`/admin/games/:gameId` 使用统一赛事管理壳，包含：

- `info`：比赛基础信息；
- `phases`：阶段与时间控制；
- `divisions`：分组/赛区；
- `review`：报名审核；
- `notices`：比赛通知；
- `challenges` 与 `challenges/:challengeId`：CTF 题目列表和编辑；
- `theory-paper`、`theory-results`：理论试卷和成绩；
- `awdp-services`：AWDP 服务管理；
- `teamlab`：比赛与 TeamLab 场景绑定。

赛事基础、题目、理论、AWDP 和 TeamLab 分别由对应的 admin adapter 负责。编辑器必须先完整回读实体再编辑，不能仅依赖列表摘要模型。

### 3.8 TeamLab 管理

| 路由 | 页面职责 | 主要交互 |
| --- | --- | --- |
| `/admin/teamlab` | 场景库 | 创建、复制、筛选和进入场景 |
| `/admin/teamlab/:topologyId/design` | 拓扑设计 | 编辑资产、网络、连线并验证 |
| `/admin/teamlab/:topologyId/releases` | 发布管理 | 创建不可变发布、查看校验结果 |
| `/admin/teamlab/:topologyId/runtimes` | 运行列表 | 计划、启动、停止和筛选运行 |
| `/admin/teamlab/:topologyId/runtimes/:runtimeId` | 运行详情 | 资产、访问、操作、事件和流量观测 |

TeamLab 通过 `features/admin/teamlab/api/` 的管理、运行、远程访问和服务配置适配器访问后端。设计草稿、不可变 release 和 runtime 是不同资源，页面不能混用 ID 或把日志当运行事实。

## 4. API 维护规则

1. 后端契约变化后先更新 OpenAPI 并重新生成客户端。
2. 在 feature adapter 内完成 DTO 规范化、兼容判断和统一错误映射。
3. 为 adapter、状态机和关键领域转换增加定向测试。
4. 运行页面流程，核对请求顺序、响应字段、权限拒绝和失败终态。
5. 删除已失效的兼容分支，并同步更新本文和缺口文档。

外部系统集成使用 `/api/open/v1`、Bearer API token、scope 和幂等键；浏览器内部 Cookie 接口不能当作稳定外部 API。完整规则见 [外部 API 标准](./commercialization/external-api-standard.md)。

## 5. 页面完成定义

一个页面只有同时满足以下条件才算完成：

- 路由、导航、权限和返回路径闭环；
- 真实数据及加载、空、错误、刷新状态齐全；
- 写入可防重复，成功和失败均由服务端事实驱动；
- 日间、夜间、键盘和 reduced-motion 可用；
- 390、1366、1920、2560 像素宽度验收通过；
- 无页面级横向滚动、重叠、抽动和不可达按钮；
- adapter、类型检查、架构检查、测试和生产构建通过；
- 涉及基础设施的流程完成真实 Docker、VM、AWDP 或 TeamLab 验收。
