# 培训页面与复制修复生产发布

## 发布结果

2026-09-16 13:22:46 UTC（北京时间 21:22:46）主站切换完成，切换及启动检查约 18 秒。13:37 UTC 补充健康核验完成。

- 服务器：`10.24.0.27`。
- 发布代码：`12e2c1a09dee1be4571e1349a2bd489cf3a2ca56`，分支 `codex/training-ui-production-20260916`，发布前已推送。
- 新目录：`/opt/gzctf/releases/training-ui-12e2c1a09dee1be4571e1349a2bd489cf3a2ca56-20260916/publish`。
- 回滚目录：`/opt/gzctf/releases/training-instance-flag-eaac7f2684755f6471b684c2432e6e18dea3eb92-20260916/publish`，由 `publish.previous` 指向。
- `gzctf.service` active/running，主进程 PID 550460，NRestarts=0；本机 Agent 持续 active，二进制摘要保持 `0acc37f7...`。
- 数据库仍为 140 条迁移，head `20260908111521_TeamLabDeviceObservation`；本次无模型变化，未执行数据库迁移。

## 为什么使用生产补丁分支

现场运行的是 `eaac7f26`，而非主工作区文档中的旧发布，也不是最新 main。该版本已包含课程容器额度和动态 Flag 修复。

为保留已上线行为，本次从实际生产提交创建发布 worktree `D:/Work/newGZCTF-training-ui-production`，将原功能分支的 `3a4ab898`、`d1ee23ae` 正常 cherry-pick 为 `76b53703`、`12e2c1a0`。没有把 main 中其他 TeamLab 结构和迁移一并上线。原功能分支仍保留，生产补丁分支也未合并 main。

相对 `eaac7f26`，数据库模型、迁移、Agent 源码和 API 字段结构均无差异。主站使用重新发布的 Linux x64 制品和新前端；执行面二进制与迁移 runner 沿用并校验原发布物，未重启或更新 Agent。

## 备份与制品

备份目录：`/opt/gzctf/backups/training-ui-pre-20260916T125755Z`。

| 内容 | 验证 |
| --- | --- |
| PostgreSQL custom dump | 835,942,876 bytes；SHA-256 `693c7a0751f6fa21e3364149b74e7cc087cdb86417ef69210bcda0a0c53748ed` |
| 共享文件归档 | 317,932,238 bytes，摘要记录于服务器 verification.json |
| 应用与服务配置 | 加密归档 20,512 bytes；配置与密钥只保存在服务器受限备份目录，不进入仓库 |
| 备份恢复 | 在 network=none、无发布端口的独立 PostgreSQL 16 容器实际恢复成功；迁移头和九类核心表计数一致 |
| 发布归档 | 31,018,054 bytes；SHA-256 `90665372af0187d49c45debefa9a4f4e650959ed8e29be213c6976c48e07b093` |
| 安装后 manifest | 379 个文件长度/摘要校验通过；appsettings、kube-config 和 files 属于显式保留的运行配置/存储 |

恢复比对计数：用户 172、战队 76、比赛 22、课程 31、章节 69、课程提交 123、章节进度 74、Files 217、Attachments 208。校验使用行数和迁移元数据，没有输出业务内容或凭据。

首次恢复命令失败后，增加最终 PostgreSQL 进程就绪检查并重新恢复成功。期间 SSH 曾中断，重新连接后读取持久化验证结果，确认 `restoreVerified=true` 和核心计数一致，才开始切换。验证容器和卷最终均已清理。

## 发布版本验证

| 项目 | 结果 |
| --- | --- |
| Release solution build / Linux publish | 通过 |
| 前端完整门禁 | locale、lint、类型、架构、337 项测试、构建及制品预算通过 |
| 后端全量单测 | 1,139/1,141；两条既有 TeamLab 调度观察点断言失败 |
| 原生产基线复核 | 独立 eaac7f26 worktree 定向重跑同两条测试，失败一致；未为本次发布修改这些断言 |
| 相关 PostgreSQL / HTTP / Docker 集成 | 13 项最终通过；BusyBox 拉取首次网络超时，导入服务器已有镜像后真实 Docker 用例复测 1/1 通过 |
| 基础烟测 | 首页与 /api/Config 为 200；前端 manifest 为新 SHA；应用实际工作目录指向新 release |
| 数据和附件 | 140 条迁移未变；shared/files 指向正确；一份 15,872 bytes 附件 HTTP 200，长度和 SHA-256 与文件记录一致 |
| 节点和运行 | 三节点均可调度且心跳新鲜；470 条镜像记录为 Ready；最终队列只含 Succeeded/Failed/Cancelled，无活动票据 |
| 日志与健康 | 发布后烟测时 error 日志为 0；指标端口 /healthz 返回 200 / Degraded，既有健康缺口仍保留 |

两条单测为 `TeamLabScheduling_MinimizesManagedRouterCrossNodeEdges`、`TeamLabScheduling_LateBindsThreePlusOneCapacityAcrossTwoNodes`。完整集成项目没有重跑，不将本次验证表述为全量门禁全绿。

## 生产页面验收

在用户已登录的内置浏览器核验课程 32 / 章节 69：

- 章节页仍显示六道实验和章节已完成；课程详情现显示 100%、1/1 和已完成。
- 题目管理中六道题全部显示绑定章节 #69。
- 使用真实浏览器点击复制，剪贴板读取值与实例入口一致。
- 旧实例在验收期间按原到期时间回收；通过正式页面新建一个课程实例，达到运行中，公网入口 HTTP 200，随后通过正式页面销毁并确认未创建状态。
- 既有实验均已答对，未再次提交 Flag 或修改历史成绩；本次生产链路覆盖创建、入口、复制与销毁，Flag 相关回归由发布版本的 PostgreSQL/Docker 测试覆盖。

原生 AX 自动点击的首次剪贴板观测不可靠，最终使用 Playwright 的实际点击并读取剪贴板确认，不以图标变化代替成功。

## 回退与后续

- 原目录及旧配置完整保留，无数据库迁移；若需回退应用，可按维护手册停止主站、原子恢复旧 publish 指针并启动验收。无需为本次应用回退降级数据库。
- 课程详情按真实章节记录显示进度；历史首页/课程目录汇总未批量回填，仍按原修复交接保留该后续项。
- 数据保留 SQL、历史 Theory 迁移来源与既有 Degraded 状态不属于本次修复，不宣称已解决。
- 本地发布包、日志和测试结果位于仓库外 `D:/Work/training-ui-deployment/`、`D:/Work/training-release-*.log`、`D:/Work/training-release-test-results/`。
