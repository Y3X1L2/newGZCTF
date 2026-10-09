# 第二批镜像生命周期发布 checkpoint

## 目标与当前状态

用户已授权第二批发布、通过正式 API 恢复旧实例、先迁移 ID 1 后释放 local 来源、退役列明的 16 个模板、受控 Registry GC，以及最终两保留 runtime 各一次正常 reset 验收。生产操作由唯一执行者顺序进行；本记录为内网连接中断时的 checkpoint，任务尚未完成。

本节原记录是首次网络中断 checkpoint；后续恢复实证见下节。不能把尚未安装的修补候选或未知备份完成状态写成已经发布/已回收。

## 修补发布与回收最终结果

PR19 合并 `334cca0ed0976555e06481dea30f5a9396751317` 与实际 Main 源码 `c25752ce3786f39e8253805aa98b9075c2d3ffcb` 同树。新 release `/srv/yinyu-data/releases/image-delete-query-c25752ce3786f39e8253805aa98b9075c2d3ffcb-20261009/publish` 已实际切换，Main DLL `4fca49098d71f96546b0b09e171a813051c17fd8700bebb08e896259997c4ab1`、前端9a、完整Agent/guest-supervisor af逐字节复用；700文件验证和真实新Main Agent摘要探针通过，4个发布差异全部安装。Main切换13.103秒，无migration；本机Agent保持PID264484，三节点实际进程/心跳af、Online/Stable/Fabric healthy，9a rollback入口与所有旧release保留。

首次1405在线备份实际因附件metadata变化失败，partial/unit日志保留。唯一新1430 capture-v2以短停Main17.779秒建立两exported snapshots/附件tar，finally恢复后在线导出；`gzctf.dump`1703433313字节、Guacamole94408字节均全文解码/catalog/SHA，schema/head与此前已真正完整恢复的v3一致，明确没有声称本次又fullrestore。成功备份 `/srv/yinyu-data/backups/image-delete-query-c25752ce3786f39e8253805aa98b9075c2d3ffcb-20261009T1430Z-capture-v2-pre`，snapshot ledger SHA `10cc89d6454167a89a4d61e2c09b9d7ac231ff45aef08f24fc030219860b9ca8`；原v3/失败partial不覆盖，Agent/教学容器未停止。

Main启动后原有reconcile正常完成全部16 Deleting尾部，元数据全部消失、LocalFilePath总量0、Deleting0、queue/imports0，5保留来源Ready/LocalNull、migration150。两Worker按精确16 IDs、原digest和原源路径只读检查 numeric/hash qcow/.part与小metadata候选无残留，保留1/519–522的inode/大小/mtime不变；没有剩余缓存，因此额外受保护Agent DELETE调用为0，不写成手动Agent清过16份。PVE118–121/release-v2 snapshot、教学yy-route-b/yy-geneve-b、原发布/数据库备份保留。

官方 Registry dry-run后按同全图signature `28ad9cc9eb168225eaf4d5f51bb0e326ddec5356c0d57b6e8e7e914e76293e13` 实际execute，精确26无引用blobs、allocated76028284928字节（70.81GiB）。df可用263004549120→339032940544字节，增76028391424字节；live repository revisions/tags保持、missing references0、1453 shared blobs全受保护、五保留payload前后全部完整SHA/长度/inode一致。stop服务32.283秒，finally恢复原config与docker-registry/HTTP200；不加delete-untagged、不rawrm、不处理约2.75GB upload遗留。GC工具source9a是该维护工具基线，实际Main为c257，未回退应用。

B generation2在14:54 UTC实查旧2008R2独自shutdown（另3机running），保留此前generation2成功/四新UUID/网络HTTP证明；已有Notification/宽限期0/wlms证据说明该镜像许可限制，未改license/clock/rearm。GC完成后15:12:42正常reset一次至B generation3，15:14:09票据成功，旧gen2域/overlay清空、四新UUID、cache复用、QGA网络/两HTTP200/跨副本隔离通过。随后15:15:57最终读取A generation3已被恢复巡检标Failed；15:17:28实际仍仅旧win7 shutdown、另3机running，出现时间早于按本轮boot推算两小时，不能硬称此次关机原因已重新完整复核。A2 generic网络首败与A3正常部署成功/此后自行关机的不同证据均保留。

