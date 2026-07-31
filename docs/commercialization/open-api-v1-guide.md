# YINYU 平台 Open API v1 使用指南

本文说明外部系统调用 `/api/open/v1` 的稳定契约。机器可读契约以
`docs/commercialization/openapi/open-v1.json` 为准；部署后可通过 `/api-docs/`
查看中文 Swagger 页面，通过 `/openapi/open-v1.json` 获取实时 JSON。

## 1. 身份认证

所有接口使用 Bearer API Token：

```http
Authorization: Bearer gzctf_pat_...
```

Token 必须包含接口要求的 scope。资源授权可进一步限制到具体比赛、镜像或
TeamLab 资源。平台不会因为调用者拥有写 scope 而绕过资源授权。

## 2. 幂等异步操作

所有异步写接口必须携带：

```http
Idempotency-Key: caller-stable-operation-id
```

服务返回 `202 Accepted` 和 `ApiOperation`。相同 Token、路由、
`Idempotency-Key` 与请求内容会复用同一 operation；相同 key 配合不同内容返回
`409 idempotency_conflict`。

轮询操作：

```bash
curl https://platform.example/api/open/v1/operations/$OPERATION_ID \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

终态为 `Succeeded`、`Failed` 或 `Cancelled`。调用方应展示 `stage`、进度和
`errorCode/errorDetail`，不要通过更换幂等键盲目重试未知状态的写操作。

## 3. 统一错误

错误响应使用 `application/problem+json`，包含稳定 `code` 与 `traceId`。

| 状态码 | 含义 |
| --- | --- |
| `400` | 请求格式、摘要或字段非法 |
| `401` | Token 缺失、失效或过期 |
| `403` | scope 或资源授权不足 |
| `404` | 资源不存在，或调用方不可见 |
| `409` | 幂等冲突、资源占用或状态冲突 |
| `422` | 业务契约不成立 |
| `429` | 调用频率超限 |
| `503` | 依赖暂时不可用 |

对 `429` 和可重试 `503` 按响应头退避；对 `400/403/404/409/422` 修正请求或
状态后再提交。

## 4. 镜像 API

所需 scope：读取 `images:read`，导入和认证 `images:write`，删除
`images:delete`。

### 4.1 Docker 镜像

注册 Registry 引用：

```bash
curl -X POST https://platform.example/api/open/v1/images/docker-references \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: docker-web-v1" \
  -H "Content-Type: application/json" \
  -d '{"name":"web-v1","registryUrl":"10.24.0.28:5000/labs/web:v1","osType":"Linux"}'
```

上传 Docker archive：

```bash
curl -X POST https://platform.example/api/open/v1/images/docker-archives \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: docker-web-archive-v1" \
  -F "file=@web-image.tar;type=application/x-tar" \
  -F "name=web-v1" \
  -F "sourceImage=web:v1" \
  -F "osType=Linux" \
  -F "expectedDigest=$ARCHIVE_SHA256"
```

平台将镜像统一解析到内部 Registry，并异步预分发到可调度 Docker 节点。

### 4.2 VM qcow2 导入

平台不在线安装操作系统，也不接收 Windows 管理员凭据。外部 CI/Image Factory
负责生成 qcow2；平台只负责摘要校验、OCI 主副本、认证、分发和运行。

```bash
curl -X POST https://platform.example/api/open/v1/images/vm-qcow2 \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: win2022-managed-candidate-v1" \
  -F "file=@windows-server-2022.qcow2;type=application/octet-stream" \
  -F "name=Windows Server 2022 Managed Candidate" \
  -F "osType=Windows" \
  -F "networkMode=Dhcp" \
  -F "expectedDigest=$QCOW2_SHA256"
```

字段说明：

| 字段 | 说明 |
| --- | --- |
| `file` | 必须为 `.qcow2`，最大 120 GiB |
| `name` | 当前所有者下唯一的模板名称 |
| `osType` | `Linux` 或 `Windows` |
| `networkMode` | `Dhcp` 或 `Preconfigured` |
| `expectedDigest` | 推荐必填，SHA-256，可带 `sha256:` 前缀 |

导入流程为：流式暂存和摘要校验、写入内部 OCI Registry、记录不可变 artifact、
创建 `Opaque` 模板、按 KVM 能力异步分发。导入成功不会自动授予 `Managed`。

### 4.3 受控认证

只有 `controlled-probe` 成功后，Opaque 候选模板才升级为 Managed：

```bash
curl -X POST https://platform.example/api/open/v1/images/$IMAGE_ID/certifications \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: certify-$IMAGE_ID-v1" \
  -H "Content-Type: application/json" \
  -d '{
    "probeKind":"controlled-probe",
    "capabilities":[
      "windows.cloudbase-init.v1",
      "windows.powershell.v1",
      "guest.qga.v1",
      "guest.supervisor.v1",
      "image.vm.prepared.v1",
      "network.e1000e.v1",
      "bootstrap.firstboot.v1"
    ]
  }'
