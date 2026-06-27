# 2026-06-27 测试反馈复核与最小闭环修复计划

## 1. 本轮边界

本轮只做功能异常、阻断、越权、数据不一致和可验证交互缺陷的复核与计划。`平台页面修改方案.xlsx` 以及对应截图类美化需求先不处理；如果其中后续被证明确实包含按钮失效、路由错误、数据丢失等功能问题，再拆成独立问题进入修复队列。

输入来源：

- `D:/Downloads/平台开发issue_数据表_表格.csv`
- `D:/Downloads/隐域 CTF 测试.md`
- `D:/Downloads/隐域测试反馈.md`
- `D:/Downloads/📄测试反馈与 Bug 报告.md`
- 用户新增截图：队伍管理的邀请码区域按钮不居中，复制失效

当前架构事实：

- FRP 反馈已过时，当前链路是 Redis + Nginx 端口代理。
- 镜像仓库按当前需求固定为 `10.24.0.28:5000`。
- 当前工作树已有大量既有修改，本文件只做复核与计划，不回滚、不清理无关文件。

## 2. 复核结论总览

| ID | 问题 | 优先级 | 当前判断 | 最小闭环动作 |
|---|---|---:|---|---|
| TEAM-001 | 队伍邀请码复制失效 | P1 | 真实存在。工具函数已存在，但队伍页未接入 | 改用 `copyText` fallback，失败时显示失败提示 |
| TEAM-002 | 邀请码区域按钮不居中 | P2 | 真实存在。CSS `align-items:flex-end` 导致按钮下坠 | 调整该控件局部 flex 对齐和固定按钮尺寸 |
| SEC-001 | 未认证/未参赛访问普通 CTF 榜单 | P0 | 主接口已修，但导出接口疑似漏鉴权 | 给 `ScoreboardSheet` 补同等 Monitor/参赛鉴权并测试 401/403 |
| SEC-002 | 未报名可访问理论榜单 | P0 | 代码显示已按 Teacher 以下要求 Accepted 参赛 | 服务器实测未报名账号 403，纳入回归 |
| SEC-003 | 渗透赛普通榜单未参赛可见 | P1 | 可疑存在。`requireParticipation:false` 对普通用户可能放行 | 收紧普通用户必须 Accepted，Teacher+ 可监控 |
| SEC-004 | Logout 后旧 Token 仍有效 | P1 | 代码显示已修，未做服务器验证 | 用旧 Cookie 访问 `/api/Account/Profile` 验证 401 |
| TRAIN-001 | 报名课程未经审核可访问章节 | P1 | 代码显示课程详情已按 `Approved` 才 include detail | 用 Pending 学生账号复测章节、资源、实验接口 |
| TRAIN-002 | 标记完成第一次无反应 | P1 | 代码看似已修，但用户反馈需复测 | 单击后按钮立即禁用、课程列表进度刷新 |
| TRAIN-003 | 实验通关后课程进度不同步 | P1 | 后端提交成功后会调用完成检查并重算进度 | 验证正确 Flag 后章节/课程进度一次刷新 |
| TRAIN-004 | 课后测试无错题/正确答案 | P2 | 代码曾补“提交后返回正确答案”，未服务器验收 | 验证提交后错题标红、我的答案、正确答案 |
| TRAIN-005 | 课程/章节无法删除 | P2 | 章节/资源/题目/模板已有删除；课程本体用归档，不是硬删 | 明确 UI 为“归档/恢复”，不要硬删历史；补列表筛选 |
| TRAIN-006 | 必做题并不是必做 | P1 | 章节完成判定已检查必做题；但 Flag 提交按钮本身不会被拦截 | 验证“必做影响章节完成”而不是“禁止提交”；文案修正 |
| TRAIN-007 | 及格线输入无法清空，会自动回 60 | P2 | 真实存在。`Number(value) || 60` 导致空值回填 | 前端允许临时空值，保存时校验 1-100 |
| TRAIN-008 | 继续学习排序失效 | P3 | 代码显示已用 `lastStudiedAt`，未服务器验收 | 访问第 4 门课后回首页应置顶 |
| OPS-001 | 本地镜像导入接口异常 | P0 | 代码显示已有错误回填和 Registry 导入链路，未实测 | 上传 docker save 包，验证模板 Ready/ErrorMessage |
| OPS-002 | 部分题目容器创建失败 | P1 | 已知根因是容器题未绑定镜像；代码已强制镜像配置 | 清理/禁用无镜像题，创建题时强制镜像和端口 |
| OPS-003 | 容器销毁后旧入口仍 HTTP 200 | P1 | 代码显示已接 Nginx 主动同步，未端到端实测 | 创建-访问-销毁-旧入口不可达-Redis/Nginx 清理 |
| OPS-004 | FRP 公网转发不通 | 过时 | 当前已不是 FRP 架构 | 按 Redis+Nginx 代理重测，不做 FRP 修复 |
| OPS-005 | Windows 靶机节点管理状态未显示 | P1 | 代码显示节点资源已聚合 VM，未实测 | 启动 Windows VM 后节点资源页出现 VM 行 |
| OPS-006 | Docker Registry 内网无认证 | P2/决策 | 不是前端 bug；当前固定内网可信 Registry | 确认网络 ACL，不暴露公网；暂不加认证 |
| TEAM-003 | 队伍退出/解散缺失 | P2 | 后端和旧弹窗已有；当前新队伍页需确认入口是否完整 | 检查当前页面队长解散、成员退出入口是否可见 |
| USER-001 | 手机号校验不严 | P2 | 代码显示前后端均已加正则，未服务器验收 | 用户页/管理员页/批量创建都测非法手机号 |
| UI-001 | 首页登出后头像菜单残留 | P3 | 代码显示 SWR 缓存会清理，未 UI 验收 | 首页登出后头像立即切为未登录态 |
| UI-002 | 外链资源应该前往而不是下载 | P2 | 资源接口已 `Redirect`，按钮文案仍叫“下载” | 对外链显示“打开”，本地文件显示“下载” |
| UI-003 | 老师管理页用途不明确 | P3 | 非阻断；培训管理入口曾反复调整 | 确认老师只看到授权子页，无空管理页 |
| IR-001 | 旧 Scenario/IR 权限边界 | P1 | 文档显示已修，未本轮复测 | 普通学生不能直接读管理配置接口 |

