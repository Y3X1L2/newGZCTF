# 管理日志发布执行记录

执行人：发布子代理；独立门禁：审查子代理；集成与回退决策：父会话。平台摘要由集成负责人维护。本记录保存已完成的发布、UI验收和仍未通过的完整业务链路边界。

## 授权与最终状态

用户明确授权合并PR #30、发布主站前后端与前向迁移，并要求记录耗时和简化发布流程；另明确授权whoami永久免密sudo。10-14已合并PR #30为 `6d3117f48a994ec3750a27096f280aca84e0ac3c`。10-16接续现场，发现日常日志噪声和历史结果误判后，以正常合并且全CI通过的PR #33作为最终应用候选。

- 最终应用：`927ceef8d731f28f9c323d354d61990f1e6de8c2`，tree `40f08f865634cf535f4ccd4eccb9ed18928cde34`。
- 10-16 12:21:20–12:21:44 UTC完成一次Main停服、同字节前向迁移、独立release原子切换与HTTP恢复；精确stop-to-HTTP耗时 **24.271秒**。
- 基础发布通过；首次UI发现日志页显示原始 `logs.*` 翻译key、壳层中文正常。根因是正式App缺i18next初始化，随后仅前端PR #34已完成受审制品promotion，见末节；最终正常/失败中文详情与无二级折叠通过[真实浏览器验收](2026-10-16-logs-browser-acceptance.md)。
- 已有管理员会话及本次请求的 User/admin/IP、后台 System 身份边界通过；Docker 完整生命周期因测试题目缺少 Ready 镜像模板未通过。失败票据已终结，无活动容器且容量已释放。未重置管理员密码、改数据库权限或构造Cookie。
- 本次不升级/重启Agent，不停止既有用户实例，不修改公网网关、Registry或生产身份。

## 首次发布后现场与回退身份（12:22 UTC快照）

| 项目 | 现场事实 |
| --- | --- |
| current | `/srv/yinyu-data/releases/logs-927ceef8d731f28f9c323d354d61990f1e6de8c2-20261016/publish` |
| previous / 实际old | `/srv/yinyu-data/releases/training-settings-06930b095cb0511d31bf06d8971477fa71f0aeb8-20261011/publish` |
| 原previous | 10-10 teamlab-delete-reservation目录仍保留；发布前现场与旧current-state不同，以本次现场为准 |
| 附件 | current/files最终为 `/opt/gzctf/shared/files` |
| 配置 | 原现场文件引用不变，SHA `ffd7297a61ddfd724b4dfca7a04c3ee2d76a9ddf27cac625472d326a4db1f54a` |
| Main | PID从1111627变为3246884，active/running、NRestarts=0 |
| 本机Agent | PID2695400在本轮前后保持，active/running、NRestarts=0；实际运行文件 `/usr/local/bin/gzctf-agent` |

切换脚本在链接更新前验证151迁移头与6项schema；失败时原子切回实际old并启动旧应用。新增nullable列和字段扩长允许旧应用兼容，未执行EF降级或删除迁移记录。旧release、唯一备份与隔离演练证据继续保留；当前未执行应用回退。

## 制品与来源

PR #33 CI run `38897971141` 前端合成提交为 `af8eb14c65a41931f66a4c71f7d03cbbf9c0d63d`；PR head、CI合成提交、正式merge tree相同。前端223文件在本地和服务器逐项长度/SHA验证，manifest保留真实CI SHA，没有改写构建身份。Main从正式merge SHA执行Linux自包含publish，使用Git外target跳过执行面构建，原5个Agent/GuestSupervisor文件逐字节复制。

| 制品 | SHA-256 |
| --- | --- |
| 最终包，78,531,997字节 | `176615373651c838af9086f95d61cbf84d4728f4697900f74341fe8d5e82eae5` |
| Main DLL | `90f2be135ee95562212fb0212023435d8e852bc4fd183c92d17882f71f48a263` |
| frontend-manifest.json | `3c9ff47bd3f22cb8b1a9debe6f8d68f34b78917ee7ab3ea1e42290c11041f0f5` |
| Agent执行文件 | `87e011cfe91a09c525efbdbdaa6194191517a6f53595339f6ba9da3ace2097fe` |
| 前向SQL，968字节 | `503c8a5508bf52927ee7e1e8aedbf30dd8748a266a497a9d3a4d21b008b0f77a` |

身份文件位于最终release父目录 `release-identity.json`，并列merge SHA、sourceTree、CI SHA/run、文件计数与DLL/SQL摘要。独立审查代理回读证明后通过预切换门禁。

Git外材料在 `D:/Work/logs-rollout-20261014/`。原6d3117候选曾完成构建和stage，但因PR #33补丁改为新候选，**从未activate**；未重复恢复或迁移演练。新包上传未结束时一次stage摘要检查拒绝，未创建release或修改服务；待传输完成后摘要通过。此项说明上传与校验应串行并以完成状态为准。

