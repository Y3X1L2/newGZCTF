# 组网场景与资产制作导入指南

本文说明组网场景交付时需要准备什么、各类资产应满足什么条件，以及如何把资产和场景导入平台。运行环境发放、赛事控制、计分和选手操作不在本文范围内。

## 1. 交付内容

一个可发布的组网场景由以下内容组成：

| 内容 | 必需 | 说明 |
| --- | --- | --- |
| 场景说明 | 是 | 场景名称、用途、资产清单、网段关系、入口服务和预期连通关系 |
| Docker 镜像 | 按场景 | 以镜像引用或 Docker archive 导入 |
| Linux VM 镜像 | 按场景 | `qcow2` 文件，包含可用系统、网卡和必要服务 |
| Windows VM 镜像 | 按场景 | `qcow2` 文件，包含可用系统、网卡、RDP 服务和固定运维账号 |
| 设备模板 | 按场景 | 用于 PLC、传感器、控制器等仿真设备，引用已经导入的镜像制品 |
| 拓扑草稿 | 是 | 网段、资产、网卡、连接关系、资源规格和画布位置 |

资产镜像必须先进入镜像模板库。拓扑只保存 `imageTemplateId`，不接受临时镜像地址、启动命令或宿主机脚本。

## 2. 制作前先确定的内容

制作镜像前先列清以下信息：

1. 每个资产使用 Docker 还是 VM。
2. 每个资产接入哪些网段，哪张网卡是主网卡。
3. 每张网卡在网段内使用的主机位偏移量。
4. 对外提供什么服务，使用 TCP 还是 UDP，内部端口是多少。
5. 平台用哪个端口判断资产已经可用。
6. 是否需要 SSH、RDP、VNC 或容器终端。
7. 是否需要设备参数；如果需要，参数名称、类型和默认值必须先固定。

可以先调用以下接口确认当前平台能力和限制：

```http
GET /api/open/v1/teamlab/capabilities
Authorization: Bearer <token>
```

重点查看 `assetKinds`、`topologySchemaVersions`、`features` 和 `limits`。不要按其他环境的限制估算当前平台。

## 3. Docker 资产

### 3.1 镜像要求

- 镜像必须能够独立启动，不能依赖宿主机预装文件或手工执行命令。
- 服务必须监听容器网卡，不能只监听 `127.0.0.1`。
- 服务端口必须固定。场景中的健康检查、服务开放和设备模板都依赖该端口。
- 启动后应直接进入工作状态，不要把交互式安装、首次初始化向导留到比赛环境。
- 运行数据需要初始化时，放进镜像或由程序首次启动生成；密码、Flag、令牌等实例机密不要写入镜像。
- 容器进程退出即视为资产停止。不要使用只负责拉起后台进程、随后立即退出的入口脚本。

### 3.2 健康检查

健康检查只判断服务是否已经可以接收请求：

| 类型 | `kind` | 适用情况 |
| --- | --- | --- |
| TCP | `0` | SSH、数据库、Modbus TCP、自定义 TCP 服务等 |
| HTTP | `1` | 目标端口确实提供 HTTP 服务 |

Modbus TCP 等非 HTTP 协议使用 TCP 检查，不要为了通过检查额外启动一个无关的 HTTP 服务。

### 3.3 导入方式

已有 Registry 镜像时，登记镜像引用：

```http
POST /api/open/v1/images/docker-references
Authorization: Bearer <token>
Idempotency-Key: image-plc-20260919-01
Content-Type: application/json

{
  "name": "PLC 仿真器",
  "registryUrl": "registry.example/teamlab/plc-modbus:1.0.0",
  "osType": 0,
  "expectedDigest": "sha256:<digest>"
}
```

只有本地 archive 时，使用：

```http
POST /api/open/v1/images/docker-archives
Authorization: Bearer <token>
Idempotency-Key: image-plc-archive-20260919-01
Content-Type: multipart/form-data
```

表单字段包括 `file`、`name`、`sourceImage`、`osType` 和 `expectedDigest`。导入返回 `202 Accepted`，按返回的 operation ID 查询处理结果；完成后再读取镜像模板，确认状态为 Ready。

## 4. Linux VM 资产

### 4.1 镜像要求

- 文件格式为 `qcow2`，提交前用 `qemu-img check` 检查。
- 镜像必须能在 KVM/libvirt 中正常启动，不能依赖制作机上的外部磁盘。
- 至少保留一张平台可接管的网卡。需要自动写入组网地址时，镜像应支持 NoCloud 网络配置。
- 需要 SSH 或文件管理时，镜像内必须启用 SSH，并配置可登录的运维账号。
- 需要稳定识别来宾地址和状态时，安装并启用 QEMU Guest Agent。
- 清理制作过程中的临时文件、历史命令和无用软件包，保留题目所需服务与工具。

建议在导入前完成以下检查：

```bash
qemu-img info --output=json linux-target.qcow2
qemu-img check linux-target.qcow2
sha256sum linux-target.qcow2
```

