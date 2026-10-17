# 身份开发定位

概念与用户流程见[概览](README.md)。先确定请求使用浏览器会话、Portal token 还是外部 API token，三者不能互换。

## 本地登录与账户

| 问题 | 首先查看 | 核对内容 |
| --- | --- | --- |
| 登录、注册、验证或找回失败 | [AccountController](../../../src/GZCTF/Controllers/AccountController.cs) | `Register`、`LogIn`、`Verify`、`Recovery`、`PasswordReset`；账号策略、验证码、禁用角色和 Identity 结果 |
| 当前账号能力或公开资料不对 | [UserProfileQueryService](../../../src/GZCTF/Services/UserProfileQueryService.cs)、[UsersController](../../../src/GZCTF/Controllers/UsersController.cs) | 公开/私有投影、本人范围与 Summary，不应返回完整用户实体 |
| 管理员更改用户角色不生效 | [AdminController](../../../src/GZCTF/Controllers/AdminController.cs)、[UserInfo](../../../src/GZCTF/Models/Data/UserInfo.cs) | 管理授权、当前角色和缓存/会话回读 |

账号数据基于 ASP.NET Identity，身份策略和中间件与业务模块共同决定访问范围。
比赛参赛归 CTF、课程报名归 Training，Identity 不持有这些业务授权事实。

账号删除由 [UserDeletionService](../../../src/GZCTF/Modules/Identity/Application/UserDeletionService.cs) 在可串行化事务内完成：使用 `UserInfo.DeletedAtUtc` 墓碑，保留所有历史外键，避免物理删除触发战队级联或运行记录外键限制。服务清除 PII、规范化登录键、Identity tokens/claims/roles、外部登录绑定和学员组关系，更新安全戳并撤销 API tokens；安全戳每次请求验证，旧 Cookie 后续请求立即失效。API token 验证每次读取当前角色与撤销状态。不会使用全局 query filter 隐藏计分与业务历史。管理列表、公开资料和按 ID 的管理写入显式排除墓碑；批量建队解析同样拒绝墓碑账号。

删除自动移除 `Team.Members`，交接队长并轮换邀请 token；孤队保留 `CaptainId` 指向墓碑以保留参赛历史，锁定且拒绝邀请/申请，不删除 `UserParticipation`、`Participation` 或提交。数据库迁移 `AddUserDeletionTombstone` 只增加可空字段。上线按生产手册先备份、隔离恢复并前向升级；应用回退保留新字段和墓碑，不执行删除字段的 Down 迁移，不恢复被清除的凭据。

其他模块通过 [IUserAccountRevocationParticipant](../../../src/GZCTF/Modules/Identity/Contracts/IUserAccountRevocationParticipant.cs) 参与删号：`PrepareAsync` 在同一数据库事务内持久化禁止访问与待清理状态，`CompleteAsync` 在提交并释放事务后回收外部通道。后者失败不会恢复账号，参与模块必须提供持久重试。并发事务冲突返回409，管理员刷新后重试；不得绕过冲突强行覆盖队长或成员状态。

`UserHubAccountConnections` 在提交后中断本机已登录用户的全部 SignalR 连接；后台每5秒回读连接用户状态，使其他 Main 进程持有的连接也能撤销。连接注册后再次核对账号状态，避免删号和建立私有战队推送之间的窗口；匿名公共推送与历史计分不受影响。

验证见 [UserDeletionTests](../../../src/GZCTF.Integration.Test/Tests/Api/UserDeletionTests.cs)（管理员权限、队长交接、孤队、旧会话、匿名历史与登录键复用）和 [UserDeletionMigrationTests](../../../src/GZCTF.Integration.Test/Tests/Database/UserDeletionMigrationTests.cs)（真实 PostgreSQL 升级与备份恢复）。账号删除不等于销毁其业务运行实例；实例按所属模块的生命周期保留、过期或由管理员单独清理，跨系统门户会话仍须目标环境联调。

## 战队与学员组

[TeamController](../../../src/GZCTF/Controllers/TeamController.cs) 处理创建、邀请、入队申请、
审核、转让、移除成员和离队；[TeamRepository](../../../src/GZCTF/Repositories/TeamRepository.cs) 保存关系。
核对当前用户是否队长/成员、战队是否锁定、申请是否属于该队以及比赛中的使用限制。
战队删除需保留相关业务清理条件，不能仅删成员关系就宣称参赛事实已清除。

[StudentGroupAdminController](../../../src/GZCTF/Controllers/StudentGroupAdminController.cs)
处理管理端学员组，学员组与战队不是同一个对象。
外部批量建战队属于[Provisioning](../provisioning/development.md)，只绑定既有用户，不批量创建账号。

## Portal SSO

入口为 `GET /api/account/portal-sso`：

