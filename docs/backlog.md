# 后续事项

维护日期：2026-10-11。这里保存尚未闭环或需要重核的事项，不把历史计划中的所有条目自动继承为待办。接手时核对最新主线与现场，并在任务记录中声明负责人、范围和验收方法；关闭后保留证据链接，删除旧的待办描述。

| 编号 | 事项与当前边界 | 接手时的验收 / 证据 |
| --- | --- | --- |
| RUN-01 | `.27` 来宾网络回读曾失败；改在 `.31` 通过不代表根因解决。10-10 记录已恢复三节点调度旗标，不能继续沿用 10-09 的停止调度结论 | [第二批发布](records/2026-10-09-image-lifecycle-second-rollout.md)、[后续发布](records/2026-10-10-teamlab-delete-rollout.md)；独立复现、修补和网络回读 |
| RUN-02 | VM 执行 vCPU 与节点 cpuUnits 报告语义不一致；自动模式下 maxVms 不能当硬限；Docker 展开预算、运行增长和磁盘硬配额仍有边界 | [第二批发布](records/2026-10-09-image-lifecycle-second-rollout.md)、[分盘修复](records/2026-10-07-teamlab-workload-storage-rollout.md)；真实 CPU/内存/文件系统准入与拒绝测试 |
| RUN-03 | `guest_network_control_failed` 等诊断缺少精确资产 / 异常分类；Main 重启后调度旗标恢复原因未独立核实 | [第二批发布](records/2026-10-09-image-lifecycle-second-rollout.md)、[删除发布](records/2026-10-10-teamlab-delete-rollout.md)；结构化诊断与维护围栏回归 |
| DATA-01 | 两条 Theory migration 来源尚未恢复；RemoveDestroyedTeamLabUdpMappings 缺元数据。源码仍存在该缺口，旧 134/132 计数已过时 | [迁移交接](records/2026-09-02-migration-drift-reconciliation.md)；新鲜备份上核 discoverable migrations / 历史差异，验证前向升级和恢复，禁止伪造历史 |
| DATA-02 | 9 月曾有指标 DbUpdateConcurrencyException；没有找到根因已闭环证据。当前指标端口仍 Degraded，但本轮未读取健康详情，不能断言同一根因 | [历史审计](records/2026-09-07-pr9-review-merge.md)；定位最新健康项、失败批次重试与并发回归 |
| IMG-01 | 管理员/课程 VM 导入仍有写 LocalFilePath 的入口；既往来源迁移不能证明未来不生成本地主副本 | [第二批发布](records/2026-10-09-image-lifecycle-second-rollout.md)；统一持久来源、摘要固定、导入与重建回归 |
| IMG-02 | 受控 Registry GC 已执行，自动 Blob GC、upload 遗留治理、冷缓存 TTL 与下载/运行增长预算未完整闭环 | [镜像发布](records/2026-10-09-image-lifecycle-rollout.md)、[第二批发布](records/2026-10-09-image-lifecycle-second-rollout.md)；活动/业务引用和 backing 保护、实际释放字节、失败重试 |
| SEC-01 | 旧交接的 SSH.NET 风险需要重核；当前 src/Directory.Packages.props 已为 2026.0.0，不能继续认定旧版本风险未处理 | 对当前锁定版本重新检查公告与调用路径；本轮未运行依赖漏洞扫描 |
| ACC-01 | 多 Worker 故障接管、完整跨节点/现场物理设备、长期监督、复杂注入和生产规模签收不足；已有限定双 Worker / 模拟端点证据 | [9 月容量汇总](records/test-reports/2026-09-18-teamlab-production-and-capacity-summary.md)、[H03](records/2026-09-08-h03-hybrid-datapath.md)；区分模拟、控制面与实机范围 |
| ACC-02 | 普通 Windows 冷缓存、完整 RDP/Guacamole 桌面、剪贴板及隔离销毁的最新版本附加验收为 NOT_RUN；旧 2008 R2 许可导致自行关机；当前 runtime 契约不支持 Windows SFTP/files | [第二批发布](records/2026-10-09-image-lifecycle-second-rollout.md)、[运行事实契约](modules/teamlab/reference/runtime-capabilities.md)；合格镜像、双实例与桌面实测 |
| ACC-03 | AWDP 真实攻击/修补/异常恢复与 Portal SSO 跨网联调仍需专门验收；外部门户不在本仓库 | [AWDP 手册](modules/awdp/guides/manual-acceptance.md)、Identity 模块与目标门户契约 |
| ACC-04 | 自主练习内容运营、真实应用回退及节点整机重启持久性仍缺最新完整签收 | [稳定基线历史](records/2026-09-08-stable-baseline.md)、[最新培训发布](records/2026-10-11-training-runtime-mainline.md)；保留现有用户实例，单独安排授权验收 |
| DOC-01 | 模块目录、11个开发地图、TeamLab专题与自动门禁已在任务分支完成，等待PR #28审查合入 | [实施记录](records/2026-10-14-documentation-implementation.md)、[文档维护](architecture/documentation.md)；以最终CI和审查确认纳入主线 |
| DOC-02 | 旧商业能力地图的会话重启清理/退避/URL恢复、批量UI、H02持续监督及协议事件仍需逐项重核，不能因旧计划归档就当已闭环 | 原审计材料保留到历史目录；按当前 application、Agent 持久化与故障恢复实测核对，明确已替代/仍缺/已验收，禁止直接恢复旧实现 |

同一事项只有一个当前描述。前端专属接口细项继续维护在 [前端契约缺口](architecture/frontend/backlog.md)，这里仅引用，不复制其清单。任务分配和执行过程放在对应 handoff 或 PR；未声明接手者的条目为未分配，不据此声称有人正在实施。
