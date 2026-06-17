# 培训课程模块开发计划与进度

## 当前基线

- 开发分支：`codex/course-training-system`
- 工作区：`D:\Work\newGZCTF\.codex_deploy\worktrees\course-training-system`
- 基线：`origin/main` + `origin/codex/role-training-permission-module`
- 前端风格基线：`codex/role-training-permission-module` / `a1f4c42 fix: allow root training`
- 导航决策：去除 `main` 的侧边栏展开/收起交互，保留培训权限分支的固定导航风格。

## 已确认产品规则

1. 培训板块以“课程”为主要对象，老师、学生围绕课程设计。
2. 比赛业务与培训业务隔离；只复用组件、素材、镜像/节点/容器能力和 Flag 校验机制。
3. 老师及以上可创建课程；老师在培训页显示“创建课程”；管理员后台也有培训课程管理模块。
4. 课程所有人可见；未报名或未通过审核时，只能看课程简介和资源摘要，不能进入章节、下载资源、启动环境。
5. 报名策略支持“自动通过”和“教师审核”，第一阶段默认“教师审核”。
6. 老师/管理员在课程页“学员管理”中通过或拒绝报名。
7. 课程状态：草稿、已发布、已归档。
8. 课程资源第一阶段只做课程级资源，所有章节都能调用。
9. 普通章节和视频章节支持页面末尾手动完成；带实验/容器章节后续通过正确 Flag 自动完成。
10. 课程资源只对已通过报名学生、授课老师、管理员开放下载。
11. 环境模板允许注册和上传镜像，复用镜像服务器逻辑和节点调度逻辑，并加入 `CourseId` 隔离。
12. 课程题目沿用当前题目的 Flag 机制，提交和进度写培训表，不进入比赛榜单。
13. 视频支持本地上传和外链。
14. 课程创建者可维护授课老师列表；非创建老师不可编辑授课老师；管理员/超级管理员全权限。

## 数据模型计划

新增课程主模型：

- `TrainingCourse`
- `TrainingCourseTeacher`
- `TrainingCourseEnrollment`
- `TrainingCourseChapter`
- `TrainingCourseResource`
- `TrainingCourseChallenge`
- `TrainingCourseChapterChallenge`
- `TrainingCourseSubmission`
- `TrainingCourseProgress`
- `TrainingChapterProgress`

复用能力：

- `ExerciseChallenge`
- `ExerciseInstance`
- `ImageTemplate`
- `Attachment` / `LocalFile` / Blob 存储
- 容器和节点调度仓储
- Flag 校验逻辑

## 后端开发计划

1. 课程实体、枚举、迁移。
2. 课程权限服务逻辑：
   - `CanViewCourse`
   - `CanLearnCourse`
   - `CanEditCourse`
   - `CanManageTeachers`
   - `CanManageEnrollments`
3. 学生端 API：
   - 课程列表、课程详情、报名/取消报名
   - 章节详情、标记完成
   - 资源列表、资源下载
   - 课程题目详情、启动/销毁实例、提交 Flag
4. 老师端 API：
   - 创建/编辑/发布/归档课程
   - 管理授课老师
   - 审核学员
   - 创建/编辑章节
   - 上传/删除课程资源
   - 课程级题目和环境模板管理
5. 管理员 API：
   - 全局课程列表、筛选、状态管理

## 前端开发计划

1. `/training`
   - 顶部品牌与课程海报轮播
   - 最近学习/我教授的课程
   - 全部课程列表
   - 老师及以上显示创建课程按钮
2. `/training/courses/:courseId`
   - 课程海报、名称、标签、任课老师、报名状态
   - 标签页：课程介绍、课程列表、课程资源
   - 老师及以上额外标签：学员管理、环境模板、题目管理
3. `/training/courses/:courseId/chapters/:chapterId`
   - 左侧章节列表
   - 中间 Markdown/视频/实验块
   - 右侧目录
   - 页面末尾完成按钮
4. 老师编辑体验：
   - 创建课程弹窗/页面
   - 课程信息编辑
   - 章节编辑
   - 资源上传
   - 题目和环境模板管理复用现有组件风格

## 当前进度

