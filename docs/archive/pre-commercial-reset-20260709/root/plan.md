# YINYU 前端全域精确修复计划 v2

## Summary
这版计划基于 CodeGraph + 全量静态风险矩阵，而不是只按截图修公共页。已确认当前问题不是单点样式，而是全局布局、footer 层级、主题入口、通知系统、公共页和管理员域的迁移不完整叠加造成的。执行时按“全局壳层 -> 共享组件 -> 公共页 -> 管理端全域 -> 游戏端残留 -> 浏览器验收”顺序推进，每一步都用文件级清单闭环。

## Audit Findings
- 全局壳层问题：`WithNavbar` 主内容 `padding-top/min-height` 导致页面普遍下移；`AppFooter` 仍在同一滚动层里像普通内容块提前出现；`AppNavbar/AppHeader` 仍有日夜主题切换和颜色面板入口。
- 公共页问题：`Index/Posts/Games/Teams/About` 已接入部分 design-lab 类，但字号、首屏位置、空态文案、心电图图标、footer reveal、按钮层级仍未对齐设计稿；`Teams` 的 loading、空队伍、已有队伍、加入/创建/编辑弹窗仍有明显残留。
- 通知/弹窗问题：全站仍直接使用 Mantine notification/modal/drawer；登录失败右下角 toast、确认弹窗、上传弹窗、管理端创建弹窗都需要统一 YINYU 外观。
- 管理端未迁移文件：`Settings`、比赛编辑 `Info/Notices/Challenges/Divisions/Review/Writeups/Screen Control`、多个 admin modal/drawer 仍没有 YINYU 包装。
- 管理端半迁移文件：`AwdServices`、`TheoryPaper`、`Images`、`Nodes`、`Queue`、`Scenarios`、`IR`、`Users`、`Teams` 等虽有 YINYU 类，但仍存在 raw style、低对比度文字、默认 loading/table/modal 行为。
- 主题残留：`useMantineColorScheme/toggleColorScheme/CustomColorModal` 分布在导航、部分图表、上传组件、帖子/赛事页等位置；主入口必须删除，图表内部仅保留暗色计算或改为固定暗色。
- 低对比度残留：大量 `c="dimmed"`、`theme.colors.gray`、灰色 icon/text 导致截图里的文字不可见，需要统一映射到 `--yy-muted-readable` 级别。

## Implementation Changes

### 1. 全局壳层和 footer
- 修 `WithNavbar/AppNavbar/AppHeader/AppFooter`：取消错误的整体下移，桌面主内容按 `height: 100dvh` 组织首屏，公共页主体自然居中但不上浮/不下沉。
- 删除主导航和移动菜单的日/夜切换、颜色面板入口；MantineProvider 固定 dark，用户不可切换 light。
- footer 改为独立 reveal 层：主内容首屏不露 footer；继续下滑时露出 footer 品牌区；footer 左侧大 logo，右侧大号中英文字，整体居中，不再小字贴左下。
- 所有背景、footer 装饰、hex field 保持 `pointer-events:none`，确保按钮可点击。

### 2. 共享设计系统补齐
- 在 `YinyuUI` 增加/标准化：`YinyuModalBody`、`YinyuDrawerBody`、`YinyuConfirmPanel`、`YinyuToolbarButton`、`YinyuReadableText`、`YinyuLoadingState`。
- 在 `YinyuTheme.css` 统一覆盖 Mantine `Notification/Modal/Drawer/Popover/Menu/Pagination/Table/Input/Button/Switch/Badge/LoadingOverlay`，避免默认样式漏出。
- 心电图动效统一使用 `YinyuHeartbeatIcon`，替换“同步公告流”、文章/赛事 loading header、状态提示中的普通 icon。
- 新增低对比度 token：正文说明文字不再使用接近背景的灰色，统一为更亮的 muted 色。

### 3. 公共页精修
- 首页：放大并左移 `YINYU CTF平台` 标识，正式化公告空态文案，删除无意义按钮，公告卡/近期赛事卡上移到首屏合理位置。
- 文章页/赛事页：统一大标题字体、kicker、数量 pill、列表卡片高度和分页按钮；loading 使用 route-loader + 心电图。
- 队伍页：完整覆盖空态、队伍卡、加入队伍 modal、创建/编辑 modal、loading；所有按钮和输入框使用 YINYU 语言。
- About 页：改为左侧大 logo distortion，右侧平台介绍与蜂巢信息框，使用错位但稳定的两栏布局；移动端上下堆叠。
- 登录/注册/找回/重置/确认/待确认/资料页：保持左表单右大 logo，但修通知、按钮、字号、字段间距和错误态。

