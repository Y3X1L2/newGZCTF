# 平台外部 API 标准

版本：v1

生效阶段：Phase 1 退出时

基础路径：`/api/open/v1`

## 1. 接口边界

- `/api/open/v1` 只接受 API token，不读取浏览器 Cookie。
- 现有 `/api/...` 是平台前端和内部集成接口，不承诺对外版本兼容。
- Agent API、节点注册 API 和公网网关 API 使用各自机器身份，不使用用户 API token。
- 外部 API Controller 只调用模块 Application contract，不能直接注入 `AppDbContext`、`AgentClient`、Docker client 或 libvirt client。
- 所有响应 JSON 使用 camelCase；v1 延续平台统一的 Unix 毫秒时间格式，标识符不复用可变名称。

## 2. API token

### 2.1 token 格式与存储

token 使用一次展示的 opaque personal access token：

```text
gzctf_pat_{tokenId:N}.{base64url(32 random bytes)}
```

数据库保存：

- token ID；
- 创建者；
- 显示名称；
- secret 的 SHA-256 摘要；
- scope；
- resource grant；
- 每分钟请求配额；
- 创建、过期、最近使用、撤销时间；
- 最近使用 IP 的不可逆摘要。

明文 secret 只在创建响应中返回一次。日志、trace、异常、数据库和前端缓存不能保存完整 token。

### 2.2 认证与 actor

`ApiTokenAuthenticationHandler` 校验身份事实：

1. token 格式和 public ID；
2. constant-time secret hash；
3. revoked/expired 状态；
4. 创建者账号是否存在且未禁用；
5. token scope 和 resource grant。

认证后、授权前的 `ApiTokenRateLimitMiddleware` 对 `/api/open/v1` 使用 Redis 原子计数执行 token 配额；超额返回 429，Redis 不可用返回 503，不能把基础设施故障伪装成 401。

认证成功后生成独立 `ClaimsPrincipal`：

```text
sub          = creator user id
actor_type   = api_token
token_id     = token id
scope        = canonical scopes
auth_scheme  = GzctfApiToken
```

token 继承创建者当前角色上限，但不能通过角色自动获得未签发 scope。有效 token 不等于管理员。

### 2.3 scope

scope 使用 `resource:action`：

| Scope | 能力 |
| --- | --- |
| `images:read` | 查询可访问镜像模板和导入状态。 |
| `images:write` | 上传或注册镜像模板。 |
| `images:delete` | 删除自己拥有且未被引用的镜像模板。 |
| `operations:read` | 查询自己发起的异步 operation。 |
| `challenges:read` | 查询出题人可访问的题目资产。 |
| `challenges:write` | 向显式授权的比赛单个或批量导入题目。 |
| `challenges:delete` | 删除显式授权比赛中的题目。 |
| `exercises:read` | 查询公共练习题库。 |
| `exercises:write` | 创建、导入和修改公共练习题。 |
| `exercises:delete` | 删除公共练习题。 |
| `teamlab.topologies:read` | Phase 3 查询可访问拓扑和 release。 |
| `teamlab.topologies:write` | Phase 3 编辑、验证和发布拓扑。 |
| `teamlab.runtimes:read` | Phase 3 查询 runtime、事件和访问状态。 |
| `teamlab.runtimes:write` | Phase 3 创建、重置和销毁 runtime。 |
| `teamlab.capture:read` | Phase 3 查询和下载授权范围内的 PCAP。 |
| `teamlab.capture:write` | Phase 3 创建和停止抓包任务。 |

resource grant 使用显式 `(resourceType, resourceId)` 行。比赛题目接口必须具有 `game:{gameId}` 或 `game:*`；教师只能签发自己拥有的具体比赛授权，`game:*` 和 `*:*` 只能由管理员签发。空 grant 不授予任何比赛。镜像接口仍按模板创建者和 `image:{name}` 授权；其他资源类型的 grant 会在签发时被拒绝。

公共练习接口使用 `resourceType=exercise`：列表、创建和批量导入需要
`exercise:*`，单题读取、更新和删除需要 `exercise:{exerciseId}`。培训课程题目
(`TrainingCourseId != null`) 不属于公共练习资源，Exercise token 不能访问或修改。

## 3. HTTP 语义

