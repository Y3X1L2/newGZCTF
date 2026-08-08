# TeamLab 外部控制面 全链路验收测试计划

> 计划依据：`docs/superpowers/plans/2026-08-02-teamlab-external-control-plane.md`
> 目标环境：`10.0.7.118:8080`（部署版本 `teamlab-rollout-control-fix2-20260806-125206`，迁移头 `20260805050703_AddTeamLabRolloutPauseCoordination`）
> 状态：**待审批，审批后执行**

---

## 0. 测试范围（本次新增/改进功能清单）

| 编号 | 功能 | 形态 | 状态 |
| --- | --- | --- | --- |
| F1 | TeamLab Control Scope（外部资源命名空间 + 授权边界） | Open v1 API + 迁移回填 | 新增 |
| F2 | API Token 资源授权 `teamlab-scope:{scopeId}` | 身份模块 | 新增 |
| F3 | Open v1 全端点（scopes/topologies/releases/rollouts/runtimes/traffic） | `/api/open/v1/teamlab/*` | 新增 |
| F4 | Rollout 全生命周期：create/replace-targets/prepare/open-access/close-access/rebuild/drain/archive | Open v1 | 新增 |
| F5 | Rollout Pause/Resume（`PauseRequested` 标志 + `RolloutPause=20/RolloutResume=21` + 目标 `Paused=8`） | Open v1 | 新增 |
| F6 | 幂等性（`Idempotency-Key` + `(scope, caller, routeKey, key, bodyHash)` + `409 idempotency_conflict`） | 全命令面 | 新增 |
| F7 | 原子 admission（runtime+generation+reservation+ticket+operation 单事务）与恢复 | 后端 | 新增 |
| F8 | Coordinator 收敛写入（Ready/Blocked 稳态零写、空 desired→`Blocked`、drain 清除 pause、teardown 按最新 Destroy ticket 判定） | 后端 | 改进 |
| F9 | Agent 级 runtime pause/resume（保留原分配与 generation，不重排不重新分发） | Agent | 新增 |
| F10 | 镜像模板 `remoteAccessProtocol` 契约修复（小写字符串序列化） | 修复 | 本次已部署 |
| F11 | TeamLab 错误消息/日志/OpenApiOperation 中文化（error code/HTTP 状态不变） | 全模块 | 改进 |
| F12 | 前端管理端 `paused` 状态（runtime 详情/徽章/比赛绑定）、设计器镜像目录 | vNext | 改进 |

**不在本次范围**：webhook（Task 10 未实施）、service profile 目录（未实施）、Penetration 迁移（未实施）。这些只做"不存在/不回归"确认。

---

## 1. 前置条件与准备

### 1.1 环境
- 主站：`http://10.0.7.118:8080`；SSH：`whoami / qwer1234!`
- 平台管理员：`admin / Admin@123`（SuperAdmin）
- Worker 节点：`worker-10.0.7.125`、`Local Server`（均在线，AgentVersion 1.0.0.0）
- 镜像库：40 个模板（Docker 与 VM 均 Ready 可用；模板 79 已启用远程访问）

### 1.2 准备步骤（测试数据隔离）
1. 登录 admin 获取 cookie
2. 创建独立 API Token：
   `POST /api/tokens` body `{ name: "ctlplane-e2e", scopes: ["teamlab.topologies.read","teamlab.topologies.write",...], resources: [{resourceType:"teamlab-scope", resourceId:"<新scopeId>"}], requestsPerMinute: 600 }`
3. 创建独立 scope：`POST /api/open/v1/teamlab/scopes` `{ key: "e2e-accept-<时间戳>", displayName: "E2E 验收专用" }`
4. 后续全部用例使用该 token（Bearer）与 scope；浏览器侧用例用 admin cookie
5. 测试资源统一命名 `e2e-ctl-*`，全部用例结束后清理（归档/销毁），清理后快照对比

---

## 2. 用例明细

### A 组：环境基线（只读）
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-A1 | 服务与迁移 | systemctl 状态；迁移头查询 | 主站/Agent active；迁移头 = `20260805050703_AddTeamLabRolloutPauseCoordination` |
| T-A2 | 队列与节点 | DB 查询 WorkerNodes、DeploymentQueueTickets | 2 节点在线；执行中票据 0 |
| T-A3 | 镜像契约 | token 调 `GET /api/v1/image-templates?page=1&pageSize=100` | 40 条全通过前端契约校验（含 `remoteAccessProtocol` 为 null/小写字符串） |
| T-A4 | 镜像详情契约 | `GET /api/v1/image-templates/79`（启用远程访问的模板） | 返回 `remoteAccessProtocol` 小写字符串或 null |