## 3. 已确认真实未修复或漏接的问题

### TEAM-001 邀请码复制失效

证据：

- `src/GZCTF/ClientApp/src/utils/Shared.tsx` 已有 `copyText`，支持 Clipboard API + HTTP/IP 下的 textarea fallback。
- `src/GZCTF/ClientApp/src/pages/Teams.tsx` 邀请码仍使用 Mantine `useClipboard`，且点击后直接显示成功提示，不检查复制结果。

风险：

- 当前部署常用 HTTP/IP 访问，`navigator.clipboard` 很容易被浏览器禁用。
- 用户会看到成功提示但剪贴板无变化，属于明确功能失效。

最小修复：

- 移除队伍页 `useClipboard`。
- `onClick` 改为 `const ok = await copyText(inviteCode)`。
- 成功显示“复制成功”；失败显示“复制失败，请手动复制”。

验收：

- 在 `http://10.24.0.27/` 队长账号点击复制，粘贴内容等于邀请码。
- 禁用 Clipboard API 或 HTTP 环境下也能 fallback 成功。

### TEAM-002 邀请码区域按钮不居中

证据：

- 用户截图中两个按钮明显不在输入框垂直中心。
- `src/GZCTF/ClientApp/src/styles/YinyuRefinement.css` 中 `.yy-team-invite-control { align-items: flex-end; }`。

最小修复：

- `.yy-team-invite-control { align-items: center; }`
- 只对该区域按钮设置稳定 `height/width`，不要全局改 Mantine ActionIcon。
- `PasswordInput` 输入框高度与按钮高度对齐。

验收：

- 截图区域内输入框、复制按钮、刷新按钮在同一中线。
- 移动端不换行、不挤出卡片。

### SEC-001b ScoreboardSheet 导出接口疑似漏鉴权

证据：

