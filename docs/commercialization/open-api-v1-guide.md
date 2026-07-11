# YINYU 平台 Open API v1 使用指南

本文档描述当前已实现的 `/api/open/v1` 外部接口。它面向出题工具、内容流水线和受控自动化脚本，不适用于浏览器前端内部接口。

## 1. 接入信息

| 项目 | 值 |
| --- | --- |
| 基础路径 | `https://{platform-host}/api/open/v1` |
| 认证方式 | `Authorization: Bearer {token}` |
| 请求格式 | JSON；Docker archive 上传使用 `multipart/form-data` |
| 时间格式 | Unix 毫秒；可空时间为 `null` |
| 错误格式 | `application/problem+json` |
| OpenAPI 快照 | `docs/commercialization/openapi/open-v1.json` |
| 开发环境 OpenAPI | `/openapi/open-v1.json` |

`/api/...` 下的其他接口属于平台内部接口，不承诺外部兼容性，不应由出题工具调用。

## 2. 创建 API Token

教师及以上角色在“账户 -> API Token”或管理员 Token 页面创建令牌。令牌明文只显示一次，格式为：

```text
gzctf_pat_{tokenId}.{secret}
```

创建比赛题目导入令牌时至少选择：

```text
challenges:read
challenges:write
challenges:delete
operations:read
```

并添加比赛资源授权：

```text
resourceType = game
resourceId   = 123
```

`game:123` 只允许访问比赛 `123`；教师只能签发自己创建并拥有的具体比赛授权。`game:*` 和全局 `*:*` 只有管理员可以签发。题目接口强制要求显式 `game` 授权，只有 scope 而没有比赛授权会返回 `403 insufficient_permission`。升级前已存在且没有所有者记录的比赛，需要由管理员签发具体比赛授权；新建比赛会自动记录创建者。

镜像导入还需要 `images:read`、`images:write`、`images:delete`。Token 支持过期、撤销和每分钟请求配额；创建者账号失效或角色降到教师以下后，Token 自动失效。

## 3. 通用请求规则

### 3.1 认证

```bash
curl -H "Authorization: Bearer $GZCTF_TOKEN" \
  https://platform.example/api/open/v1/games/123/challenges
```

不要把 Token 放入 URL、日志、Git 仓库或脚本命令行历史。生产接入必须使用 HTTPS。

### 3.2 Idempotency-Key

所有题目写操作都要求：

```http
Idempotency-Key: import-web-series-20260711-001
```

键长为 1-128，仅允许 ASCII 字母、数字、`-`、`_`、`.`。

- 同一个 Token、同一路由、相同 Key 和相同请求体：返回原 operation，不重复建题或删除。
- 相同 Key 但请求体不同：返回 `409 idempotency_conflict`。
- operation 正在执行时重复请求：仍返回相同 operation ID。

### 3.3 异步 operation

题目导入和删除返回 `202 Accepted`：

```json
{
  "id": "019bb4f0-6b26-7a8a-a523-b8727df5cf62",
  "kind": "ctf.challenge-mutation.v1",
  "status": 0,
  "stage": "pending",
  "resourceType": "game",
  "resourceId": "123",
  "currentProgress": 0,
  "totalProgress": 0,
  "attemptCount": 0,
  "errorCode": null,
  "errorDetail": null,
  "result": null
}
```

状态值：

| 值 | 状态 |
| --- | --- |
| `0` | Pending |
| `1` | Running |
| `2` | Succeeded |
| `3` | Failed |

轮询：

```bash
curl -H "Authorization: Bearer $GZCTF_TOKEN" \
  https://platform.example/api/open/v1/operations/019bb4f0-6b26-7a8a-a523-b8727df5cf62
```

operation 只能由发起它的 Token 查询。建议第一秒后开始轮询，随后使用 1、2、4、8 秒退避，最大间隔 10 秒。

题目导入阶段：

```text
pending -> challenge-validating -> challenge-persisting
        -> challenge-image-distributing -> challenges-imported -> completed
```

删除阶段：

```text
pending -> challenge-runtime-stopping -> challenge-deleting
        -> challenges-deleted -> completed
```

服务重启后 Pending/Running operation 会从数据库恢复。请求正文只在任务执行期间保留；成功或终止失败后会清除包含 Flag 的任务正文。

### 3.4 错误响应

```json
{
  "title": "The request could not be processed.",
  "status": 422,
  "detail": "Dynamic container challenges require a valid flagTemplate.",
  "instance": "/api/open/v1/games/123/challenges/batch",
  "code": "challenge_flag_template_invalid",
  "traceId": "00-..."
}
```

