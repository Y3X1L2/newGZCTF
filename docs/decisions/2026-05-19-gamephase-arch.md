# 决策: GamePhase 架构方案

日期: 2026-05-19
参与人: Lead, Follow
状态: ✅ 已解决

## 背景
原计划用全局中间件 (GamePhaseMiddleware) 拦截请求检查阶段状态。但 IRChallengeController 路由 `/api/v1/ir-challenges` 中不包含 gameId，无法从 URL 提取。同样 ScenarioController 路由也不含 gameId。

## 方案对比
- 方案 A: 控制器层面检查 — 每个控制器动作前调用 GamePhaseService.CheckAsync()
- 方案 B: 修改路由，在 URL 中嵌入 gameId（破坏现有 API）

## 决策结果
选择**方案 A**。新建 GamePhaseService，控制器层面调用。虽然代码量多于中间件，但每个端点可根据上下文精确获取 gameId（有时在 query string，有时在 body，有时在关联表中）。

## 影响范围
- 涉及文件: `GamePhaseService.cs`(新增), `IRChallengeController.cs`, `ScenarioController.cs`, `SubmissionController.cs`, `GameController.cs`
- 不需要其他节点操作