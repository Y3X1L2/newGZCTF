# TeamLab 运行界面清晰度修正：PR #14 与 .27 发布交接

日期：2026-10-06。状态：**complete / VERIFIED（下述限定范围）**。本记录依据已合并源码、CI、发布代理的备份/切换/实机回读及去敏验收事实；正式站点 GUI 因浏览器工具不可用，单独列为未验。

## 任务目标与基线

- 用户正式站点截图指出运行详情贴近壳层滚动栏、区域边界弱、当前机器不明显，以及操作记录中原始阶段/错误码直显。修正统一 TeamLab 场景、版本与运行详情的留白、工作区层级和记录表达，不修改全局壳层、生命周期或运行事实。
- PR #13 已正常合并为 `main d3b8c954`；随后 PR #14 正常合并为 `main 33983541d9ea3b319c17fee3fc90ba74ad3b1b4a`。构建提交 `b2af7defbac6303330cd755ace0e88273d2f03a5` 与 `main` 同源码树 `46c17cc3600bc6a122dbc58788f870f2cd89e9b3`（本地 Git tree 已复核）。
- 本交接分支 `codex/teamlab-runtime-clarity-handoff` 自 `origin/main 33983541` 建于独立工作树 `C:/Users/Cloud/.codex/worktrees/teamlab-managed-agent/newGZCTF`，只新增本文件。学习资料、原始请求和截图留在仓库外，不提交凭据或生产数据。

## 技术结论与变更

- PR #13 首版运行元数据投影将旧快照的 raw64/prefix71 SHA 与当前摘要作字符串比较，现场 snapshot 97 / generation 4 的四资产虽同一实质 SHA 仍被拒。后续改为严格 canonical SHA 比较，继续保留资产身份校验边界；不把旧投影拒绝误判为真实身份变更。
- PR #14 用 TeamLab feature CSS Module 统一外侧 gutter 和最大正文宽度，明确工作区 surface/边框、网段与详情分区；双网段同一机器的选中标识一致并保留 `aria-current`。操作记录依据现有代次、时间、状态、阶段和错误码显示中文已知分类；原始值留按需详情，历史失败不冒充当前环境故障。没有新增队列、数据库字段、凭据系统或 Agent 操作。
- 本次发布没有数据库 migration 或 Agent 升级。业务端口转发状态不代表目标服务可用；来宾 `observed=null` 不代表 VM 内网络已实测。

## 提交与发布身份

