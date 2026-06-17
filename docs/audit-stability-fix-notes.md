# 问题审查核实与修复记录

更新时间：2026-06-17
分支：codex/training-platform-polish-20260617

## 本轮确认并修复

1. RedisDistributedLock 本地回退锁释放竞态
   - 结论：真实存在。
   - 修复：本地 fallback 锁不再在释放时从 ConcurrentDictionary 移除，避免 Release 与 TryRemove 非原子导致同 key 出现两个 SemaphoreSlim。
   - 影响面：仅无 Redis / Fleet 本地回退锁路径。

2. CacheHelper GetOrCreateAsync 双重调用与 WaitLockAsync 无限等待
   - 结论：真实存在。
   - 修复：内存缓存 miss 后只走一次分布式缓存创建路径；等待更新锁增加 30 秒上限，避免遗留锁导致请求永久轮询。
   - 影响面：缓存稳定性与延迟，不改变缓存 key 或数据格式。

3. Submission / CacheRequest Channel 无界队列
   - 结论：真实存在。
   - 修复：统一改为容量 16384 的有界 Channel，FullMode=Wait，采用背压而不是丢任务，降低高峰 OOM 风险。
   - 影响面：极端高峰下写入方可能等待，但任务不会静默丢失。

4. 多类型提交 FirstSolve 并发竞态
   - 结论：SubmissionController 路径真实存在；核心 Jeopardy GameInstanceRepository 已有 advisory lock，不属于全局缺陷。
   - 修复：SubmissionController 的创建/审核接受路径使用事务内 pg_advisory_xact_lock，并通过导航属性绑定新提交的一血记录。
   - 影响面：仅多类型提交接口，避免并发首解重复写入或 500。

5. ScoringService 只读查询未 AsNoTracking
   - 结论：真实存在。
   - 修复：评分规则与提交查询增加 AsNoTracking。
   - 影响面：降低 ChangeTracker 压力，不改变评分逻辑。

6. 高频查询索引不足
   - 结论：部分真实存在。
   - 修复：新增索引迁移 AuditStabilityIndexes，覆盖 Submissions、Participations、FirstSolves 的常用复合过滤。
   - 影响面：数据库索引新增，不改变表结构语义。

## 本轮核实后不修 / 暂缓

1. “VM 销毁不释放 KVM 容量永久泄漏”
   - 结论：报告表述不准确，至少不是永久泄漏。
   - 依据：LocalNodeMetricsService 每 30 秒按 Running VmInstances 重新覆盖本地 CurrentVms；远端 Agent Heartbeat 也会上报 CurrentVms。销毁后可能有短暂计数滞后，但不是永久泄漏。
   - 决策：不在 DestroyVmAsync 手动 ReleaseCapacity，避免和心跳/指标同步模型双减打架。

2. AWDP 批量创建并行化、排行榜 N+1 全面重构、全局限流重构、DB AddDbContextPool 改造
   - 结论：方向可能成立，但属于架构/压测级改造。
   - 决策：本轮不混入，避免影响已部署训练模块和比赛主流程；建议后续单独开性能专项并配套压测。

3. 节点离线 Running 目标故障转移、调度器反羊群/分锁/Pending TTL
   - 结论：方向可能成立，但涉及调度语义和失败恢复策略。
   - 决策：本轮不做行为重构，仅记录为后续 Fleet 调度专项。