- `GameController.Scoreboard` 已有 `[RequireUser]` 并检查 Teacher 以下必须 Accepted 参赛。
- `GameController.ScoreboardSheet` 当前只看到导出逻辑，未看到同等 `[RequireUser] / RequireMonitor / 参赛检查`。
- API 注释写“requires Monitor permission”，代码需要对齐。

风险：

- 即使普通榜单接口已修，导出 Excel 仍可能泄露队伍成员、得分和提交信息。

最小修复：

- 复用普通榜单鉴权函数或加 `[RequireMonitor]`。
- 如果希望参赛队伍可导出自己的普通榜单，则沿用 `Scoreboard` 的 Teacher/Accepted 逻辑。
- 不新增新接口。

验收：

- 匿名请求 `/api/Game/{id}/ScoreboardSheet` 返回 401。
- 未参赛普通用户返回 403。
- Teacher/Admin 可下载。

### SEC-003 渗透赛榜单可疑放行

证据：

- `PenetrationPlayerController.GetScoreboard` 调用 `GetContextInfo(... requireParticipation:false, allowTeacherMonitor:true)`。
- 当前 `GetContextInfo` 在 `requireParticipation=false` 时会让非教师用户绕过参与状态检查的风险。

最小修复：

- 改为普通用户必须 Accepted 参赛；Teacher+ 保持监控访问。
- 只改该控制器判断，不触碰渗透部署/提交逻辑。

验收：

- 未参赛学生访问 `/api/pentest/games/{id}/scoreboard` 返回 403。
- 已参赛学生正常返回。
- Teacher/Admin 正常返回。

### TRAIN-007 及格线输入无法清空

证据：

- `theory-edit.tsx` 中 `onChange={(value) => setPaper({ ...paper, passRate: Number(value) || 60 })}`。
- 空值、0、非法中间态都会立刻变成 60。

最小修复：

- 前端维护 `passRateInput: string | number` 或允许 `NumberInput` 空值。
- 保存时统一校验 `1 <= passRate <= 100`。
- 不改后端模型。

验收：

- 用户可以选中全部删除再输入新数字。
- 空值保存提示“请输入 1-100 的及格线”。

### UI-002 外链资源按钮文案不准确

证据：

- 后端 `DownloadResource` 对资源类型为 Link 时 `Redirect(resource.ExternalUrl)`，行为已是“前往”。
- 前端资源按钮统一显示“下载”，会造成误解。

最小修复：

- 前端按 `resource.type` 显示“打开”或“下载”。
- 图标可以本地文件保留下载图标，外链用打开图标；不改接口。

验收：

- 外链资源按钮文案为“打开”，新标签页跳转目标 URL。
- 本地文件仍为“下载”。

## 4. 已修复但未经过本轮服务器验证的问题

这些问题从代码看已有修复痕迹，但本轮没有用 `10.24.0.27` 逐条实测，不能标记为完全闭环。

| ID | 当前代码证据 | 需要补的验证 |
|---|---|---|
| SEC-002 理论榜单未报名可见 | `TheoryPlayerController.Scoreboard` 要求 Teacher 以下 Accepted 参赛 | 未报名账号访问理论榜单应 403 |
| SEC-004 Logout Token 失效 | Logout 更新 SecurityStamp；Identity 校验接入 SecurityStampValidator | 登出后旧 Cookie 再请求 Profile 应 401 |
| TRAIN-001 报名审核绕过 | `CanLearnCourse` 要求 Approved；课程详情 `includeDetail` 受控 | Pending 学生不能访问章节正文/资源/实验 |
| TRAIN-002 标记完成单击 | `CompleteChapter` 返回最新章节模型，前端 `setChapter` 后 `load()` | 单击后按钮变“已完成”，课程列表同步 |
| TRAIN-003 实验完成进度 | Flag Accepted 后调用 `MarkChapterCompletedIfReady` 和 `RecalculateProgress` | 正确 Flag 后不需要二次手动点击 |
| TRAIN-004 错题复盘 | 提交后返回 correct answer 的实现已出现 | 提交后页面展示错题、我的答案、正确答案 |
| OPS-001 本地镜像导入 | `ImageTemplateController.UploadDockerArchive` 与 `ErrorMessage` 字段已存在 | 上传 docker save 包到固定 Registry 并创建容器 |
| OPS-003 端口释放 | Nginx sync 服务和销毁后主动同步已存在 | 销毁后旧入口不能访问，Redis/Nginx 无残留 |
| OPS-005 Windows VM 状态 | `NodesController.Resources` 聚合 `VmInstances` | 启动 Windows VM 后节点资源页显示 VM |
| USER-001 手机号校验 | 前端 `PHONE_PATTERN`，后端 `PhoneNumberAttribute` | 用户/管理员/批量创建三入口都拒绝非法手机号 |
| UI-001 登出头像残留 | `useLogOut` 清 SWR 缓存并跳转 | 首页直接登出头像立即消失 |
| TRAIN-008 继续学习排序 | `course.lastStudiedAt` 已用于排序 | 最近访问课程置顶 |