| 操作 | Method | 成功状态 |
| --- | --- | --- |
| 创建同步资源 | POST | `201 Created` + `Location` |
| 创建异步操作 | POST | `202 Accepted` + operation body |
| 全量替换草稿 | PUT | `200 OK` |
| 局部状态命令 | POST | `202 Accepted` |
| 查询单项 | GET | `200 OK` |
| 删除 | DELETE | `202 Accepted` 或 `204 No Content` |

禁止用 `200 OK` 包装失败。认证失败返回 401，授权不足返回 403，资源不存在或调用方不可见返回 404，版本冲突返回 409，语义校验失败返回 422，配额超限返回 429。

## 4. 统一错误

错误使用 `application/problem+json`：

```json
{
  "type": "https://docs.yinyu.example/problems/asset-in-use",
  "title": "Image template is in use",
  "status": 409,
  "code": "asset_in_use",
  "detail": "The image template is referenced by an active TeamLab release.",
  "traceId": "00-...",
  "errors": {
    "templateId": ["Referenced by TeamLab release 42"]
  }
}
```

`code` 是稳定机器标识；`title/detail` 可本地化但不能作为客户端分支条件。内部异常、路径、token、Flag、registry credential 和 Agent auth token 不能进入响应。

## 5. 幂等

- 所有非天然幂等的外部写接口要求 `Idempotency-Key`，长度 1-128，只允许 ASCII 字母、数字、`-`、`_` 和 `.`；题目 DELETE 同样要求该请求头，以便关联可恢复的销毁 operation。
- 唯一键为 `(tokenId, routeKey, idempotencyKey)`。
- 服务保存规范化请求摘要、operation ID 和最终响应引用。
- 相同 key、相同摘要返回原 operation；相同 key、不同摘要返回 `409 idempotency_conflict`。
- operation 处于 Running 时重试返回同一 operation，不重复上传、建题、建 runtime 或销毁。
- 大文件上传必须提供 `Content-Digest: sha-256=:base64:`；摘要覆盖文件内容，元数据单独做 canonical JSON hash。

## 6. 异步 operation

统一 operation 状态：

```text
Pending -> Running -> Succeeded
                   -> Failed
```

响应模型：

```json
{
  "id": "019...",
  "kind": "image.import",
  "status": 0,
  "resourceType": null,
  "resourceId": null,
  "stage": "pending",
  "currentProgress": 0,
  "totalProgress": 4,
  "errorCode": null,
  "errorDetail": null,
  "result": null,
  "createdAt": 1783641600000,
  "updatedAt": 1783641600000
}
```

- 查询：`GET /api/open/v1/operations/{operationId}`。
- operation 只能由创建者、显式授权 token 或管理员查询。
- `stage` 使用稳定枚举值，显示文案由客户端本地化。
- deployment operation 必须关联现有 `DeploymentQueueTicket`，不能复制队列状态机。
- 服务重启后 worker 从数据库恢复 Pending/Running operation；禁止使用不可恢复的裸 `Task.Run`。

## 7. 分页和过滤

- 大列表使用 cursor pagination：`?limit=50&after={opaqueCursor}`。
- `limit` 默认 50，最大 100。
- 响应包含 `items` 和 `nextCursor`；没有下一页时 `nextCursor=null`。
- 过滤字段必须列入 OpenAPI，不能接受任意数据库列名。
- 排序字段使用固定枚举；同一排序末尾追加稳定 ID，保证游标无重复和遗漏。

## 8. 上传接口

Phase 1 参考接口：

```text
POST   /api/open/v1/images/docker-archives
POST   /api/open/v1/images/docker-references
GET    /api/open/v1/images/{imageTemplateId}
DELETE /api/open/v1/images/{imageTemplateId}
GET    /api/open/v1/operations/{operationId}
```

Docker archive 上传流程：

1. 认证、scope、配额和 Idempotency-Key 校验；
2. 边读取边计算 SHA-256，写入受控 staging 路径；
3. 校验 `Content-Digest`、扩展名、大小和归档结构；
4. 创建持久化 operation 并返回 202；
5. worker 导入 Registry、创建 ImageTemplate、触发预分发；
6. operation 记录 `image-importing`、`image-ready`、`image-distributing`、`image-distributed`；
7. 成功后返回 resource location，失败后删除 staging 文件并保留脱敏错误。