## 恢复执行实证与修补发布 checkpoint（过程记录）

- 固定 9a 已实际部署：release `/srv/yinyu-data/releases/image-lifecycle-9a04916ed329b28d1f1c68cd45a711699267c5ea-20261009/publish`，Main `18e617ab…` / 前端 9a；三个正式 sync 的安装/实际进程/心跳为 Agent `af2567d0…`。Main PID 263210、本机 Agent PID 264484；实际 config `/etc/gzctf-agent/appsettings.json`、原 image/runtime 根已明确，配置早于 Agent 进程且 Main 声明指向同文件。原私有配置、共享 files、旧 release/回滚、PVE118–121/release-v2 snapshot、教学容器保留。
- 实际完整恢复的新鲜备份为 `/srv/yinyu-data/backups/image-lifecycle-9a04916ed329b28d1f1c68cd45a711699267c5ea-20261009T1148Z-fresh-v3-pre`。Main 只暂停 17.77 秒捕获两库 exported snapshots 和附件后恢复；两库完整 dump/全文解码、真实隔离恢复、schema/迁移/9 核心表及 23 Guacamole 表计数按同 snapshot 台账匹配，新 clone 停止。snapshot ledger SHA `deab07e37982983830e59487fce37ed333468741903a0fe5e7f2b75cbb92a81c`；旧失败只是原脚本最后使用 live 计数，原 dump/clone/失败日志保留。
- runtime103 正常 Destroyed；34 历史 VM 的正常 DELETE/票据成功 32，残留 `e5af9144-e9ea-4a31-bac8-842d95fc0fee`、`7f93a9f2-f6f0-4b68-a03c-6bdb01eeb85e` Error，没有对应域/overlay/XML/Agent 标记，未 SQL 改终态。真实 backing 检查通过后，ID1 operation `01a12099-60b9-7ba8-a0c3-2e4d5e2465ed` 第一次尝试成功，Registry 固定来源 SHA `81e45879880a016760d0507cc347cc43a32d2a7725540743f55cd1cc1eb6eefe` / 6040518656 字节，原源与 quarantine 消失，5 题/4 练习、Opaque/DHCP/RDP metadata 和 numeric cache 保持。
- 两份原 runtime ID 保持。A generation2 票据 `01a120b0-9fa4-77dd-bd27-15b6266235f4` 在 12:43:28–12:45:32 UTC 的来宾回读阶段失败，`guest_network_control_failed` / retryable false；补偿清除 g2 资产。现有持久事件没有 assetKey/inner exception，不能指定哪台或宣称网络代码回归；99→9a 来宾网络代码没有变。B 正常 reset 票据 `01a120ba-4e5d-7637-b795-405e58e12655` 到 g2 Ready，网络/HTTP200 通过。检查 A 服务、queue/import/imagewriters 0、CPU13.27%、IO pressure0、数据盘175.5GB及旧域0后，13:51:47 UTC 经新正常 reset 一次恢复至 g3 Ready。A/B实际四机/五网卡、HTTP200、跨副本隔离、新 UUID、原代域/overlay无残留通过，原调度恢复；没有更多 reset、没有改 guest 网络或许可。
- 子网是正常池再分配：B g2 使用 `10.66.0.0/24` / `172.22.0.0/23`；A g3 使用 `10.66.1.0/24` / `172.22.2.0/23`。两份网络 key、host offsets、冻结池和无默认网关的定义保持，不能说原 CIDR/IP 没变化。A 重试通过只证明恢复，不证明首次 generic 回读失败根因已解决。2008 R2 许可约两小时自行关机的既有限制保留。
- 正常 archive 57 历史 releases，canonical content hash 不改；45 个草稿以原 revision PUT 解引用。纯旧草稿通过 contract 原生 `imageTemplateId=0` → nullable FK 表达保留旧资产且未选镜像，不新增 placeholder；可选 editor 省略让服务复用并 normalize 既有布局，避免旧 editor 的 null Assets 导致 MVC400。正常重读无退役 draft/live runtime/active rollout/题目/练习/课程引用。
- 16 授权模板均实际 normal DELETE，已进入 Deleting，但 `CleanupTemplateForDeletionAsync` 的 positional record 投影后排序触发 EF `LINQ expression ... could not be translated`，同一 570 字符错误在 artifact cleaner 的第一个阶段失败。没有 node distribution 行、非级联 FK 余量均0，旧文件存在；没有删文件/SQL绕过，也尚未 GC。最小修补只前移两项 entity 排序。PR19 → main `334cca0ed0976555e06481dea30f5a9396751317`，源码 `c25752ce3786f39e8253805aa98b9075c2d3ffcb` 同树，真实 PG 3/3 先红后绿、units32、Release、Quality success。
- 修补完整 publish700文件、新 Main DLL `4fca49098d71f96546b0b09e171a813051c17fd8700bebb08e896259997c4ab1`，真正调用新 Main `ComputeAgentBinarySha256` 的探针确认期待/实际 af 一致。4 changed files 是 Main、GuestControl/TeamLab contracts DLL 与 static endpoints；合同源码未变，前端/完整 Agent/guest-supervisor 严格复用9a。delta5229945字节，SHA `969a6b1992b6a3e6e7a802d5a8196e294c265365f7a67a03575de45e2560d7f4`；没有私有配置/数据。
- 在线新备份 unit `yinyu-image-delete-backup-c25752ce3786f39e8253805aa98b9075c2d3ffcb-20261009T1405Z` 已收到 accepted 身份，目录 `/srv/yinyu-data/backups/image-delete-query-c25752ce3786f39e8253805aa98b9075c2d3ffcb-20261009T1405Z-online-pre`。方案是同 exported snapshots 的 dumps/全文/摘要、当前附件前后 metadata与tar、9a配置/发布归档，逐库 schema/head 对照已 fullrestore v3；不重复整个副本恢复，明确 `newSnapshotFullRestoreExecuted=false`。随后 `.27` SSH/API 均真实 connect timeout，unit 完成 proof 未读回；delta SCP 未成功。**没有安装/切换修补版、没有重发 backup launcher、没有 GC**，最新运行版本仍最后实证9a，当前网络中断时的服务实时状态未知。
- 新 root 工具已审且独立白名单：`launch-deletion-hotfix-online-backup.py`，以及 `stage-deletion-hotfix-candidate.py` / `install-deletion-hotfix-candidate.py` / `switch-deletion-hotfix-main.py`。它们使用独立新 release 与9a rollback，不覆盖任何旧发布；切换前重核 exact16Deleting、live consumers0、保留五源 inode alias保护、备份和全700发布物。网络恢复后先读取既有 unit/proof/partialupload身份，然后续正常 reconcile→实际删除/文件/Registry分别核验→受保护官方 dry-run/GC。不能因为模板已 Deleting 就称退休或字节回收完成。

