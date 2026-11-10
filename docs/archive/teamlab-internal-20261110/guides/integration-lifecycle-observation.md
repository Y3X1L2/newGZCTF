# TeamLab 对接：生命周期、观测、访问与批量通知

完整调用顺序见 [对接入口](integration.md)，认证与幂等要求见 [调用约定](integration-authentication.md)。本文说明实现契约，现场验收范围见 [当前状态](../../../current-state.md)。

### 3.11 暂停、恢复、重置和销毁

| 动作 | 接口 | 请求体 |
| --- | --- | --- |
| 暂停 | `POST /runtimes/{runtimeId}/pause` | 无 |
| 恢复 | `POST /runtimes/{runtimeId}/resume` | 无 |
| 重置 | `POST /runtimes/{runtimeId}/reset` | `ResetTeamLabRuntimeModel` |
| 销毁 | `DELETE /runtimes/{runtimeId}` | 无 |

四个接口都携带 `Idempotency-Key`，并返回 `ApiOperationModel`。

重置请求：

```json
{
  "overlays": [
    {
      "assetKey": "web",
      "secrets": {
        "FLAG": "<新代次实例值>"
      }
    }
  ],
  "releaseId": null
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `overlays` | `TeamLabRuntimeOverlayModel[]` / null | 新代次使用的资产机密 |
| `releaseId` | UUID / null | 空值沿用当前来源版本；填写后按指定发布版本重置 |

暂停保留运行身份、网络、磁盘和容量记录。恢复在原节点继续运行。重置清理当前代并重新部署，`generation` 增加 1。销毁操作成功且运行状态为 `Destroyed` 后，该环境的资产、网络、服务映射、抓包和容量租约均已进入清理终态。

## 4. 查询和辅助能力

### 4.1 运行事件与状态检查

| 接口 | 返回内容 |
| --- | --- |
| `GET /runtimes/{runtimeId}/events?after=&limit=50&generation=&stage=` | 分页运行事件 |
| `GET /runtimes/{runtimeId}/device-health` | 各资产最近一次设备健康结果 |
| `GET /runtimes/{runtimeId}/status-check` | 数据库运行事实与现场资产状态的差异 |

运行失败时，先读取 operation 的 `errorCode`、`errorDetail` 和 `stage`，再读取运行事件。状态检查返回差异；恢复动作调用单资产控制、重置或销毁接口。

### 4.2 链路策略

```http
GET  /api/open/v1/teamlab/link-policies?runtimeId={runtimeId}&status=active
POST /api/open/v1/teamlab/link-policies
POST /api/open/v1/teamlab/link-policies/{policyId}/recover
```

策略类型：`access-rule`、`bandwidth-limit`、`latency`、`jitter`、`packet-loss`、`duplication`、`link-break`。应用接口返回策略记录；恢复接口把对应网卡恢复到应用前状态。

### 4.3 流量和抓包

| 能力 | 接口 |
| --- | --- |
| 流量记录 | `GET /runtimes/{runtimeId}/traffic/flows` |
| 通信路径列表 | `GET /runtimes/{runtimeId}/traffic/paths` |
| 通信路径详情 | `GET /runtimes/{runtimeId}/traffic/paths/{pathId}` |
| 开始抓包 | `POST /runtimes/{runtimeId}/captures` |
| 抓包列表 | `GET /runtimes/{runtimeId}/captures` |
| 抓包状态 | `GET /runtimes/{runtimeId}/captures/{captureId}` |
| 停止抓包 | `POST /runtimes/{runtimeId}/captures/{captureId}/stop` |
| 下载抓包 | `GET /runtimes/{runtimeId}/captures/{captureId}/download` |

常用抓包请求：

```json
{
  "scope": "network",
  "networkKey": "office",
  "maxSeconds": 60,
  "maxBytes": 104857600,
  "expiresInSeconds": 3600
}
```

跨节点抓包会生成多个分段。下载接口返回 tar 流，包含 manifest 和各节点分段文件。

### 4.4 WireGuard 访问

| 动作 | 接口 |
| --- | --- |
| 创建访问授权 | `POST /runtimes/{runtimeId}/access-grants` |
| 查询授权 | `GET /runtimes/{runtimeId}/access-grants` |
| 下载配置 | `GET /runtimes/{runtimeId}/access-grants/{grantId}/download` |
| 撤销授权 | `DELETE /runtimes/{runtimeId}/access-grants/{grantId}` |

服务开放用于发布一个具体 TCP/UDP 服务；WireGuard 访问授权用于让运维人员进入运行环境网段。

### 4.5 现场连接器

| 动作 | 接口 |
| --- | --- |
| 查询连接器 | `GET /connectors`、`GET /connectors/{connectorId}` |
| 登记和修改 | `POST /connectors`、`PUT /connectors/{connectorId}` |
| 读取节点网卡 | `GET /connectors/nodes/{nodeId}/interfaces` |
| 查询现场状态 | `GET /connectors/{connectorId}/health` |
| 占用和释放 | `POST /connectors/{connectorId}/leases`、`POST /connectors/{connectorId}/leases/release` |
| 归档 | `POST /connectors/{connectorId}/archive` |

### 4.6 批量编排和通知

Rollout 用于对一组独立运行环境统一执行准备、开放、暂停、恢复和清理：

```text
GET    /rollouts
POST   /rollouts
GET    /rollouts/{rolloutId}
GET    /rollouts/{rolloutId}/targets
PUT    /rollouts/{rolloutId}/targets
POST   /rollouts/{rolloutId}/prepare
POST   /rollouts/{rolloutId}/open-access
POST   /rollouts/{rolloutId}/close-access
POST   /rollouts/{rolloutId}/pause
POST   /rollouts/{rolloutId}/resume
POST   /rollouts/{rolloutId}/drain
POST   /rollouts/{rolloutId}/archive
```

Webhook 接收 operation 和运行资源事件：

```text
GET    /webhooks
POST   /webhooks
GET    /webhooks/{webhookId}
DELETE /webhooks/{webhookId}
POST   /webhooks/{webhookId}/replay
```

创建 Webhook 时返回签名 secret。接收方按事件 ID 去重，收到事件后使用 operation 或运行状态接口读取最新状态。