## 5. 已过时或暂不按原反馈修的问题

### OPS-004 FRP 转发失效

当前平台已迁移到 Redis + Nginx 端口代理，不再按 FRP 修复。正确验收方式是：

1. 创建容器，平台返回当前代理入口。
2. 访问入口成功。
3. 销毁容器后旧入口失效。
4. Nginx 动态配置和 Redis 映射一致。

### OPS-006 Docker Registry 内网无认证

`10.24.0.28:5000` 当前被设计为固定内网 Registry。无认证不一定是漏洞，取决于网络边界：

- 不能映射公网。
- 只允许主服务器和受信 Worker 节点访问。
- 后续如果启用认证，需要同时改 Docker daemon 凭据、Agent 拉取凭据、上传推送凭据和旧模板迁移，不能只给 Registry 加密码。

本轮不做认证大改，先把访问面收敛作为运维验收项。

### 平台页面修改方案

用户已明确：美化部分先不管。由于附件缺失且当前问题表没有可定位功能描述，本轮不纳入代码计划。

## 6. 需要产品/业务规则确认的问题

### TRAIN-006 必做题“是不是应该禁止提交”

代码当前语义是：题目都可以提交，但必做题会影响章节完成。这是合理设计。反馈“直接点击提交就可以提交”可能表达的是“未完成必做题也能完成章节”，而不是“选做题不能提交”。

建议修复目标：

- 保持题目可以提交。
- 章节完成必须阻断未完成必做题。
- 前端在章节完成区明确显示“必做题 X/Y”，未完成时按钮提示具体缺口。

### TRAIN-005 课程删除

当前课程已有 `Archived` 状态和归档按钮。培训数据涉及学习记录、答卷、提交、容器实例，不应默认硬删。

建议修复目标：

- 管理页把“归档”表达为“删除/下架课程（保留历史记录）”。
- 列表默认隐藏已归档，提供“查看已归档/恢复”。
- 硬删除课程若未来需要，必须单独二次确认并清楚定义历史数据处理策略。

### TEAM-003 队伍退出/解散

后端已有退出、解散、转让、踢人 API；当前新队伍页已看到转让/踢人/申请处理，但还需要确认退出/解散入口是否从新页面完整露出。如果没有，补前端入口即可，不需要新后端。

## 7. 最小闭环修复计划

### 第 1 批：小改动、高确定、立刻止血

1. 修复邀请码复制：
   - 队伍页接入 `copyText`。
   - 成功/失败反馈真实化。

2. 修复邀请码按钮不居中：
   - 只改 `.yy-team-invite-control` 和局部按钮尺寸。

3. 修复理论测试及格线输入：
   - 前端允许清空中间态。
   - 保存时校验 1-100。

4. 修复外链资源文案：
   - 外链显示“打开”，本地文件显示“下载”。

验收：

- `pnpm --dir src/GZCTF/ClientApp check`
- 浏览器手测队伍邀请码复制/按钮对齐
- 手测及格线清空重输
- 手测外链资源打开

### 第 2 批：权限与信息泄露收口

