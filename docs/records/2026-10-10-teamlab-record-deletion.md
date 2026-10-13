# TeamLab 场景与运行记录删除候选

## 目标与基线

用户要求增加场景、运行环境的删除入口；完整前端重设计由后续会话处理。
本分支 `codex/teamlab-delete-backend` 从 `origin/main e8f8d00d` 创建，工作区为
`D:/Work/newGZCTF-teamlab-delete-backend`。仅本地实现和验证，未合并、推送或部署。
本次没有数据库迁移，也没有修改教学数据、镜像仓库、PVE 源机或公网入口。

## 删除语义

| 管理端接口 | 行为 |
| --- | --- |
| `DELETE /api/admin/teamlab/runtimes/{id}` | 保持原有异步资源销毁，返回 202；完成后记录仍可查询 |
| `DELETE /api/admin/teamlab/runtimes/{id}/record` | 仅完全销毁且无活动引用的环境可物理删除运行记录及其专属历史，完成返回 204 |
| `DELETE /api/admin/teamlab/topologies/{id}` | 无运行记录、比赛绑定、发放记录或活动操作引用时，物理删除场景与发布版本，完成返回 204 |

只有创建者或管理员可以删记录。授权由服务端决定，不接受仅有操作授权的其他操作者删除历史。
不存在的记录对管理员幂等返回 204，对非管理员返回 404。
普通删除不自动销毁运行中的机器，也不自动解除比赛或发放关联，更不会删除源镜像。
外部 Open API 的原有草稿删除约束保持；本候选扩展的是管理端场景删除能力。

稳定失败码包括 `runtime_not_destroyed`、`runtime_cleanup_pending`、`runtime_in_use`、
`topology_in_use`、`record_delete_conflict`、`runtime_artifact_cleanup_pending`（409），
`runtime_inventory_unavailable`（503）。界面应说明如何继续清理、解除关联或恢复节点。

## 实现与保护

- Controller 只调用 `TeamLabRecordDeletionService`；比赛引用使用现有 usage projection port。
  执行任务、预留、缓存和 API operation 状态使用 Runtime 公开 reference query，
  TeamLab 不新增比赛或调度实体的直接跨模块 DbSet 访问。
- 全部代次的资源、网络/连接器租约、远程访问、抓包、镜像引用都必须已清理。
  正常 Destroy 会删除恢复快照；缺少快照本身不阻止删除，保留的机器/节点/代次身份用于库存比对。
  无法定位节点的历史资产拒绝删除，应先通过资源对账处理。
- 对关联节点读取库存，按 numeric runtime ID、实际 native ID、稳定名称及 runtime UUID 前缀匹配；
  libvirt 的 VM 库存没有 numeric runtime ID，不能只比该字段。
  每节点探测限 20 秒，数据库锁限 5 秒、单语句限 60 秒。
- 保留库存的 Docker/KVM 可读状态。共享 wire 字段虽叫 `DockerSupported/KvmSupported`，
  Agent 实际会在读取失败时返回 false。VM 要求 KVM 库存可读，Docker 要求 Docker 库存可读，
  Docker-only 环境不因缺 KVM 被拒绝。Agent libvirt 库存的枚举、XML、UUID 和状态查询改为严格命令，
  不把读取错误静默转换为空库存。
- 使用与发布、版本生命周期、远程清理及 API operation 提交相同的锁；
  reset、Destroy 队列准入及原创建 key 也共享锁并重读事实。
  重复并发 DELETE 在锁后重新核实存在性，避免旧追踪对象产生 500。
- 抓包先经正常 Destroy 到 Expired；未完成抓包回收时拒绝删记录。
  审计文件取得已有维护与逐会话租约，只允许规范且不被其他记录共用的精确对象路径。
  `DeleteAsync` 后回读 `ExistsAsync`，防止 S3 HTTP 200 的单对象删除失败被当作成功。
  文件删除/回读失败保留数据库定位信息，可重试。若多个文件中一部分已回收再发生失败，
  数据库会回滚，已回收文件不会复制恢复；重复删除不存在对象是正常重试步骤。
- 事务显式移除 Restrict 的专属审计会话、连接器历史租约和链路策略，清空 EntryShardId 打破循环，
  其余流量、路径、网络、机器等通过现有 FK cascade 真正移除；不加载全部大流量行到内存。
  已终结的统一队列/API operation 与独立管理员审计记录保留，不保留庞大的运行历史来防重复请求。
- 删除时撤销该场景/版本的预热引用，继续用既有分发回收机制；不会撤销其他消费者或删除源制品。
- Cookie 试运行原创建 key 的小型退休 receipt 保存于现有 terminal ApiOperation，
  只保留原创建者、key、请求摘要及旧 public ID，无来宾配置或第二套队列。
  原请求延迟重放返回 410 `runtime_record_deleted`；不同请求体复用旧 key 返回幂等冲突。
  保护依赖 receipt 保留，不承诺越过数据库历史治理保留窗口后仍永久有效。
  后续治理 ApiOperation 时需明确这类 receipt 的保留策略。

## 验证

- 独立 PostgreSQL / Testcontainers 回归覆盖正常 cleanup 后删除、真实 FK cascade、EntryShard 循环、
  并发重复删除、创建 key 退休重放、权限、比赛引用、队列、缓存、租约、旧库存 metadata 缺失、
  HTTP 200 但执行引擎不可读、Docker-only、文件删除抛错与静默失败。
- TestServer 导出内部 OpenAPI 后用仓库模板生成 `ClientApp/src/generated/Api.ts`，
  生成差异仅新增 18 行 `teamLabAdminRuntimeDeleteRecord`。
- 全量单元测试 1475/1475、0 skip；全量集成测试 343/343、0 skip。
  随后将审计租约竞争从通用异常映射为可重试 409，定向 PostgreSQL 回归 1/1 通过；没有为此重复全量集成。
- 真正节点/OVN/Registry 的删除验收为 NOT_RUN，本轮没有部署候选。

## 已知边界与交接

节点库存的网络部分仍主要依据 Agent 受管状态文件，并不是直接穷举 OVN 数据库所有原生对象。
本候选不能宣称已经全面发现历史缺元数据的 OVN 孤儿；这类历史记录须先对账，再允许删除。
仍被 rollout 引用的记录/场景会拒绝删除，即使该 rollout 已终结；本轮没有新增强制删除 rollout 的能力。
PostgreSQL 删除行后会释放数据库内可复用空间，不等于立刻缩小操作系统上的数据库文件；没有执行 VACUUM FULL。

前端只接管理端接口，危险确认必须区分“销毁资源”和“删除记录”，明确历史证据不可恢复。
审查并合并本地前后端候选后，再经用户授权部署；由于本候选含 libvirt 库存安全修正，
未来发布需通过正式 Agent 同步流程更新关联执行节点，不能只换主站。
