# 部署与运维

运维手册说明如何完成一次操作。执行前核对 [当前状态](../current-state.md)、现场身份、授权和回退；历史记录中的服务器配置或许可不自动适用于本次任务。

| 任务 | 手册 |
| --- | --- |
| 主站发布、升级和回退 | [维护窗口流程](vnext-maintenance-window-rollout.md) |
| 安装节点、核对Docker/KVM与存储 | [WorkerNode部署](node-deployment.md) |
| 安装镜像Registry | [Registry部署](registry-deployment.md) |
| Windows镜像与远程访问 | [简明步骤](windows-vm-quick-deployment-guide.md)、[完整参考](windows-vm-deployment-guide.md) |
| 动态公网入口与ACK | [入口映射](public-gateway-port-map-ack.md) |
| 调度失败与实例恢复 | [运行恢复](runtime-scheduling-and-recovery.md) |
| 数据库、Redis与审计 | [数据库治理](database-governance-operations.md)、[Redis恢复](redis-deployment-and-recovery.md)、[审计恢复](observability-audit-recovery.md) |
| Agent来宾网桥恢复 | [恢复步骤](agent-guest-network-recovery.md) |
| 外部系统调用平台 | [Open API调用](open-api-integration.md)、[API标准](../architecture/external-api-standard.md) |
| TeamLab专项操作 | [模块指南](../modules/teamlab/README.md) |
| AWDP真实攻击与修补验收 | [人工验收](../modules/awdp/guides/manual-acceptance.md) |

节点与Registry安装脚本仍在 `docs/node-deployment/` 和 `docs/registry-server/`，路径被现有测试和运维命令使用；本轮只集中其阅读入口。生成OpenAPI快照仍在 `docs/commercialization/openapi/`，由现有生成/兼容门禁维护，不手工修改。