- [x] 新建独立开发 worktree 和分支。
- [x] 拉取并合并培训/权限分支。
- [x] 按 `a1f4c42` 前端风格处理公共导航方向。
- [x] 确认去除侧边栏展开/收起交互。
- [x] 完成公共 UI 冲突标记解除。
- [x] 前端 `pnpm run check` 通过。
- [x] 后端 `dotnet build src\GZCTF\GZCTF.csproj --no-restore` 通过。
- [x] 新增课程主模型和迁移。
- [x] 实现课程 API。
- [x] 实现课程前端页面。
- [x] 接入课程级题目、资源和容器。
- [x] 完成构建和流程验证。

## 2026-06-17 开发记录

- 开始补齐课程主模型。
- 决定保留旧 `TrainingDirection / TrainingModule` 代码，新增课程体系，降低与队友代码冲突。
- 决定给 `ImageTemplate` 和 `ExerciseChallenge` 增加可空课程归属字段：为空表示全局对象，非空表示课程内对象。
- 已新增课程主模型、EF 配置和增量迁移 `AddTrainingCourseSystem`。
- 已实现学生/通用课程 API：课程列表、详情、报名、章节详情/完成、资源下载、课程题目容器和 Flag 提交。
- 已实现教师/管理员课程 API：课程创建/编辑/发布/归档、报名审核、授课老师、章节、资源、题目关联。
- 迁移检查确认没有给 `GameChallenges` 增加课程字段，避免污染比赛题目表。
- 已重做 `/training` 为课程中心，包含课程海报、最近学习/我教授的课程、全部课程和教师创建课程入口。
- 已新增 `/training/courses/:courseId` 课程详情页，包含课程介绍、课程列表、课程资源、学员管理、环境模板、题目管理。
- 已新增 `/training/courses/:courseId/chapters/:chapterId` 章节详情页，包含章节列表、Markdown/视频内容、目录、实验题目、容器启动、Flag 提交和章节完成。
- 已为课程资源接入本地上传和外链绑定；课程资源下载走课程权限接口。
- 已为课程环境模板接入课程内注册 Docker、上传 Docker 包、上传 VM 镜像、本地导入和模板删除能力，默认只显示当前课程模板。
- 已为课程题目接入课程内弹窗创建、章节绑定、容器启动、Flag 提交和删除清理，避免再手填 `ExerciseChallengeId`。
- 已将课程专属 `ImageTemplate` / `ExerciseChallenge` 从全局镜像、旧培训练习和全局练习实例入口中过滤，降低培训课程与比赛/练习业务互相污染风险。

## 2026-06-17 培训平台前端产品化重构记录

- 基线已同步到远端 `main` / `1a6843a fix: 修复培训章节实例入口与flag输入`。
- 已确认协作者修复内容：章节实验题卡接入 `InstanceEntry`，支持读取已有实例入口、创建实例、续期、销毁实例和正常 Flag 输入。
- 本轮执行策略：先完成学生端课程中心、课程详情、章节学习页的完整可用闭环；教师端编辑工作台后续在不破坏现有课程管理能力的前提下渐进增强。
- 设计约束：保留老师审核报名、方向自定义、平台级签到作为后续接口闭环；章节内实验不跳转，容器题卡直接嵌入章节页。
- 质量要求：不做伪数据面板，不做不可点击的空功能；没有后端接口的数据只展示正式空态或后续能力提示。
- 已新增培训共享前端组件 `TrainingCourseUI.tsx`，统一课程状态渐变文字、轻量文字标签、课程卡、进度统计卡和空态。
- 已重构 `/training` 为学习平台式三栏布局：左侧学习导航、中间继续学习/待完成/全部课程、右侧学习概览和平台级签到预留空态。
- 已重构课程卡片为高信息密度毛玻璃卡，去除胶囊状态和大面积空封面，使用真实课程进度、教师、资源和实验数量。
- 已重构课程详情页为课程信息头图 + 左侧章节路径 + 中间课程内容标签页 + 右侧学习状态，保留报名审核、环境模板和题目管理真实功能入口。
- 已重构章节学习页为文档式三栏布局，保留 `InstanceEntry` 容器实例读取、创建、续期、销毁和 Flag 输入修复；实验题仍嵌入章节末尾，不跳转。
- 已追加培训模块专用样式，统一使用管理界面同款 ReactBits 背景、毛玻璃卡片、渐变状态文字、无胶囊标签。
- 已运行 `pnpm --dir src/GZCTF/ClientApp check`，通过。
- 已运行 `pnpm --dir src/GZCTF/ClientApp build`，通过。
- 已运行 `dotnet build src/GZCTF/GZCTF.csproj --no-restore`，通过；仅保留既有 `VmManager` 过时和 EF JSON converter 可空性 warning。

