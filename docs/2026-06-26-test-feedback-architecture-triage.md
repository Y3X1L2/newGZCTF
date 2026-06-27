# 2026-06-26 测试反馈问题汇总与架构层分析

## 1. 背景与处理原则

本文件汇总以下测试反馈来源：

- `D:/Downloads/平台开发issue_数据表_表格.csv`
- `D:/Downloads/隐域 CTF 测试.md`
- `D:/Downloads/隐域测试反馈.md`
- `D:/Downloads/📄测试反馈与 Bug 报告.md`

当前部署/验证目标环境：

- 内网平台服务器：`10.24.0.27`
  - 账号：`whoami`
  - 密码：`qwer1234!`
- 公网映射服务器：`203.195.157.191`
  - 账号：`ubuntu`
  - 密码：`Fisher(1^`
- 固定镜像存储服务器目标：`10.24.0.28:5000`

处理原则：

1. 优先处理功能异常、阻断链路、安全/越权/数据泄露类问题。
2. 前端视觉美化、布局细节、文案体验类问题降级处理，但保留记录。
3. 已过时的问题不得按原架构盲修。例如 FRP 相关反馈需要按当前 Nginx + Redis 端口代理架构重新验证。
4. 问题不一定真实存在。无法复现、无法定位或依赖截图/环境证据不足时，必须标记为“待复测/待确认”，不得在代码中做猜测式修复。
5. 修复时应优先寻找统一架构入口，例如鉴权中间件、统一错误返回、端口代理生命周期、课程权限守卫，而不是只修单个页面。

## 2. 优先级定义

| 级别 | 含义 | 示例 |
|---|---|---|
| P0 | 安全高风险、核心链路阻断、部署/运维不可用 | 未认证访问完整榜单、镜像导入失败、多节点状态不可观测 |
| P1 | 核心业务异常、权限失效、生命周期不一致 | Logout Token 不失效、容器销毁端口未释放、课程审核绕过 |
| P2 | 重要功能缺陷但不完全阻断 | 错题复盘缺失、必做题规则异常、邀请码复制失败 |
| P3 | 体验、文案、布局、可用性优化 | 首页登出 UI 残留、继续学习排序、老师管理页入口说明 |

## 3. 问题总表

| ID | 问题 | 来源 | 建议级别 | 当前判断 | 处理方向 |
|---|---|---|---|---|---|
| SEC-001 | 未认证可访问完整赛事排行榜 | CTF 测试、CSV | P0 | 高风险，需复测确认范围 | 统一榜单 API 鉴权与公开策略 |
| SEC-002 | 理论赛未报名仍可查看理论榜单 | Bug 报告 | P0/P1 | 与 SEC-001 同类 | 比赛参与状态 + 榜单访问控制 |
| SEC-003 | Logout 后旧 `GZCTF_Token` 仍有效 | CTF 测试、CSV | P1 | 高风险 | 服务端 Token 失效机制 |
| SEC-004 | Docker Registry 内网无认证 | CTF 测试 | P1/P2 | 需结合内网信任边界判断 | 固定 Registry 后做访问面收敛 |
| OPS-001 | 容器销毁后旧入口仍 HTTP 200 | CTF 测试、CSV | P1 | 高风险，需按新代理架构复测 | 容器、端口、Nginx、Redis 生命周期一致性 |
| OPS-002 | FRP 公网转发不通 | CTF 测试、CSV | 已过时/P0 复测项 | 原 FRP 结论过时 | 改按 Nginx + Redis 代理链路验证 |
| OPS-003 | 本地镜像导入接口异常 | CSV | P0 | 阻断镜像导入 | 镜像导入、Registry 推送、模板状态链路 |
| OPS-004 | 远程 Registry 镜像题目创建失败 | CTF 测试 | P1 | 可能被最新 main 修复，需复测 | 外部镜像拉取、Agent 错误返回 |
| OPS-005 | Windows 靶机状态未在节点管理实时显示 | CTF 测试、CSV | P0/P1 | 运维观测缺口 | 节点资源聚合 API + 前端状态展示 |
| OPS-006 | 存储服务器功能需固定为 `10.24.0.28:5000` | 用户补充 | P1 | 明确需求 | 移除可配置存储服务器 UI，固定 Registry |
| TRAIN-001 | 报名课程未经审核可访问章节 | CTF 测试 | P1 | 权限流程问题 | 课程报名状态守卫 |
| TRAIN-002 | 标记完成按钮第一次无反应，需点两次 | Bug 报告 | P1 | 核心交互缺陷 | 单次点击后状态、接口、进度同步 |
| TRAIN-003 | 实验完成后课程进度不自动同步 | Bug 报告旧版 | P1/P2 | 被 TRAIN-002 包含 | 后端完成规则 + 前端刷新 |
| TRAIN-004 | 课程/章节无法删除 | 培训反馈、CSV | P1 | 功能缺失 | 软删除优先，保留历史记录 |
| TRAIN-005 | 必做题并非必做 | CSV | P2 | 需复测规则 | 必做/选做规则校验 |
| TRAIN-006 | 课后测试无错题、答案、解析 | 培训反馈、Bug 报告、CSV | P2 | 功能不完整 | 学生复盘页面和教师可见策略 |
| TRAIN-007 | 继续学习区域排序不符合文案 | Bug 报告 | P3 | 体验缺陷 | last studied 时间戳排序 |
| TEAM-001 | 邀请码复制失效 | Bug 报告 | P2 | 可能受 HTTP 剪贴板限制 | clipboard fallback |
| TEAM-002 | 队伍缺少退出/解散 | Bug 报告 | P2/P3 | 生命周期缺口 | 队长/成员差异化危险操作 |
| USER-001 | 手机号校验不严 | Bug 报告 | P2 | 数据质量问题 | 前后端双重正则/长度校验 |
| UI-001 | 首页登出后头像/菜单残留 | Bug 报告 | P3 | UI 状态问题 | 全局 AuthState 清理 |
| UI-002 | 外链资源应前往而不是下载 | CSV | P3 | 交互配置问题 | 资源类型与打开方式 |
| UI-003 | 老师权限管理页用途不明确 | Bug 报告 | P3 | 信息架构问题 | 角色入口说明或隐藏无用入口 |
| UI-004 | 平台页面修改附件方案 | CSV | P3/P1 待拆解 | 附件未在本地完整获得 | 拆分为具体可验证问题 |

