# 镜像按需分发候选交接（2026-10-08）

## 任务目标与基线

从普通导入、业务发布和运行创建移除隐含的全合格节点预分发，保留既有统一队列选节点后
按需准备镜像。显式管理员预热与普通启动分开；制品就绪、节点缓存就绪和来宾业务验收是不同事实。

- 起始提交及远端 `origin/main`：`2ba6e8d8eafac9f381a59e701357863db717754c`。
- 任务分支：`codex/image-demand-distribution`。
- 独立 worktree：`C:/Users/Cloud/.codex/worktrees/image-demand-distribution/newGZCTF`。
- 本任务不部署、不访问或删除服务器镜像，不修改数据库迁移；候选由主任务整合后审核。

## 实现范围

- `ImageImportOperationHandler`、旧后台 `ImageTemplateController` 各导入入口不再 fanout。
  标准导入的最终进度改为 `image-ready` / 1 of 1，不再声称已排队全节点分发。
- `TrainingCourseAdminController` 的模板导入/绑定、题目创建/修改/绑定与
  `AcademicImportOperationHandler` 的课程导入保留业务绑定，移除隐含预分发。
- `EditController` 的比赛编辑、题目创建/编辑不再 fire-and-forget 全比赛镜像分发；
  `ChallengeMutationOperationHandler` 的普通导入/删除不再分发。删除仍保留释放旧比赛缓存引用，
  不把清理改成再次铺满；计分板刷新与持久化操作恢复保持原链。
- `TeamLabReleaseService` 的新发布及幂等重复发布、`CreateTrial` 不再提前排全节点预热。
- 原 `FleetContainerManager`、`FleetVmService` 和 `TeamLabShardDeploymentService` 已在实际选节点后
  调用节点专属 Ensure；本期保留该执行路径及 `DeploymentQueueTicket`、`ImageDistributionRecord`，
  没有建立新队列。
- 显式 TeamLab templates/release Prepare 仍可主动触发；底层显式分发服务保留。
  本期不新增管理员目标选择/预热租期 API。
- rollout coordinator 原本在普通目标替换、重建、打开访问/恢复处理时也会全节点预分发。
  本期将其准备阶段改为校验主制品，目标照原队列逐实例选节点后按需 Ensure；源不可用仍阻挡。
  rollout Prepare 本身是批量准备运行环境的动作，不再额外预热所有 Workers。

## 就绪与诊断语义

`TeamLabReleaseImagePreparationService.GetPreparationAsync` 不再把缺缓存或无关节点的旧分发失败
直接当成全 release 的启动阻碍。它验证主模板 Ready、摘要、种类及可用能力节点；每模板缓存数和
失败信息仍根据真实分发记录计算，不伪造成全部就绪。

- `ReadyToStart=true` 表示具备按需启动前提，允许所选节点下载缺失镜像。
- `State=onDemand` 表示仍需按需下载；`readyToStart` 表示各模板已有至少一个合格节点缓存。
  两者都不是一次具体 placement 的全矩阵 Ready，也不是容量资源预留。
- 无合格节点、源模板不可用或冻结摘要改变仍 `blocked`。
- 在 selected-node Ensure 执行时发生的真实下载失败仍使该运行票据失败，保留具体原因。
  release preview 不暴露具体放置节点，不能从聚合失败数推断失败属于本次选中节点。

`TeamLabAdminQueryService` 的 readiness 此前不由 cache counts 决定，但也未检查源模板状态。
本期在现有规划之前调用现有模板校验，源模板未就绪/摘要改变返回真实 blocker；能力/容量诊断保留。
前端只调整说明与手动按钮文案：普通启动只下载选中节点，手动“预热合格节点”会额外向所有合格节点分发。
接口字段结构不变，无生成 API 文件修改；外部 `State` 为字符串，本期增加 `onDemand` 并改变
`ReadyToStart` 为允许按需启动的语义，调用者不得再把该字段解释为实际缓存已全部准备完成。

## 存储边界与旧入口后续收口

