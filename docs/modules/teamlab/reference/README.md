# TeamLab API 与执行参考

按要做的事选专题。外部集成先读 [调用链路](../guides/integration.md)，场景作者先读 [制作交付](../guides/authoring.md)。这里保存字段、约束和排障细节，不要求从头读完。

| 主题 | 参考页 | 承接原操作手册章节 |
| --- | --- | --- |
| 认证、权限 scope、幂等、分页、operation | [API 约定](api-conventions.md) | 1–2 |
| 平台能力和控制范围 | [能力与范围](capabilities-scopes.md) | 3 |
| 草稿、校验、发布、版本、计划 | [拓扑与发布](topology-release.md) | 4 |
| 选中节点准备与显式预热 | [镜像准备](image-preparation.md) | 5 |
| 多目标部署、准备、开放访问、排空 | [Rollout](rollouts.md) | 6 |
| 创建、状态、重置、暂停、恢复与事件 | [运行生命周期](runtime-lifecycle.md) | 7.0–7.5 |
| VPN 授权、下载与撤销 | [WireGuard 访问](wireguard-access.md) | 7.6–7.8 |
| ACL、NAT、带宽、延迟、丢包、恢复 | [链路策略](networking-policy.md) | 8 |
| 设备参数、能力、租约与现场连接器 | [设备与连接器](devices-connectors.md) | 9 |
| 流量、路径证据、抓包、下载 | [观测](observation.md) | 10 |
| 资源池、节点缓存 | [资源池](resource-pools.md) | 11.1 |
| 运维会话与协议 | [远程会话](remote-sessions.md) | 11.2 |
| 外部通知、重放与 operation 查询 | [Webhook 与操作](webhooks-operations.md) | 11.3–11.4 |
| 浏览器管理与 Agent 机器回传 | [管理及内部 API](admin-internal-api.md) | 12–13 |
| 完整请求示例 | [调用示例](walkthrough.md) | 14 |
| 数字/字符串枚举与稳定错误码 | [枚举速查](enums.md) | 15 |
| 文件、单资产控制、会话证据 | [资产操作](asset-operations.md) | 16–18 |

原章节编号保留以便从历史记录找回内容。operation、部署票据、runtime、抓包、远程会话各有状态，不按同一组数字解析。management API 只供浏览器会话，open-v1 用 token，internal API 用机器身份；不能混用认证或把管理端能力自动视为开放契约。

专题级稳定边界另见 [基座契约](foundation-contract.md)、[外部控制面](external-control-plane.md)、[运行事实和工具能力](runtime-capabilities.md)、[组网原则](networking-practices.md)。接口字段以源码 Contracts/Controller 和生成 OpenAPI 为准，修改接口时一起更新对应专题。

[受管网络发布记录](managed-network-rollout-record.md) 保留当时的备份、限定验收和回退证明，不代表当前版本或新部署授权。当前已知现场与未决问题见 [当前状态](../../../current-state.md)。
