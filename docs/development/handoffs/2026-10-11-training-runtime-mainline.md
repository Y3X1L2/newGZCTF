# 培训实例修复回流主线、PR 与部署

## 任务目标与授权

用户要求提交 PR，确认修复不破坏现有后端行为后合并 main 并部署。本次延续禁止解题、访问漏洞接口、读取题目 Flag 文件或提交真实 Flag 的边界；验收仅通过正规平台流程和运行配置验证实例隔离、端口、队列与注入状态。

## 基线与只读证据

- 从最新 `origin/main 2479a165b553bb841560130c0211abbb011ab420` 创建 `codex/training-runtime-quota-mainline`，worktree `D:/Work/newGZCTF-training-mainline`。
- `.27` 当前 Main 发布源 `49948764bd0715142911d5b22c68852ab3d38bb8`，DLL SHA-256 `61ff70155b2fae774b6b462a818fed589123bd65ddb421f06e0fad1b19a69ae9`，目录 `/srv/yinyu-data/releases/teamlab-delete-reservation-49948764bd0715142911d5b22c68852ab3d38bb8-20261010T0645Z/publish`；前端源 `a63937c6`，Agent `87e011cf…`。
- 当前配置练习额度为 1、自动销毁为 False。课程题目 627/629/630 的 TrainingCourseId 均为 32，归属正常。
- 2026-10-11 06:29:28 UTC 创建 627；06:29:34 仓储日志明确记录自动销毁 627；06:29:40 创建 629。旧容器在 `.30` 库存不存在，629 在 `.31` Running，入口复用 30000。
- 原修复 `1515fb9a` / 发布回移 `eaac7f26` 和其课程回归测试未进入 main，当前发布重新携带旧额度/无条件自动销毁逻辑。主站程序集也不含 TrainingContainerPolicy；9 月临时发布不能替代主线集成。

## 实施与验证计划

1. 在最新主线应用已验证的培训独立额度与缺失动态 Flag 修复，审核与当前代码的兼容性。
2. 扩充必要回归：培训/公共练习互不驱逐；自动销毁开关；运行/排队计数；同用户并发与幂等；静态/动态 Flag 及既有运行实例；失败与旧实例清理。
3. 完成 Release、后端全量单测和适用 PostgreSQL 集成，检查模型/迁移、API 与架构边界，提交并推送。
4. 创建 PR，审核最终差异和 CI，确认通过后正常合并 main，记录合并身份。
5. 根据实际服务器布局和既有安全备份流程创建新备份与独立 release，原子切换并保留回退。
6. 正规 API 验证课程多实例、不同端口、独立停止/重建及注入匹配布尔结果；核对现有服务/节点/队列/镜像/教学资源，更新已证事实并推送交接。

## 当前状态

in progress。已将原修复的后端和测试变更应用到最新主线，并补充跨用户/不限额度/失败清理单测，以及并发课程 API 和队列隔离 PostgreSQL 回归。

Release solution 构建通过，后端全量单测 **1492/1492**、培训/旧 Flag 定向 **25/25** 通过。所有原有 CTF、AWDP、Theory、Runtime、Fleet、TeamLab 单测均保持通过；数据库实体、模型快照、历史 migration、Agent、生成 API 和前端源码没有差异。公开练习唯一有意行为变化是遵守 AutoDestroyOnLimitReached 开关；开启时只驱逐其他公共练习，不触碰课程实例。有效运行实例仍直接复用；缺失 Flag 仅在新容器创建前补齐，不改写已有运行容器的 Flag。

本机 Docker Desktop 当前不可用，PostgreSQL/Docker 全量集成和其他 Quality 门禁将由 PR 的 Linux CI 执行，全部通过并审核后才合并。尚未修改服务器、提交 PR 或部署。
