# 发布流程改进方案

基于 2026-10-14 PR #30 的源码、CI 与发布执行记录；2026-10-16 续作仅新增上传阶段、SSH key / sudo-n 与可信主机校验。生产步骤仍以[维护窗口手册](vnext-maintenance-window-rollout.md)为准；本页提出下一轮改进，尚未提供可靠的一键更新或一键回退。

## 最简更新方向与本轮入口

推荐单主站、独立 release、短维护窗口重启：构建、下载、上传、备份与副本验证提前完成，窗口内仅做必要迁移、切换与冒烟。当前会停止 Main，不属于无停机热更新；中断时间须现场计时，不能用构建时间代替。

本轮只补现有上传入口的 `--stage-only` 与 `--sudo-n`：

```powershell
python scripts/deployment/deploy-gzctf-release.py <release.tar.gz> --host <host> --user <user> --key-file <private-key-path> --sudo-n --stage-only
```

`status=archive-staged` 仅表示 `/opt/gzctf/incoming/<release-id>.tar.gz` 已上传且远端 SHA-256 匹配。不会解包、迁移、停服务、切链接或上传 activate 脚本；不证明包内 manifest、CI 来源或应用可启动。账户须已有目录创建所需的免密 sudo 权限。脚本读取当前用户 `~/.ssh/known_hosts`，拒绝未知主机或密钥不匹配；首次使用前通过独立可信渠道核验指纹并预置信任记录，不再自动接受未知主机。摘要失败时停止，删除错误中指定的 **incoming 内该单个 tar.gz** 后重传；不要删除 release 或备份目录。

**未传 `--stage-only` 仍为旧的上传即激活行为。** activate 的 canonical 路径、固定 Agent 更新、迁移连接参数与失败清理问题尚未修复，当前不推荐用默认调用执行生产更新。`--sudo-n` 仅改变认证方式。激活继续按维护窗口手册和本次审查脚本执行，本轮没有回退子命令。

下一步只保留同一个入口的 `update <release-id>` / `rollback <release-id>`（拟议命令，尚不存在），内部接续 stage / activate，消费已审查完整主线 SHA 的制品，输出旧/新链接、迁移头与冒烟结果。操作者不必拼 SQL、复制 DLL 或重启未改变的 Agent。旧应用兼容当前 schema 时才切回旧 release 并重启；不兼容迁移进入备份恢复流程。纯前端沿同一 release 身份发布，不增加独立版本与发布事实。

## 热更新边界