## 4. 架构层问题延伸分析

### 4.1 未认证/未授权访问类问题

已反馈问题：

- 未登录可访问 `/api/Game/{id}/Scoreboard`。
- 未报名可访问理论排行榜。

架构延伸风险：

1. 其他比赛类型可能存在同类问题：
   - Jeopardy/CTF 榜单
   - Theory 榜单
   - AWDP 榜单
   - AWD 榜单
   - Penetration/综合渗透榜单
   - Mixed 大屏聚合榜单
2. 展示大屏接口可能绕过普通页面鉴权：
   - 大屏通常为了展示便利可能设计成公开接口。
   - 若大屏返回完整队伍/用户/题目/提交详情，需要单独定义公开边界。
3. 前端隐藏按钮不能作为安全边界：
   - 所有敏感接口必须由后端鉴权。
   - 未报名、未审核、比赛未开始、比赛已结束后的可见范围应统一由后端判定。
4. 需要统一未授权返回语义：
   - 未登录：`401 Unauthorized`
   - 已登录但无权限：`403 Forbidden`
   - 比赛不存在或为了防枚举隐藏：可选 `404 NotFound`
   - 前端应有统一错误页面/提示，而不是每个页面散落处理。

修复效果目标：

- 未登录不能访问敏感榜单和队伍/成员/提交详情。
- 未报名队伍不能查看非公开比赛榜单。
- 比赛配置可明确控制“公开榜单/仅参赛队伍/仅管理员/赛后公开”等策略。
- 所有榜单和大屏接口采用同一权限判断函数，避免同类漏洞反复出现。

待确认清单：

- 当前是否已有比赛公开配置字段。
- 当前公开大屏是否允许匿名访问，允许到什么粒度。
- 是否存在导出接口、历史提交接口、通知接口间接泄露榜单数据。

### 4.2 Token 注销与会话失效

已反馈问题：

- Logout 只清理客户端 Cookie，旧 Token 服务端仍可用。

架构延伸风险：

1. 如果系统使用纯 JWT 且无服务端状态，Logout 天然无法立即失效旧 Token。
2. 如果 Token 有较长有效期，被窃取后可以长期复用。
3. 修改密码、禁用用户、角色变更后，旧 Token 是否也应失效需要统一定义。
4. 多设备登录是否允许、是否支持单设备退出/全部退出，需要明确。

修复效果目标：

- Logout 后当前 Token 立即失效。
- 用户被封禁、删除、角色降权后，旧 Token 不能继续访问高权限接口。
- 前端登出 UI 同步清理，但这只是体验层；真正边界在后端。

可能方案：

- Token 版本号：用户表维护 `TokenVersion/SecurityStamp`，Token 中带版本，校验时比对。
- 黑名单/撤销表：Logout 记录当前 Token jti 到缓存/数据库直到过期。
- 会话表：所有 Token 都对应服务端 session，可精确注销。

待确认清单：

- 当前 `GZCTF_Token` 是 JWT、Cookie session 还是自定义 Token。
- 是否已有 Redis/缓存适合做撤销表。
- 是否已有用户安全戳字段。

### 4.3 容器入口、端口代理与生命周期一致性

已反馈问题：

- 容器销毁后旧入口仍 HTTP 200。
- 旧 FRP 转发不通。

当前架构变化：

- FRP 反馈已过时，当前应按 Nginx + Redis 端口代理重新验证。