### 最终现场状态与明确缺口

15:22:16 UTC 对A仅做最后一次授权正常恢复至generation4；票据 `01a12142-03ac-74ad-8322-27aa5844fda0` 于15:24:23以同 `guest_network_control_failed` /category12/retryablefalse失败并正常补偿，未再重reset。15:27:36–15:27:42的真实final：Main c257/4fca、front9a/afAgent与原PID264484、3节点Online/sched/Fabric/Tunnel3、5来源Ready/LocalNull、local/deleting/queue/import0、migration150、首页/Config/manifest200；A4Failed、实际域0，B3Ready且4域全部Running/4唯一native，PG/Redis/Guacamole/Guacd以及`.30`教学两容器healthy/running/restarts0。没有将此前两套网/HTTP/隔离通过的时间点冒充当前8域同时Running，最终两套同时Ready目标未完成。

A2/A4同节点guest回读失败，而A3曾通过、B2/B3通过；同一99→9a来宾网络源码没有变化、服务器CPU/IO/容量/业务队列检查正常，这些证据不能指定某一VM、宣布泛型异常是网络代码回归或许可引起。Agent有意屏蔽可能包含脚本/配置的原始异常，Main失败补偿已清guest，现有持久事件仅到guest-network-verify、没有assetKey/inner type。下一步在独立补丁只记录受限assetKey/OS/stage/exceptiontype/命令类别或exitCode（绝不脚本、stdout/stderr、user-data、密码/Flag），复现前先保留当前失败证据，再按正常生命周期恢复；不SQL改Running、不用连续reset伪造成功。B3和全部源回收/备份/发布/许可证据保留。

