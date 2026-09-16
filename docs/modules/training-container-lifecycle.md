# 课程实验容器生命周期

## 实例与队列

课程实验通过 `TrainingCourseChallenge` / `TrainingCourseChapterChallenge` 关联课程拥有的 `ExerciseChallenge`，学员实例由 `(UserId, ExerciseId)` 唯一标识。不同实验使用不同的实例、容器和动态 Flag；同一实验重复创建返回现有有效容器或复用现有创建票据。

创建、延期和销毁继续使用 `DeploymentQueueTicket`，课程票据类型为 `TrainingContainer`。课程权限和章节归属由培训入口验证，容器操作由 `ExerciseInstanceRepository` 和运行底座执行。

## 数量限制

课程使用独立配置 `TrainingContainerPolicy:MaxContainerCountPerUser`，默认 **3**，按用户跨课程统计；`0` 表示不限数量，负数导致启动校验失败。可在发布配置的 `TrainingContainerPolicy` 节设置 `MaxContainerCountPerUser`，或使用 `GZCTF_TrainingContainerPolicy__MaxContainerCountPerUser` 环境变量；系统设置页面暂未提供该字段。

- 已关联容器和未生成容器的活跃 Create 票据共同占用额度；同一实例只计一次。
- 延期、停止、销毁和终态票据不额外占用创建额度。
- 达到课程额度后拒绝新建，并提示先停止或销毁已有实例；不自动销毁其他课程实验。
- 自主练习仍使用 `ContainerPolicy:MaxExerciseContainerCountPerUser`，只统计自主练习。仅在 `AutoDestroyOnLimitReached=true` 时保留原有自动清理行为，不能选中课程实例。
- 两类操作继续在用户额度锁和实例运行锁内检查，课程仍受统一调度器的节点容量和全局准入约束。

## 动态 Flag

动态容器的新实例在读取时初始化独立 Flag。历史记录可能已经 `IsLoaded=true` 但没有有效 `FlagContext`，例如题目类型变更后的旧实例。新容器创建前，在实例运行锁内再次检查，缺失或空白时生成并持久化独立 Flag，再将同一值传入 `ContainerConfig.Flag`。没有 Flag 模板时使用已有默认随机生成规则。

已有正常 Flag 和运行中的容器保持原值；旧 `TestTeamHash` 预览值仍按原有无容器修复路径处理。运行中的坏实例需要先通过正常停止/销毁流程清理，再创建以触发修复。Flag 不应关联回题目定义列表，不得将实际值写入日志或报告。

Docker 执行面通过 `GZCTF_FLAG` 环境变量传递该值。镜像启动程序仍负责消费环境变量；平台注入成功不能代替镜像内写入路径、权限和应用启动契约的验收。

## 验证与发布

`TrainingContainerLifecycleTests` 覆盖额度隔离、重复创建、排队计数和缺失 Flag 修复；`InstanceFlagIsolationTests` 验证 PostgreSQL 持久化和入队幂等；`TrainingContainerDockerTests` 使用无攻击内容的 BusyBox HTTP 服务验证三个实例同时运行、不同端口、注入值与数据库一致及单实例销毁隔离。

本修复无数据库迁移、无 Agent 协议变化、无前端布局或生成 API 结构变化。发布遵循 [维护窗口流程](../operations/vnext-maintenance-window-rollout.md)。完成源码测试不等于现有主站已经更新；现场状态与验收范围见对应任务交接记录。
