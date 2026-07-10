# 决策: Double-decay 修复架构

日期: 2026-05-19
参与人: Lead, Follow
状态: ✅ 已解决

## 背景
原计划创建 `static ScoreDecayCalculator.Apply()` 统一衰减计算，但 static 类不能解决 double-decay 根本原因——SubmissionController (创建时衰减一次) 和 ScoringService (统计总分时衰减一次) 两处都在调用衰减形成双倍衰减。

## 决策结果
1. ScoreDecayCalculator.Apply 保留作为唯一的衰减计算函数
2. ScoringService.CalculateTotalScoreAsync 改为只读 Submission.Score 已衰减值（取 best score），不再重新计算衰减
3. ScoreDecay 只在 UnifiedScoringEngine.ProcessSubmissionAsync 中应用一次（写入 Submission.Score 时）
4. ChallengeSubmissionType 的 MaxAttempts/ScoreDecay 合并到 ScoringRule，不重复定义

## 影响范围
- 涉及文件: `ScoringService.cs:73-81`, `SubmissionController.cs:524-532`, `ChallengeSubmissionType.cs`
- 不需要其他节点操作