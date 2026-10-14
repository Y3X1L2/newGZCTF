# 统一票据、容量与恢复

运行任务的事实是 DeploymentQueueTicket。各业务提交规范化身份与资源需求，数据库保存请求、operation 关联、代次、原节点和阶段；Redis 只唤醒 worker 或缓存观测，丢失唤醒不应丢失工作。

## 从受理到执行

RuntimeAdmissionPolicy 对 Create 的 owner 待排队数量做限制；RuntimeQueueSelector 先处理非 Create 操作，再按创建者现有并发数和时间轮转选取。相同 SubjectConcurrencyKey 已有 Scheduling/Scheduled/Running 工作时不再同时选取，避免同一资源两个控制操作互相覆盖。

当前 RuntimeSchedulingOptions 默认每队 4、每用户 2 个并发 Create，每 owner 32 个 Pending Create；选择窗口 512、调度批次 64。这是可配置运行参数，不是课程“最多可拥有多少实例”的业务额度。队列准入与课程/练习独立额度都必须满足，不能混为同一限制。

调度独立核对 Docker/KVM、节点在线与调度开关、CPU/内存/磁盘/端口及已预留容量。节点能力满足只是候选资格；容量预留成功后才为选中节点准备镜像并执行。资源向量和实际 inventory 的关系由 NodeCapacitySnapshotService、FleetCapacityReservationService 与对应 worker 维护。

## 数字状态各用各的模型

| DeploymentQueueTicketStatus | 值 |
| --- | --- |
| Pending | 0 |
| Scheduling | 1 |
| Scheduled | 2 |
| Running | 3 |
| Succeeded | 4 |
| Failed | 5 |
| Cancelled | 6 |

RuntimeOperationKind 为 Create=1、Extend=2、Stop=3、Reset=4、Destroy=5、Pause=6、Resume=7、AssetControl=8、Update=9。DeploymentStage 是更细的执行阶段，源码见 [票据实体](../../../../src/GZCTF/Models/Data/DeploymentQueueTicket.cs)；ApiOperation 0–3 与 TeamLabRuntimeStatus 0–11 不可替换这里的数值。

## 失败与恢复

恢复先读数据库当前身份和目标意图，再取得节点 inventory。RuntimeFactReconciliationService 生成差异预览或执行显式对账，不从日志文本反推业务状态。主动停止/暂停、旧代任务、节点离线、原生身份冲突和待清理资源必须区别对待；缺少观测不能推定资源不存在，也不能自动拉起用户已停止的资产。

销毁保留准确资源归属并可重入；失败写出真实阶段与安全错误，保留可继续处理的状态。涉及入口租约、镜像引用与容量释放时，完整清理才能收敛，不能只完成数据库状态更新。恢复动作还须遵守业务权限与运行互斥。

源码路线与测试见 [开发地图](../development.md)，现场步骤见 [运行调度与恢复](../../../operations/runtime-scheduling-and-recovery.md)。多 worker 故障、跨节点规模与磁盘硬配额的实机验收见 [当前状态](../../../current-state.md)，本页只解释已存在的代码与边界。