### 未完成与下一阶段

普通 Win 冷创建、完整桌面登录、其销毁由用户要求优先收尾而暂停，全部 NOT_RUN，未发送任何 create。既有 admin/Game23 Accepted、ID1原RDP配置均已只读确认；两 KVM节点原1缓存都在，分发Ready并持有Game23引用，未 rawrm/绕过引用、未改报名或赛时。只读契约和暂停记录在 Git外 `rollout-v2/ordinary-win-review/`。

六个当前管理员/课程 VM upload/archive/import-local入口仍能产生永久 `LocalFilePath`。本轮历史清理不证明未来不会产生旧主副本；下一阶段应以 Cookie actor薄入口复用既有持久 ApiOperation/Job/qcow2 executor/PreparedArtifact，保留转换为短期 staging，课程仅在Ready后绑定，验证取消/中断/既有Error重用/正常DELETE恢复后再移除旧本地持久写入与旧下载兼容。Worker cache/预算/backing保护和Registry主制品继续保留；upload/extract残留与Registry blob GC分别验收。本次不扩冻结源码，不删历史 migration。只读审计原文在 Git外 `vm-import-readonly-audit.md`。

最新原始证据在 `D:/Work/YINYU-Image-Lifecycle-20261009/rollout/evidence/`（实际服务/备份/两运行/正常操作），本地 Root工具及Registry证据在 `rollout-v2/`，修补build/manifest/probe/PR merge receipt在 `deletion-query-fix-verification/`。凭据只在受保护会话内存，所有 dump、镜像、cookie/私有配置留 Git外和服务器根权限目录。

## 源码与制品身份