`docker-references` 首版只接受平台内部 Registry `10.24.0.28:5000` 中的引用或无需凭据的公开引用，不接受 `registryAuth`。私有第三方 Registry 凭据需要独立 secret store 和轮换机制，不复用 `ImageTemplate.RegistryAuth` 明文列。

## 8.1 比赛题目接口

```text
GET    /api/open/v1/games/{gameId}/challenges
GET    /api/open/v1/games/{gameId}/challenges/{challengeId}
POST   /api/open/v1/games/{gameId}/challenges
POST   /api/open/v1/games/{gameId}/challenges/batch
DELETE /api/open/v1/games/{gameId}/challenges/{challengeId}
POST   /api/open/v1/games/{gameId}/challenges/batch-delete
```

- 导入和删除均返回持久化 operation；服务重启后恢复，不使用裸 `Task.Run`。
- 批量导入限制 1-100 题，先完成整批语义校验，再在单个数据库事务中创建题目、Flag、附件关系和已启用题目的实例事实。
- `externalId` 只用于调用方关联批次结果，不作为平台题目主键；operation `result` 返回 `externalId -> challengeId`。
- 数据库提交后触发比赛镜像预分发；分发失败重试时不重复创建题目。
- 删除先停止运行实例和测试环境，再删除题目；不存在的题目作为幂等成功记录在 `result.missing`。
- 完整字段、枚举、curl 示例和轮询流程见 `docs/commercialization/open-api-v1-guide.md`。

## 8.2 公共练习题目接口

```text
GET    /api/open/v1/exercises
GET    /api/open/v1/exercises/{exerciseId}
POST   /api/open/v1/exercises
POST   /api/open/v1/exercises/import
PUT    /api/open/v1/exercises/{exerciseId}
DELETE /api/open/v1/exercises/{exerciseId}
```

这些写接口均创建持久化 `ExerciseMutationJob` 和 `ApiOperation`，由可恢复 worker 执行；
资源授权、幂等键和审计规则与本标准第 5、6、10 节相同。附件契约仅允许远程 URL，
平台会在导入时复制附件，不共享比赛题目附件实体。

## 9. 并发与配额

- token 每分钟请求配额使用 Redis 原子计数和 TTL；key 包含 token ID 和 UTC 分钟桶。
- Redis 不可用时，外部写接口 fail closed 并返回 `503 quota_backend_unavailable`，不能退化为无限调用。
- 大文件导入同时受全局、token 和存储节点并发 gate 限制。
- 429 返回 `Retry-After`、`RateLimit-Limit`、`RateLimit-Remaining` 和 `RateLimit-Reset`。
- Phase 5 可以优化 Redis key 和批写策略，但不能改变本标准的 HTTP 契约。

## 10. 审计与观测

每次外部请求记录：

- trace ID、operation ID；
- token ID、creator user ID；
- scope、route key、HTTP method；
- resource type 和 resource ID；
- status code、稳定错误 code、耗时；
- request/response 字节数；
- remote IP 的规范化值；
- 幂等命中状态。

禁止记录 Authorization、Cookie、上传文件内容、Flag、完整 userdata 和 registry credential。OpenTelemetry span 使用同一 trace ID；系统日志用于治理动作，operation/部署队列用于执行生命周期。

## 11. 版本管理

- v1 内只能新增可选字段、可选 endpoint 和枚举扩展策略允许的值。
- 删除字段、改变字段语义、改变默认授权或修改状态机属于破坏性变更，发布 `/api/open/v2`。
- OpenAPI JSON 在 CI 中生成并与已提交快照比较；破坏性 diff 阻止合并。
- v1 Controller 和 contract DTO 不能直接复用内部页面 DTO，避免前端重构影响外部调用方。

## 12. 验收请求

```bash
curl -X POST https://platform.example/api/open/v1/images/docker-references \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: image-import-20260710-001" \
  -H "Content-Type: application/json" \
  -d '{"name":"web-lab","registryUrl":"10.24.0.28:5000/labs/web:v1","osType":"Linux"}'
```

相同请求重复执行必须返回同一 operation ID；更换请求体但复用 key 必须返回 `409 idempotency_conflict`；缺少 `images:write` scope 必须返回 403。