- `.NET Hot Reload` 是开发迭代能力；支持的改动可以直接应用，不支持的改动仍会重启，不能替代生产制品、迁移和回退。[微软说明](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch)
- 生产滚动替换需新旧进程并存、检查后切代理、排空旧请求与长连接。当前没有经过验证的入口。Main 启动调度、执行、恢复、镜像、Cron 等后台任务；已有 Cron leader lease，仍不能证明所有 Worker、网关操作、队列租约和新旧协议可混跑。队列继续以数据库 `DeploymentQueueTicket` 为唯一事实。蓝绿会增加双进程协调与维护成本，本轮先不引入。
- 新旧进程共用数据库需逐次审查兼容性，通常先增加兼容字段、迁移数据，再另一次删除旧字段；当前不能默认所有 migration 都满足。
- `AppExtensions.DefaultStaticFileOptions` 对 HTML 设置 no-store，其他静态文件缓存 7 天。已打开页面仍可能懒加载旧 hash chunk；旧 release 目录存在，不代表切换后的同一 URL 仍能访问旧资源。[Vite 官方说明](https://vite.dev/guide/build.html#load-error-handling)下一步在同一发布流程保留前一版 hash assets 并验证跨版本导航，或使用带保留期的共享 assets；只处理 hash 资源，不覆盖新 HTML，不清理附件。当前未实现，刷新页面不能视为无感更新。

优先减少重复构建前端、重复发布与重启未变化的 Agent/GuestSupervisor、同一备份重复恢复验证、重复计数与第二套发布状态。迁移头无变化时应跳过无变化升级包的生成和执行，保留明确的迁移判断。历史脚本等新入口覆盖真实更新/回退且引用清理后再删除，本轮不批量删有现场用途的脚本。

## 已核实的开销

| 环节 | 本轮实测 | 结论 |
| --- | --- | --- |
| 后端 Quality | PR 10 分 27 秒，main 8 分 04 秒；集成测试分别 6 分 07 秒、4 分 43 秒。PR 构建 1 分 46 秒、单测 54 秒 | 集成测试是该轮 CI 的主要耗时；保留迁移、认证、调度等共享契约门禁 |
| 前端 Quality | PR 2 分 32 秒，main 2 分 30 秒；完整构建分别 2 分 17 秒、2 分 15 秒。PR 安装依赖 3 秒 | 优先复用已验证制品，依赖安装不是该轮瓶颈 |
| 合并身份与门禁核验 | 执行人实测 74 秒 | 保留提交、远端和 CI 的精确对应 |
| CI 前端 artifact 下载 | 7 秒；223 文件、约 3.83 MB，压缩包约 1.48 MB；逐文件摘要/大小验证通过 | 本轮消除了发布阶段再次构建前端的工作，尚无同机前后对照的节省时间 |
| Linux Main publish / 前向 SQL 生成 | 执行人分别实测 71.84 秒、47.1 秒；Main 输出 698 文件，不含 Agent 目录 | 已复用前端并跳过未改变的执行面目标，本轮没有构建 `efbundle` |
| 免密 sudo 配置 | 本轮执行人配置并核验 1 分 33 秒 | 消除密码传递环节；这不是编译或数据库恢复的提速时间 |

CI 时间来自[最终 PR run](https://github.com/Y3X1L2/newGZCTF/actions/runs/38522823572)与[主线 run](https://github.com/Y3X1L2/newGZCTF/actions/runs/38525158298)的 job/step 时间戳。后端与前端并行执行，不能把两者相加当总等待时间。正式发布各阶段时间由执行人记录于[本轮交付记录](../records/2026-10-14-logs-readability.md)；未实测的节省时间不写成收益。

PR 与合入 `main` 的 CI 分别核验各自提交，发布应绑定完整主线 SHA。现有 [Quality](../../.github/workflows/quality.yml)只上传前端制品，发布机仍需构建主站和迁移包。[主站项目](../../src/GZCTF/GZCTF.csproj)默认 `Source` publish 会再次安装依赖并执行完整前端构建，还会发布 Agent 与两种 GuestSupervisor。

## 本轮处理

- 本轮复用 PR CI 临时 merge `aee5…` 的前端 artifact：执行人已证明 PR head `f8f4…`、该临时 merge 与正式 merge `6d3117f4…` 的完整源码 tree 相同，并验证 `frontend-manifest.json` 和逐文件摘要。保留真实组件来源，不能把 artifact 标签改成正式 merge SHA。`FrontendBuildMode=Artifact` / `FrontendArtifactDir` 是 **csproj publish 属性**，[构建脚本](../../scripts/deployment/build-gzctf-release.ps1)尚无对应参数。
- Agent / GuestSupervisor 输入未变化，本轮通过已核验的 Git 外 hook 跳过其 publish，复用原制品并执行 main-only 发布，避免无关重建和重启；仓库脚本尚未提供 main-only 模式。复用来源及二进制摘要保留在发布证据中。
- 本轮迁移采用该提交生成的精确前向 SQL 与容器 `psql`，由 stdin 输入；未构建 `efbundle`，也未把数据库连接秘密放入命令参数。后续部署包应明确迁移入口、起止头与摘要。
- CI 等待期间并行准备制品、现场清单和备份/恢复所需文件；正式切换仍等主线门禁、备份恢复和升级验证齐备。
- 用户本轮授权 `whoami` 用户免密 sudo，已由执行人配置并核验。上传入口现已支持 SSH key 与 `--sudo-n`，保留旧密码方式；凭据不进入 manifest 或回执。

## 下一轮的最小改动

继续使用现有 `release-manifest.json`、build / deploy / activate 与 backup / restore 脚本，集中补齐以下五处。

| 改动 | 解决的现有问题 | 最小实现边界 |
| --- | --- | --- |
| 制品身份核验 | release manifest 只有当前 HEAD 与文件摘要，未绑定 CI 来源。`SkipPublish` 新建空目录、没有载入已有 publish 的入口，并非可直接使用的复用模式。Artifact publish 只检查 `index.html` 存在 | 构建脚本接受前端 artifact 路径，传递已有 publish 属性；校验完整 SHA、CI run / artifact 来源及逐文件摘要。PR checkout 使用 merge SHA，不能拿 PR head 标签当产物来源。跨提交复用须证明相关源码 tree、lockfile、工具链一致，单独保留组件来源与摘要，不能整体改标来源 SHA |
| 显式 stage / activate | [上传脚本](../../scripts/deployment/deploy-gzctf-release.py)上传后立即 activation；断线按大小续传，固定 180 秒会话缺少实际阶段回读 | 在同一脚本增加明确阶段参数。stage 只上传并校验归档，返回后停止；本轮已提供该入口，解包及目录准备仍待后续激活改造；activate 在维护窗口显式调用。重试先检查远端摘要、目录与当前软链接，避免重传或重复切换 |
| 路径与组件适配 | [activate](../../scripts/deployment/activate-gzctf-release.sh)把 canonical 旧目录限制在默认 `/opt/gzctf/releases`，现场目录实际位于 `/srv/yinyu-data/releases`；同时固定停止、替换 Agent | 区分稳定入口 root 与现场核验的 canonical release root；组件清单沿用 manifest，main-only 只触及 Main。失败时保留失败目录、输出和摘要供排查，继续保留旧 release |
| 一份执行回执 | [备份](../../scripts/validation/preprod-database-backup.sh)与[恢复升级](../../scripts/validation/preprod-database-restore-migrate.sh)已有摘要和耗时，但后者 `diff || true` 只报告差异，activate 不核验备份/恢复证明；全表与核心计数还重复查询 | 每个 release 留一份脱敏执行报告，记录各阶段开始/结束、备份摘要、恢复与迁移头、旧/新链接和冒烟结果。核心计数从该次全表结果提取，减少重复往返。非预期差异阻止 activation；重试以现场事实和匹配的报告接续 |
| CI 出一次可部署包 | frontend artifact 已有，server / efbundle 仍在发布阶段重做；组件复用依赖人工整理 | 主线 Quality 成功后复用该 SHA 前端 artifact，调用现有构建脚本产出主站、迁移包、组件清单与 manifest。审核后才推广；PR 仍执行质量门禁，正式发布消费主线的确定制品 |

回执只补足现有 release 目录中的证据，不另建 plan 状态体系。备份与恢复证明必须对应同一备份摘要，升级证明还须对应本次迁移包；数据库迁移头、当前 release 或文件存储发生变化时重新核验相关阶段，不能仅凭上次“成功”跳过。

纯 Markdown 发布记录或方案变更可只执行文档门禁。后续在同一 workflow 保留轻量、始终报告结果的 required gate，按实际路径跳过无关重 job；直接 `paths-ignore` 跳整个 required workflow 会留下 pending。该变更需单独核对 branch protection 与源码分类后评审，本轮不修改 CI。

现有 activate 成功条件只有服务 active 与首页响应。保留首页检查，再按本次影响检查关键 API、迁移头、节点/队列、已知附件以及一条受授权业务链路；[管理验收脚本](../../scripts/validation/run-common-management-acceptance.ps1)包含写操作，复用前须核对当前契约与清理范围，不能当作只读通用探针。

## 落地顺序与验收

先补 manifest 核验、stage / activate 分离和 canonical root / main-only 适配，再把现有阶段输出汇成一份报告，最后让 CI 产出部署包。每步独立审查与验证，不改变本轮正在执行的生产激活流程。2026-10-16 唯一生产执行人已用临时探针包验证 stage-only / SSH key / sudo-n：远端摘要匹配，Main 与 Agent PID 未改变，未触发解包或 activate，探针文件随后清理；完整现场证据归本轮发布记录。

验收应证明：stage 不迁移、不停服务、不切链接；传输中断能验证后续传；已激活同一 SHA 可回读而不重复操作；备份不匹配、恢复差异或迁移失败时停止切换并保留证据；main-only 不替换未变化的 Agent。实际用时按 CI、打包、上传、备份、恢复、迁移、切换与冒烟分别记录，再用下一轮数据判断收益。