架构延伸风险：

1. 容器停止、销毁、异常退出、Agent 离线时，代理映射是否都会被清理。
2. 端口释放后是否可能被新容器复用，导致旧链接访问到新题目。
3. Nginx reload/sync 失败时，Redis 状态和 Nginx 实际配置可能不一致。
4. 多节点部署时，公网入口、内网节点地址、容器实际监听端口可能存在三方不一致。

修复效果目标：

- 创建容器后，平台返回当前架构下可访问的正确入口。
- 销毁容器后，Nginx 配置、Redis 映射、端口分配记录、容器记录同步释放。
- 任何清理失败应可观测，并进入可重试的 cleanup 状态。
- 用户看到的错误信息应明确是容器失败、代理失败、端口不足还是镜像拉取失败。

待确认清单：

- `10.24.0.27 -> 203.195.157.191` 的映射链路实际使用哪些端口。
- Nginx 同步任务是否幂等。
- Redis 端口映射 key 是否有 TTL 或 cleanup 补偿。

### 4.4 镜像导入、外部镜像与固定存储服务器

已反馈/新增需求：

- 本地镜像导入接口异常。
- 部分远程 Registry 镜像创建容器失败。
- 固定存储服务器为 `10.24.0.28:5000`，移除调整存储服务器功能，节点管理界面不显示。

架构延伸风险：

1. 镜像模板可能来自多种来源：
   - 本地上传 tar
   - 内网固定 Registry
   - 外部 Registry
   - Agent 本机已有镜像
2. 固定 Registry 后，需要确保所有节点都能稳定访问 `10.24.0.28:5000`。
3. 如果 Registry 无认证，必须明确网络边界，否则镜像列表和镜像层可能泄露。
4. 移除“可切换存储服务器”后，旧数据库配置字段是否仍存在、是否会影响运行时读取，需要兼容处理。

修复效果目标：

- 环境模板统一以 `10.24.0.28:5000` 为默认内网镜像源。
- 节点管理不再显示/修改存储服务器。
- 已有模板不因字段缺失或旧配置而失效。
- 本地镜像导入后能推送到固定 Registry，并被调度节点拉取。

待确认清单：

- 当前环境模板里镜像地址是否已经统一包含 `10.24.0.28:5000`。
- 旧的存储节点字段是否仍被后端调度逻辑读取。
- 是否需要保留配置项作为 appsettings 常量，而不是 UI 可调。

### 4.5 节点管理与 VM/容器观测

已反馈问题：

- Windows 靶机启动后，节点管理界面未实时显示状态。

架构延伸风险：

1. 节点管理可能只统计 Docker 容器，未统一 VM 资源。
2. Windows VM 状态来自不同服务或 Agent，刷新频率/状态枚举可能不同。
3. 节点下线、Agent 断连、VM 创建失败可能造成资源状态悬挂。
4. 如果状态只在创建接口返回时更新，节点管理页面无法作为运维看板。

修复效果目标：

- 节点管理统一展示 Docker 容器、Linux VM、Windows VM、渗透环境资源。
- 状态包含创建中、运行中、停止中、已停止、失败、清理中。
- 支持手动刷新；如已有 SignalR/轮询机制，可自动刷新。
- 操作入口包括销毁、查看开放地址、查看开启者、开启时间、持续时间等。

待确认清单：

- 当前 VM 状态模型和 Docker 容器状态模型是否已经统一。
- Windows VM 是否由主服务、Agent 还是独立虚拟化服务上报。

### 4.6 培训权限与课程生命周期

已反馈问题：

- 报名后未经确认可访问章节。
- 课程/章节无法删除。
- 必做题逻辑不生效。
- 标记完成需点击两次。
- 错题复盘缺失。

架构延伸风险：

1. 学生课程访问不应只看“是否报名”，还要看报名状态：待审核、已通过、已拒绝、已退课。
2. 老师/管理员课程编辑权限需要和学生分组、课程归属、角色等级一致。
3. 删除课程/章节会影响学习记录、考试记录、实验实例、资源文件，不能简单硬删。
4. 必做/选做规则会影响章节完成、课程完成、统计面板和教师查看。
5. 标记完成和实验完成可能有两个入口，必须由后端统一计算最终完成状态。

修复效果目标：

- 未审核学生不能访问章节正文、课件、实验入口和课后测试。
- 删除默认采用软删除；历史学习记录仍可审计。
- 必做题未完成时章节不能被判定完成。
- 单次点击标记完成立即生效并刷新课程进度。
- 学生提交课后测试后能看到错题、自己的答案、正确答案和解析；是否显示答案可配置。

待确认清单：

- 当前是否已有课程报名审核状态字段。
- 课程资源是否有文件引用计数或统一资源表。
- 章节完成状态由前端状态、后端记录还是运行时聚合决定。

### 4.7 队伍生命周期与复制能力

已反馈问题：

- 邀请码复制失效。
- 缺少退出队伍/解散队伍能力。

