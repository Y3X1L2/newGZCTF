# TeamLab 删除入口部署与真实生命周期验收

## 目标与最终状态

用户授权合并并部署场景删除、已销毁运行记录删除及必要 Agent 修正。状态为 **complete / VERIFIED**：普通平台流程完成自有 Docker 场景创建、发布、启动、访问、销毁、删除记录与删除已发布场景。只保留核心四机场景，不新增页面重组，不读取靶机业务，不直接改生产数据库绕过删除条件。

执行工作树 `C:/Users/Cloud/.codex/worktrees/image-demand-distribution/newGZCTF`，工程记录分支 `codex/teamlab-delete-rollout`，最终代码基线 `origin/main c2416f6fbe571d9482cc31df7cbcfb3596025c90`。主工作区、原学习资料和 PVE 源机/快照保留。

## 发布身份与回滚

| 项目 | 实际身份 |
| --- | --- |
| 删除功能源码 / 合并 | `a63937c610b1f9b5b9f41457d3e835f1f240b3ac` / PR #21 `e75e5292894b8d190f1e500ed7434cc303d1198b`，完整tree相同 |
| 预留误判修正 / 合并 | `49948764bd0715142911d5b22c68852ab3d38bb8` / PR #22 `c2416f6fbe571d9482cc31df7cbcfb3596025c90`，tree `27105b06c09177664ea0e77cc33ecee3a4df40ad` |
| 当前Main | `61ff70155b2fae774b6b462a818fed589123bd65ddb421f06e0fad1b19a69ae9`，PID662195、NRestarts0 |
| 前端 | 源 `a63937c6`；manifest SHA `b9fc7f20ed0487af24cca6442bfe747700d4cdf56dce9a8c0189da01d3ca5a7d` |
| 三节点Agent | `87e011cfe91a09c525efbdbdaa6194191517a6f53595339f6ba9da3ace2097fe`；`.27/.30/.31`实际PID641728/1856800/129813 |
| 完整包 | 700文件、215681313字节；SHA `a35c47a7d33dd7ea0b96a2a86a817f4b70b4e9a68cf58bc0e0d427b43dfc08ed` |
| Main补丁delta | 14成员、5380252字节；SHA `9be3afe4ed160448d00031290b52d31c6e0f284bd9d1a3ebb57ef5a4032fc38b`，其余686文件复用 |

当前发布目录：

`/srv/yinyu-data/releases/teamlab-delete-reservation-49948764bd0715142911d5b22c68852ab3d38bb8-20261010T0645Z/publish`

回滚目录为前一完整发布：

`/srv/yinyu-data/releases/teamlab-delete-a63937c610b1f9b5b9f41457d3e835f1f240b3ac-20261010/publish`

`/opt/gzctf/publish` 原子切换，旧目录不覆盖。未执行数据库迁移/恢复；迁移数150及完整迁移列表保持。Main-only补丁复用经过摘要验证的前端、完整Agent和guest-supervisor，真实Main内部Agent摘要探针一致，不再同步Agent。本机Agent在Main-only发布前后为同一PID。私有配置及 `/opt/gzctf/shared/files` 原链接保持。

## 现场问题及修正

自有runtime159已经Destroyed，容器、网关、OVS和OVN清理证据都为零，正常 `/record` DELETE仍409 `runtime_cleanup_pending`。数据库显示预留 `Status=Confirmed`，但 `ReleasedAt` 为实际分配完成时间；生产容量服务的 `FinishReservationAsync` 正常同时填写这两个字段。原引用判断只看Active/Confirmed，误拦截这条已完成记录。

修正不更改历史事实：Active始终阻断，Confirmed仅 `ReleasedAt` 为空时阻断。新增PostgreSQL回归覆盖已释放Confirmed可删、未释放Confirmed阻断、异常Active仍保守阻断。补丁部署后，**同一**runtime159及同一场景正常删除通过，没有重新创建、重新Destroy或手动SQL删除。

## 真实验收

临时身份：topology `01a1247e-ec3c-70d6-9dd9-d7dcd46c5f3d`、release `01a1247e-ef32-7764-b8c0-00cc95f63887`、runtime `01a1247e-f057-7cad-b27c-191d29886f49`（内部159/g1）。使用既有模板495，运行在`.31`，单网卡 `192.168.244.10/24`。

| 操作/检查 | 结果 |
| --- | --- |
| 正常创建、发布、试运行 | Running；容器标签159/g1/web一致，经 `tlsg159-entry` 私网HTTP200 |
| Running删除记录 / 有运行记录时删场景 | 两者409；原实体、代次及资产身份保持 |
| 正常Destroy | Destroyed/g1；对应容器、网关、运行盘/XML、OVS Port/Interface、10类OVN资源均无残留 |
| 删除记录 / 重复删除 / GET | 204 / 204 / 404 |
| 原创建key延迟重放 | 410，不重新发放 |
| 删除已发布场景 / 重复删除 / GET | 204 / 204 / 404 |
| 实际Agent `/api/runtime/inventory` | `.27/.31` Docker/KVM可读；`.30` Docker可读、KVM不支持，原教学容器2；凭据仅服务端内存 |
| 07:26:32 UTC最终控制面 | 首页/Config/manifest200；三节点Online、Fabric/Tunnel健康；原调度旗标均true；活动队列/导入/镜像写者0 |

