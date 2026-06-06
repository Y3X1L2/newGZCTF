# 决策: Strategy 调度键设计

日期: 2026-05-19
参与人: Lead, Follow
状态: ✅ 已解决

## 背景
原计划 IVerificationStrategy 接口使用 `ScoringSubmissionType HandledType` 作为调度键。但同一 Flag 提交可以搭配 AutoExact/AutoRegex 两种验证方式——SubmissionType 和 VerificationMode 是正交维度。

## 决策结果
将调度键改为 `VerificationMode HandledMode`，Engine 根据 `ScoringRule.VerificationMode` 选择策略。策略的映射：FlagHashVerification→AutoExact, RegexVerification→AutoRegex, ScriptVerification→AutoScript, ManualReviewVerification→ManualReview

## 影响范围
- 涉及文件: `IVerificationStrategy.cs`, `UnifiedScoringEngine.cs`, 4 个策略实现
- 不需要其他节点操作