| 项目 | 已核对事实 |
| --- | --- |
| CI / 本地门禁 | [Quality run 37449290029](https://github.com/Y3X1L2/newGZCTF/actions/runs/37449290029) 两个 job 均 SUCCESS；后端本地单元 1296/1296、真实 PostgreSQL 专项 1/1；前端完整门禁 118 文件/396 测试及 locale、lint、类型、架构、构建、制品预算通过。 |
| 活动目录 | `/opt/gzctf/releases/teamlab-runtime-clarity-b2af7def-20261006/publish`，通过 `/opt/gzctf/publish` 原子链接激活。 |
| Main | `GZCTF.dll` SHA256 `b92a849dfa4946c23e1a590a69e0b60063b2ac223519bfab8d82f0ee861077e9`，PID `2995985`。 |
| 前端 | `frontend-manifest.json` SHA256 `981976d8e5565444f9e0e03f772ec13d3c33ef84efa5a7e4a6cf896f9c9ca902`，内置 git SHA 为 `b2af7def`。 |
| Agent | SHA256 `9b2142e9731d77495322e20a100432a728cf3d86f87dc67abebf85ed58a9b3e8`，PID `1650376` 保持；Main/Agent 均 active、`NRestarts=0`。 |
| delta / 完整性 | delta `6,723,902` 字节，SHA256 `a1c1092b3855ef48fb24c968faaa9459eb7eba6253aab5d68db794c8aed67f57`；141 项非私有 core 完整 SHA、225 项 web 全集合、13 项 core 变化且 0 删除核对通过。两项配置/Agent 元数据与共享 `files` 链接保留。 |

## 备份、切换与回退

- 新在线备份 `/opt/gzctf/backups/teamlab-runtime-clarity-b2af7def-20261006-pre`：两个完整数据库 dump 全文可读，附件与配置等六项摘要通过。备份时间 `10:30:44Z` 至 `10:36:12Z`，约 328 秒；期间 Main 未停。
- 新版本于 `10:38:42Z` 切换，维护步骤 `20.16` 秒通过。旧 `d3b8` 与更早 release、原备份均保留；没有清理历史日志、镜像、Registry、PVE、工作区或学习资料。没有执行回滚演练，旧 release 仅是保留的回退边界。
- 本次既有部署授权仅覆盖已完成的该次切换。后续 GUI 或代码再修改均需重新构建、审查和按维护流程发布，不以本记录代替发布许可。

## 发布后实机验收

| 范围 | 结果与边界 |
| --- | --- |
| 服务 / 数据 | 首页、Config、served manifest HTTP 200；迁移 150，head `20261001145237_AddTeamLabManagedGuestNetwork`。三节点 online/schedulable、Fabric healthy；模板 519–522 Ready；最后队列 0。 |
| 保留四机 | 原 runtime `01a1022f-7e91-7ad3-8309-833af4771430` 保持 generation 4，四域原生身份未变，`virsh` 四台 running。没有重置、销毁或改造原 Lab2。 |
| 系统与网卡投影 | API 四资产中 dc/win 为 Windows execution-plan，oa/web 为 Linux template-current。五张网卡按 1/1/2/1 分布；assigned 的 IP、前缀、DNS、无默认网关/无静态路由逐项与原执行计划匹配。全部 `observed=null`；这只是分配事实，不是本次真实 guest 回读或业务连通测试。 |
| 能力边界 | Windows 文件 POST 返回 HTTP 422 `files.unsupported`；Windows SSH/文件能力不支持。RDP 与 Linux SSH/SFTP 缺配置时正确为 `unconfigured`。未创建远程会话、VPN 授权或抓包任务，未 LogOut。 |
| 独立 Docker 链路 | 模板 495 在独立场景正常创建；本机私网 HTTP 200（第 3 次尝试，162 B）；上传/下载逐字节一致后删除测试文件；正常 destroy、容器 gone、队列 0，`.30/.31` 原 `schedulable=true` 恢复。场景/版本审计元数据保留；没有公网入口或原 Lab2 生命周期操作。 |
| 最终 postflight | 服务、数据库、节点、镜像、队列和四域 running 复核 passed。 |

启动日志宽正则（`failed/exception/fatal`）命中 **1** 条 INFO（Priority 6）。去敏分类文件在服务器 stage 的 `clarity-log-classification.json`：固定 `AdminMutationAuditFilter` 模板匹配预期 Windows `files.unsupported` 422 负测，同刻 `10:42:27.880482Z`；未处理异常、Fatal、stack 均为 0。这是预期失败操作审计 1 条，**不能写成“日志 0”**；原始 MESSAGE 未在本记录输出或保存。

## GUI 与后续边界

- 正式站点 GUI 没有完成新版视觉签收：CUA IAB 不可用，浏览器 inventory 只有返回 `unsupported Codex auth method: apikey` 的 Chrome 扩展。没有新版浏览器截图或 390/1366/1920/2560、日夜、键盘和 reduced-motion 实看。用户原截图是缺口发现证据，旧本地合成 fixture 截图也不能证明修正后的线上显示。
- 仍未验收真实来宾网络回读、原四机图形登录和业务、RDP/SSH/SFTP 实际连接、VPN/抓包、公网入口、跨节点故障或原四机重置/双副本。不要从 API 分配投影或独立 Docker 测试外推这些能力。
- 完整原始发布、备份和学习证据保存在仓库外 `D:/Work/YINYU-TeamLab-Core-Rollout-20261006`；脚本、制品、dump、配置、学习截图及 Cookie 不得提交仓库。本文件只保留经脱敏的结果与身份。
- 下一位接手者先核对 `main` / 活动 release 身份、postflight 及上述 GUI 工具缺口；如可用浏览器恢复，再按只读清单核对页面，任何新发布仍按 `docs/operations/vnext-maintenance-window-rollout.md` 执行。
