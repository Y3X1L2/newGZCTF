# YINYU TeamLab V1/V2 组网链路性能测试报告

> 日期：2026-08-17
>
> 测试对象：YINYU 安全综合演练平台 TeamLab 组网 V1（旧执行路径）与 V2（高性能执行面）
>
> 测试环境：`10.0.7.118`（主站 + 本机 Agent + OVN Central）
>
> 当前发布：`teamlab-v2-flow-persistence-fix-20260815-19`

---

## 1. 测试目标

1. 对比同一发布下 TeamLab 组网 V1 与 V2 的全链路性能。
2. 覆盖 Docker-only 基线拓扑与 VM 混合拓扑两种场景。
3. 重复启动（3 次顺序 create→ready→destroy）验证运行稳定性。
4. 输出每个可观测小阶段的耗时数据。
5. 清理 118 上历史垃圾文件，释放磁盘空间。
6. 通过测试发现代码/部署层面的优化点。

---

## 2. 测试环境与准备

### 2.1 服务器状态

| 项目 | 状态 |
|---|---|
| `gzctf.service` | active，首页 HTTP 200 |
| `gzctf-agent.service` | active |
| 当前 release | `teamlab-v2-flow-persistence-fix-20260815-19` |
| 118/125 Agent SHA-256 | `8af6c50f9e23fb6fd86f3f4403837a89ef778e972c12d16852411b51197918d2` |
| 执行模型开关 | 测试期间通过 `TeamLabNetworkConfig.ExecutionModel` 切换 V1/V2 |

### 2.2 118 垃圾清理

| 清理项 | 清理前 | 清理后 |
|---|---:|---:|
| 根分区可用空间 | ~2.2G | ~24G |
| `/opt/gzctf/releases` 旧发布 | 17G | 493M |
| `/opt/gzctf/backups` | 1.5G | 120M |
| `/opt/gzctf-vnext/backups` | 2.9G | 532M |
| `/var/lib/gzctf/agent-backups` | 754M | 4K |
| `/opt/gzctf/agent-releases` | 101M | 4K |
| `/opt/gzctf/incoming` | 523M | 20K |
| `/tmp`（tmpfs，释放内存） | 5.3G | 0 |

清理保留策略：

- 保留当前 release 19 与 `publish.previous` release 18。
- 保留最新数据库备份及 release 17/18/19 发布备份。
- 未删除仍在运行的 Docker/PostgreSQL/Redis/Guacamole 数据卷。

### 2.3 测试拓扑

#### Docker-only 基线

- 拓扑：`Perf Docker V1V2 20260817`
- 拓扑 ID：`01a00b7f-5c5c-7ea6-8040-2dac38550ed9`
- release：`01a00b7f-fbfd-789b-b6d4-60e436421999`
- 内容：2 Docker 资产、2 网段、1 托管路由器、2 托管交换机
- 镜像模板：`admin-Test-Web-v1`（模板 15，有不可变 digest）

#### VM 混合拓扑

- 拓扑：`TeamLab E2E Acceptance 20260809`
- 拓扑 ID：`019fe2f9-db22-71f2-9543-ea41b6d4c658`
- release：`019fe773-7565-7f3f-a250-ea0252d4ce02`
- 内容：1 Docker + 1 Linux VM + 1 Windows VM、3 网段、3 托管交换机 + 1 托管路由器
- 镜像模板：模板 15（Docker）、模板 79（Linux VM）、模板 1（Windows VM）

---

## 3. 测试方法

- 使用 Open API `/api/open/v1` 驱动完整链路：publish → plan → runtime create → Ready → destroy。
- operation 状态每 200ms 轮询采集，记录 `status/stage/currentProgress/totalProgress` 转换。
- 每种执行模型执行：

  - Docker-only：3 次顺序 create→destroy + 1 次 2 并发 create。
  - VM 混合：3 次顺序 create→destroy。

- 所有 runtime 使用唯一 `ExternalReference` 与 `Idempotency-Key`。
- 每个 runtime 结束后核对数据库状态、Docker 容器、VM 残留。
- 测试结束后恢复平台为 V2 默认配置。

---

## 4. Docker-only 基线结果

### 4.1 Publish / Plan