1. `AccountController.PortalSso` 校验参数和本地 `returnUrl`。
2. [PortalSsoService](../../../src/GZCTF/Services/PortalSsoService.cs) 的 `GetProfileAsync` 请求配置的 ProfileEndpoint，处理超时、拒绝、解析失败与平台准入。
3. `FindOrCreatePortalUser` / `EnsurePortalLogin` 关联门户身份；`MapPortalRole` 映射角色。
4. 后端建立本地会话并跳转到校验后的本地路径。

远端失败先看HTTP状态和脱敏错误，不打印门户 token 或完整资料响应。
`ParseProfileResponse` 支持直接 profile 与既有 envelope 结构，变更需兼顾两种响应。
真实验收须检查平台授权、禁用用户、角色变化、本地重定向与后续会话，不能仅测试 JSON 解析。

## API token 签发、验证与授权

[ApiTokensController](../../../src/GZCTF/Modules/Identity/Api/ApiTokensController.cs)
暴露浏览器管理入口 `/api/tokens`；创建、列表和撤销本身需要教师权限。

| 环节 | 实现 | 关键判断 |
| --- | --- | --- |
| 签发 | [ApiTokenIssuer](../../../src/GZCTF/Modules/Identity/Application/ApiTokenIssuer.cs) | 创建者角色、允许 scope、资源 grant policy、过期时间和请求配额 |
| 验证 | [ApiTokenValidator](../../../src/GZCTF/Modules/Identity/Application/ApiTokenValidator.cs) | 格式、摘要、撤销/过期和创建者当前角色；角色降级后的 token 不能继续沿用旧权限 |
| 认证 | [ApiTokenAuthenticationHandler](../../../src/GZCTF/Modules/Identity/Infrastructure/ApiTokenAuthenticationHandler.cs) | 独立 ClaimsPrincipal 与 token 身份，不能借浏览器 Cookie 访问 open API |
| scope 授权 | [ApiScopeAuthorizationHandler](../../../src/GZCTF/Modules/Identity/Application/ApiScopeAuthorizationHandler.cs) | scope policy 与当前角色上限 |
| 资源授权 | [IApiTokenResourceGrantPolicy](../../../src/GZCTF/Modules/Identity/Application/IApiTokenResourceGrantPolicy.cs) | 各业务模块检查资源范围，未知资源类型不能默认放行 |

[EfApiTokenStore](../../../src/GZCTF/Modules/Identity/Infrastructure/EfApiTokenStore.cs)
保存 token 与 grant，`ApiTokenSecretHasher` 保存/比较秘密摘要。
[ActorContext](../../../src/GZCTF/Modules/Identity/Application/ActorContext.cs)
把用户 ID、角色、token ID、scope 和资源范围交给应用用例，不传数据库用户实体充当统一权限协议。

## 限流与故障语义

[ApiTokenRateLimitMiddleware](../../../src/GZCTF/Modules/Identity/Infrastructure/ApiTokenRateLimitMiddleware.cs)
使用 `ApiTokenRateLimitStore` 的 Redis 原子计数，在认证后、授权前执行配额。
超额返回429，存储不可用返回503；基础设施失败不能伪装成401。
具体 scope、grant、错误和幂等契约引用[外部 API 标准](../../architecture/external-api-standard.md)，不在此复制全表。

## 前端与定向验证

账号入口在 [features/auth](../../../src/GZCTF/ClientApp/src/vnext/features/auth)，
会话回读与缓存位于 [features/account](../../../src/GZCTF/ClientApp/src/vnext/features/account)。
`authApi.ts`、`useAuthController.ts` 与 `useCurrentAccount.ts` 是请求和状态入口。
战队请求与交互在 [features/teams](../../../src/GZCTF/ClientApp/src/vnext/features/teams)，
管理用户在 `admin/users`。资料页请求使用 `profile/api/userProfileApi.ts`。

| 改动 | 现有测试 |
| --- | --- |
| 本地认证 | [AuthenticationTests](../../../src/GZCTF.Integration.Test/Tests/Api/AuthenticationTests.cs)、[IdentityConfigurationTests](../../../src/GZCTF.Test/UnitTests/Identity/IdentityConfigurationTests.cs) |
| token签发/认证/限流 | [ApiTokenIssuerTests](../../../src/GZCTF.Test/UnitTests/Security/ApiTokenIssuerTests.cs)、[ScopedApiTokenAuthenticationTests](../../../src/GZCTF.Integration.Test/Tests/Api/ScopedApiTokenAuthenticationTests.cs) |
| Portal 响应解析 | [PortalSsoServiceTests](../../../src/GZCTF.Test/UnitTests/Services/PortalSsoServiceTests.cs) |
| 战队与公开资料 | [TeamManagementTests](../../../src/GZCTF.Integration.Test/Tests/Api/TeamManagementTests.cs)、[UserProfileTests](../../../src/GZCTF.Integration.Test/Tests/Api/UserProfileTests.cs) |

示例：`dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~ScopedApiTokenAuthenticationTests`。
前端还有 `sessionCache.test.ts` / `sessionCache.integration.test.tsx`，验证缓存与失效；
跨系统 SSO、真实角色同步和账号运维仍需目标环境证据。