架构延伸风险：

1. Clipboard API 在 HTTP 环境下可能不可用，公网 IP 非 HTTPS 测试环境尤其容易复现。
2. 队伍解散和退出会影响比赛报名、参赛记录、成绩归属、队长转让。
3. 正在参加比赛时是否允许退出/解散需要业务规则。

修复效果目标：

- 复制按钮在 HTTP/HTTPS 下都有 fallback，并给出成功/失败反馈。
- 队长可解散队伍或转让队长，普通成员可退出队伍。
- 已报名/比赛进行中队伍的危险操作需限制或二次确认。

待确认清单：

- 当前后端是否已有退出/解散 API。
- 比赛进行中是否允许队伍成员变化。

### 4.8 用户资料与前端状态一致性

已反馈问题：

- 手机号格式校验不严。
- 首页登出后头像和菜单残留。

架构延伸风险：

1. 前端校验不能防止 API 直接写入脏数据。
2. 用户资料字段可能在管理员编辑、用户自编辑、导入用户时走不同入口。
3. 登出 UI 残留可能说明全局用户状态没有统一来源。

修复效果目标：

- 手机号后端统一校验，前端提供即时提示。
- 登出后全局 AuthState 立即清空，所有页面头像/菜单同步变化。
- 401/403 后前端有统一处理，不出现假登录状态。

待确认清单：

- 手机号是否允许为空、国际号码、座机或只允许大陆手机号。
- 当前用户状态是 SWR/query、context 还是本地缓存驱动。

## 5. 统一错误与权限反馈规范

建议统一后端错误语义：

| 场景 | HTTP 状态 | 前端表现 |
|---|---|---|
| 未登录 | 401 | 跳转登录或弹出登录提示 |
| 已登录但无权限 | 403 | 显示无权限页面/提示，不自动跳登录 |
| 资源不存在 | 404 | 显示资源不存在 |
| 资源存在但为防枚举隐藏 | 404 或 403 | 按安全策略统一 |
| 业务状态不允许 | 409 或 400 | 显示具体业务原因 |
| 后端异常 | 500 | 显示统一错误页，并记录 request id |

建议统一前端错误体验：

1. 页面级 401/403/404/500 有统一组件。
2. 操作级错误用 toast/modal 展示具体原因。
3. 不再只显示 `common.error.encountered`，除非同时有可复制的详细诊断。
4. 敏感信息不暴露给普通用户，但管理员日志可追踪。

## 6. “不盲目猜测”记录规范

当问题无法定位或无法复现时，必须在修复记录中保留以下信息：

```text
问题编号：
反馈来源：
反馈现象：
当前验证环境：
验证账号/角色：
验证步骤：
实际结果：
结论：已复现 / 未复现 / 部分复现 / 依赖缺失无法验证
不确定点：
下一步需要的证据：
是否允许代码修改：是 / 否 / 暂缓
```

不得执行以下操作：

- 只凭截图猜测字段名并修改核心逻辑。
- 用前端隐藏按钮替代后端鉴权。
- 因单个页面反馈而破坏通用接口兼容。
- 对历史数据做硬删除式迁移。
- 把过时 FRP 问题按旧架构修复。

## 7. 建议执行顺序

第一阶段：安全与阻断

1. 统一榜单/大屏/理论榜单鉴权策略。
2. 实现 Logout 后 Token 服务端失效。
3. 验证并修复容器销毁后的 Nginx/Redis/端口释放链路。
4. 修复本地镜像导入。
5. 修复节点管理 VM/容器状态汇总。

第二阶段：Registry 固定化与部署链路稳定

1. 固定 Registry 为 `10.24.0.28:5000`。
2. 移除节点管理中的存储服务器切换/展示。
3. 兼容旧配置字段，但运行时不再依赖 UI 选择。
4. 复测远程/内网镜像创建容器。

第三阶段：培训核心闭环

1. 修复课程报名审核访问守卫。
2. 修复标记完成单击生效和实验完成联动。
3. 增加课程/章节软删除。
4. 修复必做/选做规则。
5. 增加错题复盘能力。

第四阶段：体验与完整性

1. 邀请码复制 fallback。
2. 队伍退出/解散。
3. 手机号校验。
4. 首页登出 UI 状态同步。
5. 继续学习排序。
6. 外链资源打开方式。
7. 老师权限管理入口说明。

## 8. 本轮明确不做的事

- 不根据 FRP 旧反馈回退或新增 FRP 逻辑。
- 不在未复测前断言外部镜像创建失败仍存在。
- 不根据无法访问的飞书图片做具体代码定位。
- 不在本文件中做代码级定位；后续修复阶段再进入具体文件和接口。

## 9. 执行进度记录

### 2026-06-26 18:51:43 +08:00

