# TeamLab 历史版本退休与镜像引用回收

日期：2026-10-09。起点为 `main 240ea370`，任务分支为 `codex/image-retirement`。

## 目的与范围

历史不可变版本保存镜像 ID、摘要和网络定义，不应在已经退休且无人使用之后永久阻止模板删除。用户已批准退役清单中的历史镜像；本变更实现平台正常归档与删除的闭环，不直接删除生产数据库行、Registry 文件或节点底盘。

本轮不加迁移、第二套队列、新的租约表、缓存 TTL 或新删除入口。ID 1 的来源迁移由另一个分支负责；最新版 519–522 保留。生产写入由发布协调者串行执行。

## 行为

- `IsArchived` 表示版本不能再创建、重建或预热。归档不销毁既有实例，版本 CanonicalJson、ContentHash 和历史运行资产的 SourceTemplateId/ImageDigest 保持。
- 未退休的发布、可编辑草稿、仍有资源的运行资产（包括后加资产）、未结束 rollout，以及正在执行的 reset 继续阻止模板全局删除。取消票据但 writer 仍持有效 claim 时也保留保护。
- 归档撤回这个版本自己的预热引用，不撤回其他版本、较新的预热或运行时消费者；重复归档会重新执行可恢复的撤引用尾部。
- `Archive`、创建规划、reset 入队和 reset 重新规划以同一个 PostgreSQL transaction advisory lock 对齐版本事实。reset 在锁内发布既有统一票据，归档检查 source 和受保护 payload 中的 target；硬件清理 RPC 不持有数据库事务。
- reset 执行前再次检查目标版本，拒绝以前排队但后来已退休的请求，避免先毁现有资源再发现不能重建。
- 外部归档入口仍要求写 Token 和同范围 grant；允许在已归档父 scope 内执行退休清理，与既有销毁语义一致。新建、reset、预热仍拒绝已退休版本；新建和预热保留父 scope 的 writable 检查。reset 保持既有 202 异步提交契约，其 operation 在执行前拒绝已退休目标且不建立部署票据。只读 Token 和其他 scope Token 无权退休。
- TeamLabRuntimeAsset.SourceTemplateId 本身没有模板外键；普通草稿更新清除资产引用后，正常 Catalog Delete 可删模板。历史投影仍能读取已销毁实例，不依赖模板仍存在。

## 验证

前任同一 worktree 的阶段验证：定向单元 56/56；真实 PostgreSQL/Testcontainers 专项 4/4。后者覆盖正常草稿清空→归档→Catalog Delete→历史读取、活跃/reset/失败 rollout 保护、真实创建读屏障与归档并发、跨 release reset 与取消但 live claim 的归档阻止、正常重建到 generation 2。

最后两处变更（外部归档授权与 canonical live-claim 保护）的回归：

| 项目 | 命令/覆盖 | 结果 |
| --- | --- | --- |
| Retirement 单元 | `dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter FullyQualifiedName~TeamLabImageRetirementTests` | 19/19，0 skip，exit 0；有效/过期 cancelled claim 各自阻止/允许回收 |
| Retirement HTTP | `dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter FullyQualifiedName~OpenTeamLabRetirementApiTests` | 1/1，0 skip，exit 0；只读403、其他范围404、合法退休/重复204、新建/预热409，reset202后 Failed/release_archived且无部署票据 |
| Release 编译 | 上述测试编译 Main、Agent、contracts 与相应测试项目 | 成功；现有包/测试分析器警告保留，不宣称整个 solution 零警告 |
| 差异格式 | `git diff --check` 与 staged diff check | 通过 |

初次 HTTP 用例因本机 Docker 未运行/管道 URI 格式未到业务断言；使用官方启动恢复 Docker Desktop，当前测试进程设置 `DOCKER_HOST=npipe://./pipe/dockerDesktopLinuxEngine` 后运行。用例也改正了既有 reset 异步202契约和平台时间字段序列化的测试读取方式，未为满足断言改动平台 API。

全量后端/前端门禁、合并发布及真实历史回收由协调分支完成，本分支不宣称已经生产部署或释放空间。

## 执行顺序

1. 部署经审查候选并完成新备份，确认最新版测试实例不受影响。
2. 正常销毁历史实例、结束/归档历史 rollout。
3. 归档历史 release，正常更新草稿去掉退役镜像资产（混合草稿仅去掉对应历史资产）。
4. 调正常镜像 DELETE；有真实使用者时仍返回 InUse，不绕过保护。
5. 分别核模板元数据、节点缓存底盘和 Registry manifest；实际 Registry blob 空间回收单独走受控 GC。

原始执行脚本、清单和生产结果只保存在仓库外。下一位接手者先核 `origin/main`、代码/备份身份和唯一生产写者，然后按现有引用查询处理，不从旧日志推断资源已释放。
