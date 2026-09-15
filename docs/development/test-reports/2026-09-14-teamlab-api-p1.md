# TeamLab API P1 本地验证记录

验证日期：2026-09-15

候选分支：`codex/teamlab-api-product-integration`

验证提交：`7577754`

正式发布包：`teamlab-p1-20260915-111524`

## 验证目标

用一条可重复执行的 API 流水线验证 TeamLab 底座，而不是通过前端逐页点击或使用执行替身。流程从正式发布包启动，经过资源创建、真实部署和运行操作，最后销毁环境并检查各层状态是否一致。

## 隔离环境

- 主站、PostgreSQL 16、Redis 7、Registry 和 Guacd使用独立 Compose 项目。
- Agent 启用 TeamLab V2 执行面，运行独立 Docker Engine，不读取或污染宿主的题目容器。
- 网络使用真实 OVN northd、OVN controller、OVS 内核数据面、Linux veth 和 nftables。
- PostgreSQL 和 Redis 使用临时数据，流水线不会接触现有开发数据卷。
- 公网入口端口进入 Agent，再由 nftables 和 OVS/OVN 转发到场景资产，与生产 Worker 的流量路径一致。

完整入口：

```powershell
.\scripts\validation\teamlab\invoke-teamlab-api-p1-pipeline.ps1
```

调试时可复用已经构建的正式发布包：

```powershell
.\scripts\validation\teamlab\invoke-teamlab-api-p1-pipeline.ps1 `
  -ExistingReleaseId teamlab-p1-20260915-111524 `
  -KeepEnvironment
```

## 实际结果

流水线通过正式 HTTP API 完成以下操作，并于 2026-09-15 返回 `passed`：

1. 创建两个控制范围，并签发各自受限的 API Token。
2. 构建并导入 OCI 镜像，配置容器终端，创建设备模板。
3. 创建双资产、单网段拓扑，完成校验、发布和执行计划生成。
4. 将镜像分发到真实 Agent，创建运行环境和两个真实容器。
5. 核对 API、PostgreSQL、Agent inventory、独立 Docker、OVN 和 OVS 的资源事实。
6. 完成目录创建、文件上传、下载、移动和递归删除。
7. 完成资产停止和重新启动。
8. 对整个网段的两个资产接口应用 25 ms 时延，并恢复原状态。
9. 创建公网服务入口，确认 HTTP 内容可访问，再撤销入口。
10. 创建真实 WebSocket PTY 会话，执行终端命令，按资产名称查询会话并结束会话。
11. 生成、下载并校验操作审计证据。
12. 验证另一个控制范围的 Token 无法读取拓扑、运行实例、操作、资产和会话。
13. 通过正式 API 销毁运行环境，确认 Docker、OVN 和 OVS 无本轮残留。

结果摘要：

```text
assetCount: 2
apiDatabaseAgentDockerOvnOvsConsistent: true
wholeNetworkPolicyTargets: 2
serviceAccess: created_reached_revoked
terminal: real_websocket_pty
scopeIsolation: passed
cleanup: passed
auditEvidenceSha256: e97dd04095dd058d623ff59bff3fbbca5affb365ced322c5a98d447ee7dbe151
```

证据目录：`artifacts/teamlab-p1/evidence/teamlab-p1-20260915-111524`。该目录属于本地验证制品，不提交仓库。

## 本轮修复

- 为入口网段创建真实的 OVS internal 端口，使 Worker 可以进入 OVN 逻辑交换机。
- 公网测试端口改为映射到 Agent，而不是不执行转发规则的 API 容器。
- P1 OVS 改用生产一致的内核 `system` 数据面。
- Agent 使用独立 Docker Engine，避免宿主遗留容器影响 inventory 和状态检查。
- 修正 Registry 回环转发、管理员随机密码约束和独立 Docker 一致性查询。
- 流程失败时先通过正式 Runtime API 清理已创建资源，再撤销测试 Token。

## 当前边界

P1 已覆盖 Docker 场景的完整 API 和真实网络执行链。VM、SFTP、VNC、现场物理网卡和多 Worker 隧道不属于这条 Docker P1 流水线，它们继续使用各自的专项验收，不由本报告推断通过。
