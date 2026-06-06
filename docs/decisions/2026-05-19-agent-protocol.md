# 决策: Agent ↔ 管理端通信协议

日期: 2026-05-19
参与人: Lead, Follow
状态: ✅ 已解决

## 背景
Phase 4 计划创建独立 Agent 进程部署在工作节点上，但未定义主服务器与 Agent 之间的通信协议。当前 DockerProvider 是单 DockerClient 单例，FleetManager 选择远程节点后无法下发指令。

## 方案对比
- 方案 A: HTTP REST Pull（Agent 轮询管理端取指令）— 简单可靠，穿透防火墙 NAT 无障碍，轮询间隔 5s
- 方案 B: SignalR 双向（WebSocket 长连接）— 低延迟但需 Agent 维护连接，NAT 穿透困难
- 方案 C: gRPC Stream — 高性能但需额外基础设施，Agent 端复杂

## 决策结果
选择**方案 A**（HTTP REST Pull + HMAC 签名），理由：Agent 所在节点在公网/防火墙后，Push 模型不可靠；Pull 轮询简单可靠；Agent 侧仅需 HTTP client

## 影响范围
- 涉及文件: `FleetManager.cs`, `DeploymentTarget.cs` (新增), Agent 项目全部
- 不需要其他节点操作