## 新备份与隔离恢复

备份目录：`/srv/yinyu-data/backups/logs-6d3117f48a994ec3750a27096f280aca84e0ac3c-20261016-pre`。名称保留最初候选身份，最终候选未改变迁移字节。两库、附件与摘要11:50:10–11:56:20 UTC完成；主站持续服务。

| 文件 | 字节 | SHA-256 |
| --- | --- | --- |
| gzctf.dump | 1,149,603,039 | `4c13d3cf3ca795dae3aabe568010da121a48b96f9aac72eec0d41620d5514b69` |
| guacamole_db.dump | 92,631 | `c13a8b76f40b808a3b8bff0e767cd10bec478d48bfec872565c9e84d1d866b93` |
| files.tgz | 218,729,452 | `4121a208c40186a60e09e245c795906165abb593ad106cc68710e337685cd3d1` |

三份文件非零、SHA回读与附件tar解码通过。保留150条迁移历史、真实old路径、配置原路径和摘要、服务配置摘要、Agent包摘要。本机无/etc/nginx、Nginx服务或容器，与10-11保护引用记录一致；备份脚本在两库/附件完成后因该目录假设中止，修正实际状态记录后续做摘要，没有重复dump。未归档敏感配置明文。

完整隔离恢复在本次专用 `yinyu-logs-restore-20261016` PostgreSQL16容器执行，network-none、无发布端口、无生产数据库连接。11:56:43–12:15:44 UTC，耗时 **19分1秒**。35,678,433行历史9月日志和原有索引重建是主要耗时，不是新增迁移建索引。容器后续限2CPU/4GB和低I/O权重100；主站Config抽样保持200/约2–4ms。宿主采样I/O等待49%、CPU空闲39%，未盲目加并发。

- 双库完整pg_restore使用exit-on-error通过。
- 同字节SQL事务内断言150条历史、旧head `20261001145237_AddTeamLabManagedGuestNetwork`；成功后151条，新head `20261014060052_AddLogIdentityAndOutcome`。
- Logs新增ActorKind varchar32、ActorUserId uuid、DetailJson varchar8192、Outcome varchar32、Retryable boolean，全部nullable；OperationalEvents.DetailJson扩至8192。6项schema DO断言通过。
- 26张业务表迁移前后count完全一致，覆盖用户、战队、赛事、课程/答卷、理论、镜像/分发、节点/统一队列、TeamLab、AWDP、练习及附件；只在隔离副本计数，未在生产重新扫大Logs。
- 在副本重复执行同SQL被guard以非零状态拒绝；随后151/history/schema再次断言通过，未留下部分DDL。未保存具体退出码，不将预期拒绝写成迁移失败。
- 证明小文件：`restored-counts-before.txt`、`restored-counts-after.txt`、`sql-identities.sha256`、`rehearsal-verify.txt`、`rehearsal-repeat-guard.txt`、`rehearsal-verify-after-refusal.txt`、`rehearsal-finished.txt`。独立审查逐项回读通过。

## 生产迁移与基础冒烟

生产停止Main后，使用docker exec、psql stdin、ON_ERROR_STOP、10秒lock_timeout/60秒statement_timeout执行相同SQL；SQL摘要与副本文件一致。迁移后先DO验证151头/6项schema，再更新previous与current软链接、启动Main。未使用历史一键发布脚本。

12:22:38 UTC postflight证明保存在同一备份目录 `postflight.txt`：

- 首页/Config200；指标端口3001的healthz200、正文Degraded，与旧环境基线一致，未伪称全健康。
- 未登录访问Admin/Logs返回401；真实超级管理员Chrome会话仍有效，但日志页翻译阻断待修。
- 3节点Online，1个Docker、2个Docker+KVM；心跳28–29秒。统一队列仅终态：4=3396、5=141、6=53，与切换前一致。
- 504镜像模板Ready；分发231 Ready、1 Failed，与切换前一致。已有失败未当作本次成功或重试。
- 已知附件 `/Assets/4aeec5087f8cd44aaf7765a5f025b5fc4b839a936465f75ae964b2869fa9319f/maze.exe` 前后均200、15,872字节，SHA和文件字节相同；共享附件链接保持。
- Main/Agent active/running、NRestarts=0；发布后journal优先级error行数0。Agent进程与5个执行包保持，未向节点推送执行包。

## 实际耗时与可简化边界

| 阶段 | 墙钟 |
| --- | --- |
| 10-14预检及正常merge | 1分14秒 |
| 10-14 sudo专属规则 | 1分33秒 |
| 首次Main构建 / SQL生成 | 71.84秒 / 47秒 |
| 10-16完整新备份与摘要 | 6分10秒 |
| 双库隔离恢复与同SQL演练 | 19分1秒 |
| 最终927 Main构建 | 34.00秒，695文件 |
| Main停止到HTTP恢复 | 24.271秒 |

