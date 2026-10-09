# 第二批镜像生命周期发布 checkpoint

## 目标与当前状态

用户已授权第二批发布、通过正式 API 恢复旧实例、先迁移 ID 1 后释放 local 来源、退役列明的 16 个模板、受控 Registry GC，以及最终两保留 runtime 各一次正常 reset 验收。生产操作由唯一执行者顺序进行；本记录为内网连接中断时的 checkpoint，任务尚未完成。

最新证据的范围是 `VERIFIED` 的候选/只读盘点/正常单资产启动与队列取消，以及 `BLOCKED` 的后续生产操作。不能把候选能力或后台请求未知接受状态写成已部署、已备份或已回收。

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