最终场景库仅核心97，核心public ID `01a1021a-b3b3-7fd2-96f5-cc9aa22a2ea5`，latest v2 `01a10261-890a-718f-ae86-250e4db86746`。draft2、四VM/五NIC、canonical hash `3211caea…`、既有runtime158及模板1/519–522保持；模板495也保留。`.30`两教学容器运行状态及重启次数保持。未改Registry、公网入口或PVE。本次没有重跑四机冷启动/重置、Windows桌面登录或2008R2长时间运行；新删除验收不代替这些既有业务验收。

## 备份证据与后续必须遵循的捕获方法

首次完整备份：

`/srv/yinyu-data/backups/teamlab-delete-a63937c610b1f9b5b9f41457d3e835f1f240b3ac-20261010T0350Z-pre`

两库新exported snapshot、完整dump解码/摘要与隔离全恢复、同snapshot结构/迁移/计数比较通过，验证副本停止。它是本轮实际完整恢复依据。

Main-only补丁新备份：

`/srv/yinyu-data/backups/teamlab-delete-reservation-49948764bd0715142911d5b22c68852ab3d38bb8-20261010T0645Z-capture-v3-pre`

gzctf dump1390504142字节、Guacamole dump94962字节；全文解码、摘要、同已恢复结构/迁移核验及稳定附件捕获通过。**新capture-v3快照没有再次完整恢复**，没有生产数据恢复；不能将这份证明写成独立新恢复。其snapshot ledger SHA `844ef1c06aa58f36dc4430f360703625e4962887610461125f446c2bc5f6fd4f`，checksums SHA `01d7dfae27dae9cf3e635bb1ac7c744df35ffca83ce03c9bdbb1bbac88e0edad`。

在线捕获首次失败源于共享files下一个持续增长的日志，uploads与teamlab附件元数据未变化。旧capture-v2又暴露备份工具问题：exporter读取 `__EFMigrationsHistory` 后长期持有AccessShare，Main启动请求AccessExclusive；脚本却等Main HTTP恢复后才dump并释放exporter，形成互等。真实锁证据为exporter301352挡Main301398；停止**精确自有**备份unit释放整组exporter后HTTP恢复，没有终止Main或业务数据库连接。失败目录和证据保留，未宣称它们备份成功。此故障造成主站HTTP暂不可用，教学容器保持运行不等于门户零中断。

后续维护必须使用以下顺序，不能直接复用旧 `ExportedSnapshot.begin` 的持锁元数据读取方式：

1. exporter只开启只读事务并调用 `pg_export_snapshot`，保持快照，**不读取业务或迁移表**。
2. 独立短事务import同一快照，读取schema、迁移和计数，随后结束该事务；实际确认exporter持有公共关系锁为0。
3. 短暂停Main时捕获稳定附件；tar设有界超时（本次60秒），`finally`立即启动Main，单独验证HTTP恢复。
4. Main恢复后，`pg_dump --snapshot` import仍由exporter保持的快照；dump完成关闭exporter，再核全文解码、摘要与结构。

薄exporter方案已先在真实两库只读验证，随后capture-v3成功：停止至start完成11.770秒、start请求至HTTP12.196秒、总捕获至HTTP23.952秒；最后Main-only原子切换12.886秒，无Agent/教学容器停止。新增迁移仍须按发布规范做真实升级/恢复验证，不能因为本次无迁移补丁复用结构依据而普遍省略恢复。

具体脚本、制品和原始证明保存在Git外的 `D:/Work/YINYU-TeamLab-Delete-Rollout-20261010` 与 `D:/Work/YINYU-TeamLab-Delete-Reservation-20261010`；工程交接仅记录非敏感身份。完整包GitHub下载发生连接重置，最后用已有免密SSH上传，不能写成GitHub中转下载成功。

## 软件门禁与待办

Release、单元1475/1475、完整集成347/347（0 skip）、前端119文件/412测试及全部Quality门禁通过；预留删除PostgreSQL定向19/19通过。部署阶段没有重复这些全量门禁。

维护期间正规PATCH确认三节点false，Main重启后在正式sync前曾观察`.27`回到true；原因没有在本轮独立核实。后续应修正并实测维护cordon的重启保持，不能将本次写成全窗口持续禁止调度。原`.27`来宾网络故障、CPU单位/磁盘硬配额、同组跨节点放置和2008R2许可限制仍是既有事项，未由删除补丁解决。

下一位运维先核当前软链接、真实组件摘要、队列和教学现状；备份采用上述薄exporter/importreader顺序，避免再次阻塞Main。学习笔记与待发送的前端会话提示词只保存在本地，不随工程提交推送。
