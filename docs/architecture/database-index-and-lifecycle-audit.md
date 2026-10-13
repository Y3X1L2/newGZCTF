# 数据库索引与生命周期审计基线

版本：1.0

文档状态：现行数据库治理基线

事实来源：当前源码、PostgreSQL migration 与真实查询计划

## 1. 审计规则

1. PostgreSQL 是业务事实、部署状态、API operation 和可恢复状态的唯一来源。
2. 索引必须绑定实际查询；没有 query contract 和 EXPLAIN 证据的索引不得进入生产。
3. 新表必须登记 owner、预计规模、写入频率、查询路径、唯一约束、删除策略和敏感字段。
4. 自动清理只适用于显式登记的数据集。Submission、Participation、课程进度、理论答题和 AWDP 比赛事实不应用时间默认值删除。
5. 分区只用于已证明的高频追加表。首轮固定为 Logs 和 TeamLabTrafficFlows。
6. 所有时间边界使用 UTC；游标排序必须包含唯一 ID，避免相同时间戳重复或遗漏。

## 2. 查询与索引矩阵

| 数据集 | 主查询 | 目标索引/约束 | 生命周期 |
| --- | --- | --- | --- |
| Participation | 比赛参赛队、状态/分组、队伍参赛关系 | unique `(GameId, TeamId)`；`(GameId, Status, DivisionId, TeamId)` | owner managed |
| Submission | 比赛/题目/队伍时间流、题目尝试数、待检查 Flag | `(GameId, SubmitTimeUtc DESC, Id DESC)`；`(ChallengeId, SubmitTimeUtc DESC, Id DESC)`；`(TeamId, SubmitTimeUtc DESC, Id DESC)`；`(ParticipationId, ChallengeId)`；待检查 partial index | owner managed |
| GameChallenge | 比赛启用题目、实例初始化 | `(GameId, IsEnabled, Id)` | owner managed |
| TrainingCourseProgress | 课程学员状态、最近学习 | unique `(CourseId, UserId)`；`(CourseId, Status, UpdatedAt DESC, UserId)` | owner managed |
| TrainingChapterProgress | 学员章节状态 | unique `(ChapterId, UserId)`；`(UserId, UpdatedAt DESC)` | owner managed |
| TheoryQuestion | type/tag/关键词/更新时间游标 | tag normalized unique；binding `(QuestionId, TagId)`；pg_trgm title/bank；`(Type, UpdatedAt DESC, Id DESC)` | owner managed |
| TheoryAnswerSheet | 用户比赛单次答卷、结果榜 | unique `(UserId, GameId)`；`(GameId, Status, SubmittedAt DESC, Id DESC)` | owner managed |
| DeploymentQueueTicket | active claim、节点队列、终态历史 | active identity partial unique；`(Status, CreatedAt, Id)`；`(TargetNodeId, Status, CreatedAt, Id)`；terminal completion partial index | terminal 180 days |
| ApiOperation | 幂等、lease claim、用户 operation 历史 | idempotency unique；`(Status, NextAttemptAt, Id)`；`(ActorUserId, CreatedAt DESC, Id DESC)` | terminal 90 days |
| ImageDistributionRecord | 模板/节点当前状态 | unique `(ImageTemplateId, WorkerNodeId)`；`(WorkerNodeId, Status, LastCheckedAt)` | current fact |
| ImageDistributionReference | 业务引用 | unique `(DistributionRecordId, Kind, ResourceId)`；`(Kind, ResourceId)` | owner managed |
| TeamLabEvent | runtime/generation 时间线 | `(RuntimeId, Generation, CreatedAt DESC, Id DESC)` | terminal runtime 180 days |
| TeamLabTrafficFlow | runtime/generation/window/网络查询 | 时间分区；`(RuntimeId, Generation, CapturedAt DESC, Id DESC)`；window 内 fingerprint unique | raw 7 days |
| TeamLabTrafficFlowAggregate | runtime/network/协议趋势 | unique 完整聚合维度；`(RuntimeId, Generation, BucketStart)` | 180 days |
| WorkerNodeMetricSample | 节点分钟级容量趋势 | unique `(WorkerNodeId, WindowStart)`；`(WindowStart, WorkerNodeId)` | 180 days |
| LogModel | 时间/级别/logger | 月分区；`(TimeUtc DESC, Id DESC)`；`(Level, TimeUtc DESC, Id DESC)` | raw 30 days |
| AwdpRound | 当前轮次和历史 | unique `(GameId, RoundNumber)`；active `(GameId, Status, RoundNumber DESC)` | owner managed |
| AwdpCheckerTask | round/service/team 和状态重试 | unique `(RoundId, ServiceId, TeamId)`；`(Status, UpdatedAt, Id)` | owner managed |

