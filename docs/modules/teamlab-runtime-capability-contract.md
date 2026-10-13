# TeamLab 运行事实与工具能力契约

实现契约基线：2026-10-06 第一轮工作流。描述已有认证运行详情响应的增量字段，不定义另一套运行队列或新的探测操作。现场验收与源码测试分别记录。

## 运行详情

`GET /api/admin/teamlab/runtimes/{runtimeId}` 保留原字段并增加：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `topologyId` | UUID 或 null | 通过 release 关联得到的公开场景 ID，用于导航。 |
| `topologyName` | string 或 null | 已执行 release 定义中的冻结名称，不读取可编辑场景的当前名称；不可变来源不可读时为 null。 |
| `releaseVersion` | integer 或 null | 既有发布版本号。 |
| `generation` / `planRevision` | integer | 当前运行代次与计划修订；以下资产事实均限定在该身份。 |

全局 runtime 搜索已有 `topologyId`、`scenarioName`、`releaseVersion`、`createdById`、`createdAt`、`updatedAt`。其中 `scenarioName` 是当前可编辑场景名称，界面不能标作冻结版本名称；响应没有可靠的到期时间或来源分类。

`assets[]` 保留兼容字段 `kind`、`networkKeys`、`primaryIp`，并增加：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `operatingSystem` | `windows` / `linux` / `unknown` | 操作系统元数据；不得从缺失枚举或机器名称推断。Docker 仍以 kind 区分。 |
| `operatingSystemSource` | `execution-plan` / `template-current` / `unknown` | 显式执行快照优先；匹配当前模板时注明是当前元数据，不能称冻结或来宾观测事实。 |
| `sourceTemplateId` | integer 或 null | 本代运行资产绑定的模板 ID，仅用于设置导航，不返回凭据。 |
| `interfaces` | array | 本代运行资产与已执行资产定义中的逐接口分配。 |
| `capabilities` | array | 适用工具，包含 kind、status、reason、settingsTemplateId。 |

接口项包含 `key`、`networkKey`、`primary`、`assigned`、`observed`。没有有效冻结分配时 assigned 为 null，否则包含 `ipAddress`、`prefixLength`、`dnsServers`、可空的 `gatewayIp`、`staticRoutes`（destinationCidr、nextHop、metric）。

只有可信且关联当前代次的来宾回读已持久化时才能填 observed，否则为 null。未来观测对象须携带独立值及 observedAt；当前 GET 不探测机器。分配地址与网关不能证明来宾可达，缺失逐接口地址时不能用 primaryIp 冒充。

工具 kind 为 `console`、`rdp`、`ssh`、`sftp`、`terminal`、`files`；status 为 `unsupported`、`unconfigured`、`configured-unverified`、`currently-unavailable`。reason 是稳定解释标识。只有相关设置属于已有镜像模板远程访问配置时，才填写 settingsTemplateId。

完整 SSH/RDP 模板配置最多给出 configured-unverified，不能声称服务已可达；当前资产/runtime 状态可能给出 currently-unavailable。Windows VM 支持控制台和可选 RDP，不支持 SSH/SFTP/files；Linux VM 支持控制台及可选 SSH/SFTP；Docker 支持终端和文件。调用仍遵守已有授权检查。

## 边界

- 数据来自当前代次资产记录和已执行计划，运行更新后的当前 planRevision 也适用；场景草稿或更新的 release 不能覆盖运行资产事实。
- 旧计划继续可读。旧 JSON 可能省略默认 Linux 枚举，缺失字段不足以认定 Linux；只有模板 ID 和镜像摘要匹配已执行资产时才能使用当前模板 OS，标明 template-current，否则 unknown。任何 OS 元数据都不是来宾观测。
- 读取模板配置不返回用户名或秘密；修改配置可能影响已有 runtime 的新连接。
- TeamLab 通过 Content 所有的批量 query contract 读取 OS、镜像摘要和远程配置存在性；新增投影不直接读取 Content 表。
- Windows 或未确认 OS 的 VM 文件请求在 SFTP 调度前返回既有 `files.unsupported` 错误，不暗示已实现 Windows SFTP。
- GET 不创建远程会话、VPN grant、抓包任务或深度探测。本地门禁不能替代实机验收。
