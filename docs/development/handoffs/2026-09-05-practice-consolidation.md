# 练习模块 PR 整理与验证

## 结论与范围

从 `main@bbd5a5d4da8488ad4c32c7bf49523f3136e63831` 整理旧 PR #6 的有效增量，
不原样合并历史分支，不改 migration ID，不覆盖历史 Designer/模型快照，不部署生产。
当前产品创建者追踪暂不恢复，原始实现继续由归档引用和旧 PR 保存。

## 进度

- [x] 为旧 #6、#7 保留归档引用。
- [x] 12 个本地文件逐个校验哈希后移入 Git 排除的 `.local-notes/legacy-20260905`。
- [x] 原本地 AGENTS 修改由 `archive/local-instructions-20260905` 保存。
- [x] TeamLab 测试使用独立临时目录，明确覆盖 nftables/iptables，不改变生产默认值。
- [x] 补本地附件引用计数、真实关联检查及共享附件保护。
- [x] 补停用题管理列表、容器导入字段和镜像模板解析。
- [x] 发布草稿 PR #8，完整后端 CI 尚待通过后才标为可合并。
- [x] 提交保留后移除临时工作区，最终只保留一个日常工作区；Git fsck 通过。

## 根因与实现

| 问题 | 实现位置与处理 |
| --- | --- |
| CI 依赖宿主机路径和 nft 工具 | `TeamLabCommandBuilderTests` 隔离路径与能力探测，保留生产默认值 |
| 管理列表看不到停用题 | `ExerciseController` 独立教师管理入口，学生列表规则不变 |
| 批量导入丢失运行配置 | `ExerciseOpenApiContracts` 补字段，`ExerciseWriteValidation` 统一校验 |
| 模板 ID 不等于可运行镜像 | `ExerciseManagementService` 校验 Ready/Docker，解析 RegistryUrl |
| 共享附件引用计数偏低 | `BlobRepository` 和 Exercise 绑定流程统一取得/释放引用，同 hash 更新不销毁原附件 |
| 删除与绑定交错 | 文件 hash advisory lock、文件行锁；头像/海报/课程封面绑定事务覆盖关系保存 |
| 重试上传重复计数 | `AssetApplicationService` 在事务内保存幂等操作，复用内容不重复递增计数 |
| 有效授权被历史清理删除 | `TerminalHistoryCleaner` 保留仍有文件实体的成功资产上传操作 |

物理清理只针对没有真实引用的资产。事务中的附件释放保留对象字节，避免回滚后丢文件；
这些闲置字节需要提交后显式清理，本次未实现自动垃圾回收。
物理删除发生在元数据提交之后；存储失败可能留下孤立对象，但不会删除仍有业务引用的文件。

## 迁移红线

`20260815012026_AddExerciseCreatorTracking` 已在主线恢复，原始提交是
`c6a2b7f4b5637f5622cfa6bdb42624d5242a0c80`。
本次不复制旧 Designer，不引入重复建列的 `AddAssetUploaderAudit`、
`AddGameChallengeCreatorTracking`，不操作生产 `__EFMigrationsHistory`。

## 本地布局

目标为 `D:\newGZCTF` 一个工作区，活动分支 `codex/practice-consolidation`。
未合并历史保留为轻量 Git 引用，不删除旧 PR 的远端分支。
本地资料仅在 `.local-notes`，由 `.git/info/exclude` 排除；不提交凭据、tar、数据库或运行日志。

## 验证与后续门禁

本地主站和 Agent Release 编译通过，前端严格类型检查、全量 87 文件/280 项测试、
locale/lint/架构检查和 Vite 生产构建/制品预算通过。
全量结果以该分支 CI 为准；原主线有 8 项 TeamLab 失败，本次不跳过测试或降低门禁。
提交 `3424125` 的 CI 单测 971/971 通过；集成测试 263/276 通过，13 项失败中
2 项为待重新生成的 OpenAPI 快照，其余涉及既有 VM 路由/图片接口和 TeamLab 契约。
旧测试调用 `/instance-credentials`，当前主线路由为 `/remote-access`；TeamLab lease
返回 201 的现行契约与要求统一 202 的测试冲突，不能为使本 PR 变绿擅自更改产品契约。
图片 GET/DELETE 的 500 原因仍需异常栈确认，不报告为已修复。
后续已离线生成 OpenAPI 快照，并补二进制上传 schema/必需摘要头断言，等待 CI 复核。
覆盖率只排除自动生成的迁移 Designer/Snapshot；手写迁移 Up/Down 和所有测试继续执行。
本机 Docker 引擎未运行、测试包缓存不完整，未未经批准安装依赖。
新增 PostgreSQL 用例覆盖共享附件、事务回滚、并发引用、删除与封面绑定交错和授权历史保留。

生产服务器当前不可用，不进行生产验收或发布。后续需在新鲜生产备份副本验证，
并完成真实题目创建、附件读取、实例启动/销毁和权限验收；本地/CI 通过不能替代这些步骤。
