# 培训容器动态额度设置

## 目标与基线

在 `/admin/system` 添加培训模块每用户容器启动上限，继续沿用独立培训策略、配置存储和统一队列。用户追加要求将 `/practice` 与 `/admin/exercises` 改为服务端分页，保留筛选和正确总数。不进行解题、读取题目 Flag 文件或提交真实 Flag。

任务从最新 `origin/main 03440c51` 创建 `codex/training-quota-settings`，独立 worktree `D:/Work/newGZCTF-training-quota-settings`。现场只读核验 `.27` 仍运行 `training-runtime-quota-60170a5266f684581f21e95013687b0e7367515f-20261011`，Main DLL `e2265545…`、首页200。

## 改动与不变量

- 内部配置 GET/PUT 增加可选 `TrainingContainerPolicy`，非负整数校验在写入任何区段前执行；继续保持管理员权限和部分区段保存。
- 使用现有 `Configs` 与配置重载机制，下一请求的 OptionsSnapshot 动态生效；没有修改运行准入、调度器、队列执行、Flag、节点容量或 Agent。
- 前端增加独立额度面板与状态 hook，0保持为不限个人数量，空值、小数、负数和溢出不可保存；失败保留草稿。回读仅更新实际变化的其他区段，避免丢失未保存修改。
- 内部 OpenAPI 由当前源码及其原有 NSwag/JSON设置导出，再按仓库模板重新生成客户端；保持既有路由大小写生成约定。导出工具、制品与日志保存在 Git 外。
- 无数据库迁移，不改 Agent 协议。降低上限不销毁已有实例，不取消已排队票据；自主练习额度与自动清理保持隔离。
- 新题库分页接口默认20、最大100，数据库先筛选再分页，后台摘要和标签元数据跨页完整。管理端停用题目、学员角色可见范围和课程题隔离保持。旧数组接口和 `/practice/stats` 兼容使用保留，聚合统计优化未纳入本次。

## 验证与发布

状态：`complete / VERIFIED`。最终Quality运行 `38126520893` 全部成功：Release、1493单元、366 PostgreSQL/Docker集成（0 skip）、前端122文件422测试及locale/lint/strict类型/架构/构建/体积、EF模型、查询计划和OpenAPI兼容。前端本地三页日夜/四宽度/减少动效矩阵及键盘保存0、移动翻页通过。正式页面培训额度3可见，练习/后台第2页均2/27；三页四宽度无页面横向溢出，日夜主题可用，临时视口与动效覆盖已恢复。

本机 Docker Desktop 不可用，完整 PostgreSQL/Docker集成由GitHub Linux Quality执行。Git外证据目录：`D:/Work/YINYU-Training-Settings-20261011`，包含最终CI、失败/复跑、Npgsql查询编译、制品与发布脚本。凭据、Cookie和真实Flag未写入仓库或证据报告。

第一轮CI366项集成中364通过、2项新增分页用例返回500。Npgsql查询编译复现：标签列为JSON文本value converter，新增标签搜索 `Any` 和元数据 `SelectMany` 无法翻译为SQL。已改为在PostgreSQL展开标签元数据，再通过EF组合可见性/筛选，不在客户端加载题库；最终366/366通过。该失败轮未合并或切换服务器。另一次本地全量单测的未改动调度并发用例1秒超时，定向5/5及最终全量1493/1493复跑通过，失败证据保留。

## 合并与发布身份

- 代码提交 `050cbf74`（动态配置）、`6d10a67e`（分页）、`06930b09`（JSON文本标签查询）。[PR #26](https://github.com/Y3X1L2/newGZCTF/pull/26) 正常合并为 `b1812d150f674ba1d6d73ec6685d5621b883c94c`，完整tree均为 `c34f78cf4d1be6709c15bbc60da8ef41cdf587ab`。
- 新release：`/srv/yinyu-data/releases/training-settings-06930b095cb0511d31bf06d8971477fa71f0aeb8-20261011/publish`；Main SHA256 `0d3ea1e2ed8ea6cd758ee4e518993a186581305e6e10293e3f4a5ae341da7793`，前端source069/224文件，368新payload文件与权限/摘要验证通过。
- 原5份Agent/guest-supervisor文件完整复用，Agent SHA256 `87e011cfe91a09c525efbdbdaa6194191517a6f53595339f6ba9da3ace2097fe`、PID641728保持；无Agent sync或重启。新配置软链接引用旧release原配置，files仍指向 `/opt/gzctf/shared/files`。
- 09:32:02 UTC原子切换通过，主站切换至HTTP恢复14.657秒；Main PID1111627/NRestarts0，迁移仍150/head `20261001145237_AddTeamLabManagedGuestNetwork`。未执行数据库迁移或生产恢复。
- 完整回退目录：`/srv/yinyu-data/releases/training-runtime-quota-60170a5266f684581f21e95013687b0e7367515f-20261011/publish`。应用与旧接口仍兼容，无数据库降级需求，旧release和新备份继续保留。

## 备份与正式验收

新备份：`/srv/yinyu-data/backups/training-settings-06930b095cb0511d31bf06d8971477fa71f0aeb8-20261011-pre`。两库同各自exported snapshot完整dump、全文解码、稳定附件及摘要通过；新两库dump实际在network-none隔离PostgreSQL恢复，比对结构/迁移/核心计数，临时容器和数据目录清理完成。完成时间09:30:16 UTC，gzctf压缩dump1,392,968,888字节。备份短Main捕获至HTTP恢复33.058秒，Agent/教学容器持续运行；原配置与服务配置引用路径和摘要保留，未归档敏感配置明文。

正式API在同一Main PID中保存1、0及恢复3：1立即阻断新准入，已有两个实验有效复用且容器ID保持；0回读、-1/1.5拒绝400，公共容器/账号/平台身份配置保持。此处只使用平台配置与生命周期API，不请求题目服务或Flag提交接口。首次验收脚本错误使用Profile的 `id`，核对当前契约后改为 `userId` 重跑，错误发生在保存配置前。

题库API练习total539、管理total540，默认items20，分页5/5无重复、标签跨页稳定、空搜索0与页大小101拒绝400。正式页面可翻第2页，分别2/27。课程634/636的容器与执行面原ID保持，Running/Ready、端口42761/42760保持；三节点Online/可调度、新鲜心跳、Agent身份未变，494镜像状态分布与队列0保持。

首页/Config/frontend-manifest200，已知共享PNG328781字节与摘要匹配，新Main错误日志0。健康端点位于指标端口3001：200/Degraded是既有降级；8080/healthz按当前契约404，不应判作发布故障。

## 范围和耗时取舍

代码PR共34文件：实际实现21文件（419增/48删），测试7文件（396增/5删），生成客户端1文件（267增/25删），文档5文件（55增/4删）。生成代码、测试与文档占主要diff；功能涉及管理配置、两个分页用例、筛选统计和URL状态，没有扩展其他业务模块。

两轮后端CI累计约22分钟；无迁移的本次变更仍做了新快照全库恢复与大型流量索引重建，验证范围比最小充分门禁更重。后续同类无迁移配置/列表变更应在满足备份与回退要求的基础上，结合新dump解码、结构校验和已有恢复证据，优先验证实际受影响链路；避免机械重复完整索引重建。

遗留：`/practice/stats` 和旧数组接口兼容保留，统计聚合接口属于后续优化；既有健康降级、其他模块现场验收边界保留。没有解题、读取题目Flag文件或提交真实Flag，也没有停止/销毁既有课程、VM或TeamLab资源。