- 当前分支：`main...origin/main`。
- 当前工作树：仅本文件为新增未跟踪文档，另有既有未跟踪 `artifacts/`，本轮不触碰。
- CodeGraph：`.codegraph` 已存在，`codegraph status` 显示索引最新；当前 Codex 工具面板未暴露 `codegraph_*` MCP 工具，改用 `codegraph` CLI 执行结构查询。
- 第一批修复范围：
  1. `OPS-006` 固定镜像仓库为 `10.24.0.28:5000`，移除节点管理/存储服务器可配置入口。
  2. `SEC-001/SEC-002` 榜单/理论榜单未认证或未参赛访问控制。
  3. `SEC-003` Logout 后服务端 Token 失效机制。
  4. `OPS-001` 容器销毁后 Nginx/Redis/端口入口清理链路核查，若高确定缺陷存在则修复。

### 2026-06-26 19:16:22 +08:00

- 已完成 OPS-006 固定 Registry 第一批实现：
  - 默认/空配置统一回退到 10.24.0.28:5000。
  - 节点管理接口不再返回或展示存储节点/Registry 端口/Registry 地址。
  - 节点管理 UI 移除“设为镜像存储服务器”和存储节点统计，镜像管理页只展示固定 Registry 地址与上传限制。
  - 一键注册节点只配置固定 Registry 以及模板实际引用的外部 Registry 信任，不再追加目标节点自身的 {host}:5000。
  - 本地节点注册不再自动成为存储节点，不再自动启动本机 registry，只配置固定 Registry 信任并修复历史内部镜像引用。
- 已完成 SEC-001/SEC-002 第一批榜单鉴权：
  - CTF/Jeopardy 普通榜单要求登录且必须是已审核参赛队伍；未登录返回 401，未参赛/未审核返回 403。
  - 理论赛榜单同样要求登录且必须已审核参赛。
  - 大屏公开接口未在本批次收紧，后续应按“大屏公开边界”单独审查，避免把展示场景和普通排行榜混用。
- 已完成 SEC-003 Logout 服务端失效：
  - Logout 时更新当前用户 SecurityStamp 并登出 Cookie。
  - Cookie 校验接入 SecurityStampValidator，ValidationInterval 为 0，旧 GZCTF_Token 会在后续请求中失效。
  - 当前实现会使该用户所有已有登录会话失效；这是安全优先取舍。
- 已完成 OPS-001 Redis + Nginx 生命周期第一批修复：
  - 新增 INginxProxySyncService，NginxSyncService 同时作为后台周期同步器和业务侧主动同步器。
  - Nginx 同步使用串行锁，避免定时同步和业务触发并发写配置。
  - 容器销毁后，DB 容器记录删除成功即主动同步 Nginx；Redis 端口释放仍由 FleetContainerManager 执行，周期同步继续作为兜底。
  - 普通 CTF、培训/练习、测试容器、AWDP、渗透编排、节点强制清理等容器创建/销毁路径已接入主动同步；AWDP/渗透批量创建按阶段末尾同步，避免 Nginx 频繁 reload。
- 验证结果：
  - pnpm --dir src/GZCTF/ClientApp check 通过。
  - dotnet build src/GZCTF/GZCTF.csproj --no-restore 通过；仅存在既有 nullable/obsolete warning。
  - dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore 通过。
  - git diff --check 通过。
- 明确保留：
  - WorkerNode.IsStorageNode / RegistryPort 字段和历史 migration 表暂不删除，避免破坏已有数据库结构。
  - DockerRegistryMigrationService 类保留为历史兼容代码，但当前 UI/API/DI 不再提供切换存储服务器入口。

### 2026-06-26 19:50:00 +08:00

- 第二批修复开始，范围限定在高确定 P1/P2 功能链路，不混入视觉优化：
  1. TRAIN-001：课程详情接口在未审核/未批准报名时不再返回章节正文、资源 URL、题目列表等学习内容，只返回课程公开简介和报名状态。
  2. TRAIN-002：章节完成接口不再静默成功。若必做实验或课后测试未完成，返回明确 400；满足条件时返回最新章节模型，前端按钮立即变为不可点击的“已完成”态。
  3. TRAIN-004：章节删除从直接硬删改为事务内清理关联：子章节父级解除、章节测试题/答题记录清理、提交记录解除章节引用、章节进度和章节题目挂载清理，再删除章节，避免外键导致“删除无效/报错”。
  4. 顺手修复课程题目挂载时的乱码错误提示，改为“课程章节不存在。”。
- 当前未完成：
  - 继续核查 OPS-003 本地镜像导入/Registry 推送链路。
  - 完成后运行前端 check、后端 build、git diff --check，并按需要重新部署到 10.24.0.27 验收。

### 2026-06-26 20:32:00 +08:00

