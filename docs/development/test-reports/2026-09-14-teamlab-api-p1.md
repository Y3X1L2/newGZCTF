# TeamLab API P1 本地验证记录

验证日期：2026-09-14

候选分支：`codex/teamlab-api-product-integration`

候选提交：`61b951f`

## 验证目的

建立可以重复执行的本地 TeamLab API 检查入口，确认候选主站能够在本地 PostgreSQL、Redis 和 Docker 环境中启动，并由正式 HTTP 接口完成调用身份、范围授权、资源发现和运行状态读取。

## 本地组合

- Docker Engine `29.4.0`。
- `newgzctf-main-db-1`：PostgreSQL 16，沿用本地开发数据卷。
- `newgzctf-main-redis-1`：Redis 7。
- `newgzctf-main-guacd-1`：Guacd。
- 主站：当前工作树 Release 构建，监听 `http://127.0.0.1:8080`。
- Agent：`gzctf-agent-local-sim:20260907`，连接本地主站。

电脑异常重启后，Docker Desktop 4.70 留下无法访问的 Windows Unix 套接字。停止 Docker 与 WSL、保留改名 `%LOCALAPPDATA%\Docker\run` 和 `%LOCALAPPDATA%\docker-secrets-engine` 后，Docker 恢复启动；镜像、容器和数据卷未删除。

## API 驱动结果

执行入口：

```powershell
.\scripts\validation\teamlab\run-teamlab-api-p1.ps1 `
  -AdminUser <本地管理员> `
  -AdminPassword <本地管理员密码>
```

脚本只通过正式 HTTP 接口工作：登录后签发带 `teamlab-scope:*` 的短期 Token，结束时撤销 Token。脚本完成控制范围创建和归档，并检查能力、控制范围、拓扑、运行实例、资源池、设备模板、连接器、远程会话和 Operation 列表。存在授权运行实例时，继续检查运行详情、事件、访问授权、服务开放、远程访问、设备健康、运行状态差异、流量、路径和抓包列表。

脚本在电脑重启前后各执行一次，均成功。重启后的结果为：发现 1 个拓扑、1 个授权运行实例、1 个资源池和 1 个连接器；远程会话与 Operation 当前为空；该运行实例的 10 个详情入口全部返回有效响应。两次创建的测试控制范围均已归档，临时 API Token 均已撤销。

相关集成测试：

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj `
  -c Release --no-build `
  --filter "FullyQualifiedName~OpenTeamLabCapabilityResourcesApiTests|FullyQualifiedName~OpenTeamLabOperationsApiTests"
```

结果：8/8 通过，覆盖设备模板、连接器、文件操作、会话、操作审计、设备健康、运行状态检查以及控制范围隔离。

## 能力边界

当前本地 Agent 配置为 `TeamLab__Enable=false`、`TeamLab__DryRun=true`。本轮确认的是主站真实 HTTP、真实 PostgreSQL/Redis、Token 授权和协议级执行替身能够组成可重复的 P1 环境；没有据此认定 Docker 组网、OVN/OVS、VM、SFTP、VNC、公网服务开放和 PCAP 实际链路通过。

P2 使用同一脚本和候选主站，更换为启用 TeamLab 的当前 Agent 后补跑创建运行实例、节点执行、运维、服务开放和销毁链。出现失败时只修复实际断点，不新增平行状态机或通用重试框架。
