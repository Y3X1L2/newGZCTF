# 决策: GuacamoleProxy 签名变更同步

日期: 2026-05-19
参与人: Lead, Follow
状态: ✅ 已解决

## 背景
计划将 GuacamoleProxy.CreateConnectionAsync 从 3 参数改为 5 参数（新增动态用户名/密码），但 IRChallengeController.cs:420 作为唯一调用方，未被列入 Phase 3 的 Modify 列表，会导致编译失败。

## 方案对比
- 方案 A: Phase 3 直接加 IRChallengeController 为 Modify 目标
- 方案 B: 保留旧签名作为 overload，逐步迁移

## 决策结果
选择**方案 A**。IRChallengeController 本来就是唯一调用方，加旧 overload 会增加技术债。

## 影响范围
- 涉及文件: `IRChallengeController.cs:420`, `GuacamoleProxy.cs`, `EnvironmentService.cs`
- 不需要其他节点操作