标准 qcow2/Docker 导入已有可靠 Registry 来源、摘要和元数据，成功后清理暂存文件。
旧 VM Upload、Local 和 Archive 仍可能以 `LocalFilePath` 为唯一源，并在真正分发时懒迁制 Registry。
本期仅去掉预分发，不删除这些唯一源，不宣称主站已无全部本地源文件；源镜像仍被引用时须有可靠下载来源。
Registry 制品按业务引用保护，不要求无条件永久保存；其全局删除/GC 由独立生命周期任务处理。

若后续真实清单证明所有 LocalFilePath-only 模板均已受控删除或成功迁移，且用户决定新导入只接受
标准 qcow2/Docker，可进一步收口：

1. 移除后台普通 VM Upload、import-local、Archive 入口及课程内同类入口，改为一个标准导入用例。
2. `LocalImageImporter` 的文件复制/登记职责、`ImageStorage.SaveImageAsync` 与对应 DI 可移除。
   `DetectOsType` 是被其他代码使用的名字推断工具，若保留相关入口需先提取，不能连类直接删除。
3. `ImageStorage.DeleteImageAsync` 仍被 `ImageTemplateArtifactCleaner` 调用；须确认所有可删源已处理，
   修改清理端口、覆盖恢复和残留场景后才能移除该依赖。
4. `ArchiveExtractor` 的解包、OVA/VMDK→qcow2 转换仍有镜像制作价值；只有正式决定不再接受
   这些格式，或将转换移到制作工具时才删除服务器端路径。删除旧模板本身不等于它没有用途。
5. `VmArtifactStore` / `VmImageRegistryService` 的旧源恢复分支可以在核验无旧源后单独收口；
   有效 OCI/qcow2 导入、不可变摘要、`VmPreparedArtifact`、规范导入暂存清理、QGA/cloud-init 与
   节点缓存/实例差异盘都是当前能力，不能因“旧镜像已删除”一起裁掉。

此收口是后续任务，不在本提交中大幅移除格式或运行模式。

## 验证

| 项目 | 结果 |
| --- | --- |
| `dotnet build src/GZCTF.Test/GZCTF.Test.csproj -c Release` | 通过，含主站/Agent/Test，已有依赖与分析器警告 |
| 导入无副本、重复恢复、发布幂等、冷缓存/失败缓存、源不可用/无节点、rollout按需/源失败、selected Ensure、Fleet/Controller 定向单测 | 57/57 通过，0 skip |
| OpenImageApiTests PostgreSQL/Testcontainers 定向 | 13/13 通过，0 skip |
| 版本页前端行为测试 | 5/5 通过 |
| locale/lint/strict TypeScript/frontend architecture | 通过 |
| `git diff --check` | 通过 |
| 真实两套环境与不选节点无下载副作用 | NOT_RUN，留主任务候选批准后验收 |

本子任务未重复全量后端/前端测试、前端生产构建或四宽度视觉验收；主任务整合后执行充分门禁。
新增前端改动限说明文案，没有视觉布局变更。没有以 mock 或元数据 cache Ready 当作真实运行验收。

## 当前状态、限制与交接

实现及定向门禁完成；未推送、未合并 `main`、未部署，worktree 保留。最终提交身份由主任务整合记录。
并行回收任务修改 `ImageDistributionService` 引用 reconcile、普通 destroy 释放回调及准备释放 cutoff，
本任务未修改其文件；`TeamLabReleaseImagePreparationService` 保留 Release/Scope 入口，其新增
ReleaseBefore 方法应与本任务投影改动正常合并。

真实验收至少应证明：选定节点下载/启动正常；未选且低空间节点不新增分发记录、缓存和临时文件；
冷缓存、命中、所选节点真实失败都如实报告；销毁与重新创建仍能恢复。不能由一次 Ready 推断完整生命周期稳定。
容量准入、取消传输、冷缓存回收和 Registry GC 不是本子任务已实现内容，按整合后的各阶段验收范围记录。
