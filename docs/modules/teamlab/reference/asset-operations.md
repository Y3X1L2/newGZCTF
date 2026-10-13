# TeamLab：文件、单资产操作与会话证据

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

<a id="16-在研资产文件管理"></a>

## 16. 资产文件管理

运行详情“资产运维”支持 Docker 文件与可确认系统为 Linux 的 VM SFTP；Windows/unknown VM 在 SFTP 调度前返回 `files.unsupported`。先选择当前代资产并读取目录，再上传、下载、创建目录、移动或删除。旧 JSON 的 Base64 上传/下载上限 8 MiB；当前二进制流传输由 `MaxFileTransferBytes` 限制，默认 1 GiB，闲置超时默认 120 秒。单目录最多 1000 项。覆盖、删除和 SSH 身份重登记必须 `confirmed=true`；删除目录可显式 `recursive=true`，仍遵守安全路径与文件类型限制。

上传使用目标目录下的平台保留暂存目录，写完后原子替换。异常返回时清理本次暂存；进程终止留下的一小时以上暂存，在下次读取该目录或上传时回收，近期暂存保留。保留目录不作为用户文件开放，普通文件操作不能进入它。

管理 API：`POST /api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/files`。

旧 JSON 请求字段：`generation` 为当前代次，`operation` 支持 `list/upload/download/delete/mkdir/move/reset-ssh-identity`，`path` 为资产内绝对路径；JSON 上传携带 Base64 `content`，覆盖携带 `overwrite=true` 和 `confirmed=true`，删除携带 `confirmed=true`，移动使用 `destinationPath`，递归删除使用 `recursive=true`。响应的 `entries` 返回名称、类型、字节数；下载的 `content` 是 Base64。浏览器由 feature adapter 转成下载文件，接口不对外承诺 Open API v1 兼容。

操作需要当前资产 `FileTransfer` 权限；重新登记 SSH 身份同样经 FileTransfer 校验并要求明确确认。主站允许 runtime 为 Running 或 Failed 的当前代资产，但节点仍校验资源与执行条件，不保证 Failed 中所有资产都可操作。Agent 验证容器标签、代次和进程身份，通过 Linux openat2 相对于容器根目录解析；拒绝符号链接、特殊文件、`..` 和跨挂载访问，包括宿主 bind mount、Docker volume、`/proc` 等。这里不是宿主文件管理器，不支持直接编辑挂载卷。审计记录操作者、资产、代次、操作和路径，不记录文件内容。

容器文件 Agent 需要 Linux 5.6+、可见的宿主 PID/cgroup 以及访问目标进程根目录的权限。直接部署在节点上的 Agent 使用节点 `/proc`；容器化模拟 Agent 需要 host PID/cgroup。缺少能力时明确报错。

可确认 OS 的 Linux VM 文件管理使用镜像模板中已启用的 SSH 运维账号，支持密码或未加密 SSH 私钥。Agent 复查虚拟机 UUID、代次与地址，并使用管理网络连接 SSH 服务；文件权限由该 SSH 账号决定。首次使用先探测并登记 SSH 主机身份，后续连接必须匹配。若在平台外重装虚拟机或更新 SSH 密钥，具有 FileTransfer 权限的用户可确认“重新登记 SSH 身份”，不必重建磁盘；对应请求为 `operation=reset-ssh-identity`、`path=/`、`confirmed=true`。普通 SFTP 需要来宾 SSH 服务和管理网络，VNC 控制台不受此限制。


### 16.1 当前二进制流与开放 API

管理入口除兼容 JSON `POST .../files`，还有 `GET .../files/download` 与 `POST .../files/upload`。上传正文是 `application/octet-stream`，必须提供实际 `Content-Length`，缺少返回 411；query 携带 generation/path/overwrite/confirmed，成功 204，不把整个文件放到 JSON。

开放 API 的前缀为 `/api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/files`：