- 已完成 OPS-003 本地 Docker 镜像导入/固定 Registry 链路核查与收敛：
  - 全局镜像上传链路已确认会先构建 `gzctf-internal://` 引用、上传 tar 后 `docker load/tag/push` 到固定 Registry，并在失败时返回后端 `message`，临时目录会清理。
  - 课程内 Docker 镜像上传补齐与全局入口一致的 `repository/tag` 校验异常捕获，非法仓库路径或 Tag 现在返回 400 业务错误，不再落入 500/common.error.encountered。
  - 保持固定 Registry 架构，不恢复节点管理里的存储服务器切换入口。
- 已完成 EXP-001 队伍邀请码复制兼容修复：
  - 新增前端 `copyText` helper，优先使用 Clipboard API，HTTP/IP 非安全上下文失败时回退到临时 textarea + `execCommand('copy')`。
  - 队伍邀请码点击复制改用 fallback，避免公网 IP/HTTP 验收环境下复制静默失败。
- 已完成 PROFILE-001 手机号校验第一批修复：
  - 新增后端 `PhoneNumberAttribute`，个人资料、管理员修改用户、批量创建用户三条入口统一校验：允许空值；非空时允许大陆 11 位手机号或 E.164 国际号码。
  - 个人资料页和管理员用户编辑页增加即时校验提示。
  - 顺手修复个人资料保存后 `disabled` 状态未恢复的问题，避免保存后表单一直锁定。
- 下一步：运行 `pnpm --dir src/GZCTF/ClientApp check`、`dotnet build src/GZCTF/GZCTF.csproj --no-restore`、`dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore`、`git diff --check`，根据结果修正编译/类型问题。

### 2026-06-26 20:47:00 +08:00

- 第二批修复验证完成：
  - `pnpm --dir src/GZCTF/ClientApp check` 通过。
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore` 通过；仅保留既有 nullable/obsolete warning。
  - `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore` 通过。
  - `git diff --check` 通过。
- 下一批核查顺序：
  1. TRAIN-005 课后测试/理论培训错题复盘是否已有后端数据和前端展示；若缺失，优先补只读复盘接口和页面。
  2. EXP-002 队伍退出/解散/转让 API 与前端是否已经闭环；若已存在则只记录，不重复实现。
  3. EXP-003 外链资源打开方式、继续学习排序等体验类问题，按可稳定修复原则收敛处理。

### 2026-06-26 21:08:00 +08:00

- 已完成 TRAIN-005 课后测试错题复盘第一批闭环：
  - 学生课后测试接口在答卷 `Submitted` 后返回每题正确答案索引；草稿/未提交状态不返回答案，避免提前泄露。
  - 学生课后测试页面提交后显示每题“正确/错误”、我的答案、正确答案，形成可用的错题复盘。
  - 当前课程题库模型没有解析/Explanation 字段，本轮不做空实现；若后续需要展示解析，应先扩展课程题库模型、编辑器和迁移，再在复盘中展示。
- EXP-002 队伍退出/解散/转让/踢人链路核查结果：
  - 后端已有 Leave、DeleteTeam、Transfer、KickUser、InviteCode、UpdateInviteToken API。
  - 前端队伍管理弹窗已接入退出、解散、转让队长、踢人和邀请码刷新；本轮只补了 HTTP 复制 fallback，不重复实现已有业务链路。
- 本批验证结果：
  - `pnpm --dir src/GZCTF/ClientApp check` 通过。
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore` 通过；仅存在既有 nullable/obsolete warning。
  - `git diff --check` 通过。

### 2026-06-26 21:24:00 +08:00

- 新一轮继续修复开始：
  - 已同步 CodeGraph 索引，当前有 68 个变更文件被重新解析；后续结构查询优先使用 `codegraph` CLI，刚编辑文件以磁盘内容为准。
  - 当前工作树仍包含多轮累计未提交改动，继续遵循“不回滚、不清理 artifacts、不混入无关视觉重构”的原则。
  - 本轮优先核查并修复剩余高确定问题：继续学习排序、外链资源打开方式漏点、401/403 统一反馈明显空缺、大屏公开边界是否泄露普通榜单受控数据。

### 2026-06-26 21:58:00 +08:00

- 已完成 TRAIN-007 继续学习排序修复：
  - 课程模型新增只读 `LastStudiedAt`，取自已有 `TrainingCourseProgress.UpdatedAt`，不新增数据库字段和迁移。
  - 学生进入章节时会刷新课程学习进度的 `UpdatedAt`，使“继续学习”按真实最近学习行为排序；课程编辑时间仅作为兜底。
- 已完成 UI-002 外链安全漏点收敛：
  - 课程题目附件、章节实验附件外链补齐 `rel="noopener noreferrer"`。
- 已完成 401/403 统一反馈第一批：
  - `showErrorMsg` 对 401/403 增加明确标题：未登录为“请先登录”，无权限为“无权访问”。
  - 所有 locale 的 common error 补齐 `unauthorized/forbidden`，避免显示裸 key。
