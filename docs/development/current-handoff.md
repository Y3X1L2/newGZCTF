# YINYU 当前项目交接说明

更新时间：2026-09-01

本文面向刚接手项目的开发者或 AI。它补充 `AGENTS.md`、`docs/development/current-state.md` 和 `docs/README.md`，用于快速建立项目上下文；如果与当前运行行为、源码或 OpenAPI 冲突，以真实运行行为和当前源码为准。

> 2026-09-05 本次任务更新：请先读 [PR #8 改分支交付与服务器验证交接](handoffs/2026-09-05-pr8-branch-deployment-handoff.md)。PR #8 已关闭而未合并，活动分支为 `codex/practice-deployment-validation`。本文下方的 2026-09-01 release、账号密钥路径、端口未确认说明和代理地址属于旧交接背景，不作为本次工作机连接依据；当前生产 SHA 为 `3e5526dc`，新分支部署验收未执行。

## 1. 接手顺序

首次接手先阅读：

1. `AGENTS.md`：长期协作、架构边界、编码、测试和发布禁区；
2. `docs/development/current-state.md`：当前能力和真实缺口；
3. `docs/README.md`：现行文档入口；
4. `docs/platform-commercialization-master-plan.md`：平台总架构；
5. 本文：服务器、基线、最近修复和交接动作。

然后执行：

```powershell
git status --short --branch
git fetch origin --prune
git log -5 --oneline --decorate
```

不要把 `docs/archive/` 中的阶段计划、历史服务器地址、旧测试数量或旧发布状态当作当前事实。

## 2. 当前基线

### 2.1 运行代码基线

当前稳定运行代码基线：

```text
标签：stable-20260831
提交：81a6e02b7dbe3d1f12094b606e5b3a93fd86de0c
```

10.24 运行 release 由该标签精确标识。GitHub `main` 已在 `1a390432b1135da055a5a8488575fd10015f0bbd` 合入 Phase 09 TeamLab networking，当前 `main` 明确比运行 release 更新；需要复现服务器二进制时使用该稳定标签，需要继续开发时从最新 `origin/main` 创建分支。

发布物：

```text
Release ID：stable-20260831
文件数：991
归档 SHA-256：01cdace30a0f212411f3006cac0e8a5b5b8e3bff2a3f9307d4defffd86baff2b
```

本地发布物目录：`artifacts/releases/stable-20260831/`。发布物、数据库副本和运行附件禁止提交到 Git。

### 2.2 10.24 发布事实

主站服务器 `10.24.0.27` 当前活动目录：

```text
/opt/gzctf/releases/stable-20260831/publish
```

manifest 的 `gitCommit` 为 `81a6e02b7dbe3d1f12094b606e5b3a93fd86de0c`，主站 DLL 和 Agent 二进制摘要已与 manifest 核对一致。服务器曾回读到的数据库迁移头为：

```text
20260815012026_AddExerciseCreatorTracking
```

以上为历史发布记录。主线已于 2026-09-03 恢复该迁移，原始 Git 提交也已定位为 `c6a2b7f4b5637f5622cfa6bdb42624d5242a0c80`。后续生产迁移与发布事实见 `current-state.md`；不得据此旧发布目录判断现网状态，也不得删除迁移历史或用旧快照覆盖主线。

发布前备份目录：

```text
/opt/gzctf/backups/stable-20260831-pre
```

包含 PostgreSQL dump、旧 release、shared 附件和 systemd 配置。

## 3. 架构速览

```text
浏览器
  -> 主站 Contracts / Application / Domain
       -> PostgreSQL：业务和运行状态事实
       -> Redis：缓存、租约、协调和高频缓冲
       -> Runtime / Fleet / VM / TeamLab ports
            -> AgentClient -> GZCTF.Agent
                 -> Docker / KVM / libvirt / 网络工具
```

关键规则：

- 主站是模块化单体，Agent 是独立节点执行面。
- Controller 只处理协议、授权、用例调用和 HTTP 映射，不直接编排 Agent 命令。
- 跨模块读取使用公开 query contract，写入使用 application command。
- PostgreSQL 是业务、运行状态、队列和审计事实源；Redis 不能作为恢复事实源。
- Docker、VM、培训、AWDP 和 TeamLab 运行任务共用 `DeploymentQueueTicket`。
- Agent 只执行已经校验的本机操作，不读取比赛、课程、计分或权限实体。
- Docker 和 KVM 能力独立判断，缺少 KVM 不能阻断 Docker 调度。

正式前端入口：

```text
src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx
src/GZCTF/ClientApp/src/vnext/app/shell/moduleRegistry.ts
```

前端依赖方向：

```text
Route -> feature controller/hook -> feature panel -> foundation component
                         |
                         +-> feature API adapter -> generated API client
```

## 4. 功能状态

| 模块 | 当前能力 | 仍需注意 |
| --- | --- | --- |
| CTF | 赛事、题目、附件、静态/动态 Flag、Docker、KVM/Windows VM、提交、计分、榜单 | Docker/VM/公网入口仍需真实环境验收 |
| 理论考试 | 题库、组卷、草稿、提交、成绩和答案回顾 | 批量预检、答卷详情和导出契约仍可增强 |
| 培训 | 课程、章节、资源、教师、报名审核、实例、理论作业、进度和学员详情 | 培训 Windows VM 不在当前范围 |
| 自主练习 | `/practice`、筛选、来源导入、附件、多 Flag、Docker 实例、提交、统计和后台管理 | 已进入主线，目标生产仍需完整迁移和运营验收 |
| AWDP | 服务、轮次、Checker、攻击、修补、重置、恢复、停止、计分和日志 | 真实攻击/修补按人工验收手册执行 |
| 运行底座 | Agent、Docker/KVM、镜像、分发、容量、队列、事件、日志和恢复 | 多节点故障、容量和长时间运行需现场签收 |
| TeamLab | 拓扑、发布、计划、runtime、执行计划 V2、OVN/OVS、链路策略、连接器、设备包、资源池、远程访问、流量、抓包和管理页面 | 10.24 尚未发布本次合并；双 Worker、规模、长期流量和复杂注入需现场验收 |
| 身份与管理 | 本地登录、Portal SSO、用户、战队、学员组、系统设置、个人主页和管理端 | Portal 对接方源码不在本仓库 |

真实缺口集中记录在 `docs/yinyu-vnext-deferred-contract-gaps.md`、`docs/modules/README.md` 和 `current-state.md`，不要在页面中伪造缺失接口的成功状态。

## 5. 服务器清单

| 主机 | 账户 | 角色和端口 | 重要说明 |
| --- | --- | --- | --- |
| `10.24.0.27` | `whoami` | 主站 `8080`、Agent `5001`、PostgreSQL `5432`、Redis `6379`、Guacamole `4822` | 当前稳定 release；发布前必须备份数据库和 `/opt/gzctf/shared/files` |
| `10.24.0.30` | 以节点注册信息为准 | WorkerNode Agent `5001` | Docker/KVM 能力以节点 manifest 和实时健康为准 |
| `10.24.0.31` | 以节点注册信息为准 | WorkerNode Agent `5001` | Docker/KVM 能力以节点 manifest 和实时健康为准 |
| `203.195.157.191` | `ubuntu` | 公网 Nginx `80`、WireGuard `51820`、动态 TCP 端口池 `30000-30059` | 只负责公网转发和 WireGuard；不要碰 9091、18080 业务 |
| `10.24.0.28` | 由环境配置提供 | 内网 Docker Registry `5000` | 作为 Docker 镜像来源，凭据不得写入文档 |
| `10.0.7.118` | 由环境配置提供 | 备用测试平台 `8080` | 使用前必须重新探测，不视为当前生产事实 |
| `106.52.207.52:42755` | 无 | 历史公网平台入口 | 只在实际路由可达时使用，不替代 10.24 当前主站核对 |

10.24 还曾观察到 `8081`、`3001` 监听，当前用途未确认；除非先完成进程和配置归属核对，否则不要占用、停止或修改这两个端口。

本机访问 GitHub 必要时使用代理：`http://127.0.0.1:10808`。

## 6. 认证与服务器访问

项目禁止把服务器密码、API token、Cookie、Agent token、WireGuard 私钥、Registry 凭据和完整连接串写入 Git、文档、日志或测试快照。

为降低新会话接手成本，已将本机 SSH 公钥安装到：

- `whoami@10.24.0.27`；
- `ubuntu@203.195.157.191`。

公钥文件：`C:\Users\Cloud\.ssh\id_ed25519`，指纹：

```text
SHA256:COAjEsRMGm5MSMyoUspXGAUdOsOCzpTSwPDinjk/41s
```

PowerShell 免密码连接示例：

```powershell
ssh -i "$env:USERPROFILE\.ssh\id_ed25519" whoami@10.24.0.27
ssh -i "$env:USERPROFILE\.ssh\id_ed25519" ubuntu@203.195.157.191
```

需要 `sudo` 时使用安全的交互认证，不要把密码写入命令行、脚本或交接文档。Agent 和网关 token 只从服务器 root 配置读取，排查时只输出脱敏后的状态。

## 7. 发布与回滚

当前正式发布入口：

```powershell
pwsh -NoProfile -File scripts/deployment/build-gzctf-release.ps1 `
  -Configuration Release `
  -ReleaseId <release-id>
```

发布前必须确认数据库备份和 shared 文件备份，使用：

```powershell
python scripts/deployment/deploy-gzctf-release.py `
  artifacts/releases/<release-id>/<release-id>.tar.gz
```

连接参数通过环境变量提供：`GZCTF_DEPLOY_HOST`、`GZCTF_DEPLOY_USER`、`GZCTF_DEPLOY_PASSWORD` 或 `GZCTF_DEPLOY_KEY_FILE`。生产发布步骤以 `docs/operations/vnext-maintenance-window-rollout.md` 为准。

禁止使用根目录的历史 `scripts/deploy*.py`、`scripts/deploy*.sh` 和 `scripts/one-click-deploy.*` 直接发布生产。

发布后至少检查：

1. release manifest 的 Git SHA 与目标提交一致；
2. manifest 中主站和 Agent 文件摘要与磁盘一致；
3. `publish/files` 指向 `/opt/gzctf/shared/files`；
4. 主站、Agent、数据库、Redis、节点和队列状态正常；
5. 首页、登录、附件、一条 Docker 实例创建/入口/销毁链路正常；
6. 公网入口 ACK 和实际访问正常；
7. 失败时按备份和旧 release 原子回退。

## 8. 最近故障与修复

### 附件 404

曾出现数据库中存在附件记录、shared 文件存在，但 `/assets/<hash>/<name>` 返回 404。根因是当前 release 的 `files` 链接错误继承了旧 release 的目录，而不是共享持久化目录。

当前规则已固定为：

```text
/opt/gzctf/publish/files -> /opt/gzctf/shared/files
```

`scripts/deployment/activate-gzctf-release.sh` 已固定使用 `$root/shared/files`，并由 `RegistrySetupScriptTests` 覆盖。发布后必须下载一个已知附件，并核对状态码、长度和 SHA-256。

### 公网同步恢复

203 网关曾因旧同步服务未启用而无法恢复动态转发。当前使用 Nginx stream 同步器和 timer，timer 同时配置 `OnBootSec`、`OnActiveSec` 和 `OnUnitActiveSec`，避免手动重启 timer 后不再触发。旧同步服务保持 disabled。

### 数据库迁移漂移

`20260815012026_AddExerciseCreatorTracking` 的历史恢复已完成；详见 `handoffs/2026-09-02-migration-drift-reconciliation.md`。另外两条旧 Theory migration 的来源仍未恢复，不得伪造；任何后续升级仍须使用新鲜生产备份副本核验。历史列的存在不代表当前业务模型重新启用了创建者功能。

## 9. 下一位接手者第一步

1. 使用本文和 `current-state.md` 建立上下文；
2. 确认 `git status`、`origin/main` 和当前稳定标签；
3. 不从 10.24 当前运行目录复制源码，不做 DLL 热替换；
4. 先读任务涉及的模块契约和 API，再从 `main` 创建 `codex/<task-name>`；
5. 新功能至少同步更新 adapter、测试、必要文档和 current-state；
6. 涉及部署时先备份，再使用独立 release 目录和原子切换；
7. 完成任务后记录提交、测试、部署、回滚和未完成事项。

## 10. 当前不应做的事

- 不把归档计划当作当前产品需求；
- 不恢复旧前端页面或全局 CSS 覆盖；
- 不为缺失 API 伪造统计、运行状态或成功结果；
- 不直接从 Controller 编排 Agent 命令；
- 不建立第二套运行队列；
- 不修改 203 上 9091、18080 的业务；
- 不在仓库或聊天中复制服务器密码、Token、Cookie、私钥和 Flag。