### 4.2 远程访问

Linux VM 的 SSH 和文件管理共用镜像模板中的运维账号。账号必须在镜像内真实存在，SSH 服务必须监听配置端口。平台只保存模板的访问配置，不会替镜像创建系统账号。

VNC 是 libvirt 图形控制台，不依赖来宾系统网络，也不使用 SSH 账号。

## 5. Windows VM 资产

### 5.1 镜像要求

- 文件格式为 `qcow2`，系统能够在 KVM/libvirt 中正常启动。
- Windows 许可或 Evaluation 有效期应覆盖使用周期。
- 网卡驱动、磁盘驱动和时间同步必须正常。
- 需要远程运维时，镜像内预先创建固定 RDP 账号，启用 TermService，并放行实际 RDP 端口。
- 关闭首次登录向导、强制改密提示和会阻断自动启动的弹窗。
- 提交前关机，不要在休眠或挂起状态制作最终镜像。

建议执行：

```bash
qemu-img convert -p -O qcow2 -c windows-builder.qcow2 windows-final.qcow2
qemu-img info --output=json windows-final.qcow2
qemu-img check windows-final.qcow2
sha256sum windows-final.qcow2
```

### 5.2 RDP 配置

导入后为镜像模板配置远程访问：

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

这里填写的是镜像内已经配置好的账号。VNC 控制台不在此处配置。

## 6. VM 镜像导入

Linux 和 Windows VM 都使用：

```http
POST /api/open/v1/images/vm-qcow2
Authorization: Bearer <token>
Idempotency-Key: image-vm-20260919-01
Content-Digest: sha-256=:<base64 digest>:
Content-Type: multipart/form-data
```

表单字段：

| 字段 | 说明 |
| --- | --- |
| `file` | `qcow2` 文件 |
| `name` | 镜像模板名称 |
| `osType` | `0`=Linux，`1`=Windows |
| `networkMode` | 按镜像实际网络方式选择 |
| `expectedDigest` | 文件 SHA-256 摘要 |

上传完成不代表模板已经可用。先查询 operation，再调用 `GET /api/open/v1/images/{imageTemplateId}`，确认导入状态、镜像类型、操作系统和摘要正确。

## 7. 设备模板

设备模板用于说明一个仿真设备怎样运行，包括制品、资源下限、端口、普通参数和健康检查。它不负责解析 Modbus、OPC UA 等业务协议。

设备模板必须引用已经存在的制品：

- Docker 设备使用 `artifactKind: "oci-image"`。
- VM 设备使用 `artifactKind: "vm-image"`。
- `artifactReference` 和 `digest` 必须与已导入镜像一致。

登记示例：

```http
POST /api/open/v1/teamlab/device-packages
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "modbus-plc",
  "displayName": "Modbus PLC",
  "version": "1.0.0",
  "artifactKind": "oci-image",
  "artifactReference": "registry.example/teamlab/plc-modbus:1.0.0",
  "digest": "sha256:<digest>",
  "description": "提供 Modbus TCP 寄存器读写",
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
    },
    "required": ["unitId"]
  },
  "healthDeclaration": {
    "kind": "tcp",
    "port": 502
  },
  "protocolEventTypes": []
}
```

设备参数的传递方式固定：

- Docker 从环境变量 `GZCTF_DEVICE_PARAMETERS` 读取 JSON。
- VM 从 QEMU fw_cfg 的 `opt/org.gzctf/device-parameters` 读取；来宾程序需要自行实现读取。

参数只放公开设备配置。密码、令牌和实例 Flag 不应写入设备模板参数。

登记结果同时包含公共 UUID `id` 和用于拓扑绑定的数字 `bindingId`。拓扑资产的 `devicePackageId` 填 `bindingId`。

## 8. 场景拓扑制作

### 8.1 网段

每个网段至少填写：

- `key`：场景内稳定标识，例如 `office`、`control`。
- `name`：页面显示名称。
- `addressPool.poolCidr`：可分配的 RFC1918 地址池。
- `addressPool.runtimePrefixLength`：每个独立运行环境实际取得的子网前缀。
- `isEntry`：是否为入口网段。

地址池之间不能重叠。拓扑保存的是地址池，具体运行环境的实际 CIDR 由平台创建运行环境时分配。

### 8.2 资产

资产的主要字段：

| 字段 | 说明 |
| --- | --- |
| `key` | 场景内唯一且发布后稳定的资产标识 |
| `name` | 页面显示名称 |
| `kind` | `0`=Docker，`1`=VM |
| `imageTemplateId` | 已 Ready 的镜像模板 ID |
| `resources` | `cpuUnits`、`memoryMiB`、`storageMiB` |
| `interfaces` | 网卡及其网段、主机位偏移和主网卡标识 |
| `exposePort` | 场景默认展示的服务端口，可不填 |
| `healthCheck` | TCP 或 HTTP 检查及端口 |
| `devicePackageId` | 设备模板的数字 `bindingId` |
| `deviceParameters` | 通过设备模板 schema 校验的参数对象 |

