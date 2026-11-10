# TeamLab 开发地图

功能入口见 [模块说明](README.md)，配置与上线顺序见 [接入手册](netlab-integration.md)。

## 调用链

| 职责 | 源码 |
| --- | --- |
| 工作区与连接设置入口 | [NetlabWorkspaceController](../../../src/GZCTF/Modules/TeamLab/Api/NetlabWorkspaceController.cs) |
| 用户、比赛和队伍访问范围 | [NetlabWorkspaceAccess](../../../src/GZCTF/Modules/TeamLab/Application/NetlabWorkspaceAccess.cs) |
| 配置、服务凭据与能力检查 | [NetlabConnection](../../../src/GZCTF/Modules/TeamLab/Infrastructure/Netlab/NetlabConnection.cs) |
| 同源 HTML、API、SSE、WebSocket 和文件流 | [NetlabProxy](../../../src/GZCTF/Modules/TeamLab/Infrastructure/Netlab/NetlabProxy.cs) |
| 业务使用的底座 API | [NetlabClient](../../../src/GZCTF/Modules/TeamLab/Infrastructure/Netlab/NetlabClient.cs) |
| 比赛绑定、部署、重建与退出清理 | [PenetrationTeamLabAdapter](../../../src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs) |
| 目标与动态 Flag | [PenetrationObjectiveService](../../../src/GZCTF/Modules/Penetration/Application/PenetrationObjectiveService.cs) |
| 上层工作区容器与 API adapter | [features/teamlab](../../../src/GZCTF/ClientApp/src/vnext/features/teamlab) |
| 管理入口 / 选手入口 | [TeamLabPage](../../../src/GZCTF/ClientApp/src/vnext/features/teamlab/TeamLabPage.tsx)、[games/teamlab](../../../src/GZCTF/ClientApp/src/vnext/features/games/teamlab) |

上层创建环境使用持久化的 `CreationRequestId`；重建沿用同一次请求标识并读取底座操作结果。比赛和队伍删除先请求销毁，底座返回 `destroyed` 后才删除本地业务记录。定时清理复用现有 Runtime 定时任务，处理比赛结束和退出队伍后的关联环境。

旧 TeamLab 实体、EF 映射和只读镜像引用检查用于保留待迁移数据；旧 Controller、Agent 执行器、调度 worker 与页面已由 Netlab 接入链替换。普通 CTF、培训、练习和 AWDP 继续使用原 Runtime/Agent。

## 契约与生成

底座 OpenAPI 保存在 [contracts/netlab/openapi.yaml](../../../contracts/netlab/openapi.yaml)。更新源契约后执行 [generate.ps1](../../../contracts/netlab/generate.ps1)，生成 `NetlabContract.g.cs`。上层公开 API 变化按仓库 NSwag 流程重新生成前端客户端。

工作区使用能力接口声明的 `workspaceProtocol`；底座页面按实际权限显示入口。上层实现业务需要的 API 调用，其余工作区请求由受控代理转发。

## 验证

- [NetlabProxyTests](../../../src/GZCTF.Integration.Test/Tests/Runtime/NetlabProxyTests.cs)：真实 HTTP/WebSocket，覆盖路径、权限头、HTML 基址、上传、SSE、文件下载与响应重定向。
- [NetlabCompetitionTests](../../../src/GZCTF.Integration.Test/Tests/Runtime/NetlabCompetitionTests.cs)：真实 PostgreSQL 前向迁移、历史绑定保留、创建复用、队伍 Flag、重建重试及删除前资源清理。
- 前端执行 locale、lint、TypeScript、架构检查、测试、构建及工作区日夜/宽窄屏检查。

本轮验收聚焦上层调用链、授权和界面。底座并发调度与物理执行沿用底座验证；现有数据迁移和生产业务冒烟随上线进行，证据记录在 [任务记录](../../records/2026-11-10-netlab-integration.md)。