| HTTP | 含义 |
| --- | --- |
| `400` | JSON、游标、请求字段或 Idempotency-Key 格式错误 |
| `401` | Token 缺失、无效、过期或已撤销 |
| `403` | 缺少 scope 或比赛资源授权 |
| `404` | 比赛、题目或 operation 不存在 |
| `409` | Idempotency-Key 与原请求冲突 |
| `422` | 题目配置在业务语义上无效 |
| `429` | Token 请求配额耗尽，按 `Retry-After` 重试 |
| `503` | Redis 配额后端不可用，外部 API 暂停写入 |

## 4. 题目接口

### 4.1 接口清单

| Method | 路径 | Scope | 说明 |
| --- | --- | --- | --- |
| `GET` | `/games/{gameId}/challenges` | `challenges:read` | 游标分页查询题目 |
| `GET` | `/games/{gameId}/challenges/{challengeId}` | `challenges:read` | 查询题目完整配置和 Flag |
| `POST` | `/games/{gameId}/challenges` | `challenges:write` | 导入一个题目 |
| `POST` | `/games/{gameId}/challenges/batch` | `challenges:write` | 原子批量导入 1-100 个题目 |
| `DELETE` | `/games/{gameId}/challenges/{challengeId}` | `challenges:delete` | 停止环境并删除一个题目 |
| `POST` | `/games/{gameId}/challenges/batch-delete` | `challenges:delete` | 批量停止环境并删除 1-100 个题目 |

这里的“题目”是某个比赛中的 `GameChallenge`。Phase 10 的全局题目池是独立领域，不会改变这些比赛题目接口的 v1 语义。

### 4.2 题目类型和环境

`type`：

```text
StaticAttachment
StaticContainer
DynamicAttachment
DynamicContainer
```

`environment`：

```text
None
Docker
WindowsVM
```

规则：

- Attachment 类型只能使用 `None`，不能填写容器或 VM 字段。
- Container 类型省略 `environment` 时默认使用 `Docker`。
- Docker 必须填写 `containerImage` 和 `exposePort`，不能填写 `imageTemplateId`。
- Docker 镜像必须先通过镜像 API 注册为 `Ready` 模板，`containerImage` 使用镜像导入结果中的规范 Registry 引用；平台会把比赛题目绑定到该全局模板并记录节点分发事实。
- WindowsVM 必须填写已就绪 Windows VM 模板的 `imageTemplateId`，不能填写 Docker 字段。
- 启用的非 DynamicContainer 题目必须至少包含一个 Flag。
- DynamicContainer 必须提供有效 `flagTemplate`，例如 `flag{web_[TEAM_HASH]}`。
- 一个批次最多 100 题，`externalId` 在批次内必须唯一。

支持分类：`Misc`、`Crypto`、`Pwn`、`Web`、`Reverse`、`Blockchain`、`Forensics`、`Hardware`、`Mobile`、`PPC`、`AI`、`Pentest`、`OSINT`、`IR`。

### 4.3 导入一个静态题目

```bash
curl -X POST https://platform.example/api/open/v1/games/123/challenges \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: challenge-web-intro-001" \
  -H "Content-Type: application/json" \
  -d '{
    "externalId": "web-intro",
    "title": "Web Intro",
    "content": "访问附件并提交 Flag。",
    "category": "Web",
    "type": "StaticAttachment",
    "isEnabled": true,
    "originalScore": 500,
    "minScoreRate": 0.25,
    "difficulty": 5,
    "flags": [
      {
        "flag": "flag{web_intro}",
        "orderIndex": 0,
        "scoreMode": "InheritDecay",
        "answerType": "Flag"
      }
    ],
    "attachment": {
      "remoteUrl": "https://assets.example/challenges/web-intro.zip"
    }
  }'
```

远程附件只接受绝对 `http` 或 `https` URL。外部 API 当前不接受平台本地文件 hash；本地附件上传将在内容资产 API 中提供独立受控上传流程。

### 4.4 批量导入 Docker 与 Windows VM 题目

批量导入是整批原子操作：任何一题配置无效时不会创建任何题目。数据库写入成功后会触发比赛镜像预分发；节点暂时离线不会回滚已经创建的题目，失败的分发事实由平台后台 reconcile 继续处理，并在镜像分发状态和后续部署阶段显示。