| 阶段 | V2 | V1 |
|---|---:|---:|
| Publish release | 1.83s | 使用同一 release |
| Placement plan | 0.25–0.56s | 0.23–0.80s |

### 4.2 Create（operation 完成时间）

| 轮次 | V2 | V1 |
|---|---:|---:|
| #1（冷启动） | 8.94s | 9.88s |
| #2 | 3.31s | 7.66s |
| #3 | 4.19s | 4.19s |
| 平均 | 5.48s | 7.24s |

### 4.3 Destroy（operation 完成时间）

| 轮次 | V2 | V1 |
|---|---:|---:|
| #1 | 13.53s | 15.78s |
| #2 | 12.25s | 14.94s |
| #3 | 11.70s | 14.05s |
| 平均 | 12.49s | 14.92s |

### 4.4 2 并发 Create

| 指标 | V2 | V1 |
|---|---:|---:|
| 两个 runtime 全部 Ready | 8.19s | 10.05s |
| 单个 operation 完成 | 3.30s / 3.84s | 3.91s / 5.25s |

---

## 5. VM 混合拓扑结果

### 5.1 Create（operation 完成时间）

| 轮次 | V2 | V1 |
|---|---:|---:|
| #1 | 87.99s | 92.59s |
| #2 | 60.13s | 96.42s |
| #3 | 84.17s | 90.39s |
| 平均 | 77.43s | 93.14s |

### 5.2 Destroy（operation 完成时间）

| 轮次 | V2 | V1 |
|---|---:|---:|
| #1 | 7.42s | 14.80s |
| #2 | 9.72s | 11.36s |
| #3 | 9.89s | 15.05s |
| 平均 | 9.01s | 13.73s |

---

## 6. 小阶段耗时明细

### 6.1 Docker-only V2 Create #1

| Stage | 耗时 |
|---|---:|
| pending | 0.23s |
| running | 0.92s |
| runtime-assigned | 1.28s |
| runtime-deploying | 2.11s |
| runtime-ready | 3.97s |
| completed | 0.42s |
| **合计** | **8.94s** |

### 6.2 Docker-only V1 Create #1

| Stage | 耗时 |
|---|---:|
| pending | 0.22s |
| runtime-queued | 0.41s |
| runtime-deploying | 2.05s |
| completed | 7.20s |
| **合计** | **9.88s** |

### 6.3 VM 混合 V2 Create #1

| Stage | 耗时 |
|---|---:|
| running | 0.24s |
| runtime-queued | 0.44s |
| runtime-deploying | 0.86s |
| completed（资产部署/VM 启动主导） | 86.45s |
| **合计** | **87.99s** |

### 6.4 VM 混合 V2 Create #2

| Stage | 耗时 |
|---|---:|
| pending | 0.20s |
| runtime-queued | 0.41s |
| runtime-assigned | 2.34s |
| runtime-deploying | 0.83s |
| completed | 56.34s |
| **合计** | **60.13s** |

### 6.5 VM 混合 V1 Create #1

| Stage | 耗时 |
|---|---:|
| pending | 0.30s |
| running | 0.42s |
| runtime-assigned | 2.20s |
| runtime-deploying | 2.11s |
| runtime-ready | 86.61s |
| completed | 0.95s |
| **合计** | **92.59s** |

### 6.6 VM 混合 Destroy 平均

| Stage | V2 | V1 |
|---|---:|---:|
| 排队/分配/部署前 | ~1.5s | ~2.5s |
| completed（Agent 清理主导） | 6.77s | 11.21s |
| **合计** | 9.01s | 13.73s |

---

## 7. 稳定性与残留核对

- 所有 perf runtime 数据库状态均为 `Status=10（Destroyed）`。
- 无残留 `tl/perf/teamlab` Docker 容器。
- 无残留测试 VM（仅保留环境原有 `tl97-ad-dc` 关机 VM）。
- 服务切换 V1/V2/恢复 V2 后均能正常启动并返回 HTTP 200。
- 测试结束后：

  - `gzctf.service` active
  - `gzctf-agent.service` active
  - `TeamLabNetworkConfig.ExecutionModel=V2`
  - 根分区可用约 24G

---

## 8. 结论