### B 组：Scope 与授权
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-B1 | 创建 scope | POST scopes（重复 key 两次） | 成功；重复 key → 稳定错误码（非 500） |
| T-B2 | 列出 scope | GET scopes | 返回内置平台 scope + 新建 scope |
| T-B3 | 越权可见性 | 用**无授权** token（新 token 不授任何资源）GET rollout 列表（scopeId=新建） | `404 resource_not_found`（不能是 403/200） |
| T-B4 | 无 token | 不带 Bearer 调任意 open v1 | `401` |
| T-B5 | 归档 scope | archive 后：新建 rollout / 修改 topology | 拒绝：`scope_archived`；读/查/drain/销毁仍可用 |
| T-B6 | 内置 scope | 平台 scope 下管理端已有拓扑仍可读 | 浏览器管理端不回归 |

### C 组：幂等与重复提交
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-C1 | 同 key 同 body | 同一 `Idempotency-Key` 连续 2 次 POST 创建 rollout | 第 2 次返回**同一 operation**（202，相同 operation URL） |
| T-C2 | 同 key 异 body | 同 key，body 修改 Targets | `409 idempotency_conflict` |
| T-C3 | 并发同 key | 5 个并发请求同 key 同 body | 仅 1 个 rollout 落库，其余返回同一 operation 或 409；DB 无重复行 |
| T-C4 | 并发异 key 同 body | 10 并发各带唯一 key 创建 rollout（同 release 同 externalReference） | external reference 唯一约束生效：仅 1 成功，其余稳定 409/422 |
| T-C5 | 无 key | 不带 Idempotency-Key 的 mutation | `400`（Required 校验） |
| T-C6 | 浏览器重复提交 | admin 前端连续双击 pause/resume 按钮（如前端有该按钮则测；否则 API 层同 T-C1） | 不产生重复 operation |
| T-C7 | 跨 scope key | 同 key 在另一 scope 下 | 互不影响（各自独立 operation） |

### D 组：Topology 契约
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-D1 | 创建+查询 | token 创建拓扑（含 docker/linux-vm 资产、editor 布局），GET 详情 | 200；editor 字段可回读 |
| T-D2 | 布局不影响执行 | 仅改 editor 布局后 PUT 更新再 publish | 发布 digest 与改布局前一致；revision 变化 |
| T-D3 | 校验 | 对含无效网络 key 的拓扑 validate | 422（`topology_schema_unsupported` 或结构错误码） |
| T-D4 | capabilities | GET capabilities | 包含 editorLayoutVersion/rollouts/pauseResume 等 |
| T-D5 | 未知 schema | 提交 schemaVersion 不支持的 payload | `422 topology_schema_unsupported` |
| T-D6 | 发布 | POST releases | 202 + operation；轮询至成功；release 可 GET |
| T-D7 | plan | release 后 POST plan | 200；plan 与 release digest 一致 |

### E 组：Rollout 生命周期（正常流）
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-E1 | 创建 | POST rollouts（scopeId/releaseId/externalReference/targets×2） | 202；target 状态 Pending；counts 正确 |
| T-E2 | prepare | POST prepare，轮询 | 全部 target → Ready（容器/VM 均拉起）；`PreparationRequested=true` |
| T-E3 | open-access | POST open-access，轮询 | 全部 target → AccessOpen；`AccessOpenedAt` 记录 |
| T-E4 | close-access | POST close-access | 全部回 Ready（或契约定义的 closed 状态），access 关闭 |
| T-E5 | 替换 targets | PUT targets（+1 新 target） | 新 target 进入 Pending 并在后续 prepare 拉起；旧 target 不动 |
| T-E6 | drain | POST drain，轮询 | 全部 → Destroyed；运行时销毁；队列终态 Succeeded；`CompletedAt` 记录 |
| T-E7 | archive | POST archive | 200；rollout 归档；后续 mutation → 归档拒绝码 |
| T-E8 | 状态机非法跳转 | 未 prepare 直接 open-access | 拒绝（稳定码，非 500） |

