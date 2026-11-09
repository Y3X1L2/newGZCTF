# 批量导入开发定位

[概览](README.md)说明适用范围。Provisioning 处理业务数据导入，VM/容器部署问题应转到[运行模块](../runtime/development.md)。

## 四个入口与授权

| 接口 | Controller | scope 与资源范围 |
| --- | --- | --- |
| `POST /api/open/v1/training/courses/import` | [TrainingOpenApiController](../../../src/GZCTF/Modules/Provisioning/Api/TrainingOpenApiController.cs) | `training:write`，`training-course:*` |
| `POST /api/open/v1/theory/questions/import` | [TheoryOpenApiController](../../../src/GZCTF/Modules/Provisioning/Api/TheoryOpenApiController.cs) | `theory:write`，`theory-bank:*` |
| `PUT /api/open/v1/theory/games/{gameId}/paper` | 同上 | `theory:write`，对应 `game:{gameId}` |
| `POST /api/open/v1/teams/import` | [TeamsOpenApiController](../../../src/GZCTF/Modules/Provisioning/Api/TeamsOpenApiController.cs) | `teams:write`，`team:*`，管理员拥有的 token |

[AcademicImportResourceGrantPolicies](../../../src/GZCTF/Modules/Provisioning/Infrastructure/AcademicImportResourceGrantPolicies.cs)
维护资源授权签发边界，通用 scope 与认证见[Identity](../identity/development.md)。
外部身份和浏览器会话分开，创建请求不能以 Cookie 绕过 token 授权。

## 校验失败与引用错误

[AcademicImportApplicationService](../../../src/GZCTF/Modules/Provisioning/Application/AcademicImportApplicationService.cs)
提供 `SubmitTrainingCoursesAsync`、`SubmitTheoryQuestionsAsync`、`SubmitTheoryPaperAsync` 和 `SubmitTeamsAsync`。
请求体在 [AcademicImportContracts](../../../src/GZCTF/Modules/Provisioning/Contracts/AcademicImportContracts.cs) 定义。

| 数据 | 首次定位 | 需要保留的条件 |
| --- | --- | --- |
| 课程 | `NormalizeCourse` / `RejectChapterCycles` | 每批1–50；外部标识、章节父子引用和环、实验/理论卷的章节关联 |
| 理论题库 | `ValidateQuestion` | 每批1–1000；复用 Theory 的题型、选项和答案规范 |
| 比赛试卷 | `SubmitTheoryPaperAsync` | 1–1000题、正分值、有效目标比赛与来源题 |
| 战队 | `ValidateUserReference` | 每批1–200；队长和成员至少给可定位的用户引用 |
| 附件与视频 | `ValidateRemoteAttachment` / `ValidateHttpUrl` | 合法 HTTP/HTTPS 来源，不能接受任意本机路径 |

ExternalId 是导入请求中用来连接父子对象和返回结果的外部标识，
不是平台数据库 ID，也不等同跨批次更新协议。
课程、章节、实验、卷引用先在请求中校验，执行时再核对数据库资源是否仍存在。

## 幂等与任务保存

`SubmitAsync` 规范化请求、计算摘要，创建种类为 `academic.import.v1` 的 operation。
[EfAcademicImportSubmissionStore](../../../src/GZCTF/Modules/Provisioning/Infrastructure/EfAcademicImportSubmissionStore.cs)
持久化 [AcademicImportJob](../../../src/GZCTF/Modules/Provisioning/Domain/AcademicImportJob.cs) 和操作关联。
同 token、routeKey、Idempotency-Key 的相同请求复用原操作，摘要不同则冲突。
接口返回202不代表对象已经创建。

通用 operation 生命周期、租约与恢复由[审计模块](../audit/development.md)的
`ApiOperationWorker` / `IdempotencyService` 管理，不能改成裸后台 `Task.Run`。
请求含实验 Flag 等敏感内容，日志不得输出完整导入 payload。

## 后台执行与结果

[AcademicImportOperationHandler](../../../src/GZCTF/Modules/Provisioning/Infrastructure/AcademicImportOperationHandler.cs)
按任务 kind 分派，在数据库事务中创建对象、保存结果并清除 PayloadJson。
已有 ResultJson 会直接返回，终态失败也清除 payload。
[AcademicImportResultProvider](../../../src/GZCTF/Modules/Provisioning/Infrastructure/AcademicImportResultProvider.cs)
向 operation 查询提供 ExternalId、资源类型与平台 ID 映射。

关键业务分支：

- `ImportTrainingCoursesAsync` 核对执行者教师资格，建立课程 Owner、章节、课程题快照、理论题和课后卷；发布由导入的 Publish 意图决定。
- `ResolveImageTemplateAsync` 解析并检查可用镜像，课程只建 binding；不能创建第二份镜像主资产。
- `ImportTheoryQuestionsAsync` 复用题库目录与标签接口。
- `ImportTheoryPaperAsync` 要求 Theory/Mixed 比赛与有效来源题；存在 Submitted 答卷拒绝替换。
- `ImportTeamsAsync` 再检查管理员角色、用户引用是否一致及队长既有战队数，每名队长最多3支战队。执行时排除 `DeletedAtUtc` 非空的匿名历史账户，即使请求保存了旧用户 ID，也不能重新把已删除账户加入战队。

失败时保留 operation ID、错误码和脱敏定位信息，不把202受理或部分日志当作成功。
课程内容、理论判分和战队管理的稳定行为分别见[Training](../training/development.md)、
[Theory](../theory/development.md)、[Identity](../identity/development.md)。

## 定向验证与现有缺口

[AcademicImportRegistrationTests](../../../src/GZCTF.Test/UnitTests/Provisioning/AcademicImportRegistrationTests.cs)
验证导入 handler 按 operation kind 注册，不能证明所有导入业务成功。
[ApiTokenIssuerTests](../../../src/GZCTF.Test/UnitTests/Security/ApiTokenIssuerTests.cs)
和 [ScopedApiTokenAuthenticationTests](../../../src/GZCTF.Integration.Test/Tests/Api/ScopedApiTokenAuthenticationTests.cs)
覆盖通用签发/认证边界；[ApiOperationWorkerTests](../../../src/GZCTF.Test/UnitTests/Services/ApiOperationWorkerTests.cs)
定位后台 worker 行为。

示例：`dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~AcademicImportRegistrationTests`。
当前没有在本页声明完整四类批量导入 HTTP 验收；更改 handler 或事务时，
需验证相同幂等键重试、内容冲突、缺失引用、整批回滚、角色变化和结果查询范围。
真实外部联调、重启恢复与目标环境证据另记[任务记录](../../records/README.md)。
协议遵守[外部 API 标准](../../architecture/external-api-standard.md)，依赖遵守[模块边界](../../architecture/module-boundary-map.md)。