每张网卡的 `networkKey` 必须引用本场景网段。`hostOffset` 在同一网段内不能重复，也不能使用网络地址、网关和广播等保留地址。

### 8.3 画布位置

通过 API 创建或更新场景时，应同时提交 `editor`。键名必须与网段、资产和基础设施的 `key` 一致：

```json
{
  "editor": {
    "networks": {
      "office": { "x": 80, "y": 80, "width": 360, "height": 240, "collapsed": false },
      "control": { "x": 520, "y": 80, "width": 360, "height": 240, "collapsed": false }
    },
    "assets": {
      "jump-host": { "x": 180, "y": 150, "width": 180, "height": 80, "collapsed": false },
      "plc": { "x": 610, "y": 150, "width": 180, "height": 80, "collapsed": false }
    },
    "infrastructure": {}
  }
}
```

坐标只控制画布排版，不参与网络执行。网段背景色和连线颜色由前端按网段关系生成，不在拓扑 JSON 中手写颜色。

### 8.4 创建场景

```http
POST /api/open/v1/teamlab/topologies
Authorization: Bearer <token>
Idempotency-Key: topology-industrial-20260919-01
Content-Type: application/json

{
  "name": "工业控制基础场景",
  "controlScopeId": "<scopeId>",
  "schemaVersion": 2,
  "networks": [
    {
      "key": "control",
      "name": "控制网",
      "addressPool": { "poolCidr": "10.60.0.0/16", "runtimePrefixLength": 24 },
      "isEntry": true,
      "orderIndex": 0
    }
  ],
  "assets": [
    {
      "key": "plc",
      "name": "PLC",
      "kind": 0,
      "imageTemplateId": 116,
      "resources": { "cpuUnits": 1, "memoryMiB": 256, "storageMiB": 512 },
      "interfaces": [
        { "key": "eth0", "networkKey": "control", "hostOffset": 20, "primary": true, "orderIndex": 0 }
      ],
      "healthCheck": { "kind": 0, "port": 502 },
      "devicePackageId": 12,
      "deviceParameters": { "unitId": 1 },
      "orderIndex": 0
    }
  ],
  "connections": [],
  "infrastructure": [],
  "observation": { "flowMetadataEnabled": true, "onDemandPcapEnabled": true },
  "editor": {
    "networks": { "control": { "x": 80, "y": 80, "width": 420, "height": 260, "collapsed": false } },
    "assets": { "plc": { "x": 190, "y": 160, "width": 180, "height": 80, "collapsed": false } },
    "infrastructure": {}
  }
}
```

返回 `202 Accepted`。从 operation 的 `resourceId` 取得拓扑 ID；也可以在 operation 完成后用拓扑列表查询。

更新草稿使用：

```http
PUT /api/open/v1/teamlab/topologies/{topologyId}
Authorization: Bearer <token>
Idempotency-Key: topology-industrial-update-20260919-01
Content-Type: application/json
```

请求体必须带当前 `revision`，并提交完整的网段、资产、连接关系和 `editor`。修订号过期会返回 `409 topology_revision_conflict`。

## 9. 校验与发布

先校验：

```http
POST /api/open/v1/teamlab/topologies/{topologyId}/validate
Authorization: Bearer <token>
```

只有 `valid` 为 `true` 才发布。`issues` 会给出字段路径和具体原因，应修改草稿后重新校验。

发布不可变版本：

```http
POST /api/open/v1/teamlab/topologies/{topologyId}/releases
Authorization: Bearer <token>
Idempotency-Key: topology-industrial-release-20260919-01
Content-Type: application/json

{
  "revision": 3
}
```

发布返回异步 operation。完成后通过以下接口确认版本内容和部署计划：

```http
GET  /api/open/v1/teamlab/topologies/{topologyId}/releases
GET  /api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}
POST /api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}/plan
```

发布版本不可修改。需要调整资产或网络时，修改草稿并发布新版本。

## 10. 交付检查

提交场景前逐项确认：

- 所有镜像模板均为 Ready，摘要与提交记录一致。
- Docker 服务监听容器网卡，端口和健康检查匹配。
- Linux VM 能正常启动；需要 SSH 时，账号和端口可用。
- Windows VM 能正常启动；需要 RDP 时，固定账号、端口和防火墙已配置。
- 设备模板制品与镜像摘要一致，参数能被设备程序实际读取。
- 网段地址池不重叠，主机位偏移没有冲突。
- 每个资产的 `imageTemplateId`、资产类型和资源规格正确。
- `devicePackageId` 使用设备模板的 `bindingId`。
- API 导入时已提交 `editor`，画布没有节点重叠。
- 拓扑校验通过，发布 operation 成功，计划接口没有阻断项。

机器可读的完整字段定义以 [`commercialization/openapi/open-v1.json`](commercialization/openapi/open-v1.json) 为准。
