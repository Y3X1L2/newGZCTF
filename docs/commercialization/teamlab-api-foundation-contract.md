# TeamLab 外部基座 API 合约

版本：v1

生效阶段：Phase 3 退出时

基础路径：`/api/open/v1/teamlab`

## 1. 基座定位

TeamLab 是与比赛玩法无关的网络环境控制面，负责：

- 拓扑草稿；
- 拓扑校验；
- 不可变发布版本；
- 资源和分片计划；
- runtime 创建、状态、重置和销毁；
- WireGuard 访问授权；
- runtime 事件；
- 流量元数据和按需 PCAP。

TeamLab 不负责：

- 比赛参与关系；
- 题目分值；
- Flag 判定；
- Penetration 提交和排行榜；
- 课程、练习或 AWDP 业务状态。

平台内部模块通过 C# application contracts 调用 TeamLab；外部平台通过本文件定义的 HTTP API 调用。两种入口共享同一 application service、operation、部署队列和数据库事实，不能形成两套实现。

## 2. Phase 3 与后续阶段

| 阶段 | 对 TeamLab 的交付 | 不得改变 |
| --- | --- | --- |
| Phase 3 | 独立 topology/release/runtime 模型、外部 API、Penetration adapter、现有 Docker/Linux 部署纵向验收 | 本文件的资源身份、状态语义和错误模型 |
| Phase 4 | 索引、保留、归档、迁移和查询 SLA | API 资源结构 |
| Phase 5 | 流量聚合批写、Redis 缓冲、缓存失效 | Traffic query 语义 |
| Phase 6 | 多队伍队列、原子容量预留、能力协商、多节点调度 | Runtime create/operation 契约 |
| Phase 7 | 日志、metrics、trace、事实恢复、故障定位 | Event/operation 资源身份 |
| Phase 8 | VM 类型、Linux SSH、Windows RDP、访问端点 | Asset kind 与 access endpoint 扩展规则 |
| Phase 9 | Windows、多节点故障、全路径流量、PCAP、管理体验、容量验收 | 不重新引入 Game/Team 到 TeamLab 聚合根 |

Phase 3 退出时 API 已可供外部调用。Phase 9 提升能力覆盖和商业 SLI，不负责补做 Phase 3 的解耦。

## 3. 身份模型

### 3.1 公共 ID

- Topology、release、runtime、shard、access grant 和 capture job 对外使用 UUID；新建资源使用 UUIDv7，迁移资源可以使用数据库生成的 UUIDv4。
- 当前 `TeamLabRuntime.Id` 整数可以保留为节点资源名和数据库内部主键，但 API 只暴露新增 `PublicId`。
- RuntimeNetwork 和 RuntimeAsset 通过 runtime public ID 加 topology key 标识；TrafficFlow 通过 opaque cursor 查询。
- 外部调用不能依赖数据库整数 ID、GameId、TeamId、WorkerNodeId 或 Linux resource name。

### 3.2 所有权

- topology 记录 `OwnerUserId`，由 API token 的 creator user 决定。
- release 继承 topology 所有权，发布后不可修改。
- runtime 记录 `CreatedById`、`TopologyReleaseId` 和可选 `ExternalReference`。
- `(CreatedById, ExternalReference)` 在 ExternalReference 非空时唯一；相同 request hash 的重复 create 返回既有 runtime，不同 request hash 返回 `external_reference_conflict`。
- Penetration 的 Game/Team 关系只存在于 `PenetrationGameLabBinding` 和 `PenetrationTeamRuntimeBinding`。
- 每个 runtime network 持有独立 `TeamLabNetworkLease`；所有未释放 lease 的 CIDR 在整套 TeamLab Fabric 内不得重叠，销毁完成后才写入 ReleasedAt。

## 4. 拓扑模型

### 4.1 请求结构

