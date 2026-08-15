# TeamLab V2 执行链修复交接（2026-08-15）

## 基线

- 工作树：`C:\Users\87701\.config\superpowers\worktrees\newGZCTF-main\teamlab-high-performance-a`
- 分支：`codex/teamlab-high-performance-a`
- 已部署 release：`teamlab-execution-chain-fix-20260815-16`
- 主站 `10.0.7.118:8080` 已激活该 release；118/125 Agent 二进制 SHA-256：
  `8af6c50f9e23fb6fd86f3f4403837a89ef778e972c12d16852411b51197918d2`
- 数据库迁移头未变化（本 release 无新迁移），`efbundle` 已在激活时执行并返回 up to date。

## 本轮代码修复

1. OVN/OVS/libvirt 端口身份闭环：
   - `TeamLabOvnNaming.LogicalPortName` 改为 `LogicalPortId`，对 `runtime+generation+network+port` 生成确定性 UUID。
   - 同一 UUID 同时写入 OVN `Logical_Switch_Port.name`、OVS `external_ids:iface-id`、VM XML `interfaceid`。
   - 可读语义仍保留在 `external_ids`（gzctf-network-key / gzctf-asset-key / gzctf-runtime / gzctf-generation）。
   - 原因：libvirt 要求 `interfaceid` 必须是 UUID；OVN 按 `iface-id == Logical_Switch_Port.name` 绑定，二者必须一致。
2. libvirt 原生互操作释放：
   - `virFree` 在 libvirt 12.0.0 不存在，已改为 libc `free` 释放 `virConnectListAllDomains` 返回数组和 `virDomainGetXMLDesc` 返回字符串。
   - 域句柄仍由调用方 `virDomainFree` 释放。
3. 本工作树自上一 release 起已含并随本 release 部署的修复：
   - VM inventory 的 `residual:` 事实不再进入 `ReadInventoryAsync`；残留域只由 `DestroyResidualsAsync` 清理。
   - `TeamLabExecutionPlanV2.IsValid` 只在路由 `NextHop` 非空时校验地址。
   - OVN 清理顺序按引用方向调整（先子表后父表）。
   - OVSDB 操作错误解析不再假定 `table/error/details` 是字符串。

## 冒烟验收证据（真实节点 125）

使用 runtime 141 的原始 V2 计划快照直接调用 Agent：

- `execution-plan/cleanup`：Docker、linux-vm、windows-vm、网络全部 `succeeded`，inventory 为空。
- `execution-plan/apply`（同结构计划，仅替换 VM 模板为节点上摘要匹配的 1/69 镜像）：网络、Docker、两台 VM 全部 `succeeded`，inventory 三个资产均 `running`，耗时约 91s。
- OVN NB：Docker/两台 VM/player-gateway 的 LSP name 均为 UUID。
- OVS：`tlh*`、`tlv*` Interface 的 `iface-id` 与 OVN LSP name 一致，`ovn-installed=true`、`iface-status=active`。
- OVN SB：三个逻辑端口均绑定 chassis `fd21e95f-737a-46c9-95dd-654f974ec5fd`。
- 再次 cleanup：全部成功；`virsh`、OVS、OVN NB、Docker 均无本轮运行时残留，`/var/lib/gzctf/teamlab/<runtime>/<generation>` 仅剩空目录。

## 环境遗留（非代码缺陷，需测试侧处理）

- 125 上模板 79 的本地镜像与库中摘要不一致：库 `ImageTemplates` 为 `7a574e70...`，本地 `/var/lib/gzctf/images/79.qcow2` 为 `4f529f1c...`。
  该问题导致含 template 79 的 Linux VM 计划在 `VM base image does not match the execution-plan digest.` 失败，需要重新分发模板 79 的正确制品后再验收 Linux VM。
- runtime 141（`01a00379...`）、142（`01a0038b...`）在平台 DB 仍为 Status 7（cleanup-pending），物理资源已由新 Agent 清理完毕；平台再次触发 destroy 应能正常收敛。

## 测试建议

- 优先复跑：runtime 141/142 destroy、模板 79 重新分发后的 V2 全链路（Docker + Linux VM + Windows VM）。
- 覆盖：apply/cleanup 幂等、Agent 重启后 inventory、OVN 收敛、暂停/恢复/销毁清理、多节点计划、并发与故障注入。
- 回归重点：OVN LSP UUID 命名是否影响任何按名称匹配的外部逻辑；libvirt inventory 与 cleanup 不再出现 `virFree`/`virDomainListFree` 缺失。
