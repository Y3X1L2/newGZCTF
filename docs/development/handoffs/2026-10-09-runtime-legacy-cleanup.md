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
