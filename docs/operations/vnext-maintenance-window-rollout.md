# YINYU vNext 维护窗口部署实施手册

> 目标主机：`10.24.0.27`
>
> 当前服务：`gzctf.service`
>
> 当前入口：`http://10.24.0.27:8080`
>
> 发布方式：停写、完整备份、独立目录部署、原子切换、开放前验收
>
> 状态：待执行

## 1. 使用范围

本手册把原“基础设施验收、完整业务验收、离线演练、正式上线”合并到一次有计划的维护窗口中。已经完成的前端、单元、集成和 IAM 全量测试不在窗口内重复执行；窗口集中验证只有真实基础设施才能证明的迁移、节点、镜像、实例、公网入口和回滚能力。

| 原计划动作 | 简化后处理 |
| --- | --- |
| 单独搭建完整双实例预发布环境 | 取消；当前单实例上线不重复做 HA 验收 |
| 再跑全部前端、单元和 Docker 集成测试 | 复用既有通过记录；最终 SHA 只跑快速门禁，业务代码再变化时才补受影响全量测试 |
| 在数据库副本重复恢复、迁移和回滚演练 | 不再重复；维护窗口使用最终备份直接迁移，开放前失败按本手册完整恢复 |
| OpenTelemetry Collector 导出验收 | Collector 未部署时不阻塞；保留本地指标、结构化日志和脱敏门禁 |
| AWDP 全流程占用维护窗口 | 缩减为启动、一次攻击判分、停止和清理；修补、重置、恢复按人工文档继续记录 |
| 新旧版本并行灰度 | 取消；停写后一次性切换，开放前完成实机验收 |

本手册不包含：

- TeamLab vNext 前端上线。
- 独立练习模块。
- AWDP 3D 态势等增强功能。
- Linux VM、统一题目池和批量导入事务能力。

## 2. 强制原则

1. **不原地覆盖。** 新版本必须进入独立发布目录，旧发布目录保持不变。
2. **不只备份二进制。** 回滚包必须包含数据库、文件存储、配置和公网网关状态。
3. **开放前不写业务数据。** 维护窗口内只使用可清理的验收数据；开放前失败可以完整恢复数据库。
4. **不执行 EF 降级回滚。** Phase 2 会删除历史明文 RDP 密码，正确回滚方式是恢复完整数据库备份。
5. **不部署本地孤立提交。** 发布 SHA 必须已推送远端，并有不可变 RC 标签。
6. **不使用旧部署脚本。** 禁止使用 `scripts/deploy.sh`、`scripts/deploy-server.py`、`scripts/one-click-deploy.*`；这些脚本包含原地覆盖、强制杀进程或旧分支假设。
7. **不输出敏感值。** 日志和报告只能记录配置键、文件摘要和状态，不能记录连接串、Token、Cookie、密码或 Registry 凭据。

## 3. 人员与决策

维护窗口开始前填写：

| 角色 | 姓名 | 职责 |
| --- | --- | --- |
| 发布执行人 | 待填写 | 构建、上传、迁移、切换 |
| 验收执行人 | 待填写 | 按清单验证业务和实例 |
| 网关执行人 | 待填写 | Nginx、WireGuard、FRP、公网入口 |
| Go/No-Go 决策人 | 待填写 | 开放或回滚最终决策 |
| 记录人 | 待填写 | 保存时间、结果和证据路径 |

所有失败项记录“时间、步骤、错误摘要、处置和复测结果”。凭据输入期间暂停终端录制。

建议时间预算如下。时间用尽不能替代门禁，任何阻断项仍按 No-Go 处理。

| 阶段 | 建议耗时 | 是否占用停机时间 |
| --- | --- | --- |
| A：冻结、快速门禁和构建 | 45-90 分钟 | 否 |
| B：停写、清理和完整备份 | 20-40 分钟 | 是 |
| C：校验、迁移和切换 | 15-30 分钟 | 是 |
| D：真实基础设施最小验收 | 45-90 分钟 | 是 |
| Go 决策与开放 | 10 分钟 | 是 |
| 开放后观察 | 30 分钟、2 小时、24 小时 | 否 |

## 4. 目录与变量

以下变量仅为命令模板，执行前按实际环境确认：