1. `ScoreboardSheet` 补齐鉴权。
2. `PenetrationPlayerController.GetScoreboard` 普通用户必须 Accepted 参赛。
3. 建立一组最小 API 验收：
   - 匿名访问普通榜单/导出：401
   - 未参赛学生访问普通榜单/理论榜单/渗透榜单：403
   - 已参赛学生访问：200
   - Teacher/Admin 监控访问：200

验收：

- `dotnet build src/GZCTF/GZCTF.csproj --no-restore`
- 服务器 API curl 验证 401/403/200

### 第 3 批：培训闭环复测与少量补齐

1. 复测报名审核：
   - Pending 学生不能进章节内容、资源、实验。

2. 复测章节完成：
   - 无实验章节单击完成立即变化。
   - 有必做实验章节未完成时显示明确原因。
   - 正确 Flag 后自动刷新课程进度。

3. 课程归档语义：
   - 列表默认隐藏 Archived 或清晰标记。
   - 管理端提供查看/恢复归档课程入口。

4. 错题复盘验收：
   - 提交后展示错题、我的答案、正确答案。
   - 如果没有解析字段，不造空解析；后续单独扩展。

验收：

- 服务器端学生/老师账号端到端操作。
- 不新增臃肿接口；优先复用已有课程状态和答卷模型。

### 第 4 批：容器/节点/镜像运维闭环复测

1. 本地 Docker 镜像导入：
   - 上传成功模板 Ready。
   - 上传失败模板 ErrorMessage 可见。

2. 无镜像容器题：
   - 新建/编辑/启用/启动都给出明确错误。
   - 清理已有启用但无镜像的题目。

3. 容器端口释放：
   - 创建、访问、销毁、旧入口不可达。

4. Windows VM 节点资源：
   - 启动 VM 后节点资源页出现 VM。
   - 销毁后进入历史或消失，状态不悬挂。

验收：

- 所有业务验收在 `10.24.0.27` 上进行。
- Docker/Redis/Nginx 状态在服务器端验证。

## 8. 质量门槛

每批修复都必须满足：

- 不新增重复接口，优先复用现有 API。
- 不用前端隐藏替代后端鉴权。
- 不硬删培训/比赛历史数据。
- 不把过时 FRP 问题按旧架构修。
- 修复后更新本文件“执行进度记录”。

静态检查：

```powershell
pnpm --dir src/GZCTF/ClientApp check
dotnet build src/GZCTF/GZCTF.csproj --no-restore
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore
git diff --check
```

服务器验收：

```bash
systemctl status gzctf.service --no-pager
curl -I http://127.0.0.1:8080/
journalctl -u gzctf.service -n 120 --no-pager
```

## 9. 执行进度记录

### 2026-06-27

- 已重新纳入 4 份反馈源逐条复核。
- 已按用户订正将“平台页面修改方案”美化类需求排除在本轮功能修复范围外。
- 已确认队伍邀请码复制和按钮对齐为真实未修复问题。
- 已确认 `ScoreboardSheet`、渗透榜单为需要进一步收口的同类鉴权风险。
- 已确认及格线输入为真实前端逻辑问题。
- 已确认课程删除反馈需要拆分为“课程归档/恢复语义”和“章节/资源/题目删除已存在”。

## 10. 用户点名问题补充复核（2026-06-27）

本节只记录本轮用户明确追问的缺口，先归档到计划，再统一修复；不把纯平台页面美化项纳入本轮。

### BUG-004（更新版）：培训章节“标记完成”单击无反馈/需要双击

当前代码状态：

- 后端 `TrainingCourseController.CompleteChapter` 会调用 `MarkChapterCompletedIfReady`，成功后返回带 `CompletedAt` 的 `TrainingCourseChapterModel`。
- 前端章节页 `src/GZCTF/ClientApp/src/pages/training/courses/[courseId]/chapters/[chapterId]/index.tsx` 在 `complete()` 中 `setChapter(res.data)` 后又 `await load()`，理论上可单击更新。
- 但当前交互仍缺少两个闭环保障：
  - 成功后没有同步刷新/提示课程列表中的进度状态，用户从课程页回来可能看到旧状态。
  - 按钮成功后的不可点击态完全依赖 `chapter.completedAt`，若 `load()` 返回延迟或失败，用户会看到可再次点击。

