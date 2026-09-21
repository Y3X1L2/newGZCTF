# 联赛第一阶段接口约定

负责人：lxy（T0、T1、T4）。本版按 2026-09-21 分工手册实现，加入用户确认的双队席位和中止原因。代码位于 `Modules/League`；公共类型以 `Contracts/LeagueContracts.cs` 为准。

## 数据和身份

`LeagueMatches` 保存配置、状态、操作编号、赛果和清理意图；`LeagueRegistrations` 保存报名、审核、席位、固定名单和准备进度。复用现有 User/Team，不写 CTF `Participation`，不新增部署队列。部署仍由 yhr 使用 `DeploymentQueueTicket`。

- 场次、准备、开赛、清理、runtime、release、用户使用 UUID；战队使用现有 int ID。
- 金额单位为金币，使用 `int`，范围 0–2147483647。T7 自行保存账户和流水。
- 队长为未锁定战队报名；重复报名返回已有记录，拒绝后的报名也不自动变成待审。
- 管理员审核和选择两队。`firstTeamId` 对应 `seat=1`，`secondTeamId` 对应 `seat=2`；双港场景可显示为东港/西港，平台不写死公司名称。
- 准备时重新读取战队，冻结成员、名称、席位、release 和初始金币。队长包含在名单中；同一账号不能同时属于本场两队。之后改战队成员不改变本场权限。
- 管理员可查全部报名和名单；选手只看自己的报名、入选队伍摘要及本队名单/runtime 标识。其他队伍的成员和 runtime 标识不返回。
- 管理操作仅 Admin+；HTTP 使用现有网页登录身份，不从请求体接受操作者或管理员标志。

## 状态和恢复

| `LeagueMatchState` | 含义 |
| --- | --- |
| 0 Draft | 可以报名、审核、修改配置和选择席位 |
| 1 Preparing | 配置已固定，等待双方环境、Flag 和入口检查 |
| 2 Ready | 双方就绪，选手访问仍关闭 |
| 3 Starting | 已保存开赛操作，等待双方访问开放完成 |
| 4 Running | 开放完成，记录统一 `startedAt`，允许 T5 确认 KO |
| 5 Ended | 已保存唯一赛果；独立展示清理状态 |

`LeagueProgressState`：0 Pending、1 Running、2 Ready（本操作完成）、3 Failed。一队失败时场次仍为 Preparing；部分开放失败时仍为 Starting，不能判 KO。非终局均可中止。首期没有暂停或原场次重建；需要重建时中止并新建场次。

`LeagueFailure`：0 None、1 DependencyUnavailable、2 EnvironmentFailed、3 FlagBindingFailed、4 AccessFailed、5 CleanupFailed、6 ProviderUnavailable、7 InvalidProviderResponse。页面按枚举显示失败原因，不依赖异常字符串；提供者异常正文不进入响应、场次记录或日志。

后台每 5 秒读取数据库中 Pending/Running 意图，单次调用最多 30 秒。超时或进程退出保留原操作编号。明确失败停止自动推进，由管理员按 `retryable` 重试或中止；重试继续使用原编号。不同进程通过 PostgreSQL 场次行锁串行推进，网络提供者必须支持幂等和取消。

## 给 lcx：HTTP

基础路径 `/api/league/matches`，使用 camelCase，枚举为上表数字，时间为 Unix 毫秒。默认关闭入口；独立开发环境配置 `League__Enabled=true` 后可使用 T1。未接入真实提供者时 prepare 返回 503，不冻结配置、不返回假就绪。

