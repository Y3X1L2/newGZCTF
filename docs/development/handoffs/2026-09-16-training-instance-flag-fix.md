# 课程实例隔离与 Flag 注入排查

## 任务目标与边界

- 排查课程 32 / 章节 69 创建第二个靶机后第一个消失及端口重复，以及三道题中疑似 `rce_2` 的 Flag 注入失败。
- 仅检查平台配置、实例/队列事实、镜像启动契约和注入结果；不解题、不运行攻击载荷、不提交真实 Flag。
- 从最新 `origin/main` 的 `f389d26c` 创建 `codex/training-instance-flag-fix`；worktree 为 `D:/Work/newGZCTF-training-instance-fix`。

## 已确认的现场事实

- `.27` 的主站与本机 Agent 均 active；`/opt/gzctf/publish` 实际解析到 `/opt/gzctf/releases/agent-fabric-dd2f464da2bf-20260910`。目录内 manifest 仍标识 `teamlab-production-a8438f676a747876312ed5e5b3475270efb27f91-20260910`，不能据此断言当前二进制完全对应某个 Git SHA。
- 数据库配置为 `ContainerPolicy:MaxExerciseContainerCountPerUser=1`、`AutoDestroyOnLimitReached=False`。
- 课程 32 / 章节 69 的题目为 627（`ez_rce_1`）、629（`ez_rce_2`）、630（`ez_rce_3`），均为 DynamicContainer。
- 首次只读核验时，627 在 `.30` 有运行容器，节点映射端口 42762、公共入口端口 30000；629 和 630 无关联容器。629 已 `IsLoaded=true`，却没有 `FlagId`；627 和 630 有实例 Flag。629 没有模板，但已有生成器本来就支持无模板时生成随机值，因此不能把“没有模板”本身当作注入失败根因。
- `.30` 上 627 的 Docker 配置包含非空 `GZCTF_FLAG`。三道题的注册镜像均以 `/entrypoint.sh` 启动，启动脚本都消费 `GZCTF_FLAG`。检查使用未启动、无网络的临时容器读取启动脚本，随后立即删除；未读取题目应用源码、未调用漏洞接口、未输出 Flag。

## 根因与修复

1. `ExerciseInstanceRepository.CreateContainer` 把课程与公共练习混在同一用户额度内，并且达到上限后无条件销毁最早的实例，未检查自动销毁开关。旧实例销毁后公共端口可以回收，故后续看到相同端口并不代表两个存活容器占用了同一端口；未修改端口分配器来掩盖生命周期问题。
2. `GetInstance` 对 `IsLoaded=true` 直接返回，后续创建只修复特定旧预览值，没有修复缺失的实例 Flag。629 的当前数据状态会令 `ContainerConfig.Flag=null`，Docker 执行面因而不添加 `GZCTF_FLAG`。该历史状态最初如何产生未得到审计证据，不能断言一定是人工改题类型导致。

修复为独立 `TrainingContainerPolicy`（默认每用户 3 个，0 不限，负值启动拒绝），课程达到上限只拒绝新建。公共练习仅在显式打开自动销毁时清理其他公共练习；课程/公共练习互不计数、互不驱逐。排队只统计尚未关联容器的活跃 Create 票据，避免漏记培训队列、把延期计作新建或重复计数。

创建新容器前在运行锁内补齐缺失/空白的动态 Flag 并保存，再传入容器配置；有效 Flag、已有运行实例及原有旧预览修复行为保留。培训 API 达到额度时给出明确提示。配置、维护边界及测试入口见 [课程容器生命周期](../../modules/training-container-lifecycle.md)。

## 改动范围

- 后端仓储、独立培训容器策略及 DI 注册、培训创建失败提示。
- 无数据库迁移、无 Agent 协议变化、无前端文件或生成 API 结构变化。
- 单元测试覆盖同页三实例、两类额度、自动清理开关、排队/运行双计数、Flag 修复、既有 Flag 保留和创建幂等。
- PostgreSQL 集成覆盖旧记录修复和入队幂等；独立 BusyBox 容器实测只运行 HTTP 服务及测试用环境变量写入校验。

## 验证与当前状态

- Release solution build 通过；最终增量的集成测试项目也单独构建通过。
- 后端全量单测 1159/1159 通过。
- 定向 PostgreSQL / Docker 集成测试 11/11 通过（`InstanceFlagIsolationTests`、`ExerciseWorkflowTests`、`ContainerEntryPublicationTests`、`TrainingContainerDockerTests`）。真实 Docker 用例验证了三个不同映射端口、三个存活容器、数据库/环境变量/测试文件值一致，以及销毁第二个后第一和第三个继续运行。
- `git diff --check` 通过。全量集成、前端全套门禁、浏览器多尺寸和真实 Nginx 公网入口没有执行；前端无改动，未执行与本任务无关的 AWDP 流程。
- 本机 Docker Desktop 因残留 inference socket 无法启动，未重置用户 Docker 数据。远端测试使用 `/tmp/yinyu-training-regression-20260916`，独立 Testcontainers PostgreSQL/Redis 数据库；未连接现有业务数据库执行测试写入。Ryuk 拉取被网络阻断后仅对此次测试禁用；测试结束后按明确容器 ID 和 session label 清理本任务 8 个已退出数据库/Redis 容器及其匿名卷，临时包和目录已删除。
- 三个 BusyBox 运行容器及三个未启动的镜像诊断容器均已清理，最终 Testcontainers / 镜像诊断容器无残留；主站与本机 Agent 仍 active。
- 跨平台临时测试包的 static-web-assets 路径改为临时目录，未修改业务发布目录；初轮用户名长度及内部端口/外部端口断言错误已修正后复测。

## 提交、部署与后续

- 修复提交 `1515fb9af702ff885d0fa67ab42acbf117e9a086` 已推送到 `origin/codex/training-instance-flag-fix`，通过远端引用回读核对。本条记录随后以文档提交更新；不合并 `main`，保留 worktree 供审查。
- 用户后续明确授权部署，2026-09-16 05:54 UTC 已原子切换主站至维护分支修复 `eaac7f26`。Agent、前端、数据库 schema 和 Registry 未更新；发布与备份见 [发布记录](2026-09-16-training-hotfix-rollout.md)。
- 现场 629 已通过正式 API 创建流程补齐实例 Flag 关联（记录 626），独立停止/重建后仍有效；未手工改写 Flag 或业务实例行。三个课程实例最终同时运行，供用户继续测试。
- 发布阶段已核对实际维护版本和历史增量包，单独回移课程修复，避免夹带最新 main 的 TeamLab 重构和迁移。新 release、旧回退目录及已恢复验证的新鲜备份均保留。
