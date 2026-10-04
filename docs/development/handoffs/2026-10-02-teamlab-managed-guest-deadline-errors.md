# ManagedStatic 超时与故障透传修正

日期：2026-10-02。基线 `bb09393c`，独立分支 `codex/teamlab-managed-deadline-errors`。本任务仅本地后端开发；未连接或修改服务器，未改变数据库 schema、Agent、队列结构或前端契约。

## 真实触发与结果

服务器验收中，缺少 QGA 的旧 Windows 镜像还在来宾代理的三分钟等待范围内，主站却按原来的约 150 秒取消请求。这样既提前打断了 Agent 的明确失败结果，也使用户最终只见通用故障。另一个问题是 Agent 已返回 guest 阶段和代码，分片服务与运行编排在返回 Success/Message 时再次丢失了分类。

现在 ManagedStatic 执行计划单独计算有界预算：原单资产原生执行预算 120 秒，加 QGA 就绪 180 秒、初次回读 30 秒、Windows 应用 120 秒或 Linux 应用 250 秒、验证 45 秒。整计划另有原先 30 秒余量、原有健康探测预算，以及失败补偿的 120 秒。单 VM 无健康探测的上界为 Windows 645 秒、Linux 775 秒；正常操作可提前返回，缺 QGA 的结果仍由 Agent 的 180 秒等待决定，不需等到完整上界。

计划门限 `TeamLabExecutionOperations` 是并发计划数，不能当作来宾资产并发数。Agent 实际资产并发至少为 2；新预算保守使用不超过 2 的批并发，按各 OS 最长资产阶段计算。DHCP/Preconfigured 的原 120 秒批预算保持不变，纯网络更新与清理调用也不引入来宾初始化预算。

## 错误信息与边界

已知 guest 错误通过现有 `OperationalError` 与 `IOperationalFailureException` 保存类别、原代码、节点和实际阶段。分片仍先补偿；运行编排仍先持久化失败及清理状态，之后仅将这个 typed guest 故障重新抛给原部署队列，使原队列的失败处理保存正确代码。没有第二套运行事实或错误队列。

错误说明来自有限的公开描述列表，未把 QGA 脚本、标准输出、完整 user-data 或未知 Agent 原文拼入错误。未知 guest 代码归为安全的 `guest_network_control_failed`。已有运行事件保存 Agent 报告的确切阶段；故障投影对明确阶段的代码显示 guest-ready/guest-network-apply/guest-network-verify。可发生于多个阶段的共有代码显示较宽的 guest 阶段，不猜测细分位置。

这项修正让缺组件或实际网络不一致继续失败，不放宽来宾校验，不让“进程运行”代替网络就绪。

## 验证

- 新定向测试 7/7：原 DHCP/预配置预算、实际 Agent 阶段上界、OS 差异、计划/资产并发区别、真实分片调用补偿，以及错误经原队列分类进入故障投影。
- 错误测试故意放入模拟敏感诊断文本，确认异常和故障详情不包含它；原 guest_qga_unavailable 代码与 guest-ready 阶段保留。
- 完整后端单元测试 **1251/1251** 通过，`git diff --check` 通过。Release 编译随定向测试完成；存在既有依赖/analyzer 警告。
- 日志与 TRX 位于仓库外 `D:/Work/YINYU-Managed-Network-20261001/tests/deadline-errors`。

本任务未执行整合后的集成/前端门禁或真实旧镜像负例复验。父任务合入后负责发布物身份、授权部署范围及真实验收。