- 起始 rollout 分支 `codex/image-lifecycle-rollout`，从 PR #17 合并后 `240ea370` 延续。第二批 PR #18 正常合并到 `32356555bdc217eec2404946badec90c509b63d2`；候选源码 `9a04916ed329b28d1f1c68cd45a711699267c5ea`，完整 tree 均为 `f3167ab387c81fe860c3a85bb690562994e5a4d1`。
- 本地串行单元 1471/1471、PostgreSQL 等集成 325/325，0 skip。PR Quality [37885404850](https://github.com/Y3X1L2/newGZCTF/actions/runs/37885404850) 全部 success，前端 118 文件/399 测试与完整门禁通过；EF model、查询计划、OpenAPI 通过。最终 SHA 相比已测后端 `9f7de476` 仅增加工程计划文档，无新增 migration。存在既有分析器/依赖警告，不宣称 solution 零警告。
- PR 前端制品来自 synthetic merge `e5515b2c`，完整 tree 已与 9a 核验一致。为保持最终 manifest 字面身份，冻结 9a HEAD 仅重新执行 Vite 构建、manifest 生成和体积门禁；没有手改 manifest。最终前端来源为 9a，223 文件。
- 完整 Linux x64 publish 与真实 `NodeDeployService.ComputeAgentBinarySha256` 探针通过：Main DLL `18e617ab1e13171a1d0c1b25514f429ba78db8b39d9bc355061f9b282c237363`；Agent 与 Main 期待均为 `af2567d07da2e6c6a79b91b0f4c45a23805915c855a6b1c86cf63dd52c9afedd`。程序集 InformationalVersion 仍为固定值，源码来源由干净树、实际 publish 命令和门禁 receipt 证明。
- 归档 215,660,374 字节，SHA-256 `aa974524c42b687f34c6a1182990f21fdace4f6b5042c51d04b31c53178f151b`，独立 **700/700** 文件核验通过。不可变[中转制品](https://github.com/Y3X1L2/newGZCTF/releases/tag/image-lifecycle-9a04916ed329b28d1f1c68cd45a711699267c5ea-20261009) 已上传；归档不含服务器私有配置、数据库、镜像、凭据或运行数据。

## 已完成的正常操作与只读证据

31 条本轮明确 Pending、VirtualMachine/Destroy、TargetNodeId 与 VM NodeId 为空、代次匹配的历史票据经管理员队列 DELETE 正常取消，之后 SQL 只读确认活动队列 0。每条 ID、VM、generation、createdAt、before/after Status 和 completedAt 已保存到仓库外账本；无 SQL 更新，VM 资源尚未声称销毁。

两份保留环境仍为 `01a11e28-b860-7c4e-a373-06cac79bbb30` / `.27` 与 `01a11e2b-db7f-7fd7-873f-32f7a860b96b` / `.31`。只读 inventory 发现各自仅旧 Windows shutoff，另六 VM running；数据库正确显示 `resource_not_running`。原创建请求不包含 duration/TTL，runtime 表没有到期列；结构化事件记录 inventory 纠正状态，未发现对应 Stop/Destroy/Reset 票据。

通过正常单资产接口仅启动资产 956/960，两任务 `01a11ef3-cc80-7e7d-bd3d-2b5e6eda2339` / `01a11ef4-8381-7b0e-b908-5123817d6d70` 成功，八个 native UUID 保持，其他六 VM 状态保持。QGA 使用镜像内既有 LegacyPowerShellHost 仅读 OS、许可证和限定系统关机事件，两台均为 Server 2008 R2 Enterprise、LicenseStatus 5/宽限期 0，最近关机进程 `wlms.exe`。没有激活、rearm、修改 guest 网络或读取业务文件；guest UTC 与主机有八小时偏移，事件本机时间不能直接当主机 UTC，中文 reason 字段不作为结论。

随后两份环境各四 VM/五网卡的 MAC、IP、掩码、DNS、无网关、限定内部 TCP 独立回读通过。最后实读八资产 Running，两个 runtime/shard 聚合仍 Failed；正常单资产操作不会自动提升聚合，Failed 也没有直接 Resume 入口。最终发布后各一次正常 reset 是已批准的版本验收步骤，不直接 SQL 标 Running，也不借此宣称旧许可证能长时运行。

16 旧 local 源的 resolved path、device/inode 与保留 ID 1 不共享。三条缺域旧 VM 共享约 2.37 GB overlay/XML，仍 backing 到 ID 1 旧源；不能在正常恢复销毁前释放旧源。Main 当前真实 UID/GID/groups 的探针可读 Agent `/proc`、cwd 与配置，无需扩大权限；explicit roots/新 Main 实际配置声明仍须在第二发布执行并重启正式 Agent 后验证。

Registry 真实 backend 为 systemd `docker-registry`，binary `/usr/bin/docker-registry`，配置 `/etc/docker/registry/config.yml`，storage `/var/lib/gzctf-registry/registry`。只读全图 909 repositories、7084 blobs、123,081,288,982 逻辑字节，1443 跨 repo 共享；现阶段全部有 revision 引用，缺失引用与无引用均为 0。五个保留 payload 1/519–522 已完整 SHA/长度读取通过。519–522 使用真实 `vm-imports` repository，不能猜为 `vm-template/{id}`。85 upload 文件约 2.75 GB，和 blob GC 分开处理。

## 网络中断与未知后台接受状态

已确认 `.27` 创建新 staging `/home/whoami/yinyu-image-lifecycle-9a04916ed329b28d1f1c68cd45a711699267c5ea-20261009`，只有发布工具、允许公开的配置路径与制品摘要，当前运行 release 未在该步骤修改。

此后 relay launcher 返回 TimeoutError，backup launcher 返回 SSHException，没有接收 launched proof。免密 SSH 与独立 TCP 探针确认 `.27/.28` 连接超时，`.31` 曾恢复 banner 后仍须现场重验。**尚不能确认后台任务从未启动，也不能确认备份已完成。** 没有发起 Main switch、正式 Agent sync、历史恢复、ID 1 迁移、16 模板删除或 GC。线上版本最后实读为第一批 99e923cd / Agent ef1ce307，网络中断后的服务状态未知。

恢复连接后，第一步只读检查以下两个 unit，并读取已有证明和实际文件；禁止先重复 launch：

- `yinyu-image-relay-9a04916ed329b28d1f1c68cd45a711699267c5ea`
- `yinyu-image-backup-9a04916ed329b28d1f1c68cd45a711699267c5ea-20261009`

新的备份身份为 `/srv/yinyu-data/backups/image-lifecycle-9a04916ed329b28d1f1c68cd45a711699267c5ea-20261009-pre`，验证目录与 container 都带完整 9a SHA。新 release 计划为 `/srv/yinyu-data/releases/image-lifecycle-9a04916ed329b28d1f1c68cd45a711699267c5ea-20261009/publish`，next/rollback 链接也带完整 SHA，第一批固定 rollback 保留。

## 恢复后的顺序和保护条件

1. 只读确认 Main/Agent、两保留运行、队列、上述 unit 与服务器 SHA；已有任务继续查询，失败或未启动按实际 checkpoint 处理，不覆盖既有目录。
2. 完整新鲜备份与两库真实隔离恢复通过后，校验新 release 全部文件，安装私有配置和共享 files，原子 Main 切换，正常同步三个 Agent，声明并观察原路径的 explicit roots。
3. 正常恢复旧 runtime/VM，验证真实域和差异盘无残留；backing 空且配置/proc 可观察后，正常 API 迁移 ID 1，保留全部题目/练习绑定，完成普通 Windows 冷缓存创建、访问/登录与销毁验收。
4. 正常归档旧 release、按 revision 更新旧草稿、删除授权 16 模板，删除前重读引用/alias。保留 1、519–522、PVE 原机/release-v2 snapshot、教学容器、旧 release 和数据库备份。
5. 再取全 Registry 图和保留 payload 完整摘要，暂停上传并等待 in-flight；停止真实服务后用同一 binary/config 先官方 GC dry-run，核全部删除集合不含 live/shared/保留 digest，再执行同一官方 GC。不加 delete-untagged，不 raw rm，finally 恢复原服务/写入策略，实读 df 与所有保留来源。
6. 两保留 runtime 各一次正常 reset，保存新 generation/native，核前代残留、网络/HTTP/隔离和真实聚合 Running，明确约两小时的旧 Windows 许可限制。

## 原始资料与文档门禁

仓库外目录为 `D:/Work/YINYU-Image-Lifecycle-20261009/rollout-v2/`，既有运行证据仍在 `rollout/evidence/`，全量门禁在 `verification/`。本 checkpoint 不把根会话的受保护进程或本机 pipe 当长期认证配置，跨会话只复用已授权的安全输入流程，凭据不落盘。

本次 checkpoint 只改文档，执行 `git diff --check`；没有重复后端/前端全量测试。第二批现场备份、切换、来源迁移、模板删除、GC 和最终 reset 尚未获得真实通过结果，恢复后逐阶段续记。