```json
{
  "name": "enterprise-lab",
  "revision": 7,
  "networks": [
    {
      "key": "entry",
      "name": "Entry",
      "addressPool": { "poolCidr": "10.40.0.0/16", "runtimePrefixLength": 24 },
      "isEntry": true
    },
    {
      "key": "core",
      "name": "Core",
      "addressPool": { "poolCidr": "192.168.0.0/16", "runtimePrefixLength": 24 },
      "isEntry": false
    }
  ],
  "assets": [
    {
      "key": "jump-host",
      "name": "Jump Host",
      "kind": "Docker",
      "imageTemplateId": 42,
      "resources": { "cpuUnits": 10, "memoryMiB": 512, "storageMiB": 2048 },
      "interfaces": [
        { "key": "eth0", "networkKey": "entry", "hostOffset": 10, "primary": true },
        { "key": "eth1", "networkKey": "core", "hostOffset": 10, "primary": false }
      ],
      "routingEnabled": true,
      "healthCheck": { "kind": "Tcp", "port": 22 }
    }
  ],
  "connections": [
    { "key": "entry-to-core", "fromNetworkKey": "entry", "toNetworkKey": "core", "viaAssetKey": "jump-host" }
  ]
}
```

### 4.2 拓扑规则

- `key` 在所属 topology 内唯一，格式为 `[a-z][a-z0-9-]{0,62}`。
- address pool 只允许 RFC1918 IPv4，同一 topology 的 pool 不能重叠。
- `runtimePrefixLength` 必须比 pool prefix 更具体且至少产生一个可用子网；批量部署计划根据目标 runtime 数验证剩余 lease 容量。
- topology 保存地址分配意图，不保存某个 runtime 的实际 CIDR；实际 CIDR 通过数据库唯一 lease 分配并写入 `TeamLabRuntimeNetwork`。
- 一个 topology 至少有一个 entry network，首版只允许一个 player entry network。
- asset 至少有一张网卡；每张网卡引用已存在 network。
- interface 使用 `hostOffset`，不能占用 network、broadcast、gateway、DHCP/DNS 或 WireGuard 保留偏移；实际 IP 由 runtime CIDR 和 hostOffset 计算。
- Docker 和 VM 都通过 `imageTemplateId` 引用 Content 资产。
- connection 只表达 L3 可达路径，不接受 protocol、port range、allow/deny ACL 或 UI 坐标。
- `viaAssetKey` 必须引用连接两个目标网段并启用 routing 的 asset。
- topology 不包含 Flag、分值、前置目标、玩家提示或比赛 ID。

### 4.3 草稿并发

- PUT 必须携带当前 `revision`。
- revision 不匹配返回 `409 topology_revision_conflict` 和当前 revision。
- 保存成功 revision 原子加一。
- validate 不修改草稿。

## 5. 不可变 release

发布流程：

1. 加载指定 topology revision；
2. 执行结构、镜像、地址、路由和能力校验；
3. 生成 canonical JSON；
4. 计算 SHA-256 content hash；
5. 写入 `TeamLabTopologyRelease`；
6. 返回 release public ID 和 version。

release 字段：

```json
{
  "id": "019...",
  "topologyId": "019...",
  "version": 3,
  "schemaVersion": 1,
  "contentHash": "sha256:...",
  "publishedBy": "019...",
  "publishedAt": "2026-07-10T00:00:00Z"
}
```

相同 topology revision 和 content hash 重复发布返回原 release，不创建重复版本。runtime 始终引用 release ID；草稿修改不会改变已存在 runtime。
历史迁移 release 的 `publishedBy` 可以为 null；通过 v1 新发布时必须等于当前 actor user ID。

## 6. Runtime 请求与 overlay

```json
{
  "releaseId": "019...",
  "externalReference": "customer-match-2026-001-team-17",
  "constraints": {
    "preferredRegion": null,
    "requiredCapabilities": ["docker", "teamlab-fabric"]
  },
  "overlays": [
    {
      "assetKey": "jump-host",
      "environment": { "LAB_MODE": "competition" },
      "secrets": { "FLAG": "flag{runtime-value}" }
    }
  ]
}
```

