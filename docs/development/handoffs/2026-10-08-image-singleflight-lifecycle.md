# 镜像共享传输结束清理修正（2026-10-08）

## 目标、基线与状态

用户要求镜像按需分发、容量保护和回收闭环。独立审计整合候选
`b7fc64dc`（普通按需分发、回收与 Agent 首次容量保护已整合）时发现：
唯一 HTTP 等待者取消后，shared writer 仍继续；它结束时没有等待者进入原 finally，
已完成的 Lazy 留在 singleflight 字典。缓存后续被正常回收，同 key 请求会取得旧成功响应，
没有再次执行真实缓存校验和下载。失败 task 也可能滞留。

- 修复分支：`codex/image-transfer-singleflight`，从 `origin/main 2ba6e8d8` 建立。
- worktree：`C:/Users/Cloud/.codex/worktrees/image-demand-distribution/newGZCTF`。
- 原 `codex/image-demand-distribution` 分支及其提交保留，不改写历史。
- 本候选没有部署、服务器操作、数据库迁移或外部 DTO 变更。

## 实现

`ImageTransferSingleFlight` 由实际 shared task 的 finally 移除其槽位，
使用 `(key, owner Lazy)` 比较移除，旧任务不能删除同 key 的新 owner。
等待者只等待并可独立取消，不承担任务终态清理；它们取消不停止实际 writer，
也不提前归还容量 lease。下一次请求必须重新运行现有缓存检查流程。

范围仅该服务、行为测试和此工程交接。Docker pull 超时、AgentClient 分类及容量服务由其他任务处理，
本提交不修改 `DockerService`、`AgentClient` 或 `ImageDistributionService`。

## 验证

定向单元测试 14/14 通过、0 skip，包含新增六例和既有 Agent capability 回归；其构建同时完成主站/Agent/Test（已有依赖/分析器警告）。新增六例覆盖：

- 唯一等待者取消、writer 完成、模拟实际缓存文件回收后，下一请求重新准备缓存。
- 多等待者只共享进行中的任务；结束后的下一请求重新执行。
- 等待者取消不能停止 writer 或移除它的进行中槽位。
- 同步/异步失败后可真正开始下一次尝试。
- 注入同 key 新 owner，验证旧 writer 的结束清理不能删除新 owner。

最后一例只注入字典替换模拟身份竞争；其它测试通过公开调用和实际临时 cache 文件验证行为。
没有用 sleep 推断传输完成，临时 cache 文件在测试 finally 清理。
`git diff --check` 通过；整合后全量门禁和真实实例验收由主任务执行。

## 整合审计摘要与限制

按 `b7fc64dc` 只读代码审计，普通导入、课程/CTF 编辑、TeamLab 发布/试运行与 rollout
不再调用全节点 fanout，显式 templates/release Prepare 保留。Docker、普通 VM、TeamLab shard 和
热添加资产的已选节点 Ensure 仍存在；热添加源模板/节点身份在 Ensure 前落库。
回收 cutoff 在分发锁内重读引用；较新的 prewarm 受到保护，active Reset 票据保护运行/准备/主制品引用；
实际清缓存仍检查数据库引用与 Agent backing/inventory。

审计另外报告 Docker 全局传输锁缺少 writer 自身期限，卡住的单次 pull 可阻塞所有不同镜像；
已交独立 Agent 任务处理，不能将本 singleflight 修复当成该问题也已解决。

同盘预算的 Check/Reserve 原子且 monitor 重入不构成死锁，VM 续传按剩余新增字节准入，
已完整且摘要正确的 `.part` 可直接提升。以下仍是明确边界：

- Agent 账本仅管 active image writers，主站开机/长期增长预留没有加入该本机账本。
- Docker 默认 8 GiB 是保守允许量，不是可信展开上界或磁盘 quota，不能保证任意大镜像绝不填满盘。
- 没有末消费者取消共享 writer、冷缓存 TTL/LRU 或 PVE thin pool 的物理容量门禁。
- 标准镜像可走 Registry；仍有旧 LocalFilePath 唯一源，不能未经迁移/引用检查删除。

本审计不重复已安排的 AgentClient VM Storage 分类丢失修正，也不声称真实两套环境已验收。

## 已取消请求的入口边界补充

进入 RunAsync 时 waiter token 已取消，会在创建或启动共享 writer 前直接抛出取消；
不影响其它请求已经启动的 writer，也不添加末消费者停止下载机制。新增公开行为测试证明
已取消请求的 operation 执行次数为 0，后续未取消请求正常执行。扩大定向结果 15/15 通过、0 skip，diff 通过。

## 全量并发下的测试同步修正

全量门禁暴露测试只等待 inner operation 完成后就模拟缓存回收，未等待 shared task 的 finally；
定向通过不能证明该同步在并发压力下准确。现捕获已经启动的 Lazy.Value 并直接等待实际共享任务终态，
不新增 RunAsync 等待者，不用 sleep，也不改变生产共享任务语义。