### F 组：Pause/Resume 专项
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-F1 | paused 拒绝 prepare/open-access | AccessOpen 后 POST pause 完成 → 再 POST open-access / prepare | `409 rollout_paused`；已开访问的 target 保持 |
| T-F2 | paused 拒绝重复 pause | 已 paused 再 POST pause | 幂等返回（同一或已暂停，不报错/不报 500） |
| T-F3 | resume 恢复正常 | POST resume 后 open-access 可再次执行 | 可恢复；`PauseRequested=false`；target 状态保留原分配 |
| T-F4 | paused 期间资源保留 | pause 后查 runtime 状态（地址/网络/overlay 不变；进程暂停；容量预留保留） | 与计划"pause 不是销毁、不释放容量"一致 |
| T-F5 | drain 清除 pause | paused 状态直接 POST drain | 成功执行 drain（清除 pause）；全部 → Destroyed |
| T-F6 | paused 时 replace targets | paused 中 PUT targets | 按契约：允许记录或拒绝，但**不能**产生新拉起 |
| T-F7 | paused 时 rebuild | paused 中 POST rebuild failed target | 按契约：拒绝 `rollout_paused` 或排队，不产生运行时变更 |
| T-F8 | resume 幂等 | 连续 2 次 resume | 无副作用（第二次不重排不报错） |

### G 组：并发与边界
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-G1 | 并发 pause+resume | 同时发 pause 与 resume | 终态收敛为二者之一，且能继续后续操作；无死锁/无 500 |
| T-G2 | 并发 replace+drain | 同时 PUT targets 与 drain | 唯一约束保证无重复 target/运行时 |
| T-G3 | 并发 create runtime（open v1） | 同 key 并发 2 次 `POST /runtimes` | 仅 1 个运行时；DB 唯一索引兜底 |
| T-G4 | 并发 drain+rebuild 同 target | 同时发 | 状态机只允许一个生效，另一个稳定拒绝码 |
| T-G5 | Coordinator 收敛 | Ready 稳态下连续观察 Revision/UpdatedAt 2 分钟 | **零写入**（Revision 不变） |
| T-G6 | 空 desired 集 | 创建 0 target rollout 或 replace 成空后 prepare | → `Blocked` + `rollout_no_desired_targets`，不虚报 Ready |

### H 组：强行打断与恢复
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-H1 | 创建中断恢复 | 发 create rollout 后（响应前）断开；用同 key 重试 | 返回同一 operation；无重复 rollout |
| T-H2 | runtime 无 ticket 恢复 | 模拟：DB 删除某运行时的 ticket 行（备份后操作）→ 观察 | 协调/恢复路径重建 ticket 关系，**不创建第二个运行时**（如该恢复路径已实施） |
| T-H3 | prepare 中断 | prepare 后立刻重启 gzctf.service（sudo）→ 服务恢复后轮询 | rollout 继续或明确 Failed；**无自动重试掩盖**；可通过 rebuild 显式恢复 |
| T-H4 | 服务重启中断迁 | 重启后查队列/operation | 未提交 work 无残留；已提交票据被扫描恢复 |
| T-H5 | Failed→rebuild | 强制制造失败（如使用不存在的镜像模板发布后 prepare）→ target Failed | rebuild 后恢复；不 rebuild 则保持 Failed（不自动重试） |
| T-H6 | 客户端断连恢复 | token 客户端：创建→记 cursor→断开→用 cursor 续读 events/operations | 从 cursor 恢复全部进度，无丢失/无重复 |

### I 组：Runtime 生命周期（open v1 + admin）
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-I1 | open v1 创建 runtime | POST /runtimes（releaseId） | 202 + operation；轮询至运行 |
| T-I2 | pause/resume（runtime 级） | POST /runtimes/{id}/pause → 状态 Paused；resume | 状态正确；resume 后地址/overlay 不变 |
| T-I3 | reset | POST reset | 新 generation；operation 记录 |
| T-I4 | delete | DELETE | 202；最终销毁；票据 Succeeded |
| T-I5 | admin trials | 管理端 POST trials 创建 trial runtime | 与 open v1 等价 operation 事实 |
| T-I6 | 远程会话 | 对已启远程访问的资产创建 remote session → connect → end | 全链路 200；审计文件生成（如启） |
| T-I7 | access-grants | 创建/列表/下载/删除 grant | 契约一致 |
| T-I8 | events 分页 | GET events after cursor 翻页 | cursor 稳定、无重复、无丢失 |

### J 组：镜像与远程访问契约
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-J1 | 模板 79 remote-access 详情 | `GET /api/v1/image-templates/79/remote-access`（admin cookie） | `protocol` 为小写 `"containerTerminal"` 等；前端 parseRemoteAccess 可通过 |
| T-J2 | 更新远程访问 | PATCH 模板 79（enabled/protocol/port）→ 回读 | 小写协议；hasCredential 正确 |
| T-J3 | 协议合法性 | 对 Linux VM 配 rdp、对 Docker 配 ssh | 400 稳定码（业务校验） |
| T-J4 | 设计器镜像目录 | token 调设计器同款 `listTeamLabImageOptions` 链路（list 全量页） | 全部页通过契约校验（覆盖分页 pageSize=100） |