最小修复目标：

- 单击成功后立即把按钮置为不可点击并显示“已完成”。
- 成功后刷新章节和课程详情；如果刷新失败，也保留接口返回的完成态，不让用户误以为没生效。
- 对未满足完成条件的章节给出明确缺口提示，不吞掉第一次点击。

验收：

- 无实验纯理论章节：单击一次“标记完成”后按钮立即变“已完成”，课程进度刷新。
- 有必做实验/课后测试章节：未完成时单击返回明确错误；完成后单击一次生效。

### TRAIN-004：课后测试提交后缺少错题复盘

当前代码状态：

- 后端 `TrainingCourseChapterTheoryPlayerPaperModel` 已在提交后通过 `answerIndexes` 暴露正确答案索引。
- 前端 `theory.tsx` 已能在当前题卡内显示“我的答案/正确答案”，但没有独立的结果总览、错题列表、个人错误作答集中查看和解析区域。
- 当前题库模型没有独立 `analysis/explanation` 字段。本轮不新增数据库字段，避免为复盘引入大迁移；先基于已有 `content/options/answerIndexes/answers` 做可用复盘，并在没有解析字段时显示“解析暂未配置”。

最小修复目标：

- 提交后在答卷顶部展示复盘面板：总分、正确题数、错题数、正确率、通过状态。
- 展示错题列表，点击可跳到对应题；错题条目显示题号、题型、我的答案、正确答案。
- 每道题提交后显示“答题复盘”：我的答案、正确答案、解析（暂无解析时明确提示）。
- 题目索引在提交后区分正确/错误，方便复盘定位。

验收：

- 提交后不只是总分；能看到错题列表、个人作答、标准答案和解析占位。
- 全对时显示“本次没有错题”，而不是空白。

### TRAIN-007：课后测试及格线无法清空

当前代码状态：

- `theory-edit.tsx` 使用 `Number(value) || 60`，空值会立即回填 60，真实存在。

最小修复目标：

- `NumberInput` 允许临时空值。
- 保存时校验 `1-100`，无效时提示，不提交。
- 不改后端模型。

### UI-003：队伍退出/解散入口缺失，以及邀请码复制/对齐

当前代码状态：

- 后端 `TeamController` 已有 `TeamLeave` 与 `TeamDeleteTeam`，无需新增接口。
- 新队伍页已接入保存、头像、转让、剔除、入队审核，但未露出普通成员“退出队伍”和队长“解散队伍”入口。
- 邀请码仍用 Mantine `useClipboard`，在 HTTP/IP 环境下可能失败且当前代码无失败反馈；页面已有 `copyText` fallback 工具但未接入。
- 邀请码按钮垂直不居中，`.yy-team-invite-control { align-items: flex-end; }` 是直接原因。

最小修复目标：

- 队长展示“解散队伍”危险按钮，二次确认后调用 `api.team.teamDeleteTeam`，成功后刷新队伍列表。
- 普通成员展示“退出队伍”危险按钮，二次确认后调用 `api.team.teamLeave`，成功后刷新队伍列表。
- 邀请码复制改用 `copyText(inviteCode)`，按真实结果显示成功/失败。
- 局部修正邀请码输入框和按钮中线对齐。

验收：

- 队长能解散；普通成员能退出；成功后回到未加入/其他队伍状态。
- HTTP 访问下复制邀请码后能粘贴正确内容；失败时提示手动复制。
- 邀请码按钮与输入框视觉居中。

### UI-004：无权限管理页不应粗糙 404，应隐藏入口并强访回首页

当前代码状态：

- `AppNavbar` 对学生已隐藏管理入口/显示培训入口，这部分基本符合。
- `WithAdminTab` 已按角色过滤子标签，老师不会看到 Admin 级标签。
- 但 `WithRole` 对权限不足会 `navigate('/404')`，与用户要求不一致。
- 如果老师强行访问 `/admin/teams` 等 Admin 子页，`WithAdminTab` 会重定向到其第一个可访问管理页；如果学生强行访问 `/admin/*`，`WithRole` 当前会进 404。

