# 组网场景与资产交付标准

本文规定 Docker 镜像、虚拟机镜像、设备模板和组网场景的交付格式，以及对应的导入接口。

## 1. 交付清单

| 交付项 | 格式 |
| --- | --- |
| Docker 资产 | 镜像仓库地址，或 Docker 镜像 tar 文件 |
| Linux 虚拟机 | `qcow2` |
| Windows 虚拟机 | `qcow2` |
| 设备模板 | 设备名称、版本、镜像、端口、资源规格和设备参数 |
| 组网场景 | 网段、资产、网卡、连接关系和画布位置 |

## 2. Docker 镜像标准

- 容器启动后，题目服务随容器自动启动。
- 容器保持运行，主进程退出时容器结束。
- 题目服务监听 `0.0.0.0`，服务端口固定。
- HTTP 服务按 HTTP 端口检查；SSH、数据库、Modbus TCP 和其他 TCP 服务按 TCP 端口检查。
- 镜像内包含题目运行所需的程序和文件。
- Flag、比赛账号和每场比赛变化的密码由运行环境注入。

交付结果：容器持续运行，目标端口连接成功，题目页面或协议服务返回预期内容。

## 3. 虚拟机镜像标准

### 3.1 Linux 虚拟机

- 镜像格式为 `qcow2`。
- 从关机状态启动至登录界面，执行关机后虚拟机停止。
- 启动后取得场景分配的地址。
- 题目服务随系统启动，服务端口连接成功。
- 使用 SSH 运维时，镜像内已启用 SSH，并配置可登录的账号和端口。
- 使用平台文件管理时，使用同一 SSH 账号。

### 3.2 Windows 虚拟机

- 镜像格式为 `qcow2`。
- 从关机状态启动至登录界面，执行关机后虚拟机停止。
- 启动后取得场景分配的地址。
- 题目服务随系统启动，服务端口连接成功。
- 使用 RDP 运维时，镜像内已启用远程桌面，并配置固定账号、密码和端口。
- 首次设置、强制改密和开机确认项在交付前处理完毕。

Linux 和 Windows 镜像均以关机状态提交。平台为每个运行环境创建独立磁盘。

## 4. 设备模板标准

PLC、传感器、控制器等仿真设备先制作成 Docker 或虚拟机镜像，再登记设备模板。

| 字段 | 填写内容 |
| --- | --- |
| `name` | 稳定英文标识 |
| `displayName` | 页面显示名称 |
| `version` | 本次交付版本 |
| `artifactKind` | Docker 填 `oci-image`，虚拟机填 `vm-image` |
| `artifactReference` | 已导入的镜像地址或镜像标识 |
| `digest` | 镜像 SHA-256 摘要 |
| `supportedAssetKinds` | `docker` 或 `vm` |
| `cpuMillis`、`memoryMib`、`storageGib` | 设备运行所需的最低资源 |
| `ports` | 设备实际监听的端口和 TCP/UDP 协议 |
| `parameterSchema` | 场景中可填写的设备参数；无参数时填空对象 |
| `healthDeclaration` | 设备服务的 TCP 或 HTTP 检查端口 |

使用设备参数时：

- Docker 程序读取环境变量 `GZCTF_DEVICE_PARAMETERS`。
- 虚拟机程序读取 QEMU fw_cfg 项 `opt/org.gzctf/device-parameters`。

设备模板登记成功后，拓扑使用返回结果中的 `bindingId` 作为 `devicePackageId`。

## 5. 上传资产

### 5.1 上传 Docker 镜像

镜像已上传到镜像仓库：

```http
POST /api/open/v1/images/docker-references
Authorization: Bearer <token>
Idempotency-Key: <本次上传编号>
Content-Type: application/json

{
  "name": "PLC 仿真器",
  "registryUrl": "registry.example/teamlab/plc-modbus:1.0.0",
  "osType": 0,
  "expectedDigest": "sha256:<镜像摘要>"
}
```

上传 Docker 镜像 tar 文件：

```http
POST /api/open/v1/images/docker-archives
Authorization: Bearer <token>
Idempotency-Key: <本次上传编号>
Content-Type: multipart/form-data
```

表单填写 `file`、`name`、`osType` 和 `expectedDigest`。

### 5.2 上传虚拟机镜像

```http
POST /api/open/v1/images/vm-qcow2
Authorization: Bearer <token>
Idempotency-Key: <本次上传编号>
Content-Digest: sha-256=:<文件摘要的 Base64 值>:
Content-Type: multipart/form-data
```

| 字段 | 填写内容 |
| --- | --- |
| `file` | `qcow2` 文件 |
| `name` | 镜像名称 |
| `osType` | Linux 填 `0`，Windows 填 `1` |
| `networkMode` | DHCP 镜像填 `0`，镜像内已固定网络配置时填 `1` |
| `expectedDigest` | 文件 SHA-256 摘要 |

上传接口返回 `202 Accepted` 和 operation ID。查询：

```http
GET /api/open/v1/operations/{operationId}
GET /api/open/v1/images/{imageTemplateId}
```

镜像状态达到 Ready 后，记录 `imageTemplateId` 并加入场景。

### 5.3 配置虚拟机运维账号