- environment key 必须满足 `[A-Z_][A-Z0-9_]{0,63}`。
- secret value 使用持久化 Data Protection key ring 和独立 purpose 加密后短期保存；全部 shard 确认注入后清除密文，只保留 payload hash 和消费时间，不进入 release、operation payload、queue ticket、event detail、日志或查询响应。
- Penetration adapter 通过 overlay 注入动态 Flag；TeamLab 不理解 Flag 语义。
- create 返回 `202 Accepted` 和 Phase 1 `ApiOperation`。
- operation 关联唯一 `DeploymentQueueTicket`；队列 active identity 以内部 runtime ID 为核心，不依赖 Game/Team。

## 7. Runtime 状态

```text
Pending -> Planning -> Scheduled -> Deploying -> Probing -> Running
                                      |             |
                                      +-----------> Failed
Running -> Destroying -> Destroyed
Failed  -> CleanupPending -> Destroying -> Destroyed
```

- reset 保持 runtime public ID 和 external reference 不变，但必须先清理当前 generation 的全部节点资源，再基于同一 release 创建下一 generation；禁止原地复用未知节点资源。
- runtime generation 单调增加；shard、network、asset、grant、event、flow 和 capture facts 带 generation。查询 projection 默认只返回当前 generation，历史事实保留审计，访问授权和旧 resource ID 在 reset 后失效。
- `Stopped` 只用于平台明确支持可恢复暂停的资产；不能把已清理 runtime 标记为 Stopped。
- runtime 聚合状态由 shard 和关键基础设施事实计算，不能只读取单个 WorkerNode。

Runtime response：

```json
{
  "id": "019...",
  "releaseId": "019...",
  "generation": 1,
  "status": "Running",
  "stage": "ready",
  "openForAccess": true,
  "shards": [
    {
      "id": "019...",
      "status": "Running",
      "networkKeys": ["entry"],
      "assetKeys": ["jump-host"]
    }
  ],
  "createdAt": "2026-07-10T00:00:00Z",
  "updatedAt": "2026-07-10T00:03:00Z",
  "error": null
}
```

外部 response 不暴露 WorkerNode 内网地址、Agent token、bridge name、namespace name 或宿主机路径。

## 8. 资源和分片计划

`POST /topologies/{topologyId}/releases/{releaseId}/plan` 只生成计划，不预留资源、不创建 runtime。

Plan response 包含：

- 规范化 networks 和 assets；
- 每类能力需求；
- Docker/VM slot 数；
- shard 数和每个 shard 的 network/asset key；
- 跨 shard connection 数；
- 地址分配摘要；
- capability warnings；
- plan hash。

外部调用不能指定 WorkerNode ID。管理员内部 API 可以在故障诊断时提供受控节点约束，但该约束不进入公开 v1。

plan preview 返回候选 CIDR，不写 lease；create runtime 在数据库 transaction 中分配 lease。PostgreSQL 使用 `cidr` 列和 GiST exclusion constraint 阻止 active CIDR 重叠，并发冲突时重试下一个子网；不能只依赖进程内计数或 runtime ID 取模。

## 9. Access grant

```text
POST   /runtimes/{runtimeId}/access-grants
GET    /runtimes/{runtimeId}/access-grants/{grantId}
DELETE /runtimes/{runtimeId}/access-grants/{grantId}
```

- 首版 grant type 为 WireGuard。
- 创建响应可以返回一次性配置下载 URL，URL 短时有效且只能使用一次。
- 私钥不在普通 runtime query 中返回。
- runtime reset/destroy 自动撤销全部 grant。
- Penetration 选手入口由 Penetration adapter 创建 grant，并执行比赛参与权限检查。

## 10. Traffic 与 PCAP

```text
GET    /runtimes/{runtimeId}/traffic/flows?after={cursor}&limit=100
POST   /runtimes/{runtimeId}/captures
GET    /runtimes/{runtimeId}/captures/{captureId}
POST   /runtimes/{runtimeId}/captures/{captureId}/stop
GET    /runtimes/{runtimeId}/captures/{captureId}/download
```

- flow 返回聚合后的五元组、方向、字节数、包数、firstSeen、lastSeen、shard public ID 和 network topology key。
- capture 请求必须指定 scope、maxSeconds、maxBytes 和 expiresInSeconds，服务端应用更严格上限。
- capture 下载需要 `teamlab.capture:read` 和对应 runtime grant。
- Phase 3 保持当前可用采集能力；Phase 5 完成批写和保留，Phase 9 完成全部关键抓包点和协议验收。

