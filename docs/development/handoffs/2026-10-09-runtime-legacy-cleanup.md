# 历史运行资源清理修正

基线 `main 240ea370`，分支 `codex/runtime-legacy-cleanup`。只修改清理安全行为，不加迁移、队列或恢复表；生产仍由唯一发布写者操作。本记录仅含已证实阶段。

## 旧 TeamLab DNS 定义

生产 runtime 103 / shard 80 的 v2 冻结计划身份、代次和摘要一致。服务器当前真实 Contracts 校验唯一拒绝原因为同网段 DNS hostname 重复；这是创建配置约束，不应阻止销毁已有资源。

共享 `IsValidForCleanup` 与 `IsValid` 使用同一实现，仅清理不检查 DNS hostname 唯一。Main 快照读取、Agent 清理入口与 OVN 删除入口同步使用清理校验；创建/应用仍使用严格校验。冻结 JSON、原 PlanDigest/NetworkDigest 保持，没有临时修改计划或绕开内容摘要验证。

Release 定向测试 **47/47**（0 skip、exit 0），覆盖 contract、OVN provider 与清理编排；重复 DNS 只允许清理，路径型资产键、非法 MAC、无效 runtime 身份和篡改摘要仍拒绝。Main/Agent/contracts 编译成功，既有依赖和测试告警保留。全量组合门禁与生产销毁、资源清零由主会话完成，本分支未部署。

## 尚待独立完成

旧普通 VM 的 owner 恢复和本机 domain/差异盘/XML 幂等清理仍在后续意图边界，不能把上述 TeamLab 修正说成所有历史资源已经清空。现场只读证据与退休清单保存在仓库外，不包含凭据或完整计划正文。
