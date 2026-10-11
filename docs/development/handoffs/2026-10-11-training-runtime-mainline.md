# 培训实例修复回流主线、PR 与部署

## 任务目标与授权

用户要求提交 PR，确认修复不破坏现有后端行为后合并 main 并部署。本次延续禁止解题、访问漏洞接口、读取题目 Flag 文件或提交真实 Flag 的边界；验收仅通过正规平台流程和运行配置验证实例隔离、端口、队列与注入状态。

## 基线与只读证据

- 从最新 `origin/main 2479a165b553bb841560130c0211abbb011ab420` 创建 `codex/training-runtime-quota-mainline`，worktree `D:/Work/newGZCTF-training-mainline`。
- `.27` 当前 Main 发布源 `49948764bd0715142911d5b22c68852ab3d38bb8`，DLL SHA-256 `61ff70155b2fae774b6b462a818fed589123bd65ddb421f06e0fad1b19a69ae9`，目录 `/srv/yinyu-data/releases/teamlab-delete-reservation-49948764bd0715142911d5b22c68852ab3d38bb8-20261010T0645Z/publish`；前端源 `a63937c6`，Agent `87e011cf…`。
- 当前配置练习额度为 1、自动销毁为 False。课程题目 627/629/630 的 TrainingCourseId 均为 32，归属正常。
- 2026-10-11 06:29:28 UTC 创建 627；06:29:34 仓储日志明确记录自动销毁 627；06:29:40 创建 629。旧容器在 `.30` 库存不存在，629 在 `.31` Running，入口复用 30000。
- 原修复 `1515fb9a` / 发布回移 `eaac7f26` 和其课程回归测试未进入 main，当前发布重新携带旧额度/无条件自动销毁逻辑。主站程序集也不含 TrainingContainerPolicy；9 月临时发布不能替代主线集成。

## 实施与验证计划

1. 在最新主线应用已验证的培训独立额度与缺失动态 Flag 修复，审核与当前代码的兼容性。
2. 扩充必要回归：培训/公共练习互不驱逐；自动销毁开关；运行/排队计数；同用户并发与幂等；静态/动态 Flag 及既有运行实例；失败与旧实例清理。
3. 完成 Release、后端全量单测和适用 PostgreSQL 集成，检查模型/迁移、API 与架构边界，提交并推送。
4. 创建 PR，审核最终差异和 CI，确认通过后正常合并 main，记录合并身份。
5. 根据实际服务器布局和既有安全备份流程创建新备份与独立 release，原子切换并保留回退。
6. 正规 API 验证课程多实例、不同端口、独立停止/重建及注入匹配布尔结果；核对现有服务/节点/队列/镜像/教学资源，更新已证事实并推送交接。

## 当前状态