- 已完成大屏公开边界复核与修复：
  - 普通 CTF 榜单和理论赛榜单仍要求登录。
  - 学生用户必须已审核参赛才能读取普通榜单，继续修复未报名越权。
  - 老师及以上作为管理/监控视角可读取榜单，避免管理端大屏复用选手接口时被误伤。
  - AWDP 与渗透大屏已走 admin API，本轮不改。
- 本批验证结果：
  - `pnpm --dir src/GZCTF/ClientApp check` 通过。
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore` 通过；仅存在既有 nullable/obsolete warning。
  - `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore` 通过。
  - `git diff --check` 通过。

### 2026-06-26 20:49:48 +08:00

- 已完成 OPS-005 节点管理资源汇总补齐：
  - 复核确认原节点资源面板已覆盖普通 Docker 容器和 VM，但综合渗透运行节点缺少独立资源语义；底层容器即使出现，也缺少队伍环境、拓扑资产、内网地址、公开入口和清理入口上下文。
  - 后端 `/api/v1/nodes/{id}/resources` 新增 `pentest` 资源类型，聚合 `PenetrationRuntimeNodes`，展示比赛、队伍、资产节点、网络名、内网地址、公开入口、运行状态和持续时间。
  - 普通容器列表排除已挂载到综合渗透 runtime 的底层容器，避免“全部资源/容器筛选”出现重复资源。
  - 前端节点管理面板新增“综合渗透”筛选和“渗透资产”计数；运行态、清理中、孤儿资源、需人工清理等状态使用统一渐变状态文字。
  - 综合渗透资源的危险操作改为“清理该队伍环境”，走既有 `/api/admin/pentest/games/{gameId}/teams/{teamId}/cleanup`，不伪造单节点销毁能力，避免破坏渗透环境整体生命周期。
- 本批验证结果：
  - `pnpm --dir src/GZCTF/ClientApp check` 通过。
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore` 通过；仅存在既有 nullable/obsolete warning。
  - `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore` 通过，0 warning / 0 error。
  - `git diff --check` 通过。

### 2026-06-26 20:53:58 +08:00

- 已完成运维接口权限面收敛：
  - 复核发现 `/api/v1/nodes`、`/api/v1/nodes/{id}`、`/api/v1/deployment-targets`、`/api/v1/deployment-targets/{id}` 原先只要求登录，会向学生等普通用户暴露节点地址、负载、调度容量、部署目标主机和任务状态。
  - 以上节点和部署目标读接口已收紧为 `RequireAdmin`；Agent 心跳、节点注册、资源清理、内部 token 下载链路未改。
  - 复核全局镜像模板列表/详情只被管理和教师侧页面使用，但原先只要求登录且列表返回 `LocalFilePath`；已收紧为 `RequireTeacher`，并从列表响应去掉本地文件路径。
  - 学生培训页使用课程内资源/课程镜像模板接口，不依赖全局 `/api/v1/image-templates`，本次收紧不影响学生学习链路。
- 本批验证结果：
  - `pnpm --dir src/GZCTF/ClientApp check` 通过。
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore` 通过；仅存在既有 nullable/obsolete warning。
  - `git diff --check` 通过。

### 2026-06-26 20:56:01 +08:00

- 已完成剩余 `Authorize` 控制器复核：
  - `ProxyController` 是选手容器代理入口，保持登录即可访问，不能收紧为教师/管理员。
  - `ImageTemplateController` 类级 `Authorize` 作为兜底保留，实际列表、详情、上传、删除、Registry 配置等管理操作已由方法级教师权限保护；匿名下载仍通过节点 token 或管理员身份校验，不放开。
  - `GamePhaseController` 阶段列表当前只由管理端阶段页使用，属于赛事控制面配置，已从登录即可读收紧为 `RequireTeacher`。
- 本批验证结果：
  - `pnpm --dir src/GZCTF/ClientApp check` 通过。
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore` 通过；仅存在既有 nullable/obsolete warning。
  - `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore` 通过，0 warning / 0 error。
  - `git diff --check` 通过。

### 2026-06-26 21:02:12 +08:00

- 已完成外链和本地路径展示收敛：
  - 全局环境模板列表后端已不返回 `LocalFilePath` 后，前端镜像管理页同步移除 `localFilePath` 字段、搜索条件和来源展示，避免管理 UI 暴露或暗示服务器本地路径。
  - 题目编辑页 Windows 模板提示从“路径”改为“模板名称”，不再依赖本地路径字段。
  - 统一补齐 `_blank` 链接和下载窗口的 `noopener noreferrer` / `noopener,noreferrer`：题目附件、实例入口、Writeup 下载、流量包下载、节点资源入口、IR 远程桌面等。
  - 复扫确认前端 `localFilePath` 已无残留；剩余 `_blank` 调用均带安全参数或安全 rel。
