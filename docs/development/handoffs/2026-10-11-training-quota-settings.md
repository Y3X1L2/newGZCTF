# 培训容器动态额度设置

## 目标与基线

在 `/admin/system` 添加培训模块每用户容器启动上限，继续沿用独立培训策略、配置存储和统一队列。用户追加要求将 `/practice` 与 `/admin/exercises` 改为服务端分页，保留筛选和正确总数。不进行解题、读取题目 Flag 文件或提交真实 Flag。

任务从最新 `origin/main 03440c51` 创建 `codex/training-quota-settings`，独立 worktree `D:/Work/newGZCTF-training-quota-settings`。现场只读核验 `.27` 仍运行 `training-runtime-quota-60170a5266f684581f21e95013687b0e7367515f-20261011`，Main DLL `e2265545…`、首页200。

## 改动与不变量

- 内部配置 GET/PUT 增加可选 `TrainingContainerPolicy`，非负整数校验在写入任何区段前执行；继续保持管理员权限和部分区段保存。
- 使用现有 `Configs` 与配置重载机制，下一请求的 OptionsSnapshot 动态生效；没有修改运行准入、调度器、队列执行、Flag、节点容量或 Agent。
- 前端增加独立额度面板与状态 hook，0保持为不限个人数量，空值、小数、负数和溢出不可保存；失败保留草稿。回读仅更新实际变化的其他区段，避免丢失未保存修改。
- 内部 OpenAPI 由当前源码及其原有 NSwag/JSON设置导出，再按仓库模板重新生成客户端；保持既有路由大小写生成约定。导出工具、制品与日志保存在 Git 外。
- 无数据库迁移，不改 Agent 协议。降低上限不销毁已有实例，不取消已排队票据；自主练习额度与自动清理保持隔离。
- 新题库分页接口默认20、最大100，数据库先筛选再分页，后台摘要和标签元数据跨页完整。管理端停用题目、学员角色可见范围和课程题隔离保持。旧数组接口和 `/practice/stats` 兼容使用保留，聚合统计优化未纳入本次。

## 验证与发布

状态：`in progress / IMPLEMENTED`。新增后端正式 API/PostgreSQL 配置、动态准入和分页回归、运行实例保留单测和页面交互回归。培训额度切片Release和1493单测、前端locale/lint/类型/架构/120文件418测试/生产构建与体积门禁通过；追加分页后正在重新验证完整门禁。PR、视觉验收和发布结果待执行后补录；未完成的检查不视作通过。

本机 Docker Desktop 不可用，完整 PostgreSQL/Docker 集成由 GitHub Linux Quality 验证，必须等待通过才合并与部署。Git 外证据目录：`D:/Work/YINYU-Training-Settings-20261011`。
