# TeamLab 高性能执行面运行手册

## 上线前提

1. 所有旧 TeamLab runtime 已销毁或迁移，队列无旧执行中的运行任务。
2. 每个参与节点心跳同时报告 `teamlab.execution-plan.v2`、`teamlab.ovs-ovn.v1`、`teamlab.libvirt.native.v1`（仅 VM 节点）和 `teamlab.artifact-cache.v2`。
3. 节点已部署并运行 OVS/OVN，`br-int`、OVN Northbound socket、libvirt URI 和镜像缓存目录与 Agent 配置一致。
4. 发布版本的制品已预热到计划涉及的节点；正式启动阶段不应发生镜像传输。

## 执行原则

- 主站把校验后的不可变计划按节点分片提交到 Agent；每个分片包含网络、资产、制品和观测意图。
- Agent 依次确认制品、网络、计算资源和观测点，并以 Docker、libvirt、OVN 与 inventory 事实生成事件。
- 对同一 `runtime + generation + shard`，Agent 串行执行；相同 digest 且已收敛时返回 inventory，计划不同则拒绝覆盖。
- 销毁必须使用同一计划身份。Agent 删除容器、VM overlay、OVS 接入和 OVN 资源后回读 inventory；镜像缓存删除也必须返回目标缓存 inventory。任何资源仍存在时保持 cleanup 失败，不伪造已清理。
- 多节点 apply 在任何一个分片失败、取消或 inventory 不完整时，会先补偿已经成功的分片；只有全部分片的 inventory 完整时，主站才写入 V2 provider 成功标记。

## 切换步骤

1. 在维护窗口前执行 `scripts/validation/teamlab/run-high-performance-a-acceptance.ps1` 的独立环境验收。
2. 关闭新 TeamLab runtime admission，等待旧运行和镜像分发 claim 排空。
3. 仅向已报告 V2 能力的节点启用 V2 计划编译与提交。
4. 用一组独立场景执行 apply、重复 apply、暂停/恢复、cleanup 与 inventory 复核。
5. 观察无 orphan 容器、domain、overlay、OVS port、OVN logical switch/port、capture 或缓存引用后，才删除旧主路径。

## 回滚

在 V2 验收未通过前，不删除旧执行路径。关闭 V2 capability admission 后，保留现有 runtime 事实和节点 inventory，按维护窗口的数据库与发布物回滚流程恢复。不得通过重建运行时覆盖或清除仍在运行的队伍环境。
