# YINYU vNext 现网与远端基线记录

> 采集时间：2026-07-21 14:07 UTC
> 采集方式：SSH 只读检查
> 目标环境：`10.24.0.27`
> 本次检查未重启、停止、替换或修改任何现网服务

## 1. 现网应用

| 项目 | 当前值 |
| --- | --- |
| 主机名 | `whoami` |
| 服务单元 | `gzctf.service` |
| 服务状态 | `active / running` |
| 启动时间 | `2026-07-11 17:43:25 UTC` |
| 运行用户 | `root` |
| 监听地址 | `*:8080` |
| 发布目录 | `/opt/gzctf/releases/phase2-20260711T173902Z` |
| 当前软链接 | `/opt/gzctf/publish` |
| 数据库迁移头 | `20260711115423_CompletePhaseOneChallengeApi` |
| 主数据库大小 | `133 MB`（139,910,167 字节） |
| `/healthz` | `404`，当前现网版本尚未提供该端点 |

当前发布制品没有嵌入可检索的 Git SHA，也没有同目录版本清单，无法可靠地把运行二进制反推到某个源码提交。为保证回滚身份可验证，记录以下制品摘要：

| 文件 | SHA-256 |
| --- | --- |
| `GZCTF` | `df487cba6099ba293291da985ffaa0af9dc6f1915f5027cb93717eb4448b4090` |
| `GZCTF.dll` | `25cca85dd30493cfd4801228060027dab94abf5d64febe17dd23e0cc33162edf` |

后续 RC 必须同时写入 Git SHA、构建时间和发布物 SHA-256，不能继续只依赖目录名称识别版本。

## 2. 基础容器

现网应用本身不是容器部署；PostgreSQL、Redis 和 Guacamole 由四个独立 Docker 容器运行，未发现 Compose 项目标签。

| 容器 | 镜像 | 镜像 ID | 重启策略 |
| --- | --- | --- | --- |
| `gzctf-postgres` | `postgres:16-alpine` | `sha256:16bc17c64a573ef34162af9298258d1aec5482232985b33ed7b1eac33ba35c229` | `unless-stopped` |
| `gzctf-redis` | `redis:7-alpine` | `sha256:6ab0b6e7381779332f97b8ca76193e45b0756f38d4c0ddcda72dbb3c32061ab99` | `unless-stopped` |
| `guacd` | `guacamole/guacd:latest` | `sha256:2d266a525a72ad55d8ea7cd4ca8342a1b44075759b003660d8684821b3636a922` | `unless-stopped` |
| `guacamole` | `guacamole/guacamole:1.5.5` | `sha256:0f62f6d17ab379e46aa66874b2ff564dab8556a6ef5e754a69cbb34c32d3e588a` | `unless-stopped` |

当前不存在可记录的 Compose 配置。正式发布前应备份每个容器的脱敏 `docker inspect`、挂载、端口和重启策略；不能假定执行 `docker compose up` 可以还原现网基础服务。

## 3. 配置结构

现网配置位于 `/opt/gzctf/publish/appsettings.json`。本次只记录配置键，不读取或保存密码、Token、连接串和密钥值。

已配置的主要区域：

- PostgreSQL、Redis、对象存储连接。
- KVM 和 Windows VM 资源参数。
- Guacamole 地址、认证和公网入口。
- Docker 容器提供者、公网端口池和 Nginx 映射同步。
- 节点 Agent、镜像仓库和本地节点调度。
- UDP 公网网关。
- IAM Portal SSO。

敏感值继续只保留在目标服务器配置中，不进入 Git、验收报告或发布清单。

## 4. 当前源码与远端关系

采集后的 Git 关系：

| 引用 | SHA | 说明 |
| --- | --- | --- |
| `origin/main` | `bbb217373d8c82c52cb26c5cdde83207efa2f223` | `feat: 完成 vNext 认证与个人主页` |
| `origin/codex/vnext-clean-rebuild` | `bbb217373d8c82c52cb26c5cdde83207efa2f223` | 与 `origin/main` 一致 |
| 本地阶段 1 起点 | `9e17d8a1bf25def80b18befcc933b0f10ac96f6e` | 通用管理工作台，本地领先 1 个提交 |

`9e17d8a` 的父提交就是当前 `origin/main`，远端不存在本地尚未包含的提交。本轮无需执行 merge 或 rebase，不引入额外功能变更。

## 5. 未提交差异审计

审计时的未提交改动分为三组：

1. 比赛和培训实例公网入口同步提示、8 秒缓解窗口及回归测试。
2. 数据库、CTF、理论考试、培训、通用管理和 AWDP 验收文档与脚本。
3. Microsoft、MailKit、OpenTelemetry 和 SignalR Redis 依赖补丁升级。

审计结果：

- 未发现硬编码管理员密码、IAM Token、私钥或用户提供的历史凭据。
- 验收脚本不包含默认现网地址，必须显式传入 `BaseUrl`。
- 验收脚本中的临时用户密码在运行时随机生成。
- 数据库恢复脚本只允许操作 `gzctf_vnext_*` 和 `gzctf_restore_*` 命名的隔离数据库。
- CTF 脚本的 `Remove-Item` 只清理脚本自身创建的临时附件文件。
- `git diff --check` 未发现空白错误。

## 6. 已识别现网风险

以下是现状记录，不代表本次检查修改了现网：

- 运行制品缺少 Git SHA 和统一版本清单。
- `/healthz` 尚不可用，当前只能通过进程和业务请求判断健康状态。
- GZCTF 应用以 root 用户运行，权限范围过大。
- 当前发布目录中大量文件权限为 `0777`，不符合最小权限原则。
- 基础容器没有 Compose 项目标签，恢复依赖额外的容器配置备份。
- `guacd` 使用浮动 `latest` 标签，恢复时不能仅依赖标签保证同一版本。

这些问题进入发布物与部署演练阶段处理；在候选版本通过全部门禁前，不直接调整现网服务权限和运行方式。