最小修复目标：

- `WithRole` 对已登录但权限不足统一跳转 `/`，不再展示 404。
- 未登录仍跳登录页并保留 from。
- 保持管理子标签按权限过滤：老师只看到比赛管理、题库管理、环境模板、用户管理等允许项；学生不显示管理入口。
- 后端鉴权不因前端跳转而放松，API 仍按角色返回 401/403。

验收：

- 学生直接访问 `/admin/games` 跳首页。
- 老师直接访问 `/admin/teams` 跳到老师可访问的第一个管理页或首页，不展示粗糙 404。
- 管理按钮对无权限用户不可见。

### 访问权限横向检查范围

本轮统一检查以下前端入口与后端接口是否存在同类遗漏：

- 前端：`WithRole`、`WithAdminTab`、`AppNavbar`、培训课程详情、培训章节/资源/实验/课后测试、队伍管理。
- 后端：培训课程访问 `CanLearnCourse`，管理课程 `CanEditCourse`，理论测试 `ChapterTheory/SaveDraft/Submit`，队伍成员/队长接口，榜单/导出接口。
- 处理原则：前端只做入口隐藏和友好跳转；真正权限必须由后端控制。

### 执行顺序调整

1. 先修培训章节完成、课后测试复盘、及格线输入。
2. 再修队伍退出/解散、邀请码复制和对齐。
3. 最后修 `WithRole` 权限不足跳首页，并横向检查关键受控入口。
4. 完成后更新本文件进度记录，再运行 `pnpm --dir src/GZCTF/ClientApp check` 与 `git diff --check`。如涉及后端改动，再运行 `dotnet build src/GZCTF/GZCTF.csproj --no-restore`。

## 11. 执行进度补充

### 2026-06-27 用户追问后

- 已确认 BUG-004 更新版需要补前端即时完成态和课程进度刷新闭环。
- 已确认课后测试复盘已有答案展示基础，但缺独立错题列表/复盘总览/解析占位。
- 已确认及格线输入 `Number(value) || 60` 是真实未修。
- 已确认 UI-003 后端已有退出/解散接口，当前前端新队伍页漏接。
- 已确认 UI-004 前端强访权限不足仍跳 `/404`，需要统一改为跳首页；入口隐藏和管理子标签过滤已有基础。

### 2026-06-27 实施记录

已完成第一轮统一修复：

- BUG-004（更新版）：章节完成按钮现在在接口成功后立即锁定并显示“已完成”，同时刷新章节/课程数据；切换章节会重置临时锁定态。
- TRAIN-004：课后测试提交后新增复盘总览、正确率、错题列表、错题跳转、每题我的答案/正确答案/解析占位；题目索引提交后区分正确/错误。
- TRAIN-007：课后测试及格线输入允许清空，保存时统一校验 1-100，不再自动回填 60。
- UI-002：课程资源外链/视频按钮显示“打开”，本地资源保持“下载”。
- UI-003：队伍页接入退出队伍、解散队伍入口，均使用已有后端 API 并做二次确认；邀请码复制改用 `copyText` fallback，失败时给出真实提示；邀请码按钮改为垂直居中。
- UI-004：`WithRole` 对已登录但权限不足的强行访问统一跳首页；未登录仍跳登录页；管理子标签继续按角色过滤。
- SEC-003：渗透赛榜单普通用户必须是 Accepted 参赛队伍成员，Teacher 及以上仍可监控访问。

已核实/不需要本轮改动：

- `GameController.ScoreboardSheet` 已有 `[RequireMonitor]`，普通用户导出榜单不应可访问，后续服务器验收即可。
- 课后测试题库当前无独立解析字段，本轮不新增迁移；前端显示解析占位，避免假装已有解析。

本地检查：

- `pnpm --dir src/GZCTF/ClientApp check` 通过。
- `dotnet build src/GZCTF/GZCTF.csproj --no-restore` 通过，仅有既有 nullable/obsolete 警告。
- `git diff --check` 通过，仅有 CRLF 提示。