## 当前遗留项

- 课程内“题目管理”已支持弹窗创建课程专属题目，但表单仍是轻量版；后续可继续补齐比赛题目富编辑表单里的高级字段。
- 课程内“环境模板”已支持课程内注册和上传，后续可继续补齐镜像构建日志、批量操作和更细的状态追踪。
- 课程进度第一阶段以章节完成和 Flag 提交为主；更细的视频播放进度、资源阅读记录还未统计。

## 验证记录

- 2026-06-17：`pnpm run check` 通过。
- 2026-06-17：`dotnet restore src\GZCTF\GZCTF.csproj` 通过。
- 2026-06-17：`dotnet build src\GZCTF\GZCTF.csproj --no-restore` 通过。
- 2026-06-17：课程模型/API/前端完成后，`pnpm run check` 通过。
- 2026-06-17：课程模型/API/前端完成后，`dotnet build src\GZCTF\GZCTF.csproj --no-restore` 通过。
- 2026-06-17：课程内环境模板接口在 `10.0.7.118` 验证通过，`GET /api/admin/training/courses/5/image-templates` 返回 `200`。
- 2026-06-17：课程题创建/删除流程在 `10.0.7.118` 验证通过，创建课程专属题后删除可同步清理底层题目和关联数据。
- 当前后端构建仍存在既有 warning：`VmManager` 过时、EF JSON converter 可空性差异；无新增 error。
- 2026-06-17：培训前端产品化重构阶段一完成后，`pnpm --dir src/GZCTF/ClientApp check` 通过。
- 2026-06-17：培训前端产品化重构阶段一完成后，`pnpm --dir src/GZCTF/ClientApp build` 通过。
- 2026-06-17：培训前端产品化重构阶段一完成后，`dotnet build src/GZCTF/GZCTF.csproj --no-restore` 通过，仍仅有既有 warning。

## 2026-06-17 培训平台第二阶段修复记录

- 已修复培训首页文案乱码，重写 `/training` 首页为三栏学习平台布局：左侧学习导航，中间课程流，右侧学习概览。
- 已将培训页纳入 ReactBits 背景体系，并在培训页内隐藏全局背景层，避免管理背景与旧蜂巢/信号背景叠加。
- 已统一课程卡片尺寸、内边距、状态展示和标签展示：小课程卡不再展示海报，课程海报只在课程详情头图区展示。
- 已修复“教学入口 3/3 但只展示 2 项”的显示错误，入口区现在最多展示 3 张课程卡，计数与渲染一致。
- 已新增平台签到后端实体 `TrainingCheckIn`、迁移 `AddTrainingCheckInsGenerated`、个人概览接口和签到接口。
- 已在学生端概览中接入真实数据：可见课程、已加入课程、课程完成数、平均进度、章节进度、CTF 实验进度、理论培训进度、累计打卡、连续打卡和 42 天学习活跃图谱。
- 已将培训首页签到按钮接入后端，按 UTC+8 自然日防重复签到。
- 已新增课程详情“编辑工作台”标签页，把课程信息、课程介绍 Markdown、章节树、章节正文 Markdown、章节预览整合到页面内，避免大段内容仍在小弹窗中编辑。
- 已保留资源、镜像、题目创建等短事务弹窗；这些操作不适合强行塞进大编辑器，后续可单独做批量管理视图。

## 当前第二阶段遗留项

- 课程详情页仍保留旧“编辑课程”弹窗作为兼容入口，但主编辑路径已经切换到“编辑工作台”。
- 课程资源、题目和环境模板管理仍以表格/弹窗为主，后续可以继续做更强的侧栏属性面板和批量操作。
- 学习概览已接入课程制和旧模块理论统计，但视频播放进度、资源阅读时长等更细颗粒数据仍未建模。

## 风险与注意事项

- 旧培训分支已有 `TrainingDirection / TrainingModule`，但产品目标已变为课程主模型；后续需要谨慎迁移，避免旧接口和新接口混乱。
- 课程题目需和比赛题目隔离，不能让训练提交污染比赛榜单。
- 课程镜像模板需要 `CourseId` 隔离，但底层镜像服务器和节点调度仍复用现有能力。
- 前端要保持平台现有风格，参考图的布局，不照搬洛谷视觉。
- 后续每完成一个开发阶段都要更新本文档。
