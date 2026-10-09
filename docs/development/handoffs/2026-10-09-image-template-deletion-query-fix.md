# 镜像模板删除查询修复

## 基线与范围

任务分支 `codex/image-template-deletion-query-fix` 从 `origin/main 32356555bdc217eec2404946badec90c509b63d2` 创建，使用独立 managed worktree。只修改 `ImageDistributionService.CleanupTemplateForDeletionAsync` 的 SQL 排序位置，并增加真实 PostgreSQL 回归；没有 API、前端、实体或迁移变更。本任务没有连接或修改生产，没有合并或发布。

## 已核实问题与修复

正常删除先进入 `Deleting`，随后清理分发缓存。原查询先投影为位置参数 record `TemplateCleanupCandidate`，再按该 record 的属性排序，Npgsql/EF 无法将排序翻译为 SQL。查询编译发生在数据库返回结果之前，所以没有分发记录的历史模板也会失败，Registry 和本地源文件删除尚未执行。

将 `OrderBy(ImageTemplateId).ThenBy(WorkerNodeId)` 放在实体查询上，再投影候选 record，保持原有候选顺序、锁、有效 claim 保护、引用释放和 Agent 库存确认。对同一删除链的相邻 projection/order 表达式核对后没有第二处同类 SQL 查询；引用展示排序是在 materialize 后的客户端集合上执行。

## 验证

`ImageTemplateDeletionQueryPostgresTests` 使用 PostgreSQL 16 Testcontainers，实际调用生产的 `ImageDistributionService`。无分发记录场景通过 distribution-only artifact adapter 将真实删除用例和 EF catalog 接到实际查询；有记录场景只有远端硬件调用以库存响应替代。Registry 和本地源文件物理清理不在此 fixture 范围。新增三个场景：

- 无分发记录：正常删除完成，不调用 Agent。
- 两条空闲分发记录：实际 SQL 查询、锁及 claim 更新通过，两节点缓存清理响应确认不存在，引用释放且 claim 清空。
- 已有有效 claim：保留原引用和 claim，不调用 Agent。

原源码下三个场景均失败，其中无记录和有记录场景直接复现同一 LINQ 翻译异常，有效 claim 场景也被该异常挡在预期保护之前。修复后三个场景全部通过，无跳过；真实 PostgreSQL 覆盖了原有 InMemory 单测与使用替代 artifact cleaner 的退休集成测试未经过的查询。

- Release solution build 通过，0 错误；现有依赖和测试分析器警告未在本任务处理。
- 相关分发、catalog 删除及 TeamLab 镜像引用单测 32/32 通过，无跳过。
- `git diff --check` 通过。

Git 外验证证据保存在 `D:/Work/YINYU-Image-Lifecycle-20261009/deletion-query-fix-verification/`：`before/query-before.trx`、`after/query-after.trx`、`unit/query-related-unit.trx` 和 `release-build-restored.log`。首次以 `--no-restore` 构建 fresh worktree 因其它项目尚无 assets 文件退出，随后正常 restore/build 成功，首份日志保留为 `release-build.log`。

本次没有重跑前端或全量后端测试，因为没有相关契约、迁移或前端变更；已有完整基线不代替本次定向 PostgreSQL 验证。真实 Agent、Registry 和生产源文件删除仍由授权发布执行者验收，不能用这些测试声称真实物理清理已完成。

## 交接

改动已在任务分支提交并推送，供 root 审查。接手者先核对提交与远端关系，审查最小 diff 和红/绿 PostgreSQL 证据，再决定是否制作独立可识别发布物。生产重试使用正常删除或既有 reconciliation 路径，保留 `Deleting` 事实；不通过 SQL 改状态、直接删除源文件或跳过 Agent/Registry 清理绕过错误。