```bash
export RELEASE_SHA='<最终提交的完整 SHA>'
export RELEASE_TAG='vnext-rc.4'
export RELEASE_TS="$(date -u +%Y%m%dT%H%M%SZ)"
export RELEASE_ROOT='/opt/gzctf/releases'
export VALIDATION_ROOT='/opt/gzctf-vnext'
export BACKUP_ROOT="${VALIDATION_ROOT}/backups/${RELEASE_TS}"
export REPORT_ROOT="${VALIDATION_ROOT}/reports/${RELEASE_TS}"
export OLD_RELEASE="$(readlink -f /opt/gzctf/publish)"
export NEW_RELEASE="${RELEASE_ROOT}/vnext-${RELEASE_SHA}"
export APP_SERVICE='gzctf.service'
export AGENT_SERVICE='gzctf-agent.service'
export POSTGRES_CONTAINER='gzctf-postgres'
export DATABASE_NAME='gzctf'
export METRIC_PORT='3001'
# 仅本地文件存储填写绝对路径；外部对象存储保持为空。
export STORAGE_PATH=''
```

不得在确认 `OLD_RELEASE`、`NEW_RELEASE` 和 `BACKUP_ROOT` 后继续执行：

```bash
printf 'old_release=%s\nnew_release=%s\nbackup_root=%s\n' \
  "$OLD_RELEASE" "$NEW_RELEASE" "$BACKUP_ROOT"
test -d "$OLD_RELEASE"
test "$OLD_RELEASE" != "$NEW_RELEASE"
test -L /opt/gzctf/publish
case "$NEW_RELEASE" in /opt/gzctf/releases/vnext-*) ;; *) exit 2 ;; esac
case "$OLD_RELEASE" in /opt/gzctf/releases/*) ;; *) exit 2 ;; esac
case "$BACKUP_ROOT" in /opt/gzctf-vnext/backups/*) ;; *) exit 2 ;; esac
```

现网基线已经确认 `/opt/gzctf/publish` 是软链接。若现场检查不一致，停止执行并先处理目录迁移，不得让后续 `mv -T` 尝试覆盖真实目录。

## 5. 阶段 A：窗口前冻结候选

### 5.1 Git 门禁

在开发机执行：

```powershell
git status --short
git fetch origin
git log --oneline HEAD..origin/main
git log --oneline origin/main..HEAD
git diff --check
```

要求：

- 工作区没有来源不明或未处理文件。
- `HEAD..origin/main` 为空；如果不为空，先做兼容审查，不能直接部署。
- 所有上线提交都已推送到远端工作分支。
- 历史标签 `vnext-rc.2` 和 `vnext-rc.3` 不移动；`vnext-rc.3` 因 Linux RID 构建说明缺失已在部署前淘汰，不得用于生产发布。

```powershell
$releaseSha = git rev-parse HEAD
$releaseTag = 'vnext-rc.4'
git tag -a $releaseTag $releaseSha -m "YINYU vNext maintenance-window release candidate"
git push origin codex/vnext-clean-rebuild
git push origin $releaseTag
```

### 5.2 构建与快速门禁

全量 `124/124`、`493/493`、`247/247` 和 IAM 实时验收已有记录。最终 SHA 只执行以下发布门禁；若最终 SHA 又包含业务代码变化，则重新执行受影响的全量测试。

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj `
  -c Release --no-build --filter "FullyQualifiedName~AuthenticationTests"

Set-Location src/GZCTF/ClientApp
pnpm validate:locales
pnpm lint:check
pnpm check
pnpm check:architecture
pnpm test
Set-Location ../../..
```

### 5.3 构建不可变发布物

```powershell
$sha = git rev-parse HEAD
$artifactRoot = Join-Path $PWD "artifacts/$sha"
dotnet tool restore
dotnet restore src/GZCTF/GZCTF.csproj --runtime linux-x64
dotnet publish src/GZCTF/GZCTF.csproj -c Release --no-restore `
  --runtime linux-x64 --self-contained false `
  -o "$artifactRoot/publish"
dotnet tool run dotnet-ef migrations bundle --project src/GZCTF/GZCTF.csproj `
  --startup-project src/GZCTF/GZCTF.csproj --configuration Release `
  --target-runtime linux-x64 --self-contained `
  --output "$artifactRoot/gzctf-migrate"
