# TeamLab：设备包与现场连接器

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

## 9. 连接器与设备包

<a id="90-本地在研执行协议2026-09-08未发布"></a>

### 9.0 执行协议与验收边界

设备包目录返回 `id`（公共 UUID）和 `bindingId`（拓扑现行数字绑定值）。编辑器使用 `bindingId` 保存绑定，选择设备包时按制品摘要匹配同类型镜像，并提高资源配置至设备包最低要求；没有匹配镜像时明确提示先导入镜像。目录中的 `memoryMib` 与 Unix 毫秒时间在 feature adapter 内统一转换，不在页面里猜测字段名或时间格式。

发布和运行规划均校验设备包制品摘要、镜像类型、最低资源和参数 schema。schema 只允许包内引用。常见字符串、数字、整数、布尔值和枚举提供参数表单；复杂声明和导入的无效 JSON 保留高级编辑入口，不静默丢弃草稿。

设备包的程序必须实现以下参数消费协议，不能把参数已传输等同于设备业务已执行：

- Docker：读取环境变量 `GZCTF_DEVICE_PARAMETERS`，内容是经校验的 JSON 对象。
- VM：读取 QEMU fw_cfg 的 `opt/org.gzctf/device-parameters`。Linux 支持相应驱动时可从 `/sys/firmware/qemu_fw_cfg/by_name/opt/org.gzctf/device-parameters/raw` 读取；其他来宾需设备程序自行适配。
- 参数仅用于公开的设备配置，不填写密码或令牌。参数和设备包身份进入不可变执行快照，重建复用该快照。
- 声明的 TCP/HTTP 健康检查进入现有启动就绪检查；目前尚不能把 `intervalSeconds` 或 `protocolEventTypes` 的目录声明视作持续健康监督或协议事件采集已实现。

本地已验证真实 libvirt 接受 fw_cfg 配置并转换为 QEMU 参数；尚未完成真实设备程序在 Docker/VM 中读取参数、执行业务和持续健康/协议事件验收。连接器的真实 provider 生命周期仍待补，目录与租约不代表已经接通物理设备。

### 9.0.1 运行检查与 NAT 修复边界

运行详情“资产运维”新增“运行状态检查”：主动读取节点 inventory，展示资产期望状态、实际状态、缺失、身份冲突和节点不可查询状态。检查不写数据库、不回收资源；可处理的差异需要确认，提交前重新检查，再进入原资产操作队列，结果仍在资产任务区查看。网络/连接器差异与旧代残留的显式清理尚未覆盖。

NAT 执行改为按 runtime、代次、网络摘要和网关端口定位 OVN 路由器，使用 V2 的 `OvnNorthboundEndpoint`；禁止采用“第一个路由器”或写死的测试节点。DNAT 端口入口通过 OVN VIP load balancer 实现，避免同端口映射错误地开放整个地址。映射携带原网络归属，并纳入执行计划的清理流程。

链路策略新增 nullable `Generation`。旧行保持未知代次，不能自动套用当前代；需要按原执行记录核实。定时恢复和销毁后的策略清理必须实际请求执行端，失败保留可见状态和重试资格，不再只更新数据库为“已恢复”。完整的端口分配、并发冲突管理、可达探测与服务入口产品链路仍待完成。真实 OVN NB 验证只证明控制面规则与精确清理，不证明实际网络流量可达。

连接器和设备包用于把现场真实/半实物设备接入网络场景。外部系统只读写目录与租约，不接触设备接入地址。

### 9.1 设备包

#### 列出设备包

```http
GET /api/open/v1/teamlab/device-packages?name=&limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.device-packages:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `name` | string | 否 | 按名称过滤 |
| `limit` | int | 否 | 默认 50 |
| `after` | string | 否 | 游标 |

响应：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "modbus-slave",
      "displayName": "MODBUS 从站模拟器",
      "version": "1.0.0",
      "artifactKind": "oci-image",
      "artifactReference": "10.0.7.118:5000/labs/modbus-slave:v1",
      "digest": "sha256:...",
      "description": "MODBUS TCP 从站",
      "supportedAssetKinds": ["docker"],
      "cpuMillis": 1000,
      "memoryMib": 256,
      "storageGib": 1,
      "ports": [
        {
          "name": "modbus",
          "port": 502,
          "protocol": "tcp"
        }
      ],
      "parameterSchema": null,
      "healthDeclaration": null,
      "protocolEventTypes": ["modbus.read", "modbus.write"],
      "enabled": true,
      "archived": false,
      "createdAt": "2026-08-19T10:00:00+00:00",
      "updatedAt": "2026-08-19T10:00:00+00:00"
    }
  ],
  "next": null
}
```

注意列表分页字段是 `next`（不是 `nextCursor`）。

#### 获取设备包

```http
GET /api/open/v1/teamlab/device-packages/{packageId}
Authorization: Bearer <token>
Scope: teamlab.device-packages:read
```

响应结构同上单项。

### 9.2 连接器

#### 列出连接器

```http
GET /api/open/v1/teamlab/connectors?scopeId={scopeId}&limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.connectors:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `scopeId` | guid | 否 | 控制范围过滤；不传返回平台级+已授权范围 |
| `limit` | int | 否 | 默认 50 |
| `after` | string | 否 | 游标 |

响应：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "sim-plc",
      "displayName": "模拟 PLC",
      "kind": "managed-nic",
      "controlScopeId": null,
      "supportsSharedUse": false,
      "capacity": 1,
      "occupiedSlots": 1,
      "activeLeases": [
        {
          "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
          "connectorId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
          "runtimeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
          "slot": 1,
          "acquiredAt": "2026-08-19T10:00:00+00:00",
          "releasedAt": null,
          "releaseReason": "none"
        }
      ],
      "health": "healthy",
      "healthObservedAt": "2026-08-19T10:00:00+00:00",
      "description": null,
      "archived": false,
      "createdAt": "2026-08-19T10:00:00+00:00",
      "updatedAt": "2026-08-19T10:00:00+00:00"
    }
  ],
  "next": null
}
```

`kind` 取值：`managed-nic`、`vlan`、`segment`、`serial`、`usb-gateway`、`dedicated-network`。
`health` 取值：`unknown`、`healthy`、`degraded`、`unreachable`。
`releaseReason` 取值：`none`、`manual-release`、`runtime-destroyed`、`admin-revoked`、`node-lost`。

#### 获取连接器

```http
GET /api/open/v1/teamlab/connectors/{connectorId}?scopeId={scopeId}
Authorization: Bearer <token>
Scope: teamlab.connectors:read
```

#### 占用连接器

```http
POST /api/open/v1/teamlab/connectors/{connectorId}/leases
Authorization: Bearer <token>
Content-Type: application/json
Scope: teamlab.connectors:write
```

请求体：

```json
{
  "runtimeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

独占连接器同一时间只属于一个运行时；重复申请幂等返回。成功 `201 Created`，响应为租约对象。

#### 释放连接器

```http
POST /api/open/v1/teamlab/connectors/{connectorId}/leases/release
Authorization: Bearer <token>
Content-Type: application/json
Scope: teamlab.connectors:write
```

请求体同上。重复释放幂等。成功 `200 OK`，响应为租约对象。

---