complete / VERIFIED。[PR #24](https://github.com/Y3X1L2/newGZCTF/pull/24) 已在 2026-10-11 07:54:10 UTC 合并 main，合并提交 `60170a5266f684581f21e95013687b0e7367515f`。最终审查 head `1bdfff835b0e07944dc39bba71bbc987e0694e74` 与合并提交的完整 tree 同为 `a032ec92da47a3bc7a3be3c6bbfea45752fe2635`。

## 兼容性审查与软件验证

- Release solution 构建、本地全量单测 **1492/1492**、培训/旧 Flag 定向 **25/25** 通过。
- 最终 PR Linux Quality run `38122166194` 全部通过：单元 **1492**、PostgreSQL/Docker 集成 **352/352（0 skip）**、前端 **119 文件/412 测试**，以及 locale/lint/类型/架构/构建/体积、EF 模型、查询计划和 OpenAPI 向后兼容门禁。没有 pending model changes。
- 首轮新增 API 夹具只填 CreatedById，没有正常课程教师关系，现有权限规则正确返回 403；已补齐教师 Owner 关系，并增加未报名学员不得创建、不得产生实例或票据的回归。没有放宽或修改权限逻辑。
- 新增回归覆盖：并发四个课程请求最多三个活跃创建票据、重复请求复用票据、公共练习/课程队列隔离、跨用户隔离、不限额度、旧容器清理失败、静态/动态 Flag 及真实 BusyBox 三实例与不同映射端口。
- 所有原有 CTF、AWDP、Theory、Runtime、Fleet、TeamLab 单测保持通过。数据库实体、模型快照、历史 migration、Agent、生成 API 和前端源码没有差异。
- 公开练习有意改为遵守 AutoDestroyOnLimitReached 开关；开启时只驱逐其他公共练习。课程达到额度拒绝新建，有效运行实例直接复用；缺失 Flag 仅在新容器创建前补齐。统一 DeploymentQueueTicket、节点容量约束及原运行锁/额度锁保持。

## 新备份与恢复验证

- 成功备份目录：`/srv/yinyu-data/backups/training-runtime-mainline-d2ca4019-20261011-pre-v3`，证据/执行目录：`/srv/yinyu-data/maintenance/training-runtime-mainline-d2ca4019-20261011`。
- 备份以 PR 较早 revision `d2ca4019` 标识创建，后续仅修改测试夹具；该 revision 与最终合并提交的 Main/Agent/契约源码目录没有差异。备份原始身份未改写。
- gzctf dump：**1,392,784,706 字节**，SHA-256 `784e27356848dbb8d33f801e9027840bdd38369b4eed4cfd8b0c52c91bc1585f`。
- Guacamole dump：**95,055 字节**，SHA-256 `153fdfd2177bef1742a46ff147590aa5db4b65a3fe33d86ddea7cb3d7ecb3ed6`。
- 附件归档：**216,724,847 字节**，SHA-256 `49130c3cd766134ebc5b622375a2149ebeddd5ecc4b8ee4294a94c8969af3780`。
- 每库 exporter 只持只读 exported snapshot，元数据由单独短 import 事务读取并结束，实际公共关系锁为 0。稳定附件捕获窗口 29.130 秒后恢复旧 Main；Agent 和既有实例未停止。随后同 snapshot dump、全文解码和 SHA 校验通过。
- 首次严格复用昨日恢复证明失败，差异是新增日期分区 `TeamLabTrafficFlows_p20261013`、移除旧分区 `p20260910`，各 20 列，业务迁移仍 150。第一次脚本调整遗漏旧结构比对，又产生一次多余短捕获窗口；两个失败目录与 journal 保留，主站都由 finally 恢复，没有切换新代码、更新数据库或删除业务资源。
- 成功 v3 在独立、无网络 PostgreSQL 16 容器中完整恢复**这两份新 dump**，包括索引/约束，并与同 exported snapshot 的结构、迁移和核心表计数逐项一致。临时容器和精确 restore-data 目录已清理；没有恢复生产数据库，配置凭据不进入仓库/报告。

## 发布身份与切换

- 新独立 release：`/srv/yinyu-data/releases/training-runtime-quota-60170a5266f684581f21e95013687b0e7367515f-20261011/publish`。
- 新 Main DLL SHA-256：`e2265545d6b26097d9dc888cbf1ee2a58dfd60d367ca5ed6d0ccca57f2b73710`；manifest SHA-256：`5b9bd469fb3b6d304f1b2e68e9e4115cd07efa860a06c9f592f1598db37fc52a`。
- 复制原 release 后只替换 Main DLL。699 个 payload 文件长度、模式和摘要已检查；manifest 明确记录合并 commit、审查 head/tree 和继承前端 `a63937c6`。私有配置及共享 files 原链接保持。
- 2026-10-11 **08:01:30 UTC（北京时间 16:01）** 完成 Main-only 原子切换，窗口 **17.671 秒**。本机 Agent 同一 PID641728、SHA `87e011cf…` 保持；远端 Agent 未更新或重启。没有执行数据库迁移，仍 150 条、head `20261001145237_AddTeamLabManagedGuestNetwork`。
- `publish.previous` 指向完整回滚目录 `/srv/yinyu-data/releases/teamlab-delete-reservation-49948764bd0715142911d5b22c68852ab3d38bb8-20261010T0645Z/publish`，旧 release 和新备份保留。

## 真实验收与最终边界

- 原有 629 容器 `a7d81f336011…` / 记录 `01a129a7-1d06-7391-87e6-40407efbf81f` 被复用，记录与容器 ID 保持。
- 正规课程 API 创建 627、630 后，三实例均在 `.31` Running，入口均 Ready；最终端口 **627→30001、629→30000、630→30002**，三条公网 TCP 握手通过。
- 仅停止并重建本任务新建的 627，629/630 的容器记录保持；627 的 Flag 记录621保持，重建后再次确认三实例同时 Running、端口独立。三个容器环境变量均与其各自数据库 Flag 匹配，只输出布尔结果，未输出值或摘要。
- 首页/Config200，Main/Agent active、三节点心跳新鲜、活动队列0。328,781 字节共享 PNG 经 HTTP 回读，摘要 `7fca1da7…` 与存储记录一致。临时恢复资源和上传文件无本任务残留，三个课程实例留给用户继续测试。
- 未解题、未读取题目 Flag 文件、未访问漏洞接口、未提交真实 Flag；没有重复 Windows/VM、AWDP、SSO 或核心 TeamLab 业务实机验收。健康仍为既有 200/Degraded，不将本次课程修复描述为消除历史健康降级。

## 回退与主线交接

如出现相关新回归，停止 Main，将 publish 原子切回上述完整回滚目录并启动，再核对服务/API/节点/实例。无数据库迁移，应用回退不需数据库降级；回退会恢复旧单实例驱逐行为，应暂停新建课程实例。未经独立授权不使用备份覆盖发布后的业务数据。

修复代码和回归测试已经通过 PR 进入 main，后续发布从 main 构建即可包含修复；本次部署记录另提交文档 PR，不把文档提交伪写为实际部署二进制源码身份。