1. **V2 整体性能优于 V1**：
   - Docker-only：Create 平均快 ~1.8s（24%），Destroy 平均快 ~2.4s（16%）。
   - VM 混合：Create 平均快 ~15.7s（17%），Destroy 平均快 ~4.7s（34%）。
2. **VM 混合拓扑的 Create 主导耗时在资产部署/VM 启动**，约占总耗时 95%。
3. **Destroy 两端都比 Create 稳定态慢**，VM 混合下 V1 清理明显更慢。
4. 多启动稳定性：连续 3 次 create/destroy 无失败、无残留、无服务异常。
5. 清理后 118 磁盘压力从 99% 降到 81%，后续发布/测试空间充足。

---

## 9. 优化建议

### 9.1 【高优先级】配置段名不一致，V1 切换可能静默失效

- 现象：`appsettings.json` 写的是 `"TeamLabNetwork"`，代码 `ServicesExtension.AddConfig<TeamLabNetworkConfig>()` 实际绑定 `"TeamLabNetworkConfig"`。
- 实测：只改 `TeamLabNetwork` 段不会切到 V1；补充 `TeamLabNetworkConfig` 段后 V1 才生效。
- 建议：
  - 统一发布模板为 `TeamLabNetworkConfig`；
  - 或代码增加兼容段读取，避免运维切换失效。

### 9.2 【高优先级】`files` 软链不能指向 release 内部路径

- 现象：当前 release 的 `files` 曾指向旧 release 目录，清理旧 release 后主站启动失败。
- 建议：激活/部署脚本固定执行：
  ```bash
  ln -sfn /opt/gzctf/persistent/files /opt/gzctf/publish/files
  ```

### 9.3 【高优先级】V2 资产执行被 `TeamLabExecutionOperations=1` 串行化

- 代码：`src/GZCTF.Agent/Services/TeamLab/TeamLabExecutionPlanExecutor.cs`
  ```csharp
  var limit = Math.Max(1, agent.ExecutionLimits.TeamLabExecutionOperations ?? 1);
  await Parallel.ForEachAsync(plan.Assets,
      new ParallelOptions { MaxDegreeOfParallelism = limit, ... }, ...);
  ```
- 当前 manifest：`teamLabExecutionOperations=1`。
- 影响：VM 混合拓扑中 Docker、Linux VM、Windows VM 独立但串行启动，两个 VM 启动时间直接叠加。
- 建议：
  - 在 118/125 容量验证后提升 `teamLabExecutionOperations` 到 2–3；
  - 或拆分 Docker/VM 独立信号量，用 `dockerCreates` / `vmCreates` 分别限流。

### 9.4 【中优先级】增加按资产粒度的阶段计时

- operation 仅暴露到 `runtime-deploying` / `completed`，无法区分 Docker/Linux VM/Windows VM 各自耗时。
- 建议在 `ApplyDockerAsync` / `ApplyVmAsync` 内部使用 Stopwatch，输出 `assetKey + stage + ms` 结构化日志或事件。
- 这是进一步定位 VM 启动瓶颈的必要基础设施。

### 9.5 【中优先级】Destroy 清理链路优化

- VM 混合 Destroy 中 V1 平均 13.73s、V2 平均 9.01s。
- 建议对 Agent cleanup 增加 OVN/OVS、Docker detach、libvirt undefine、lease 释放拆分计时，再决定是否可并行化。

---

## 10. 测试产物

| 文件 | 说明 |
|---|---|
| `.tmp/perf-v2-report.json` | Docker-only V2 原始报告 |
| `.tmp/perf-v1-real-report.json` | Docker-only V1 原始报告 |
| `.tmp/perf-mixed-v2-report.json` | VM 混合 V2 原始报告 |
| `.tmp/perf-mixed-v1-report.json` | VM 混合 V1 原始报告 |

---

## 11. 限制与后续

- 本次为单 shard、单次发布、2 资产（Docker-only）与 3 资产（Docker + 2 VM）基线。
- 未覆盖：4/20/50 资产、4/8 网段、双节点分片、10/50/100 队并发、故障注入、pause/resume/reset 全矩阵。
- 后续建议按交接文档 8.2 未签收清单继续补齐 V2 完整矩阵。