## 3. 默认生命周期矩阵

| 数据集 | 原始保留 | 聚合保留 | 清理前置条件 | 清理动作 |
| --- | ---: | ---: | --- | --- |
| system-log | 30 天 | 180 天 | 小时聚合已校验 | drop 完整月分区或批量删除 |
| teamlab-flow | 7 天 | 180 天 | 5 分钟聚合已校验且 runtime window 关闭 | drop 完整日分区 |
| deployment-ticket | 180 天 | 365 天日聚合 | terminal、无 active operation 引用 | SKIP LOCKED 分批删除 |
| api-operation | 90 天 | 不聚合 | terminal、超出幂等恢复窗口、无运行 job | SKIP LOCKED 分批删除 |
| teamlab-event | 180 天 | 不聚合 | runtime terminal 且事件不属于当前 generation 排障窗口 | SKIP LOCKED 分批删除 |
| governance-run | 365 天 | 不聚合 | terminal | SKIP LOCKED 分批删除 |
| worker-node-metric | 180 天 | 不聚合 | 已持久化分钟样本 | SKIP LOCKED 分批删除 |

上述值是可配置默认值，不是硬编码业务常量。任何缩短必须先完成备份/合规确认并记录配置变更审计。

## 4. 分区约束

- Logs 按 UTC 月分区；TeamLabTrafficFlows 按 UTC 日分区。
- 应用始终提前创建当前和后续两个分区；缺失分区是 health degradation。
- partition DDL 使用 PostgreSQL advisory transaction lock。
- 分区 drop 前必须对候选分区完整重聚合；日志的 source count 与聚合 count 必须一致，流量的 flow/packet/byte 总量必须一致。随后在分区写锁内复核 source count、runtime window 和分区级 `DataGovernanceRun`，任何迟到写入都会阻止删除。
- default partition 只作为迁移保护，生产稳定运行时不允许数据长期落入 default partition。

## 5. 查询计划验收

- CI 只判断结构性退化：主查询是否命中目标索引、时间查询是否分区裁剪、是否出现无过滤大表 Seq Scan。
- 专用 commercial benchmark 记录 p50/p95/p99、shared hit/read blocks、WAL bytes、索引尺寸和 dead tuple。
- 不在普通 CI 硬编码机器相关耗时。
- 查询计划制品不得包含 Answer、Flag、token、WireGuard key、密码、完整 userdata 或 PCAP payload。

## 6. 删除与恢复

- 业务拥有者删除使用 application service 进行引用检查和显式顺序，不使用 cascade 代替业务规则。
- 数据治理 worker 只处理 catalog 中的 operational data set。
- migration contract 切换后若需回滚，使用切换前数据库备份和 WAL 恢复；不依赖有损 Down migration 重建旧 JSON 或旧分区结构。
- 每次治理运行保留行数、窗口、分区、错误码和耗时，可证明哪些数据在何时因何策略被处理。

## 7. 可执行证据

- `DatabaseGovernanceMigrationTests` 从迁移前 schema 播种旧镜像 JSON、跨月 Logs、跨日 TeamLab flow 和 Theory bank，再迁移到 latest；验证 count/checksum、关系回填、分区路由、聚合幂等、advisory lease、清理门槛和唯一约束。
- `seed-commercial-baseline.sql` 提供 CI 与 Commercial 两档确定性合成数据，不读取生产信息。
- `capture-query-plans.ps1` 只允许目标数据库名包含 benchmark/test/phase4/ci，使用 `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` 生成制品。
- `assert-query-plans.ps1` 验证七类主查询使用目标索引、无大范围 Seq Scan，Logs/TeamLab flow 只访问一个命中分区。
- `.github/workflows/quality.yml` 在独立 PostgreSQL 16 服务上迁移、播种并执行 query-plan contract，并上传 JSON plan artifact；PostgreSQL 17 Commercial 数据量结果单独记录，不将共享 runner 延迟作为容量结论。
- `RedisGovernanceMigrationTests` 验证 projection revision、节点分钟指标和公网端口 owner lease 的迁移与历史事实回填；`TeamLabTrafficStreamTests` 验证写库前崩溃留下的 pending 可由其他 consumer reclaim。
- `rehearse-pitr.ps1` 使用隔离 PostgreSQL 16、WAL archive 和 base backup 恢复至 Contract 前时间点，并校验 migration head 与升级前后标记事实。
