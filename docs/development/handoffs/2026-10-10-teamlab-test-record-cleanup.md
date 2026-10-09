# TeamLab 历史测试记录清理交接

## 目标与基线

用户明确授权直接从数据库清理历史测试场景和环境，仅保留最新版VM118–121四机场景；不新增删除按钮，不删除比赛、用户、报名或提交。基线main `e8f8d00d`，独立writer工作区复用 `codex/teamlab-test-record-cleanup`；本次无软件源码、迁移或部署变化，工程文档仅本地提交、不自动推送。

## 实际结果与保留范围

已确认的无业务绑定测试集合通过一次精确事务删除63拓扑、75旧release、94runtime、32独立测试scope，共5,553,063行。先按runtime ID清原始分区父表2,754,489 flows、2,628,049 observations及15,948 aggregates，再按现场FK leaf顺序清关联记录；未DROP/TRUNCATE分区，也未声称普通DELETE立即回收物理数据库磁盘。未知通用ApiOperation/jobs/audit和wildcard token grants保留。

当前还剩Topology1/33/34/39/97、4个release、Runtime31/32/34/39（均Destroyed）和唯一Platform scope。核心97/PublicId `01a1021a-b3b3-7fd2-96f5-cc9aa22a2ea5`、release `01a10261-890a-718f-ae86-250e4db86746`/version2、draft revision2与canonical hash `sha256:3211caeadb83e3069222547c990e789bc451074950495b903481a225d8e0e7bb`保持；519–522四VM/5NIC及entry/internal网络定义正确。核心当前无运行副本，本轮未新建或启动实例进行网络/桌面验证。

4个保护场景关联Games24/60/61/66。60有3个参与、61/66各1个，61有4条PenetrationSubmission；不能只凭test/Acceptance名称删除。其GameLabBindings、TeamRuntimeBindings及比赛/报名/目标/提交事实保持，尚待用户明确是否解除这些历史绑定，因此“只剩核心1场景”目标尚未完全达成。后续如获明确授权，可仅对这4组精确备份和小事务清理，不重复本次5百万行备份/事务。

原保留A155/g5、B156/g3在本轮只读开始前已正常Destroyed：B票据 `01a12173-06b0-7b27-81ae-1c58e9d46483` 于16:15:48–16:15:52 UTC成功，A票据 `01a12182-baae-7041-a998-33a21b9921c5` 于16:32:58–16:33:02成功。本轮没有再次Destroy或SQL伪造终态，先核三节点实际TeamLab域/容器/实例盘/XML均0才删除历史实体；此前两套Ready证明仍是15:48的历史验收。

## 资源、备份和事务门禁

唯一物理残留为runtime98/PublicId `01a0b846-1d90-7326-9f4c-e9abe8df2bd4`/g1的一LS四ports。正常Destroyed尾部只finalize、现有plan snapshots为0；已审固定工具核DB终态/队列/引用0、OVN external_ids/generation/digest及switch-port UUID双向一致后仅ls-del此LS，教学3LS1LR及其他ports前后完全等值。旧release `01a0ecbf-b74c-7cf6-ace2-729f457d234a` 的8条prewarm引用经正常archive撤销，临时token已finally撤销；模板1/506–509/519–522元数据和五保护缓存身份保持，没有Registry操作或镜像删除。

精确私有备份在 `/srv/yinyu-data/backups/teamlab-test-record-cleanup-20261010-4a87dcc694a51b12-pre`：44表按固定selectors、同一exported READ ONLY snapshot导出binary COPY/gzip level1，共5,553,063行、300,546,693字节压缩；90份审计小文件54,309字节按原SHA、路径及inode/mtime备份。所有COPY exit0、全gzip CRC/头尾/文件SHA及同snapshot计数通过；manifest SHA `dafb851ce67495da1b9daf777e85dbd4accd2c779b1966833a142e22a749f458`。原已验证全库备份保留，本次没有新全库dump/恢复，也未停Main。

固定SQL工具 `9633f97ed8e6efd78b361b114bbd6566d7d8f2da9c6a370bf75aec020982d309` 已经主任务和FK审核者审查：5秒lock_timeout、单statement300秒，临界表锁阻止新无FK票据/绑定穿越；重核94Destroyed、无活动队列/claim/reservation/cache引用或业务绑定。每项删除数须等于私有备份；core定义、4组业务绑定与共享业务计数事务前后等值。90份审计文件校验备份、路径、inode/mtime、长度/SHA后精确释放，事务失败会恢复原owner/mode/mtime/SHA；COMMIT前持久化attempt台账，确认丢失时先独立锁读44selectors及保护摘要决定提交/回滚，unknown不擅自恢复。本次正常收到COMMIT确认，全部5,553,063行与90文件已真实完成。

## 最终验证与接手

正式API library返回5场景，核心详情4VM/5NIC正确；DB后置flows/observations/aggregates/queue0、迁移150、五来源Ready/LocalNull且原hash保持。Main4fca/前端9a/Agentaf、Main/本机Agent PID337484/264484与NRestarts0保持，首页/Config/manifest200，PG/Redis/Guacamole/Guacd正常；7条普通CTF Guac连接不变。`.30`教学两容器运行/restarts0，教学OVN3LS1LR保持，`.27/.31`实际TeamLab域/overlay/XML0。`.27`继续停止新调度但服务active，`.30/.31`可调度；旧2008R2许可、自行关机与VM CPU单位问题仍为稳定性backlog，没有修改激活、rearm、时钟或来宾网络。

证据仅Git外 `D:/Work/YINYU-TeamLab-Cleanup-20261010/`：`keep-delete-id-plan.proposed.json`、inventory/bindings、`private-backup-latest-progress.json`、`sql-cleanup-result.json`、`final-metadata-readonly.json`、`final-library-api-result.json`、`final-acceptance-receipt.json`及health/worker证明；私有行、审计备份、凭据不进Git，提示词只留本地且未发送。服务端提交台账为上述备份目录的 `sql-delete-result.json`。文档执行git diff --check；没有重复软件全量测试或新增实例验收。只剩4组比赛关联范围待用户回复，不占用生产写入继续等待，也不重复已完成集合。