```

平台会在隔离管理网启动临时 VM，验证初始化、Guest Supervisor、声明能力、受控
重启、观测就绪与干净关机，然后精确清理临时 domain、overlay 和配置盘。

`external-evidence` 只登记供应链证据摘要，不会把 Opaque 模板升级为 Managed。
镜像摘要变化后，旧认证不再有效。

### 4.4 查询与删除

```bash
curl https://platform.example/api/open/v1/images/$IMAGE_ID \
  -H "Authorization: Bearer $GZCTF_TOKEN"

curl -X DELETE https://platform.example/api/open/v1/images/$IMAGE_ID \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

删除会先检查比赛、课程、练习和 TeamLab 引用，再清理节点缓存、OCI artifact 和
artifact 元数据。仍被引用时返回 `409 asset_in_use`。

## 5. Bootstrap Profile

Bootstrap Profile 是签名、版本化的服务注入包，不接受拓扑中的任意脚本文本。

| 方法 | 路径 | Scope |
| --- | --- | --- |
| `POST/GET` | `/bootstrap-profiles` | `bootstrap-profiles:write/read` |
| `GET/DELETE` | `/bootstrap-profiles/{profileId}` | `bootstrap-profiles:read/write` |
| `POST` | `/bootstrap-profiles/{profileId}/versions` | `bootstrap-profiles:write` |
| `GET` | `/bootstrap-profiles/{profileId}/versions/{version}` | `bootstrap-profiles:read` |

Managed 模板必须具有 Profile manifest 声明的当前认证能力。Opaque 模板不能执行
需要 Guest Supervisor 的 Profile。

## 6. 题目批量导入

题目写操作使用 `games.challenges:write`，读取使用 `games.challenges:read`。单题与
批量接口都使用稳定 `externalId`，并返回可恢复的 `ApiOperation`。单次批量最多
100 题；调用方应保存 `externalId -> challengeId` 映射。

具体路径、请求 schema 和删除接口以 Swagger 的 `Challenges` 分组为准。

## 7. TeamLab 组网 API

| 能力 | 路径前缀 | Scope |
| --- | --- | --- |
| 能力查询 | `/teamlab/capabilities` | `teamlab.topologies:read` |
| 拓扑与发布 | `/teamlab/topologies` | `teamlab.topologies:read/write` |
| Runtime 生命周期 | `/teamlab/runtimes` | `teamlab.runtimes:read/write` |
| 流量与路径 | `/teamlab/runtimes/{id}/traffic` | `teamlab.traffic:read` |
| PCAP | `/teamlab/runtimes/{id}/captures` | `teamlab.capture:read/write` |

Topology v2 只表达逻辑资产、交换机、路由器、网段、连接、依赖、Bootstrap 和观测
意图，不接受 WorkerNode ID、bridge、namespace、Fabric IP 或宿主机命令。

Runtime 可拆分为多个 shard。一个逻辑网段归属一个 Worker，跨节点通过 L3 Fabric
路由；未声明连接的网段保持隔离。Docker 只调度到 Docker 节点，VM 只调度到 KVM
节点，缺少 KVM 不影响 Docker 组网。

写操作返回 operation。创建成功后可查询 runtime 聚合状态、创建一次性 WireGuard
访问授权、读取流量与有序 path、按需启动 PCAP。销毁后授权立即失效，平台清理所有
shard、路由、capture 和镜像运行引用。

## 8. 自动化建议

1. 为流水线签发最小 scope、短有效期 Token。
2. 使用稳定业务 ID 生成 `Idempotency-Key`。
3. 先导入并认证镜像，再发布 Bootstrap 和 TeamLab topology。
4. 轮询 operation 到终态，记录 `traceId` 和资源 ID。
5. 仅在契约明确可重试时重试；未知结果先查询原 operation。
6. 流式处理大文件上传和 PCAP 下载，避免完整载入内存。
7. 流水线结束后撤销 Token，不复用长期全局 Token。