| Method / 相对路径 | 请求体或参数 | 成功 |
| --- | --- | --- |
| GET 空路径 | `limit=30`（1–100）、`after=UUID` | 200，`LeagueMatchPage` |
| POST 空路径 | `{name, topologyId, releaseId, initialCoins}` | 201 + Location，详情 |
| GET `/{id}` | 无 | 200，详情 |
| PUT `/{id}` | `{revision, draft: {name, topologyId, releaseId, initialCoins}}` | 200，详情 |
| POST `/{id}/registrations` | `{teamId}` | 200，详情 |
| POST `/{id}/registrations/{teamId}/review` | `{state: 1}` 通过，`{state: 2}` 拒绝 | 200，详情 |
| POST `/{id}/teams` | `{revision, firstTeamId, secondTeamId}` | 200，详情 |
| POST `/{id}/prepare` | `{revision}` | 202，详情，`operation.id` 为准备编号 |
| POST `/{id}/start` | 无 | 202，详情，`operation.id` 为开赛编号 |
| POST `/{id}/abort` | `{reason}`，1–500 字，不能全为空白 | 200，详情，终局含 `abortReason` |
| POST `/{id}/retry` | 无 | 202，原准备/开赛操作 |
| POST `/{id}/cleanup/retry` | 无 | 202，原清理操作 |

草稿可暂不选择场景，此时 topologyId/releaseId 必须同时为空。配置场景时两者都必填，release 必须属于该 topology 且未归档。准备时必须已配置场景并选定两队。列表按 UUID 升序分页，以响应 `nextCursor` 继续。

详情含 `match`、配置、`registrations`、两队 `preparation`、`operation`、`cleanup` 和 `allowedActions`。轮询详情即可恢复页面。`allowedActions` 是状态/角色提示，prepare 的最终条件仍由后端校验。当前接口不提供 Flag 提交、钱包或访问凭据；分别接 T5、T7 和 yhr 的访问能力。

成功与等待/失败样例见 [phase1-examples.json](phase1-examples.json)，由 `LeagueContractTests` 使用真实 DTO 和平台序列化规则生成、校验，限开发和测试使用。示例中的姓名和 ID 都是测试值。

失败使用 `application/problem+json`，例如：

```json
{"status":503,"code":"league_dependency_unavailable","title":"The request could not be processed.","detail":"运行、Flag 或金币服务尚未接入。"}
```

主要错误：401 登录、403 非管理员/非队长、404 不存在、409 `league_revision_conflict` / `league_configuration_frozen` / `league_not_ready` / `league_not_retryable`、422 配置/席位/名单/原因无效、503 `league_disabled` / `league_dependency_unavailable`。数据注解解析错误沿用平台 400。GET 不推进后台操作。

客户端已从本次真实 `/openapi/v1.json` 重新生成，使用 `api.leagueMatches`。此轮完整生成也同步了主线原有 TeamLab 等契约的滞后类型，未手工改生成文件。复现生成：先运行 `LeagueApiTests`，用 `LEAGUE_INTERNAL_OPENAPI_PATH` 指定仓库外 JSON 输出，再执行：

```powershell
pnpm exec swagger-typescript-api generate -p <JSON路径> -t template -o src/generated --module-name-first-tag --sort-routes
```

## 给 yhr：运行端口

注册 `ILeagueRuntimePort`，替换默认不可用实现。输入 `LeagueFrozenMatch` 已包含两队固定名单、席位、场景、配置版本、初始金币和准备编号。

1. `PrepareAsync(match, cores)`：按 `preparationId` 幂等准备，返回稳定 `operationId` 和恰好两队的状态。初次可返回 Pending、空 binding；runtime 确定后返回 `{teamId,runtimeId,generation}`。同一准备过程不能替换已确认的 runtime 或代次。
2. 就绪必须同时满足环境、Flag 注入、入口准备检查和访问关闭；两队 runtime 必须不同。缺一项仍等待。单队失败返回 Failed、Failure 枚举、Retryable；保留另一队进度。
3. `OpenAccessAsync(match, startOperationId, bindings)`：开放双方约定的入口，完成返回 `{completed:true,failure:0,retryable:true}`；部分完成返回 `completed:false`。失败可返回 `failure:4`，重试同一编号。T4 只有收到完成才记录 Running 和统一开赛时间。
4. `CleanupAsync(match, cleanupOperationId)`：先阻止旧准备/开放请求再次生效，再关闭访问和回收。须能按准备编号找回尚未成功回报给 lxy 的资源。清理完成返回 `completed:true`；失败返回 `failure:5` 和是否可重试。不得影响其他场次。