- 本批验证结果：
  - `pnpm --dir src/GZCTF/ClientApp check` 通过。
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore` 通过，0 warning / 0 error。
  - `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore` 通过，0 warning / 0 error。
  - `git diff --check` 通过。

### 2026-06-26 21:16:13 +08:00

- 已完成 OPS-004 远程 Registry 镜像注册/拉取失败可观测性修复：
  - ImageTemplate 新增 ErrorMessage 字段和 EF migration，用于记录最近一次导入、拉取或分发失败原因。
  - 全局环境模板 egister-docker 后台拉取成功会清空错误，失败会将 Docker/Registry 的实际失败原因写回模板，管理页列表可直接查看。
  - 课程内环境模板 Docker 注册路径同步修复；后台拉取改为独立 DI scope，避免请求结束后复用已释放 DbContext 导致状态更新不稳定。
  - Docker 包上传成功会清空旧错误；重新注册 Error 模板会清空旧错误并进入 Importing。
  - 该修复不改变固定 Registry、Nginx + Redis 或节点调度架构，只补齐失败诊断闭环。
- 验证结果：
  - pnpm --dir src/GZCTF/ClientApp check 通过。
  - dotnet build src/GZCTF/GZCTF.csproj --no-restore 通过；仅保留既有 nullable/obsolete warning。
  - dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore 通过，0 warning / 0 error。

### 2026-06-26 21:21:06 +08:00

- 已完成 UI-003/权限入口一致性修复：
  - 管理外壳 AdminPage 从 Role.Admin 调整为 Role.Teacher 起步，避免老师在导航可见管理入口后被前端壳层阻断。
  - 管理导航补齐“培训管理”教师可见入口；节点、部署队列、日志、设置等高危页面仍在 WithAdminTab 中按 Role.Admin 过滤，并继续由后端权限兜底。
- 已完成 SEC-004 固定 Registry 边界收敛记录：
  - docs/nginx-redis-port-proxy-usage.md 新增固定内网 Registry 边界说明，明确 10.24.0.28:5000 仅允许主服务器和受信任 Worker 节点访问，不应映射到公网。
  - 明确一键节点注册会配置 Docker insecure-registries，拉取失败时优先检查节点到固定 Registry 的内网连通性和 Docker daemon 重启状态。
  - 暂不临时加入 Registry 认证；若未来启用认证，需要同步改造 Docker daemon 凭据分发、Agent 拉取凭据、上传推送凭据和已有镜像引用迁移。
- 验证结果：
  - pnpm --dir src/GZCTF/ClientApp check 通过。
  - dotnet build src/GZCTF/GZCTF.csproj --no-restore 通过，0 error。
  - git diff --check 通过。

### 2026-06-26 22:18:00 +08:00

- 已完成旧 Scenario / IR 实验模块权限边界补齐：
  - `/api/v1/scenarios` 列表与详情、`/api/v1/ir-challenges` 列表与详情收紧为管理员配置接口，避免学生直接读取阶段、评分规则、检查点、镜像等管理配置。
  - Scenario / IR 创建实例、时段列表、预约时段、旧排行榜统一要求普通学生已审核参赛；教师及以上保留监控/教学读取能力。
  - Scenario / IR 实例详情、提交、重置增加实例归属校验；普通学生只能读写自己的实例，管理员保留运维处理能力。
  - 多类型提交接口和 Writeup 上传接口不再信任客户端传入的 `gameId/teamId/participationId`，改为后端按题目所属比赛和当前用户已审核参赛关系计算归属，防止伪造成绩写入其他队伍或其他比赛。
- 验证结果：
  - `pnpm --dir src/GZCTF/ClientApp check` 通过。
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore` 通过，0 warning / 0 error。
  - `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore` 通过，0 warning / 0 error。
  - `git diff --check` 通过。
- 下一步：
  - 生成完整发布包并部署到 `10.24.0.27`，部署后检查 `gzctf.service`、8080 端口、本机首页响应和最近服务日志。

### 2026-06-26 23:58:00 +08:00

- 已完成部署到 `10.24.0.27`：
  - 本地生成完整发布包：`artifacts/publish-gzctf`，压缩包 `artifacts/gzctf-publish-20260626-2218.tar.gz`。
  - 使用 Python/Paramiko 上传到服务器 `/tmp/gzctf-publish-20260626-2218.tar.gz`。
  - 服务器侧停止 `gzctf.service`、备份 `/opt/gzctf/publish`、保留 `appsettings.json` / `files` / `keys`、替换发布目录并重启服务。
- 部署健康检查结果：
  - `gzctf.service` 为 `active`。
  - `ss -lntp` 显示 `*:8080` 已由 `GZCTF` 监听。
  - `curl -I http://127.0.0.1:8080/` 返回 `HTTP/1.1 200 OK`。
  - 最近日志显示服务启动、Docker 初始化、Redis 锁、端口分配、节点注册正常；未见启动失败或静态资源错误。
- 注意：
  - 第一次部署脚本因远端 `sudo` 等待密码卡住，已清理残留进程并改用 `sudo -S` 重新执行成功。
