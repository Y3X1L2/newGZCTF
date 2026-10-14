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
