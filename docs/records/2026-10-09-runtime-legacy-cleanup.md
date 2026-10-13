# 历史运行资源清理修正

基线 `main 240ea370`，分支 `codex/runtime-legacy-cleanup`。只修改清理安全行为，不加迁移、队列或恢复表；生产仍由唯一发布写者操作。本记录仅含已证实阶段。

## 旧 TeamLab DNS 定义

生产 runtime 103 / shard 80 的 v2 冻结计划身份、代次和摘要一致。服务器当前真实 Contracts 校验唯一拒绝原因为同网段 DNS hostname 重复；这是创建配置约束，不应阻止销毁已有资源。

共享 `IsValidForCleanup` 与 `IsValid` 使用同一实现，仅清理不检查 DNS hostname 唯一。Main 快照读取、Agent 清理入口与 OVN 删除入口同步使用清理校验；创建/应用仍使用严格校验。冻结 JSON、原 PlanDigest/NetworkDigest 保持，没有临时修改计划或绕开内容摘要验证。

Release 定向测试 **47/47**（0 skip、exit 0），覆盖 contract、OVN provider 与清理编排；重复 DNS 只允许清理，路径型资产键、非法 MAC、无效 runtime 身份和篡改摘要仍拒绝。Main/Agent/contracts 编译成功，既有依赖和测试告警保留。全量组合门禁与生产销毁、资源清零由主会话完成，本分支未部署。

## 旧普通 VM 控制 owner 与本机遗留盘

现场 31 个 NodeId 为空的 Error VM 中，28 个 gen1/nativeId 空且没有历史 Create 目标；另 3 个 gen2–4 有同一实例/代次的历史 remote owner。三个本机 destroy 失败记录的 domain 已不存在，但约 2.37 GB 差异盘与 XML 仍在；它们仍依赖 ID 1 旧 local 源。单纯把缺 domain 当成功无法回收字节。

- Destroy 调度优先采用记录 owner 或同一 VmInstanceId/generation 的历史 Create 目标；只有 gen1/native 空、KVM/Error、原本机命名且无 remote dispatch 证据，才使用唯一显式 IsLocal/KVM 注册。多 owner、多本机、其他未知代次保持阻断，不改 Create。
- 已核目标通过既有 execution context 传给 FleetVmService；缺 NodeId 不再悄悄落本机。成功清理后才记录恢复 owner 和 Destroyed；其他活跃同名代次会阻止清理。
- 本机 provider 成功枚举全部 active/inactive libvirt 定义，检查 UUID、XML 与精确实例盘归属；缺域不吞别的 inventory 错误。只删除受证实差异盘及自己的 XML，保留模板底盘。其他域、qcow2 backing、别名使用及未知 inode/符号链接/路径变化拒绝删除。
- 单文件原子改名及 stat 设备/inode/大小/mtime 比对保护替换竞态；失败保留/恢复原文件，重入可继续处理同一限定实例文件。没有通用恢复目录、第二队列或数据库终态修改脚本。

最终定向单元 **46/46**、真实 PostgreSQL owner 调度 **3/3**（均 0 skip、exit 0）；Release Main/测试编译与 diff check 通过。覆盖 remote owner 到实际 Agent 调用、本机 legacy、未知/歧义 owner、较新活跃代次、缺域幂等、UUID 冲突、目录外 XML、其他域与 inactive 定义/backing 使用、inode 变更和重入。首次 PostgreSQL fixture 预先缓存 seed 票据导致 SQL claim 后读取旧 tracked entity，改为生产实际 fresh scope 后通过；未为测试修改调度行为。

Linux 的真实 libvirt/stat/backing 回收与磁盘差值尚未执行，不能把 command fixture 和 PostgreSQL 通过当作物理回收已完成。生产由唯一写者通过原 DELETE/queue 重入，确认 domain、实例盘/XML、缓存引用、ID 1 来源迁移和 Registry GC 各阶段；全量组合门禁仍由主会话执行。现场原始证据与退休清单在仓库外，不包含凭据或完整计划正文。

## 审查后的本机清理身份保护

在 `0607433a` 上继续收口防误删边界；没有迁移或 API 形状变化，未操作生产。

- 缺 libvirt 域时，调用方提供的 native UUID 必须与捕获 XML 的 UUID 一致；冲突或 XML 缺少该 UUID 均保留差异盘/XML。匹配的 UUID 和原本无 native UUID 的自有旧孤盘仍能幂等回收。
- 旧本机清理发现同名 Agent `.generation`、`cloud-init`、`runtime-injection`、`/var/lib/gzctf/vm-runtime` 文件/目录，或 XML 中的代次 description/runtime metadata 时，明确拒绝并要求 Agent 身份清理。删除前重新检查标记；标记在原子改名期间出现时，恢复原盘并保留 XML、基盘和标记。目录检查使用实际 Agent 路径，不新建恢复表或泛化清理目录。
- 同名活跃 VM 保护按 Worker 的规范化 HostAddress、本机/loopback 身份识别同一主机，避免旧 local 与本机 managed Agent 使用不同 Worker ID 时漏检。仅旧本机 provider 额外保护有本代 Create 派发目标或 native 身份的 Error；早期 Agent 失败后 NodeId 清空仍受本代票据保护。没有派发/native 证据的历史 Error 和 Destroyed 行不互锁，另一主机的证据也不阻止本机清理。
- 远程 Error 继续交 Agent 的 native/generation fencing；三个同名远程失败代次按 4→3→2 进入正式 Agent RPC 的正例通过，未通过主站查询让它们相互永久阻断。
- backing 检查覆盖所有现存 active/inactive 域的 file-backed 磁盘，即使磁盘位于 ImageStoragePath 外；与原镜像目录扫描合并去重，不扫描整个宿主。多级链优先使用 full-backing-filename，否则按各链节点 filename 目录解析相对 backing。任何命令/链检查失败保留实例文件。

最终 Release 定向单元 **117/117**（0 skip、exit 0）通过，涵盖 `LegacyKvmCleanupTests`、`RuntimeControlPlaneTests`、`KvmProviderTests` 和 `VmGuestControlTests`。实际临时文件验证 UUID 冲突、合法匹配、有效/空/损坏 sidecar、Agent 目录/metadata、清理中出现标记及域外 active/inactive/多级相对 backing 均保留原字节；既有 owner 恢复、底盘保留和重入正例保持通过。

真实 PostgreSQL/Testcontainers 定向 **11/11**（0 skip、exit 0）通过，包含既有三项 owner 调度和新增八项 Fleet 执行/状态回读，证明相关 EF 同主机/本代派发查询能翻译并按实际数据库事实拒绝或放行。最终报告为 `legacy-guard-unit-passed.trx`、`legacy-guard-postgres-accepted.trx`。首次因 Docker 未启动/default pipe 不可达而在 fixture 初始化失败；恢复本地 Linux engine 并仅在测试进程指定 endpoint 后，修正夹具用户名长度及与实际 RuntimeExecutionService 一致的终态 SaveChanges 边界，再通过完整定向回读；未为测试改变生产执行或查询。

Main、Agent、contracts 和两测试工程 Release 编译、`git diff --check` 通过，既有依赖/分析器告警保留。本子任务未运行 solution/后端/前端全量门禁、未验收真实 Linux libvirt/stat/qemu-img 回收、未合并 main 或部署；最终组合门禁、生产回收和状态文档由主会话整合。TRX/日志仅留仓库外。