```http
PATCH /api/open/v1/images/{imageTemplateId}/remote-access
Authorization: Bearer <token>
Content-Type: application/json

{
  "enabled": true,
  "protocol": "rdp",
  "port": 3389,
  "username": "operator",
  "credential": "<镜像内账号密码>",
  "clearCredential": false
}
```

Windows 的 `protocol` 填 `rdp`；Linux 填 `ssh`，端口填写镜像内对应服务端口。

### 5.4 登记设备模板

```http
POST /api/open/v1/teamlab/device-packages
Authorization: Bearer <token>
Content-Type: application/json
```

```json
{
  "name": "modbus-plc",
  "displayName": "Modbus PLC",
  "version": "1.0.0",
  "artifactKind": "oci-image",
  "artifactReference": "registry.example/teamlab/plc-modbus:1.0.0",
  "digest": "sha256:<镜像摘要>",
  "supportedAssetKinds": ["docker"],
  "cpuMillis": 500,
  "memoryMib": 256,
  "storageGib": 1,
  "ports": [
    { "name": "modbus", "port": 502, "protocol": "tcp" }
  ],
  "parameterSchema": {
    "type": "object",
    "properties": {
      "unitId": { "type": "integer", "minimum": 1, "maximum": 247 }
    }
  },
  "healthDeclaration": { "kind": "tcp", "port": 502 },
  "protocolEventTypes": []
}
```

## 6. 场景标准

### 6.1 网段

| 字段 | 填写内容 |
| --- | --- |
| `key` | 场景内唯一英文标识 |
| `name` | 页面显示名称 |
| `addressPool.poolCidr` | 该网段可分配的私有地址池 |
| `addressPool.runtimePrefixLength` | 每个运行环境使用的子网大小 |
| `isEntry` | 该网段是否作为场景入口 |

同一场景的地址池互不重叠。

### 6.2 资产

| 字段 | 填写内容 |
| --- | --- |
| `key` | 场景内唯一英文标识 |
| `name` | 页面显示名称 |
| `kind` | Docker 填 `0`，虚拟机填 `1` |
| `imageTemplateId` | 已达到 Ready 的镜像模板 ID |
| `resources` | CPU、内存和磁盘规格 |
| `interfaces` | 网卡、所属网段、地址偏移和主网卡 |
| `healthCheck` | 资产服务的 TCP 或 HTTP 检查端口 |
| `devicePackageId` | 设备模板返回的 `bindingId` |
| `deviceParameters` | 本资产使用的设备参数 |

同一网段内，每张网卡使用不同的 `hostOffset`。

### 6.3 画布

API 导入场景时提交 `editor`：

```json
{
  "editor": {
    "networks": {
      "office": { "x": 80, "y": 80, "width": 360, "height": 240, "collapsed": false }
    },
    "assets": {
      "web": { "x": 170, "y": 150, "width": 180, "height": 80, "collapsed": false }
    },
    "infrastructure": {}
  }
}
```

`networks` 和 `assets` 中的键与场景里的 `key` 保持一致。

## 7. 导入场景

### 7.1 创建场景

```http
POST /api/open/v1/teamlab/topologies
Authorization: Bearer <token>
Idempotency-Key: <本次导入编号>
Content-Type: application/json
```

```json
{
  "name": "工业控制基础场景",
  "controlScopeId": "<scopeId>",
  "schemaVersion": 2,
  "networks": [],
  "assets": [],
  "connections": [],
  "infrastructure": [],
  "observation": {
    "flowMetadataEnabled": true,
    "onDemandPcapEnabled": true
  },
  "editor": {
    "networks": {},
    "assets": {},
    "infrastructure": {}
  }
}
```

更新场景：

```http
PUT /api/open/v1/teamlab/topologies/{topologyId}
Authorization: Bearer <token>
Idempotency-Key: <本次更新编号>
Content-Type: application/json
```

更新请求带当前 `revision`，并提交完整场景内容。

### 7.2 校验和发布

```http
POST /api/open/v1/teamlab/topologies/{topologyId}/validate
```

`valid` 为 `true` 后发布：

```http
POST /api/open/v1/teamlab/topologies/{topologyId}/releases
Authorization: Bearer <token>
Idempotency-Key: <本次发布编号>
Content-Type: application/json

{
  "revision": 3
}
```

发布 operation 成功后，记录返回的 `releaseId`。

## 8. 交付检查表

| 检查项 | 通过标准 |
| --- | --- |
| Docker | 容器持续运行，目标端口连接成功 |
| Linux VM | 可启动至登录界面并完成关机；取得网络地址；题目服务可访问；配置 SSH 时账号可登录 |
| Windows VM | 可启动至登录界面并完成关机；取得网络地址；题目服务可访问；配置 RDP 时账号可登录 |
| 设备模板 | 镜像摘要一致，端口和参数与设备程序一致 |
| 场景网段 | 地址池无重叠，网卡地址无冲突 |
| 场景资产 | 镜像 ID、资产类型、资源规格和服务端口正确 |
| 画布 | 网段和资产位置已提交，页面无重叠 |
| 发布 | 场景校验通过，发布 operation 成功，已取得 `releaseId` |

完整字段定义见 [`commercialization/openapi/open-v1.json`](commercialization/openapi/open-v1.json)。