## 11. Endpoint 清单

```text
GET    /capabilities
POST   /topologies
GET    /topologies
GET    /topologies/{topologyId}
PUT    /topologies/{topologyId}
DELETE /topologies/{topologyId}
POST   /topologies/{topologyId}/validate
POST   /topologies/{topologyId}/releases
GET    /topologies/{topologyId}/releases
GET    /topologies/{topologyId}/releases/{releaseId}
POST   /topologies/{topologyId}/releases/{releaseId}/plan
POST   /runtimes
GET    /runtimes/{runtimeId}
POST   /runtimes/{runtimeId}/reset
DELETE /runtimes/{runtimeId}
GET    /runtimes/{runtimeId}/events
POST   /runtimes/{runtimeId}/access-grants
GET    /runtimes/{runtimeId}/traffic/flows
POST   /runtimes/{runtimeId}/captures
GET    /runtimes/{runtimeId}/captures/{captureId}
POST   /runtimes/{runtimeId}/captures/{captureId}/stop
GET    /runtimes/{runtimeId}/captures/{captureId}/download
```

所有写接口使用 Idempotency-Key；异步接口返回 Phase 1 operation。

## 12. Capabilities

`GET /capabilities` 返回平台当前可接受的契约能力，而不是单个硬编码协议阈值：

```json
{
  "apiVersion": "v1",
  "topologySchemaVersions": [1],
  "assetKinds": ["Docker", "Vm"],
  "networkModel": "L3RoutedFabric",
  "features": {
    "multiNode": true,
    "linuxVm": true,
    "windowsVm": false,
    "trafficFlows": true,
    "onDemandPcap": true
  },
  "limits": {
    "networksPerTopology": 32,
    "assetsPerTopology": 128,
    "interfacesPerAsset": 8
  }
}
```

Phase 9 可以把 `windowsVm` 改为 true，不需要发布 v2。删除 asset kind、改变 network model 或收紧已发布 topology schema 需要新 API 主版本。

## 13. 稳定错误码

| Code | HTTP | 含义 |
| --- | --- | --- |
| `topology_invalid` | 422 | topology 结构或语义无效。 |
| `topology_revision_conflict` | 409 | 草稿 revision 已变化。 |
| `release_immutable` | 409 | 尝试修改 release。 |
| `image_template_unavailable` | 422 | 模板不存在、无权限或未 Ready。 |
| `address_pool_exhausted` | 409 | 至少一个 topology network 没有可分配的 runtime 子网。 |
| `capability_unavailable` | 409 | 当前节点集合不满足计划能力。 |
| `external_reference_conflict` | 409 | 同一 creator 的 external reference 已用于不同请求。 |
| `runtime_not_ready` | 409 | 当前状态不允许访问或抓包。 |
| `runtime_cleanup_pending` | 409 | 清理未完成，不能创建下一 generation。 |
| `capture_limit_exceeded` | 422 | 抓包时间、大小或并发超过上限。 |
| `operation_failed` | 500 | operation 执行失败，detail 已脱敏。 |

## 14. 外部基座验收

Phase 3 必须在没有 Penetration Game/Team 实体参与的测试中完成：

1. 使用 scoped token 创建 topology；
2. 保存混合 RFC1918 网段；
3. validate 并发布 release；
4. plan 返回 Docker/Linux VM 资源摘要；
5. 创建 Docker runtime 并轮询 operation 到 Succeeded；
6. 获取 WireGuard access grant；
7. 访问 entry 服务并通过已发布 connection 到下一网段；
8. 查询 events 和 traffic flow；
9. destroy runtime；
10. 确认部署队列、bridge、namespace、容器、VM、WireGuard 和临时文件无残留。

随后运行 Penetration adapter 验收，确认 Game/Team 只存在于 binding 和玩法层，TeamLab 数据表与 API response 不依赖 GameId/TeamId。