这些方法负责快速受理/查询已有运行操作，不应等待整个部署完成。后台持有场次行锁调用，单次预算 30 秒。不得在同一 DbContext 中另开事务，不得修改 League 实体。异步运行继续使用现有部署队列。终局后的清理必须在提供者端阻止迟到的开放/创建动作；仅在 lxy 侧检查 Ended 不足以防止进程崩溃后的迟到请求。

## 给 lmr：Flag、金币和终局

`ILeagueFlagPort.PrepareAsync` 按 preparationId/teamId/coreKey=`core` 生成或复用两份独立材料，返回不含秘密的 `LeagueCoreReference`。yhr 通过双方约定的受保护内部通道凭 materialId 取得注入材料，不能通过本 HTTP DTO 交付明文。`ConfirmBindingsAsync` 负责保存/核对对应 runtime 与 generation 的绑定，成功返回 true；T4 在待开赛和开赛时均调用它。

`ILeagueCoinPort.InitializeAsync` 在准备过程中发放双方初始金币，以 preparationId/teamId 幂等。重复调用必须沿用原账户与初始流水；金额为零也建立合法账户。两个账户的写入参与当前 scoped AppDbContext 事务，不另开嵌套事务。T7 尚未接入时不会跳过金币并声称准备成功。

`ILeagueMatchQuery` 提供摘要、固定配置和按 userId 查本场参赛队。T5 应从登录身份查出队伍，不能相信请求体传入的 winnerTeamId。

T5 的有效提交必须使用与 `ILeagueFinalizationService` 相同的 scoped AppDbContext：

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);
// 校验本场对方核心、代次及请求幂等；提交记录只保存必要的脱敏信息。
var result = await finalizer.ConfirmKnockoutAsync(command, ct);
// 根据 Applied/已有赛果保存提交结果；同 key 返回原提交结果。
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

`LeagueKnockoutCommand` 带场次、提交 ID、服务端确认的 userId/winnerTeamId、preparationId、对方 runtime 绑定。终局服务要求外层事务，锁场次并复核固定成员、Running、对方和代次，然后保存赛果及清理意图；它不会提交事务。`Applied=false` 返回已有终局，调用方不能把第二份提交写成第二个胜者。任何后续保存失败都必须回滚整个事务，不得捕获后提交部分结果。lmr 负责 Flag 真实性和提交幂等，lxy 不复制 Flag 或 Submission 表。

同一场次的两个有效提交及管理员中止使用同一数据库行锁。第一笔成功提交终局事务者决定结果；中止无胜者。清理失败不改变结果。T5 还需验证错误、自身、跨场、旧代次、赛前与赛后提交，并与本事务契约联调。

## 迁移、开关和待联调

本分支新增一笔 `AddLeaguePhaseOne`，包含报名、场次、席位和中止原因。历史 migration 保持不变。League 报名外键阻止删除仍被引用的战队，场次外键阻止删除所选 release；没有新增删除场次接口，也没有自动删除赛果的任务。lmr 后续同步本迁移与完整快照，再生成 Flag/账户迁移。

默认 DI 只返回“依赖未接入”，没有生产测试替身。T0 契约已经用户确认，仍需 yhr、lmr、lcx 各自检查具体字段；四人联调签收尚未完成。T2/T3/T6/T7 真实实现、场景内容、Flag 注入、访问隔离、真实 KO 和回收由后续接入验收。生产发布须另获授权、备份并验证迁移；应用回退时保持联赛入口关闭，不通过删除历史或降级生产库回滚。