```bash
curl -X POST https://platform.example/api/open/v1/games/123/challenges/batch \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: batch-summer-2026-001" \
  -H "Content-Type: application/json" \
  -d '{
    "items": [
      {
        "externalId": "web-dynamic-01",
        "title": "Dynamic Web",
        "content": "获取环境地址并完成利用。",
        "category": "Web",
        "type": "DynamicContainer",
        "environment": "Docker",
        "containerImage": "10.24.0.28:5000/challenges/web:v1",
        "exposePort": 8080,
        "flagTemplate": "flag{web_[TEAM_HASH]}",
        "cpuCount": 2,
        "memoryLimit": 256,
        "storageLimit": 512,
        "networkMode": "Isolated",
        "isEnabled": true,
        "originalScore": 500
      },
      {
        "externalId": "windows-ad-01",
        "title": "Windows Lab",
        "content": "通过远程桌面进入靶机。",
        "category": "Pentest",
        "type": "StaticContainer",
        "environment": "WindowsVM",
        "imageTemplateId": 42,
        "isEnabled": false,
        "originalScore": 1000,
        "flags": [
          { "flag": "flag{windows_lab}", "orderIndex": 0 }
        ]
      }
    ]
  }'
```

成功后的 operation `result`：

```json
{
  "gameId": 123,
  "imported": [
    { "externalId": "web-dynamic-01", "challengeId": 501 },
    { "externalId": "windows-ad-01", "challengeId": 502 }
  ],
  "deleted": [],
  "missing": []
}
```

客户端必须保存 `externalId -> challengeId` 映射，不要依赖题目标题定位资源。

### 4.5 分页查询

```bash
curl -H "Authorization: Bearer $GZCTF_TOKEN" \
  "https://platform.example/api/open/v1/games/123/challenges?limit=50"
```

```json
{
  "items": [
    {
      "id": 501,
      "title": "Dynamic Web",
      "category": "Web",
      "type": "DynamicContainer",
      "environment": "Docker",
      "isEnabled": true,
      "originalScore": 500
    }
  ],
  "nextCursor": "AAAB9Q"
}
```

下一页把 `nextCursor` 原样放入 `after`。游标是不可解释的稳定标识，客户端不得自行构造。

详情接口会返回完整题目内容和 Flag。Flag 属于敏感配置，只应在受控出题系统中处理，不应转发给选手端或写入日志。

### 4.6 删除题目

单题：

```bash
curl -X DELETE https://platform.example/api/open/v1/games/123/challenges/501 \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: delete-challenge-501-001"
```

批量：

```bash
curl -X POST https://platform.example/api/open/v1/games/123/challenges/batch-delete \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: delete-retired-set-001" \
  -H "Content-Type: application/json" \
  -d '{"challengeIds":[501,502,503]}'
```

删除任务会先停止该题所有运行环境和测试环境，再删除题目、Flag 和附件关系，最后刷新计分缓存。不存在的题目按幂等删除处理，放入 `result.missing`，不会让整个任务失败。

## 5. 镜像接口

| Method | 路径 | Scope | 说明 |
| --- | --- | --- | --- |
| `POST` | `/images/docker-references` | `images:write` | 注册内部或公开 Docker 引用 |
| `POST` | `/images/docker-archives` | `images:write` | 上传 Docker archive |
| `GET` | `/images/{imageTemplateId}` | `images:read` | 查询镜像模板 |
| `DELETE` | `/images/{imageTemplateId}` | `images:delete` | 删除未被引用的镜像模板 |

Docker 引用示例：

```bash
curl -X POST https://platform.example/api/open/v1/images/docker-references \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: image-web-v1-001" \
  -H "Content-Type: application/json" \
  -d '{
    "name":"web-lab-v1",
    "registryUrl":"10.24.0.28:5000/challenges/web:v1",
    "osType":"Linux"
  }'
```

允许的引用来源只有固定内部 Registry `10.24.0.28:5000`，或无需凭据且 DNS 全部解析到公网地址的公开 Registry。回环、链路本地、私网第三方 Registry 和携带 URL 凭据的引用会返回 `422 image_reference_forbidden`。

## 6. 推荐的自动化流程

1. 创建最小 scope、限定 `game:{id}` 的短期 Token。
2. 通过镜像 API 上传或注册运行镜像，轮询到 `Succeeded`。
3. 调用题目批量导入接口，为每题提供稳定 `externalId`。
4. 轮询 operation；成功后保存返回的 challenge ID 映射。
5. 使用分页和详情接口做导入后核对。
6. 比赛下线题目时调用删除接口并轮询完成。
7. 流水线结束后撤销 Token；不要复用长期全局 Token。

对 `429` 和暂时性 `503` 按响应头退避；对 `400/403/404/409/422` 修正请求或授权后再提交。不要用新 Idempotency-Key 盲目重试状态未知的写操作，应先查询原 operation。
