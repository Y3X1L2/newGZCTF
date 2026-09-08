# stable-20260908 稳定基线

## 结论与范围

用户要求核对本地、GitHub 和服务器的一致性并建立稳定基线，同时取消此前网络限制。本轮只执行 Git、服务器制品/状态和数据库只读核验，未启动或访问靶机，未重新部署或修改业务数据。

已创建并推送 annotated tag `stable-20260908`，固定指向 `ab2bd54b7e16d454e3a8f54960c4bb4689047c4a`。该标签定义已部署的主站发布基线，不代表所有 Worker 二进制相同，也不代表历史问题已经清零。标签不得移动；后续发布使用新标签。

| 对象 | 核验结果 |
| --- | --- |
| 本地 main / GitHub main | 本轮开始均为 `704d2d35c9cd16873b01ac03579be941d5a2edc8`；后续仅提交基线文档 |
| 发布标签 / manifest / 前端 manifest | 均对应 `ab2bd54b7e16d454e3a8f54960c4bb4689047c4a` |
| main 与发布提交差异 | 仅文档；`src`、`scripts`、`.github` 的 Git tree 完全相同 |
| 活动 release | `/opt/gzctf/releases/pr9-converged-ab2bd54b7e16d454e3a8f54960c4bb4689047c4a-20260908/publish` |
| 文件完整性 | `2026-09-08T09:49Z` 重新核对 994 个文件，长度和 SHA-256 全部匹配 |
| 服务 | 主站 PID 143895、本机 Agent PID 143896，实际可执行文件位于该 release；均 active/running、NRestarts 0 |
| 持久化附件 | `publish/files` 正确指向 `/opt/gzctf/shared/files` |
| 数据库迁移 | 134 条，head `20260816192540_TeamLabCapabilityClosure`；没有新 migration |
| 临时验收题清理 | 题 614、ExerciseInstance 及已知 Container 行均为 0；全局活动票据和运行 Container 行为 0 |
| 节点 | 三节点均 Online/Stable/schedulable，心跳在 22 秒内，报告运行容器/VM 均为 0，Fabric Disabled |
| 日常 PVE 分支 | 工作树干净，运行源码与 main 相同，保留 3 行独立 PVE 记录及原提交；其他历史 worktree 保留各自用途 |

## 制品、备份与回退

- 同提交 CI [34137354005](https://github.com/Y3X1L2/newGZCTF/actions/runs/34137354005) 再次核对为 success，包含 973 项单元、278 项集成、6 项网桥恢复、280 项前端测试与契约门禁；本轮没有重新触发测试。
- 发布归档 `artifacts/releases/pr9-converged-ab2bd54b-20260907/pr9-converged-ab2bd54b-20260907.tar.gz`，SHA-256 `08eabb9ea00040e30cfcd10ec2a53ef68b3679dd970e36a90f41d3d2f4288c31`，本地再次核对通过。重建可能因构建时间或环境产生不同字节，复现现网应优先使用该归档并校验 manifest。
- 服务器备份目录 `/opt/gzctf/backups/pr9-rollout-pre-ab2bd54b-20260908T080802Z`。本轮再次计算最终 `database-cutover.dump`（1020481538 bytes）及 `shared-files-cutover.tar.gz`（315077124 bytes）的摘要，均与 root 状态记录一致；`restore-cutover.json` 存在，记录 `08:40:36Z` 已完成的 134 条历史恢复及候选 bundle 无变化验证。没有重跑恢复流程。
- 应用回退目录仍是 `publish.previous` 所指的旧 `practice-validation-9eef8ac12c626672081e81fadbde39946e7d2237/publish`。稳定标签不替代备份或旧发布目录；应用回退不得顺带覆盖新业务数据。具体发布与回退证据见 [生产发布交接](2026-09-08-pr9-production-rollout.md)。

## Agent 版本与稳定性边界

当前 Agent SHA-256 前缀分别为：`.27` `d93cf21271727d18`、`.30` `3747f3535da88623`、`.31` `2f12bca5b9befb6d`。主站与本机 Agent 同一发布包；远端 Worker 保留之前的兼容版本。本轮没有同步或重启 Worker，也没有将其版本差异误报为故障。

如后续需要统一整个执行面，应另开维护任务，使用平台正式同步流程逐节点核验心跳、能力、调度状态和原有实例，保留 `.31` 网桥恢复 drop-in；不能仅修改版本字符串或为了同 SHA 批量重启。

已知限制继续保留：

- 指标端口 `3001/healthz` 当前为 HTTP 200 / Degraded，来自两条未恢复来源的历史 Theory migration，不能伪造或删除历史来变绿。
- `teamlab-flow` 保留任务 SQL `42601` 错误在发布后继续发生；应优先在独立缺陷分支修复并定向验证，随后形成新的候选发布，不热替换此基线。
- 指标持久化并发冲突截至本轮仍仅有 `08:38:22Z` 一次，之后心跳/指标持续更新；仍应补充并发和失败批次重试回归。
- 两条审计错误发生于首次临时题错误镜像引用的创建失败阶段；本轮未看到新的同类记录。失败历史保留，不做日志清零。
- 用户确认业务测试完成并删除临时题；本轮数据库和节点只读核验补齐了清理状态，但没有读取用户测试答案、重新提交 Flag 或复测公网端口。
- KVM/Windows VM、跨节点 TeamLab、AWDP、Portal SSO、整机重启持久性、真实应用回退演练及依赖/Blob GC 等既有专项缺口，仍不属于已完成的全平台稳定性认证。

## 后续使用约定

1. 日常开发从最新 `origin/main` 创建独立 `codex/<task>` 分支；需要复现本次生产源码时使用 `stable-20260908`。
2. 保留该标签、发布归档、服务器备份与旧 release；不因文档提交使 main SHA 前进而重建或重启生产。
3. 每次发布都记录“Git 提交 → CI → 归档摘要 → manifest → 迁移/备份 → 现场验收”，用新标签标识新发布。
4. 本地/GitHub 主线同 SHA、服务器发布代码等价即可称为主站运行代码统一；文档 SHA、用户数据和独立功能分支无需强制相同。
5. 当前 worktree 暂留以交付忽略目录内的制品和重放工具；网络限制已由用户解除，后续操作仍按各项任务范围授权，不自动扩展为新一轮靶机测试。