| 操作 | 方法与相对路径 |
| --- | --- |
| 列目录 | `GET /`，query 为 generation/path |
| 下载 | `GET /download`，返回二进制流 |
| 上传 | `POST /upload`，二进制流与 Content-Length |
| 建目录 | `POST /directories`，generation/path |
| 移动或重命名 | `POST /move`，generation/sourcePath/destinationPath/overwrite/confirmed |
| 删除 | `DELETE /`，generation/path/recursive/confirmed |

读取用 `teamlab.remote-sessions:read`，写入用 `teamlab.remote-sessions:write`，并继续校验 token 资源范围和资产 `FileTransfer` 权限。这些文件操作直接执行，不统一套用“202 + operation”；目录、移动、删除与上传成功 204。开放 API 未发布 reset-ssh-identity 路由。请求和权限分别见 [OpenTeamLabAssetFilesController](../../../../src/GZCTF/Modules/TeamLab/Api/OpenTeamLabAssetFilesController.cs)、[TeamLabAssetFileService](../../../../src/GZCTF/Modules/TeamLab/Application/TeamLabAssetFileService.cs)；容量常量见 [共用执行契约](../../../../src/GZCTF.TeamLab.Contracts/TeamLabAssetFiles.cs)。

<a id="17-在研单资产生命周期"></a>

## 17. 单资产生命周期

运行详情“单资产生命周期”提供启动、停止、重启、重建、暂停和恢复。需要 AssetOperate 权限，远程终端权限不自动授予生命周期权限。接口会复查当前代次、原节点、原生身份和不可变执行计划；受 rollout 托管的资产显示限制，不猜测执行资源。

- `GET /api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control`：当前权限和能力。
- `POST /api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control`：请求字段 `generation/action/reason/confirmed`，action 为 `start/stop/restart/rebuild/pause/resume`。返回 202 和原部署队列的 `ticketId`。
- `GET .../control/{ticketId}`：真实任务状态、阶段、错误码和重试资格。
- `POST .../control/{ticketId}/retry`：仅对可重试失败继续未完成阶段，返回新票据关联。

开放 API 使用同一应用服务和原 `DeploymentQueueTicket`，但仅发布以下路由：

- `GET /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control`：按 token 的 `teamlab-scope` 查询能力。
- `POST /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control`：必须携带 `Idempotency-Key`，返回 202 和原票据 `ticketId`。
- `GET /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control/{ticketId}`：查询原票据状态。

开放 API 不发布单资产 retry 路由；管理员接口的 retry 能力不是对外契约。

停止保留容器可写层和 VM 磁盘；暂停保留执行现场；重建替换目标资产资源及可写层，其他资产和场景网络不重建。VM 停止/重启会强制断电，确认框明确提示未保存数据风险。目标资产远程会话先结束，文件传输与生命周期协调执行。后台对账尊重停止/暂停意图；不要用全量 apply 自动拉起主动停止的资产。

单资产权限依据 [TeamLabAssetControlService](../../../../src/GZCTF/Modules/TeamLab/Application/TeamLabAssetControlService.cs) 与 [授权枚举](../../../../src/GZCTF/Modules/TeamLab/Application/TeamLabAuthorizationService.cs)，不能把旧文档中的 LifecycleManage 当作当前 Runtime Grant 权限名。

任务完成表示该资产执行动作和实际电源状态达到目标，不代替真实来宾业务健康或完整跨节点组网验收。

## 18. 会话证据与 VNC 验收边界

会话结束并完成清理后，可在会话“操作审计”生成和下载 JSON 生命周期证据，包含会话/运行时/资产/操作者、时间、结束结果和关联标识；不包含命令、终端输出或屏幕录制。下载重新授权并校验 SHA-256，保留期和配额见商业能力清单。

VNC 使用平台管理的 libvirt 图形控制台，不填写来宾账号，不依赖 VM 内网络。实例必须有匹配 UUID/代次的活动域和 loopback VNC 监听。当前本地已验证无网卡的 TCG 域通过 Agent/Guacamole 收到图形数据并正确关闭；硬件 KVM、完整 VM 管理网络和浏览器视觉验收分别记录。
