# 缓存失效关系表

Redis 仅承载可重建投影和短期协调数据，PostgreSQL 始终是业务事实来源。未登记在本文件和 `CachePolicyCatalog` 的业务缓存不得上线。

| 投影 | Key 维度 | Revision 来源 | L1 / L2 TTL | 最大过期时间 | 触发变更 | Redis 故障处理 | 验证 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Scoreboard | game ID、全局 revision、比赛 revision | PostgreSQL `ProjectionRevisions` | 2s / 30s | 同 revision 内 0s | Game、Submission、Participation、AWDP、Penetration score 变更；显式刷新 | 回退 PostgreSQL generator | cache policy 与 scoreboard 集成测试 |
| TheoryStatistics | game ID、全局 revision、比赛 revision | PostgreSQL `ProjectionRevisions` | 5s / 60s | 同 revision 内 0s | TheoryPaper、TheoryAnswerSheet、比赛变更 | 回退 PostgreSQL result builder | theory API 集成测试 |
| TrainingStatistics | user ID、全局 revision、用户 revision | PostgreSQL `ProjectionRevisions` | 5s / 60s | 同 revision 内 0s | 课程结构/报名全局变更；进度、提交、签到、理论答卷用户变更 | 回退 PostgreSQL overview builder | training API 集成测试 |
| ClientConfig | 全局 | 显式 tag | 30s / 10m | 10m | `[CacheFlush(client-config)]` 设置和 logo 变更 | 回读 options/config | 配置集成测试 |
| Index | 全局 | 显式 tag | 30s / 10m | 10m | 标题和描述变更 | 根据当前模板/配置重建 | index handler 测试 |
| Favicon | 全局 | 显式 tag | 30s / 10m | 10m | favicon hash 变更或 blob 缺失 | 读取当前配置并回退内置图标 | favicon handler 测试 |
| CaptchaConfig | 全局 | 显式 tag | 30s / 10m | 10m | 验证码设置变更 | 根据当前 options 重建 | info API 测试 |
| GameList | 全局 | 显式 tag | 5s / 60s | 60s | 比赛创建、更新、删除、状态变更 | 查询 PostgreSQL | game API 测试 |
| RecentGames | 全局 | 显式 tag | 5s / 60s | 60s | 比赛变更和小时窗口刷新 | 查询 PostgreSQL | game API 测试 |
| GameDetails | game ID | 显式 tag | 5s / 2m | 2m | 比赛/分组变更 | 查询 PostgreSQL | game detail 测试 |
| Posts | 全局 | 显式 tag | 10s / 5m | 5m | 公告创建、更新、删除 | 查询 PostgreSQL | post API 测试 |
| GameNotices | game ID | 显式 tag | 5s / 60s | 60s | 通知创建、更新、删除 | 查询 PostgreSQL | notice API 测试 |
| ExerciseAvailability | 全局 | 显式 tag | 10s / 60s | 60s | 练习可用性变更 | 查询 PostgreSQL | exercise repository 测试 |

## 不变量

- 保证 revision 一致性的 key 同时包含全局和资源 revision；Redis 删除失败不能暴露旧 PostgreSQL revision。
- 使用 tag 失效的投影同时携带策略级和资源级 tag；全局失效不得枚举所有 key。
- 缓存 key 的资源维度使用 hash；用户名、战队名、token、flag 和 IP 不得直接写入 Redis key。
- `PlatformCache` 捕获缓存传输故障后，为本次请求执行一次 PostgreSQL factory；缓存故障不能变成业务失败。
- 缓存指标只使用 policy/purpose 和 status 标签，不能把资源 ID 作为指标标签。