### 4. 管理端壳层
- `AdminPage/WithAdminTab/WithGameEditTab/WithChallengeEdit` 统一管理端布局：顶部 admin tab 不压低内容，比赛编辑页左侧 tab 和右侧内容保持稳定高度，不产生额外首屏下移。
- 管理端 toolbar 使用 `YinyuAdminToolbar`，loading overlay 只遮挡当前 panel，不模糊整个页面。
- 所有 admin table 外层必须使用 `YinyuTableShell`，所有表格列保持原字段，不删业务按钮。

### 5. 管理端未迁移页强制补齐
必须逐文件迁移以下页面，不允许只靠全局 CSS：
- 比赛编辑：`Info`、`Notices`、`Challenges/Index`、`Divisions`、`Review`、`Writeups`、`Screen/Control`。
- 平台管理：`Settings`、`Users`、`Teams`、`Instances`、`Logs`。
- 新功能管理：`dashboard`、`images`、`nodes`、`queue`、`theory-bank`、`TheoryPaper`、`TheoryResults`、`AwdServices`、`scenarios`、`ir-challenges`。
- re-export 路由文件只保持转发，不做样式要求。

### 6. 管理端弹窗/抽屉全覆盖
必须迁移以下组件到 `YinyuModalBody/YinyuDrawerBody`，保留所有字段、验证和 API：
- `GameCreateModal`、`ChallengeCreateModal`、`FlagCreateModal`、`GameNoticeEditModal`、`ImageUploadModal`、`TeamEditModal`、`UserEditModal`。
- `AttachmentRemoteEditModal`、`AttachmentUploadModal`、`BloodBonusModel`、`DivisionEditDrawer`、`ParticipationDivisionEditModal`。
- `ActionIconWithConfirm/CleanupButton/ChallengePreviewModal` 的确认、预览、toast 也统一样式。
- `PDFViewer` 的 PDF 页面 Paper 可保留，但外层工具条和 loading 要统一。

### 7. 游戏端和公共组件残留
- 修 `GameJoinModal`、`WriteupSubmitModal`、`ChallengePanel` skeleton/card、`GameNoticePanel`、`mobile Scoreboard`、`TrafficItems/WsrxManager` 等残留。
- 图表组件固定暗色主题，去掉对 color scheme 的依赖；保留功能和数据接口。
- 理论赛、AWDP、实例/VM 控制按钮只改视觉，不改启动/销毁/提交接口。

## Verification Gates
- 静态门禁：
  - `rg "toggleColorScheme|switch_to|CustomColorModal"` 只能剩无主入口残留或为 0。
  - admin 风险矩阵中 `NoYinyu` 只允许 re-export 文件和 `PDFViewer` PDF 页面例外。
  - `rg "<Modal|<Drawer"` 命中的文件必须包含 `YinyuModalBody` 或 `YinyuDrawerBody`。
  - `rg "c=\"dimmed\"|theme.colors.gray|color=\"gray\""` 逐项确认无低可读性 UI。
- 构建门禁：`pnpm check`、`pnpm build`、`git diff --check` 全部通过。
- 本地服务门禁：启动后端和前端，确保登录页不因后端缺失阻断后续审查。
- 浏览器验收：
  - 公共：`/`、`/posts`、`/games`、`/teams`、`/about`、`/account/login`、`/404`。
  - 管理：`/admin/games`、比赛编辑全部 tab、`/admin/settings`、`/admin/users`、`/admin/teams`、`/admin/images`、`/admin/nodes`、`/admin/queue`、`/admin/theory-bank`、`/admin/scenarios`、`/admin/ir-challenges`。
  - 交互：登录失败 toast、创建比赛、创建题目、编辑分组 drawer、上传附件、添加节点、镜像导入、删除确认、队伍加入/创建/编辑、分页、表格按钮。
- 视觉验收：
  - 页面内容不再整体下移。
  - 首屏不提前露 footer。
  - footer 是独立层级 reveal，品牌居中大气。
  - 无旧风格 toast/modal/button/table 明显露出。
  - 字体、字号、对比度在公共页和管理端一致。

## Assumptions
- 保留夜间主题为唯一主题，删除用户可见主题切换入口。
- 独立大屏 `ctf-screen/screen` 仍不纳入本轮。
- 后端 API、AWDP/VM/Windows 靶机流程、数据库和权限逻辑不修改。
- 管理端所有字段、表格列、按钮、弹窗和提交逻辑必须保留，只替换布局和视觉层。