git show -s --format='%H%n%cI%n%s' HEAD | Set-Content "$artifactRoot/version.txt"
```

发布前必须检查 `publish/GZCTF`、`publish/agent/gzctf-agent` 和 `gzctf-migrate` 的前四字节均为 `7F-45-4C-46`（ELF）。出现 `4D-5A`（Windows PE）立即停止，不得上传服务器。

在 Linux 或 Git Bash 中为发布物生成校验清单和压缩包：

```bash
cd "artifacts/${RELEASE_SHA}"
find publish -type f -print0 | sort -z | xargs -0 sha256sum > publish.sha256
sha256sum gzctf-migrate version.txt publish.sha256 > release-files.sha256
tar -czf "gzctf-vnext-${RELEASE_SHA}.tar.gz" \
  publish gzctf-migrate version.txt publish.sha256 release-files.sha256
sha256sum "gzctf-vnext-${RELEASE_SHA}.tar.gz" > "gzctf-vnext-${RELEASE_SHA}.tar.gz.sha256"
```

发布记录必须包含：完整 SHA、RC 标签、构建时间、压缩包摘要、migration bundle 摘要和预期迁移头。

## 6. 阶段 B：维护窗口开始与旧环境备份

### 6.1 先清理活动资源

在旧平台仍可操作时：

1. 禁止新建比赛、课程、容器和 VM。
2. 通知在线用户维护开始。
3. 从管理端销毁活动 Windows VM，确认 KVM 和 Guacamole 连接同步清理。
4. 停止正在执行的镜像导入、部署队列和 AWDP 测试比赛。
5. 记录仍运行的 Docker 实例、节点状态和公网映射；不能只删除数据库记录。

数据库核查：

```sql
SELECT "Id", "VmName", "Status", "NodeId", "GuacamoleConnectionId"
FROM "VmInstances"
WHERE "Status" IN (0, 1, 2)
ORDER BY "CreatedAt";
```

预期活动 VM 为 0。否则停止发布并先完成资源清理。

### 6.2 停止写入

当前主站没有独立维护代理时，以停止应用作为停写边界：

```bash
if systemctl cat "$AGENT_SERVICE" >/dev/null 2>&1; then
  sudo systemctl stop "$AGENT_SERVICE"
  test "$(systemctl is-active "$AGENT_SERVICE")" = inactive
fi
sudo systemctl stop "$APP_SERVICE"
test "$(systemctl is-active "$APP_SERVICE")" = inactive
if ss -lntp | grep -q ':8080 '; then
  echo 'Port 8080 is still listening' >&2
  exit 1
fi
```

从此刻到 Go 决策前不开放外部写入。

### 6.3 创建备份目录

```bash
sudo install -d -m 0700 -o "$(id -un)" -g "$(id -gn)" \
  "$BACKUP_ROOT" "$REPORT_ROOT"
printf '%s\n' "$RELEASE_SHA" | sudo tee "$REPORT_ROOT/candidate-sha.txt" >/dev/null
printf '%s\n' "$OLD_RELEASE" | sudo tee "$REPORT_ROOT/old-release-path.txt" >/dev/null
```

若 SSH 会话中断，重新执行第 4 节变量块，并从 `candidate-sha.txt`、`old-release-path.txt` 核对上下文后再继续，禁止凭记忆重建变量。

### 6.4 PostgreSQL 完整备份

优先使用仓库脚本：

```bash
export GZCTF_POSTGRES_CONTAINER="$POSTGRES_CONTAINER"
export GZCTF_DATABASE="$DATABASE_NAME"
export GZCTF_VALIDATION_ROOT="$BACKUP_ROOT"
bash scripts/validation/preprod-database-backup.sh \
  | sudo tee "$REPORT_ROOT/database-backup.log"
```

随后验证：

```bash
sha256sum -c "$BACKUP_ROOT/backups/latest.dump.sha256"
sudo docker exec -i "$POSTGRES_CONTAINER" pg_restore --list \
  < "$BACKUP_ROOT/backups/latest.dump" \
  | sudo tee "$REPORT_ROOT/database-contents.txt" >/dev/null
