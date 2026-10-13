# TeamLab：端到端调用示例

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

## 14. 端到端操作示例

以下示例按“创建场景 → 校验 → 发布 → 创建运行时 → 访问 → 链路策略 → 抓包 → 销毁”的完整顺序，可直接用 curl 调用。假设：

- Base URL：`https://your-platform.example.com/api/open/v1`
- Token：`$GZCTF_TOKEN`
- 每个写操作使用独立 `Idempotency-Key`。

### 14.1 查能力和控制范围

```bash
curl -s "$BASE/teamlab/capabilities" \
  -H "Authorization: Bearer $GZCTF_TOKEN"

curl -s "$BASE/teamlab/scopes" \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

### 14.2 创建拓扑

```bash
curl -s -X POST "$BASE/teamlab/topologies" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-topology-001" \
  -d '{
    "name": "two-net-demo",
    "schemaVersion": 2,
    "networks": [
      { "key": "net-entry", "name": "入口网段", "addressPool": { "poolCidr": "10.80.1.0/24", "runtimePrefixLength": 24 }, "isEntry": true, "orderIndex": 0 },
      { "key": "net-core", "name": "核心网段", "addressPool": { "poolCidr": "10.80.2.0/24", "runtimePrefixLength": 24 }, "isEntry": false, "orderIndex": 1 }
    ],
    "assets": [
      { "key": "plc", "name": "PLC", "kind": 0, "imageTemplateId": 116, "resources": { "cpuUnits": 1, "memoryMiB": 256, "storageMiB": 256 },
        "interfaces": [ { "key": "plc-eth0", "networkKey": "net-entry", "hostOffset": 10, "primary": true, "orderIndex": 0 } ],
        "exposePort": 502 },
      { "key": "scada", "name": "SCADA", "kind": 0, "imageTemplateId": 117, "resources": { "cpuUnits": 1, "memoryMiB": 256, "storageMiB": 256 },
        "interfaces": [ { "key": "scada-eth0", "networkKey": "net-core", "hostOffset": 20, "primary": true, "orderIndex": 0 } ] }
    ],
    "connections": [
      { "key": "route-entry-core", "fromNetworkKey": "net-entry", "toNetworkKey": "net-core", "direction": 1 }
    ]
  }'
```

响应 `202` 后解析 `operation.id`：

```bash
OPERATION_ID=<从响应读取>
curl -s "$BASE/operations/$OPERATION_ID" \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

操作完成后取得 `topologyId`。

### 14.3 校验并发布

```bash
curl -s -X POST "$BASE/teamlab/topologies/$TOPOLOGY_ID/validate" \
  -H "Authorization: Bearer $GZCTF_TOKEN"

curl -s -X POST "$BASE/teamlab/topologies/$TOPOLOGY_ID/releases" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-release-001" \
  -d '{ "revision": 1 }'
```

轮询 operation 后取得 `releaseId`。

### 14.4 镜像准备并创建运行时

```bash
curl -s -X POST "$BASE/teamlab/preparations/releases/$RELEASE_ID" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: demo-prep-001"

curl -s -X POST "$BASE/teamlab/runtimes" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-runtime-001" \
  -d '{
    "releaseId": "'"$RELEASE_ID"'",
    "externalReference": "demo-instance"
  }'
```

轮询取得 `runtimeId`。

### 14.5 查询运行时、事件、流量

```bash
curl -s "$BASE/teamlab/runtimes/$RUNTIME_ID" \
  -H "Authorization: Bearer $GZCTF_TOKEN"

curl -s "$BASE/teamlab/runtimes/$RUNTIME_ID/events" \
  -H "Authorization: Bearer $GZCTF_TOKEN"

curl -s "$BASE/teamlab/runtimes/$RUNTIME_ID/traffic/flows" \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

### 14.6 创建 WireGuard 授权并下载

```bash
curl -s -X POST "$BASE/teamlab/runtimes/$RUNTIME_ID/access-grants" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-grant-001" \
  -d '{ "type": "WireGuard" }'
```

轮询后取得 `grantId` 和一次性下载 `token`，然后：

```bash
curl -s -o wg.conf "$BASE/teamlab/runtimes/$RUNTIME_ID/access-grants/$GRANT_ID/download?token=$TOKEN" \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

### 14.7 应用链路策略

```bash
curl -s -X POST "$BASE/teamlab/link-policies" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "runtimeId": "'"$RUNTIME_ID"'",
    "networkKey": "net-entry",
    "kind": "packet-loss",
    "parameters": { "lossPercent": 40 }
  }'
```

### 14.8 抓包并下载

```bash
curl -s -X POST "$BASE/teamlab/runtimes/$RUNTIME_ID/captures" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-capture-001" \
  -d '{
    "scope": "network",
    "networkKey": "net-entry",
    "maxSeconds": 60,
    "maxBytes": 536870912,
    "expiresInSeconds": 3600
  }'
```

轮询后取得 `captureId`，完成后下载：

```bash
curl -s -o capture.tar "$BASE/teamlab/runtimes/$RUNTIME_ID/captures/$CAPTURE_ID/download" \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

### 14.9 销毁运行时

```bash
curl -s -X DELETE "$BASE/teamlab/runtimes/$RUNTIME_ID" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: demo-destroy-001"
```

---
