# Phase 4 数据库基准

## 数据规模

| 档位 | Participation | Submission | 课程进度 | Theory | Queue | Logs | TeamLab flow |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CI | 500 | 100,000 | 20,000 | 5,000 | 20,000 | 100,000 | 200,000 |
| Commercial | 500 | 3,000,000 | 200,000 | 50,000 | 500,000 | 5,000,000 | 10,000,000 |

数据全部由 `seed-commercial-baseline.sql` 确定性生成，不读取生产数据。CI 检查索引选择、大表顺序扫描和时间分区裁剪；Commercial 在专用 PostgreSQL 17 主机执行并记录机器相关结果。

## 性能目标

- 排行榜/Participation 事实查询 p95 < 500ms。
- Submission、Queue、Logs、Flow 历史页 p95 < 300ms。
- 单批 1000 条 flow 落库 < 250ms。
- 治理批次锁等待 p95 < 100ms。
- 新增单索引大于对应表大小 80% 时必须有查询收益证据，否则移除。

## 当前证据

- PostgreSQL 16 migration contract：通过跨月 Logs、跨日 TeamLab flow、关系回填、聚合幂等、advisory lease 和先聚合后删除验证。
- CI 查询计划：由 `.github/workflows/quality.yml` 每次提交重新生成并断言，不提交易过期的 runner 毫秒结果。
- Commercial p50/p95/p99：待在目标 PostgreSQL 17 专用主机按本文件规模执行；不得用本地开发机结果替代生产容量结论。