### K 组：前端管理端（浏览器，admin cookie）
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-K1 | 设计器加载 | 打开 `/admin/teamlab/{topologyId}/design` | 设计器正常加载，镜像目录可展开、可拖入资产 |
| T-K2 | 运行状态徽章 | 对 paused 的 rollout/runtime 查看管理页 | 显示"已暂停"（paused）状态，不再显示 stopped |
| T-K3 | 错误中文化 | 触发已知错误（如 paused 下 prepare）看页面提示 | 中文提示 + 稳定错误码 |
| T-K4 | 比赛绑定 | 管理端比赛 TeamLab 绑定页加载 | 无契约错误；paused 计数显示 |

### L 组：清理与数据完整性
| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-L1 | 归档拒绝条件 | 存在未 drain 的活跃 rollout 时 archive | 拒绝（`rollout_active_resources` 类稳定码） |
| T-L2 | drain 后无残留 | drain 完成后查 DB：runtime/容器/票据/预留 | 无残留运行时；票据终态；预留释放 |
| T-L3 | 镜像 claim | 两 rollout 共用镜像，drain 一个后另一仍可运行 | 共享镜像不被释放（如 claims 已实施则验证引用计数） |
| T-L4 | 数据快照 | 验收前/后 DB 对比测试命名资源 | 无 `e2e-ctl-*` 残留 |

### M 组：组网真实端到端场景（核心验收）
> 场景拓扑（参考现有 Phase9 素材）：
> - 1 个 Docker 资产（busybox:latest，containerTerminal 远程）
> - 1 个 Linux VM 资产（模板 79 "Phase9 Linux Managed v3"，ssh 远程）
> - 1 个交换机 + 跨网段组网（两个网络 key）
> - 可选：Windows VM（模板 69，rdp 远程）——若资源允许

| 用例 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- |
| T-M1 | 场景构建 | 用 admin 管理端创建/导入上述拓扑 → validate → publish | 拓扑就绪；release digest 固定 |
| T-M2 | rollout 批量拉起 | token 创建 rollout（3 个 target：docker/linux-vm/windows-vm）→ prepare | 3 个运行时真实拉起（节点可查容器/VM），全部 Ready |
| T-M3 | 网络验证 | 查看运行时网络接口/overlay | 组网按拓扑分配；跨网段连通（如可验证） |
| T-M4 | 开访问 | open-access → 远程会话（docker containerTerminal / vm ssh / vm rdp） | 远程操作真实可用，审计记录 |
| T-M5 | 流量观测 | 在资产内产生流量 → 查 flows/paths | 观测数据入 Redis→DB，按 generation 关联 |
| T-M6 | pause 全链路 | pause → 验证 VM 进程停止但网络地址保留 → resume | pause 保留原分配；resume 不回退到 stopped |
| T-M7 | 失败注入 | 手动将某 target 运行时改为 Failed（如杀容器/停 VM 后触发检测）或直接对坏镜像 rollout | Failed 呈现；其余 target 隔离（AccessOpen 保持） |
| T-M8 | rebuild | 对 Failed target rebuild | 恢复至 Ready（新 runtime 同目标） |
| T-M9 | 收尾 | close-access → drain → 验证销毁 → archive | 全部 Destroyed；队列空；无残留 |

---

## 3. 执行方式
- 服务器本地执行：paramiko + curl（Bearer token / admin cookie），断言脚本化，输出 JSON 证据
- 涉及破坏性操作（重启服务、删除 ticket）前先备份相关数据
- 失败用例：记录响应体与日志（journalctl gzctf.service），判断稳定码是否命中契约

## 4. 通过标准
1. A–M 全部用例通过（或与审批的偏差一致）
2. 所有异常场景返回契约稳定错误码，无 500
3. 结束后测试资源 100% 清理，队列为 0，Revision 稳态无增长
4. 前端 4 个页面用例无控制台契约错误
5. 不触碰生产赛事/课程/学员数据（验收前后 DB 对比）

## 5. 风险与回退
- 若 M 组 VM 资源（KVM 能力）受限：Docker+VM 混合场景降级为 Docker-only 多 target，偏差记录
- 若并发用例导致 DB 唯一约束冲突非预期：暂停该组，先评审代码再继续
- 全程不修改数据库绕过身份校验；涉及 DB 的模拟操作（T-H2）先备份该表行