grep -F 'DataProtectionKeys' "$REPORT_ROOT/database-contents.txt"
```

要求：备份非空、摘要通过、目录可读取，并包含 `DataProtectionKeys`。任一失败立即 No-Go。

### 6.5 旧程序、文件和配置备份

```bash
sudo tar -C / -czf "$BACKUP_ROOT/old-release.tar.gz" "${OLD_RELEASE#/}"
sudo systemctl cat "$APP_SERVICE" > "$REPORT_ROOT/gzctf.service.txt"
sudo cp -a "/etc/systemd/system/${APP_SERVICE}"* "$BACKUP_ROOT/" 2>/dev/null || true
if systemctl cat "$AGENT_SERVICE" >/dev/null 2>&1; then
  sudo systemctl cat "$AGENT_SERVICE" > "$REPORT_ROOT/gzctf-agent.service.txt"
  sudo cp -a "/etc/systemd/system/${AGENT_SERVICE}"* "$BACKUP_ROOT/" 2>/dev/null || true
fi
sudo cp -a /etc/gzctf "$BACKUP_ROOT/etc-gzctf" 2>/dev/null || true
```

根据 `ConnectionStrings:Storage` 的实际配置判断存储类型。若为本地文件存储，先将 `STORAGE_PATH` 设置为真实绝对路径，再执行以下校验和备份；不得把示例占位符直接当作路径执行：

```bash
test -n "$STORAGE_PATH"
case "$STORAGE_PATH" in /*) ;; *) echo 'STORAGE_PATH must be absolute' >&2; exit 2 ;; esac
test -d "$STORAGE_PATH"
case "$STORAGE_PATH" in /|/etc|/opt|/var) echo 'Refusing broad storage path' >&2; exit 2 ;; esac
sudo tar -C / -czf "$BACKUP_ROOT/gzctf-files.tar.gz" "${STORAGE_PATH#/}"
```

如果 Storage 使用外部对象存储，则保持 `STORAGE_PATH` 为空，记录桶名、版本策略、服务端快照或版本保留点以及只读探测结果，不在本机制作伪备份。

### 6.6 基础容器和网络配置备份

现网基础服务没有 Compose 项目标签，必须记录真实运行参数：

```bash
for container in gzctf-postgres gzctf-redis guacd guacamole; do
  sudo docker inspect "$container" > "$BACKUP_ROOT/${container}.inspect.json"
  sudo docker image inspect "$(sudo docker inspect "$container" --format '{{.Image}}')" \
    > "$BACKUP_ROOT/${container}.image.json"
done
sudo docker volume ls > "$REPORT_ROOT/docker-volumes.txt"
sudo docker network ls > "$REPORT_ROOT/docker-networks.txt"
```

在对应主机备份：

- `/etc/nginx` 和动态 stream 配置。
- FRP 客户端、服务端配置和 systemd 单元。
- WireGuard 配置和当前 peer 摘要。
- `gzctf-port-map-sync` service、timer、环境文件和令牌文件；令牌文件只保存在权限为 `0600` 的备份中，不输出内容。
- 节点 Agent 的 systemd 单元、版本和能力摘要。

最后生成备份摘要：

```bash
sudo find "$BACKUP_ROOT" -type f -print0 | sudo sort -z \
  | sudo xargs -0 sha256sum | sudo tee "$REPORT_ROOT/backup-manifest.sha256" >/dev/null
```

## 7. 阶段 C：部署和迁移

### 7.1 上传并校验

把发布压缩包和 `.sha256` 上传到服务器临时目录，不覆盖旧目录：

```bash
sha256sum -c "gzctf-vnext-${RELEASE_SHA}.tar.gz.sha256"
sudo install -d -m 0755 "$NEW_RELEASE"
sudo tar -xzf "gzctf-vnext-${RELEASE_SHA}.tar.gz" -C "$NEW_RELEASE"
cd "$NEW_RELEASE"
sha256sum -c release-files.sha256
sha256sum -c publish.sha256
```

### 7.2 配置新发布目录

从旧版本复制生产配置到新目录，再按 `appsettings.Template.json` 补齐新增配置键：

```bash
sudo install -m 0600 "$OLD_RELEASE/appsettings.json" "$NEW_RELEASE/publish/appsettings.json"
```

重点人工确认但不得输出值：

- Database、Redis 和 Storage。
- IAM Portal SSO。
- Docker、KVM、Guacamole 和节点 Agent。
- Registry 和镜像服务器。
- 公网 Nginx 同步、端口池和 Sync Token。
- `MetricPort`、OpenTelemetry 和日志输出。

### 7.3 显式执行迁移

应用启动时仍会自动检查迁移，但正式发布先使用 migration bundle 执行。bundle 从工作目录下权限为 `0600` 的 `appsettings.json` 读取生产连接串，避免把连接串暴露在命令参数、Shell 历史或共享日志中：

```bash
sudo chmod +x "$NEW_RELEASE/gzctf-migrate"
set -o pipefail
(
  cd "$NEW_RELEASE/publish"
  sudo ../gzctf-migrate
) 2>&1 | sudo tee "$REPORT_ROOT/migration.log"
test "${PIPESTATUS[0]}" -eq 0
```

迁移后记录迁移头和核心表计数。若 bundle 失败、迁移头错误或核心数据异常，不启动新应用，直接进入回滚。

### 7.4 原子切换并启动

```bash
sudo ln -sfn "$NEW_RELEASE/publish" /opt/gzctf/publish.next
sudo mv -Tf /opt/gzctf/publish.next /opt/gzctf/publish
sudo systemctl daemon-reload
sudo systemctl start "$APP_SERVICE"
test "$(systemctl is-active "$APP_SERVICE")" = active
if systemctl cat "$AGENT_SERVICE" >/dev/null 2>&1; then
  sudo chmod +x "$NEW_RELEASE/publish/agent/gzctf-agent"
  sudo systemctl start "$AGENT_SERVICE"
  test "$(systemctl is-active "$AGENT_SERVICE")" = active
fi
sudo journalctl -u "$APP_SERVICE" -n 100 --no-pager \
  | sudo tee "$REPORT_ROOT/gzctf-startup.log"
```

必须证明主站和 Agent 实际进程都来自新发布目录，不能只看 service 状态：

```bash
APP_PID="$(systemctl show "$APP_SERVICE" -p MainPID --value)"
test "$APP_PID" -gt 0
readlink -f "/proc/${APP_PID}/exe" | grep -F "$NEW_RELEASE/publish/GZCTF"
if systemctl cat "$AGENT_SERVICE" >/dev/null 2>&1; then
  AGENT_PID="$(systemctl show "$AGENT_SERVICE" -p MainPID --value)"
  test "$AGENT_PID" -gt 0
  readlink -f "/proc/${AGENT_PID}/exe" \
    | grep -F "$NEW_RELEASE/publish/agent/gzctf-agent"
fi
```

启动失败时不要反复重启，不要执行数据库降级；保留第一份日志并进入回滚。

### 7.5 健康检查

```bash
curl -fsS http://127.0.0.1:8080/ >/dev/null
curl -fsS "http://127.0.0.1:${METRIC_PORT}/healthz" \
  | tee "$REPORT_ROOT/healthz.txt"
```

验证 Database、Redis 和 Storage 健康；检查指标端点可读。若生产未配置外部 OpenTelemetry Collector，只要求本地指标、结构化日志和脱敏通过，Collector 导出不阻塞本次上线。

## 8. 阶段 D：开放前最小验收

所有测试数据统一使用 `RELEASE-${RELEASE_TS}` 前缀，并在开放前清理。

### 8.1 认证与管理

| 测试 | 期望 |
| --- | --- |
| 本地管理员登录 | `200`，进入管理端 |
| IAM Portal 登录 | 自动登录既有绑定，不重复建号 |
| 用户写操作 | 创建、编辑、禁用和恢复测试用户正常 |
| 战队和学员组 | 创建、成员变更、归档正常 |
| 系统设置 | 修改一个无副作用字段并恢复原值 |

可复用：

```powershell
./scripts/validation/run-common-management-acceptance.ps1 `
  -BaseUrl 'http://10.24.0.27:8080' -AdminUser '<管理员>'
```

密码通过安全输入传递，不写入脚本参数历史。

### 8.2 CTF、理论和培训

| 测试 | 必须验证 |
| --- | --- |
| 静态 Flag | 正确得分、重复提交不重复计分 |
| 动态 Flag | 实例 Flag 正确、其他实例 Flag 无效 |
| 附件题 | 上传、绑定、下载和权限正确 |
| Docker 题 | `Pending -> Ready`、公网入口可访问、销毁收敛 |
| 理论考试 | 草稿、最终提交、得分和不可再次修改 |
| 培训理论作业 | 章节上下文、提交和成绩回读 |
| 培训实例 | 创建、倒计时、Flag、延期和销毁 |

可复用现有脚本：

```powershell
./scripts/validation/run-ctf-business-acceptance.ps1 -BaseUrl 'http://10.24.0.27:8080'
./scripts/validation/run-theory-business-acceptance.ps1 -BaseUrl 'http://10.24.0.27:8080'
./scripts/validation/run-training-business-acceptance.ps1 -BaseUrl 'http://10.24.0.27:8080'
```

脚本创建的数据必须在报告中列出 ID，并在开放前清理或明确保留原因。

### 8.3 Windows VM

1. 选择一份按 `docs/operations/windows-cloudbase-init-image.md` 制作并认证的镜像。
2. 使用同一模板连续创建两个实例。
3. 确认两个实例密码不同，数据库不保存明文密码。
4. 分别验证 RDP 和 Guacamole。
5. 销毁两个实例，确认 KVM、Guacamole、端口和数据库记录收敛。
6. 检查平台、Agent 和 Guacamole 日志不存在明文密码。

任何一步失败都阻止开放写入；不能以“Docker 正常”替代 Windows VM 验收。

### 8.4 节点、镜像、队列和公网入口

- 节点在线、能力和容量回读正确。
- Registry 可达，拉取一份已存在的小镜像，不在窗口上传大型生产镜像。
- 部署队列从 Pending 收敛到成功终态，无长期 Running 项。
- 公网同步器成功 ACK 当前 revision。
- 比赛和培训 Docker 入口均从内网和公网各访问一次。
- Nginx `-t`、timer 和同步日志正常。

### 8.5 AWDP 最小门禁

完整 AWDP 测试仍按 `docs/yinyu-awdp-manual-acceptance.md` 执行。本窗口只要求：

1. 创建或复用隔离测试比赛。
2. 启动一轮并确认两队实例和 checker 状态生成。
3. 完成一次有效攻击 Flag 提交并看到得分变化。
4. 停止比赛并清理实例、端口和队列任务。

修补、重置和恢复的完整证据可在开放后继续补充，但启动、判分或停止任一失败必须 No-Go。

### 8.6 日志和遥测脱敏

检查应用、Agent、网关、Guacamole 和部署队列日志，不得出现：

- `flag{...}` 或真实动态 Flag。
- `GZCTF_Token`、`portal_token` 或完整 Cookie。
- PostgreSQL、Redis、Registry、RDP 或 Guacamole 密码。
- 私钥和完整 Authorization 请求头。

记录匹配数量和日志范围，不把命中的敏感原文复制到报告。

## 9. Go/No-Go 决策

满足以下全部条件才允许开放：

- 发布 SHA、RC 标签、制品摘要和迁移头一致。
- 数据库备份与备份清单验证通过。
- `/healthz` 健康，主进程无重启循环。
- IAM 和本地登录都可用。
- Docker、Windows VM、培训实例和公网入口均通过。
- 节点在线，队列没有卡住任务。
- AWDP 启动、攻击判分和停止通过。
- 测试数据、实例和公网端口已清理。
- 日志未发现敏感值泄漏或持续 5xx。

决策记录：

```text
Decision: GO | NO-GO
Time UTC:
Release SHA:
Migration head:
Backup path:
Approver:
Open issues:
```

## 10. 开放与观察

### 10.1 开放

恢复主站外部访问和写入，记录准确时间。一次性开放，不同时运行新旧写实例。

### 10.2 前 30 分钟

每 5 分钟检查：

- HTTP 5xx 和登录失败。
- 应用重启次数、CPU、内存和磁盘。
- PostgreSQL 连接、慢查询和锁等待。
- Redis 连接和错误。
- 节点在线率、队列 Pending/Running 数量。
- Nginx 同步、实例入口和用户反馈。

30 分钟无 P0/P1 故障后结束维护窗口。

### 10.3 2 小时和 24 小时

记录相同指标、失败任务、用户反馈和手工 AWDP 完整验收结果。24 小时无阻断故障后关闭上线任务。旧发布目录和最终备份在约定保留期结束前不得删除。

## 11. 回滚

### 11.1 开放写入前

此时没有需要保留的新业务写入，可以执行完整恢复：

1. 停止新应用和新公网同步器。
2. 保存失败日志和当前迁移头。
3. 恢复维护窗口数据库备份，不能执行 EF `Down`。
4. 恢复 `/opt/gzctf/publish` 到 `OLD_RELEASE`。
5. 恢复旧 systemd、Agent、Nginx、WireGuard 和 FRP 配置。
6. 启动旧应用，验证本地登录、比赛读取、节点和实例管理。
7. 决策人确认后恢复外部访问。

先显式恢复维护窗口数据库备份。该命令块只允许目标数据库为 `gzctf`，仍需人工输入确认词：

```bash
export RESTORE_DUMP="$BACKUP_ROOT/backups/latest.dump"
test "$DATABASE_NAME" = gzctf
test -s "$RESTORE_DUMP"
sha256sum -c "${RESTORE_DUMP}.sha256"
printf 'database=%s\nbackup=%s\n' "$DATABASE_NAME" "$RESTORE_DUMP"
read -rp 'Type RESTORE-gzctf to continue: ' RESTORE_CONFIRM
test "$RESTORE_CONFIRM" = RESTORE-gzctf
unset RESTORE_CONFIRM

sudo systemctl stop "$AGENT_SERVICE" 2>/dev/null || true
sudo systemctl stop "$APP_SERVICE"
test "$(systemctl is-active "$APP_SERVICE")" = inactive
sudo docker exec "$POSTGRES_CONTAINER" psql -U postgres -d postgres \
  -v ON_ERROR_STOP=1 -c \
  "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'gzctf' AND pid <> pg_backend_pid();"
sudo docker exec "$POSTGRES_CONTAINER" dropdb -U postgres --if-exists "$DATABASE_NAME"
sudo docker exec "$POSTGRES_CONTAINER" createdb -U postgres "$DATABASE_NAME"
sudo docker exec -i "$POSTGRES_CONTAINER" pg_restore -U postgres \
  -d "$DATABASE_NAME" --exit-on-error --no-owner --no-privileges \
  < "$RESTORE_DUMP"
```

然后使用已经校验过的 `OLD_RELEASE` 恢复发布链接，不解压覆盖新目录：

```bash
OLD_RELEASE="$(cat "$REPORT_ROOT/old-release-path.txt")"
test -d "$OLD_RELEASE"
sudo ln -sfn "$OLD_RELEASE" /opt/gzctf/publish.rollback
sudo mv -Tf /opt/gzctf/publish.rollback /opt/gzctf/publish
sudo systemctl start "$APP_SERVICE"
test "$(systemctl is-active "$APP_SERVICE")" = active
if systemctl cat "$AGENT_SERVICE" >/dev/null 2>&1; then
  sudo systemctl start "$AGENT_SERVICE"
  test "$(systemctl is-active "$AGENT_SERVICE")" = active
fi
```

数据库恢复必须始终显式指定备份文件，并在执行前再次打印目标数据库名和备份摘要。禁止通过拼接未知变量执行 `dropdb` 或覆盖非 `gzctf` 数据库。

### 11.2 开放写入后

不得直接用旧备份覆盖新数据库。处理顺序：

1. 重新进入维护状态并停止写入。
2. 备份当前故障数据库。
3. 统计开放后新增用户、提交、答卷、课程进度、实例和 AWDP 记录。
4. 决定修复前滚、数据补偿或版本回退。
5. 只有形成数据合并方案后才允许恢复旧数据库。

### 11.3 回滚成功条件

- 旧应用版本和旧迁移头一致。
- 本地登录可用，核心数据数量与升级前记录一致。
- 旧网关和入口恢复，没有残留新同步器。
- 没有遗留测试实例、VM、Guacamole 连接和公网端口。

## 12. 证据清单

本次上线至少保留：

- 候选 SHA、RC 标签和远端引用。
- 发布压缩包、migration bundle 和 SHA-256 清单。
- 数据库备份、摘要和 `pg_restore --list` 结果。
- 旧发布目录、文件存储和配置备份清单。
- 迁移前后迁移头和核心表计数。
- `/healthz`、启动日志和 systemd 状态。
- CTF、理论、培训、Windows VM、节点、镜像、队列、公网入口和 AWDP 结果。
- Go/No-Go 决策记录。
- 30 分钟、2 小时和 24 小时观察记录。

文档和报告不得保存 Token、Cookie、密码、Flag、私钥或完整连接串。
