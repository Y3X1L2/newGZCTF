# 培训课程动态题 Flag 标签重复问题修复说明

## 问题范围

- 页面：`/training/courses/3/chapters/4`
- 区域：章节实验的“提交 Flag”
- 现象：动态 Docker 题显示多个 `Flag 0` 切换标签，并出现横向滚动条。

## 正确行为

该题属于动态容器题。系统会为当前用户的练习实例生成一个专属 Flag，因此页面应显示：

- 一个 Flag 输入框；
- 零个 Flag 切换标签。

只有配置了多个静态 Flag 的题目才应显示 Flag 切换标签。

## 根因

`ExerciseInstanceRepository.GetInstance` 在加载练习实例时会同时加载练习题的 `Flags` 导航集合。动态容器实例创建后，每个用户的专属 `FlagContext` 都会关联到同一道 `ExerciseChallenge`。

`TrainingCourseChallengeDetailModel.FromInstance` 原先只根据 `ExerciseChallenge.Flags.Count > 1` 判断是否返回多 Flag 步骤。访问过该题的用户数量增加后，动态实例 Flag 会在导航集合中累积，并被错误地当成静态多 Flag 配置返回给前端。动态实例 Flag 的 `OrderIndex` 默认为 `0`，因此前端将这些条目全部显示为 `Flag 0`。

## 修复内容

课程挑战详情模型现在仅为非动态题返回已配置的多 Flag 步骤：

- `DynamicContainer`：不返回 Flag 切换步骤；
- `DynamicAttachment`：不返回 Flag 切换步骤；
- 静态多 Flag 题：继续返回步骤，并保持按 `OrderIndex` 排序。

本次修复不修改数据库结构，也不删除动态实例的 `FlagContext`。这些记录仍用于对应实例的判题。

## 自动化验证

新增 `TrainingCourseChallengeDetailModelTests`，覆盖：

1. 动态容器存在多个实例级 Flag 时，详情模型的 `Flags` 为 `null`；
2. 动态附件存在多个实例级 Flag 时，详情模型的 `Flags` 为 `null`；
3. 静态多 Flag 题仍返回两个配置步骤，并按 `OrderIndex` 排序。

验证结果：

```text
定向回归测试：3/3 通过
GZCTF.Test 全量单元测试：508/508 通过
```

## 发布后验收

1. 使用有课程学习权限的账号打开 `/training/courses/3/chapters/4`。
2. 定位到动态 Docker 实验的“提交 Flag”区域。
3. 确认页面只有一个 Flag 输入框，不再出现 `Flag 0` 标签和标签横向滚动条。
4. 创建实例并提交该实例对应的正确 Flag，确认判题仍返回成功。
5. 打开一道人为配置了多个静态 Flag 的题目，确认步骤标签仍按配置顺序显示。
