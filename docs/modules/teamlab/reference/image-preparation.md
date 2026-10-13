# TeamLab：发布镜像准备与引用释放

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

## 5. 镜像准备

镜像准备接口围绕“某个 release 在可调度节点上是否已就绪”。

### 5.1 查询镜像准备状态

```http
GET /api/open/v1/teamlab/preparations/releases/{releaseId}
Authorization: Bearer <token>
Scope: teamlab.runtimes:read
```

响应 `200 OK`：

```json
{
  "releaseId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "state": "readyToStart",
  "planAvailable": true,
  "readyToStart": true,
  "blockers": [],
  "images": [
    {
      "templateId": 116,
      "templateName": "modbus-slave",
      "imageType": "Docker",
      "eligibleNodeCount": 2,
      "readyNodeCount": 2,
      "preparingNodeCount": 0,
      "failedNodeCount": 0,
      "failure": null
    }
  ]
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `releaseId` | guid | 发布版本 |
| `state` | string | `onDemand` / `readyToStart` / `blocked` |
| `planAvailable` | bool | 存在适配节点能力，不代表已预留容量 |
| `readyToStart` | bool | 是否允许进入部署，包括选中节点后的按需下载 |
| `blockers` | string[] | 阻断原因列表 |
| `images[]` | array | 每个镜像模板的就绪统计 |
| `images[].templateId` | int | 镜像模板 ID |
| `images[].templateName` | string | 模板名称 |
| `images[].imageType` | string | 镜像类型 |
| `images[].eligibleNodeCount` | int | 符合条件节点数 |
| `images[].readyNodeCount` | int | 已就绪节点数 |
| `images[].preparingNodeCount` | int | 准备中节点数 |
| `images[].failedNodeCount` | int | 失败节点数 |
| `images[].failure` | object | 失败详情或 null |

`onDemand` 表示模板/摘要/节点能力允许创建，但仍有模板没有就绪节点缓存；`readyToStart` 表示每个模板已有至少一个适配节点缓存，仍须核对本次选定节点。`blocked` 表示模板、摘要或能力阻碍创建。旧的 `planAvailable`、`preparing` state 不由当前聚合返回，调用方应容忍新增字符串状态。

普通发布和创建不自动预热全部节点；创建选中节点后才准备其缺少的镜像。显式 POST 预热为全部适配节点排队，DELETE 只撤销该 release 的预热引用，保留其他消费者和实际 backing 文件需要的缓存。源码为 [TeamLabReleaseImagePreparationService](../../../../src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseImagePreparationService.cs)。

### 5.2 提交镜像准备

```http
POST /api/open/v1/teamlab/preparations/releases/{releaseId}
Authorization: Bearer <token>
Idempotency-Key: prep-001
Scope: teamlab.runtimes:write
```

无请求体。响应 `202 Accepted` + operation。幂等，适合发布后显式准备或失败后重试。

### 5.3 释放镜像准备引用

```http
DELETE /api/open/v1/teamlab/preparations/releases/{releaseId}
Authorization: Bearer <token>
Idempotency-Key: prep-release-001
Scope: teamlab.runtimes:write
```

无请求体。响应 `202 Accepted` + operation。幂等，停止继续保留准备状态。

---