阶段可重叠，不能直接相加成用户等待时间。10-14中断与10-16接续间隔不属于执行耗时。已复用成功CI前端、同字节SQL与一次完整演练；候选变化后只重建应用和stage新目录，没有重做数据库迁移准备。

长期简化可把CI制品、明确manifest、恢复证明与stage/promotion分开，让完整备份恢复在维护窗口外预做；真正切换只保留摘要、迁移、原子链接和健康检查。纯前端修复可复用已验证后端制品，仍按当前release切换流程执行；后端DLL/运行时更新需要Main重启，数据库契约变更仍须前向迁移和兼容回退。当前只有本次24.271秒停服证据，不声称已实现零停机。

## 额外工具与权限验证

PR #31的stage-only真实上传用SSH key、sudo-n与严格known_hosts通过；probe摘要 `75ed8f030105f5b182d07e7e6bcbd7435392a08ccb0dda92d388c062799f33a8` 远端匹配，未解压/activate，仅该probe文件已清理，Main/Agent PID未变。正式发布不依赖该未合入时的工具代码。

sudo原规则备份位于 `/var/backups/yinyu-sudoers/20261014T070222Z/`；`/etc/sudoers.d/90-yinyu-whoami` root:root/0440、候选与整体visudo验证、全新会话sudo-n均通过。用户授权为永久配置，本次未撤销。未修改其他用户规则、SSH认证或密码；凭据不落文件、参数或提交。

## 验收边界与后续

日志页i18n阻断已由PR #34修复并通过真实浏览器复验；未手改publish。隔离容器及其专属volume已按末节记录清理；旧release和唯一备份保留。日志展示及请求身份边界已有现场证据，Docker完整链路、代理链最终IP与关联过滤完整UI交互不宣称通过；镜像前置条件及粗粒度错误分类归入[ACC-04与RUN-03](../backlog.md)，不继续扩大本轮验收范围。


## PR #34前端单独promotion与最终执行状态

日志页翻译根因由前端代理定位为正式App缺少i18next初始化；PR #34正常合并为 `966ca6081000035c6ac46b4289f44d13dfebf75d`。按前端变更范围复用既核Main、数据库备份和同字节演练，不等待无关后端重测，不重新构建Main、不重跑SQL或恢复。本次执行不手改原运行目录。

- 新current：`/srv/yinyu-data/releases/logs-ui-966ca6081000035c6ac46b4289f44d13dfebf75d-main-927ceef8-20261016/publish`；previous为首次927release，10-11旧release仍保留，可按前文真实路径回退。
- Main组件来源保持 `927ceef8d731f28f9c323d354d61990f1e6de8c2`，DLL摘要仍为 `90f2be135ee95562212fb0212023435d8e852bc4fd183c92d17882f71f48a263`，从已核release原样复制；原Agent/配置/共享files链接同时保持。
- 前端来自CI run `38900693915`，合成提交 `6df7b3c446b1093fc19b434e758683ce04002dab`；PR head、CI、merge的ClientApp subtree均为 `699daa9181c61edafda6200b1e28931167be1251`。223文件一次逐项长度/SHA通过。
- 前端tar摘要 `0fb067825ad9356b68290f5abb1705c24bbd62ee8b932dac5df2251925b71895`；frontend-manifest摘要 `ffe4055a0fa77bc484be61965b48b4880b0cd63affacbf68794de025ee19295e`。新release父目录的release-identity.json明确记录两个组件各自来源，不把Main伪标为前端commit构建。
- 第二次短Main重启：12:33:36–12:33:48 UTC，stop-to-HTTP精确 **12.719秒**；Main PID3250837 active/NRestarts0，Agent PID2695400保持。首页/Config200后通知UI代理做一轮最终验收，发布侧不重复多层SSH/SQL检查。
- 两次Main维护累计 **36.990秒**。只有第一次执行数据库迁移；第二次没有migration、备份或恢复操作。
- 最终UI/i18n、鉴权审计与可清理业务链路由父会话统一引用独立UI记录和current-state，不再由发布代理等待或重复测试。

按父会话授权于12:35:07 UTC清理本次独立恢复容器 `yinyu-logs-restore-20261016` 与其匿名专属volume `c54609f4cdf90488c595de110e90e5b1888557d25eafc5a299ff79f1c1cc0c24`。清理前核验network-none、备份只读bind、数据volume与生产PostgreSQL挂载完全不同；docker rm -f -v仅作用于本容器，并确认该专属volume不再存在。未删除其他容器、旧release、备份或已落盘证明。清理回执保存在原备份目录 `isolated-restore-cleanup.json`。
