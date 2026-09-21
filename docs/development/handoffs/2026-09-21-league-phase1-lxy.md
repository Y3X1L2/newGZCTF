# 联赛第一阶段 lxy 交接

## 范围与基线

- T0、T1、T4 独立实现；起点为 `origin/main 9999e6b1`。
- 分支：`codex/league-phase1-lxy-20260921`；worktree：`D:/Work/newGZCTF-league-lxy-20260921`。
- 不合并 main、不部署。yhr/lmr/lcx 的真实实现和整场验收仍待接入；没有把测试替身注册到主站。
- 用户已确认独立报名事实、队长报名、准备时冻结成员、通用席位 1/2、必填中止原因，以及有效提交与终局使用同一本地事务。

## 已实现

League 模块提供场次创建/列表/详情/草稿修改、报名审核和两队席位选择。准备时冻结配置；后台保存并恢复准备、开赛和清理意图。终局用例使用 PostgreSQL 行锁，KO 与管理员中止只产生一个赛果；清理失败不改赛果。新增迁移 `20260921122333_AddLeaguePhaseOne`，历史 migration 未修改。

接口、状态、错误和调用样例见[第一阶段契约](../league/phase1-contract.md)。[共同开发样例](../league/phase1-examples.json)由真实 DTO 生成并校验。前端生成客户端已更新；完整生成同时修正主线原有的 TeamLab 等类型滞后，类型检查与前端完整门禁通过。

## 三人接入入口

| 接手人 | 第一步 | 待联调 |
| --- | --- | --- |
| yhr | 实现 `ILeagueRuntimePort`，按 preparationId/startOperationId/cleanupOperationId 幂等 | 双队场景、Flag 注入、访问关闭/开放、迟到请求阻断、部分准备和终局后的精确回收 |
| lmr | 实现 `ILeagueFlagPort`、`ILeagueCoinPort`；T5 调用 `ILeagueFinalizationService` | 材料与 runtime/generation 绑定、同库提交事务、提交幂等、初始金币发放；同步本迁移后再生成自己的迁移 |
| lcx | 从 `api.leagueMatches` 接 U1/U2；样例只用于开发，页面通过 feature adapter 调用 | 列表与详情、seat 映射公司名称、进度/可重试错误、填写中止原因；T5/T7 完成后再接提交和账务 |

默认 `League.Enabled=false`。开发环境启用 `League__Enabled=true` 可测试报名配置；缺少任何运行、Flag、金币提供者时准备返回 `503 league_dependency_unavailable`。前端导航和业务页面由 lcx 完成。本分支没有开放生产入口。

## 验证记录

测试报告在仓库外 `D:/Work/league-lxy-test-results`。

| 验证 | 最终结果 |
| --- | --- |
| `dotnet build src/GZCTF.slnx -c Release` | 通过，0 错误；既有依赖/分析器警告见下文 |
| 后端全量单测 | 1145/1145，`final-unit.trx` |
| 后端全量集成 | 306/317，11 项已在原主线复现，`final-integration.trx` |
| 联赛专项集成（包含于全量） | 17/17，覆盖权限、报名回读、固定名单/席位、失败恢复、并发终局、HTTP 与审计 |
| PostgreSQL 16 迁移与恢复 | 通过；旧用户保留、迁移前 pg_dump 实际恢复到独立库、再次升级、模型无差异 |
| 前端 `pnpm build` 完整门禁 | 348/348；locale、lint、类型、架构、构建及制品预算通过 |
| `git diff --check`、新增文档链接 | 通过 |

原主线 `9999e6b1` 的独立 worktree 复现了 8 项既有 API 失败：`OpenTeamLabOperationsApiTests` 中 6 项（AssetFiles、AssetControl、ServiceAccess、RuntimeStatusAndSessionDiscovery、RemoteAudit、RemoteSessions），以及 `OpenImageApiTests` 中 2 项（ImportedTemplate、OpenImage_GetAndDelete）。前五项预期 404 实际 403，RemoteSessions 返回 Failed，两项镜像删除返回 500。另有 3 项真实基础设施测试因 Docker Hub 的 python/ubuntu 镜像拉取或 DNS 失败无法执行；SFTP 初次构建失败，基线复跑已通过。没有修改这些不属于联赛的实现或断言来消除失败。

构建还有仓库既有 `Microsoft.Build.Tasks.Git 8.0.0` 的 NU1902 和测试分析器警告，本次未升级依赖。

真实双港场景、Flag 获取、两队网络隔离、实际访问开放/回收、页面多尺寸/主题/键盘验收未执行。需要 yhr、lmr、lcx 和授权场景测试人员共同完成，不能以此分支的测试替身结果签收整场比赛。
