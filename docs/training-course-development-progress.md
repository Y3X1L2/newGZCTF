# 培训课程模块开发计划与进度

## 当前基线

- 开发分支：`codex/course-training-system`
- 工作区：`D:\Work\newGZCTF\.codex_deploy\worktrees\course-training-system`
- 基线：`origin/main` + `origin/codex/role-training-permission-module`
- 前端风格基线：`codex/role-training-permission-module` / `a1f4c42 fix: allow root training modules`
- 导航决策：去除 `main` 的侧边栏展开/收起交互，保留培训分支的固定侧栏风格。

## 已确认产品规则

1. 培训板块以“课程”为主要对象，老师、学生围绕课程设计。
2. 比赛业务与培训业务隔离；只复用素材、组件、镜像/节点/容器能力和 Flag 校验机制。
3. 老师及以上可以创建课程；老师在培训课程页看到“创建课程”按钮；管理员后台也有培训课程管理模块。
4. 课程所有人可见；未报名或未通过审核时，只能查看课程简介和资源摘要，不能进入章节、下载资源、启动环境。
5. 课程报名策略支持 `自动通过` 和 `教师审核`；第一阶段默认 `教师审核`。
6. 老师/管理员在课程页“学员管理”中通过或拒绝报名。
7. 课程状态：`草稿`、`已发布`、`已归档`。
8. 课程资源第一阶段只做课程级资源，所有章节都能调用。
9. 章节完成条件：
   - 普通章节：页面末尾手动点击完成。
   - 带实验/容器章节：提交正确 Flag 后自动完成。
   - 视频章节：第一阶段不统计播放进度，可手动完成。
10. 课程资源仅对已通过报名的学生、授课老师、管理员开放下载。
11. 环境模板允许注册和上传镜像，复用镜像服务器逻辑和节点调度逻辑，并加入 `CourseId` 隔离。
12. 课程题目沿用当前题目的 Flag 机制，提交和进度写入培训表，不进入比赛榜单。
13. 视频支持本地上传和外链。
14. 课程创建者可维护授课老师列表；非创建老师不可编辑授课老师；管理员/超级管理员全权限。

## 数据模型计划

新增或重构为课程主模型：

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

保留并复用：

- `ExerciseChallenge`
- `ExerciseInstance`
- `ImageTemplate`
- 附件/Blob 存储
- 容器和节点调度仓储
- Flag 校验逻辑

## 后端开发计划

1. 课程实体、枚举、迁移
   - 课程状态、报名策略、报名状态、资源类型、视频类型。
   - 课程、老师、报名、章节、资源、课程题目、进度表。
2. 权限服务
   - `CanViewCourse`
   - `CanLearnCourse`
   - `CanEditCourse`
   - `CanManageTeachers`
   - `CanManageEnrollments`
3. 学生端 API
   - 课程列表、课程详情、报名/取消报名。
   - 章节详情、标记完成。
   - 资源列表、资源下载。
   - 课程题目详情、启动/销毁实例、提交 Flag。
4. 老师端 API
   - 创建/编辑/发布/归档课程。
   - 管理授课老师。
   - 审核学员。
   - 创建/编辑章节。
   - 上传/删除课程资源。
   - 课程级题目和环境模板管理。
5. 管理员 API
   - 全局课程列表、筛选、状态管理。

## 前端开发计划

1. `/training`
   - 顶部品牌与课程海报轮播。
   - 最近学习/我教授的课程。
   - 全部课程列表。
   - 老师及以上显示创建课程按钮。
2. `/training/courses/:courseId`
   - 课程海报、名称、标签、任课老师、报名状态。
   - 标签：课程介绍、课程列表、课程资源。
   - 老师及以上额外：学员管理、环境模板、题目管理。
3. `/training/courses/:courseId/chapters/:chapterId`
   - 左侧章节树。
   - 中间 Markdown/视频/实验块。
   - 右侧目录。
   - 页面末尾完成按钮。
4. 老师编辑体验
   - 创建课程弹窗/页面。
   - 课程信息编辑。
   - 章节编辑。
   - 资源上传。
   - 题目和环境模板管理复用现有组件。

## 联调验收流程

1. 老师创建课程，保存为草稿。
2. 老师上传海报、课程介绍和课程资源。
3. 老师添加章节，写 Markdown，上传或填写视频外链。
4. 老师添加课程环境模板和课程题目。
5. 老师发布课程。
6. 学生在课程中心看到课程并报名。
7. 老师审核通过。
8. 学生进入课程章节，阅读、观看视频、启动实例。
9. 学生提交正确 Flag，章节自动完成。
10. 老师查看学员进度和提交记录。

## 当前进度

- [x] 新建独立开发 worktree 和分支。
- [x] 拉取并合并培训/权限分支。
- [x] 按 `a1f4c42` 前端风格处理公共导航方向。
- [x] 确认去除侧边栏展开/收起交互。
- [x] 完成公共 UI 冲突标记解除。
- [x] 前端 `pnpm run check` 通过。
- [x] 后端 `dotnet build src\GZCTF\GZCTF.csproj --no-restore` 通过。
- [ ] 新增课程主模型和迁移。
- [ ] 实现课程 API。
- [ ] 实现课程前端页面。
- [ ] 接入课程级题目、资源和容器。
- [ ] 完成构建和流程验证。

## 验证记录

- 2026-06-17：`pnpm run check` 通过。
- 2026-06-17：`dotnet restore src\GZCTF\GZCTF.csproj` 通过。
- 2026-06-17：`dotnet build src\GZCTF\GZCTF.csproj --no-restore` 通过。
- 当前后端构建仍存在既有 warning：`VmManager` 过时、EF JSON converter 可空性差异；无新增 error。

## 风险与注意事项

- 当前培训分支已有 `TrainingDirection / TrainingModule`，但产品目标已变为课程主模型；后续需要谨慎重构，避免旧接口和新接口混乱。
- 课程题目需和比赛题目隔离，不能让训练提交污染比赛榜单。
- 课程镜像模板需要 `CourseId` 隔离，但底层镜像服务器和节点调度仍复用现有能力。
- 前端要保持平台现有风格，参考图的布局，不照搬洛谷视觉。
- 后续每完成一个开发阶段都要更新本文档。
