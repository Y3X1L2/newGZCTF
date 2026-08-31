# YINYU AI 开发与交接规范

本文面向新 AI、临时开发者和跨会话任务使用。它不替代 `AGENTS.md`；若发生冲突，以 `AGENTS.md` 为准。

## 1. 项目事实模型

```text
Git 提交              代码事实
现行文档              架构、规则和已确认进度
任务交接记录          当前任务的决策、证据和下一步
服务器发布记录        运行环境事实
```

聊天记录不是项目事实源。新 AI 必须先读文档和源码，再判断是否需要重新验证。

## 2. 新 AI 接手提示词

可直接作为新会话第一条消息：

```text
请接手当前 YINYU/newGZCTF 项目。

先阅读：
1. AGENTS.md
2. docs/development/current-state.md
3. docs/README.md
4. README.md
5. docs/platform-commercialization-master-plan.md
6. docs/development/current-handoff.md
7. docs/development/ai-development-playbook.md
8. 与当前任务直接相关的模块文档

然后执行：
git status --short --branch
git fetch origin --prune
git log -5 --oneline --decorate
git worktree list

先输出接手确认，必须包含：
- 当前分支、HEAD、origin/main 和稳定标签；
- 当前任务的 worktree 路径；
- 平台架构和本次任务涉及的模块边界；
- 已实现、已验证、未执行、阻塞和必须人工执行的事项；
- 是否允许修改代码、是否允许访问服务器；
- 本次任务的第一步。

不要读取 docs/archive/ 作为当前事实，不要恢复旧前端，不要伪造 API 数据，不要把密码、Token、Cookie、私钥或 Flag 写入文件或回复。
```

继续已有任务时追加：

```text
继续读取 docs/development/handoffs/<任务文件>.md，从其中记录的“下一位接手者第一步”开始，不要重复已经完成且有证据的步骤。
```

## 3. 分支和 Worktree

单任务：

```powershell
git fetch origin --prune
git switch main
git pull --ff-only
git switch -c codex/<task-name>
```

并行任务：

```powershell
git fetch origin --prune
git switch main
git pull --ff-only
git worktree add ..\newGZCTF-feature -b codex/feature-a origin/main
git worktree add ..\newGZCTF-bugfix -b codex/bugfix-b origin/main
```

例如：

| 任务 | 分支 | 工作目录 |
| --- | --- | --- |
| 功能开发 | `codex/feature-a` | `D:\Work\newGZCTF-feature` |
| Bug 修复 | `codex/bugfix-b` | `D:\Work\newGZCTF-bugfix` |
| 主线 | `main` | `D:\Work\newGZCTF` |

两个 AI 不得共用目录，不得同时检出同一分支，不得直接在 `main` 修改代码。每个任务只在自己的 worktree 中操作。

## 4. 任务记录和状态

任务开始时创建：

```text
docs/development/handoffs/YYYY-MM-DD-<task-name>.md
```

至少记录目标、明确不做的内容、起始提交、任务分支、worktree、涉及模块、API/数据库/服务器、已完成步骤、下一步、阻塞、测试和回滚方式。每完成一个阶段就更新，不要等上下文快满时补写。

证据状态统一使用：

| 状态 | 含义 |
| --- | --- |
| `VERIFIED` | 已由源码、测试或真实运行证据确认 |
| `IMPLEMENTED` | 已写入代码，但真实环境尚未完整验收 |
| `NOT_RUN` | 尚未执行 |
| `BLOCKED` | 有明确阻塞原因 |
| `OPERATOR_ONLY` | 需要人工登录、现场设备或专用环境 |

## 5. 同一文件的并行修改

可以并行开发，但不能假设可以无冲突合并：

1. 尽量让功能和 Bug 任务保持边界清晰。
2. 尽量避免同时修改同一个大页面、生成客户端、迁移快照或全局样式文件。
3. 主线前进后，在自己的 worktree 中同步并解决冲突。
4. 解决冲突后重新运行受影响测试。
5. 不使用 force push，不覆盖他人提交。
6. 生成代码必须由统一 OpenAPI 来源重新生成，不能手工拼接版本。

## 6. 提交和合并

提交前：

```powershell
git diff --check
git status --short --branch
git add <相关文件>
git commit -m "<type>: <short description>"
git push -u origin codex/<task-name>
```

源码、测试和必要文档应在同一任务分支闭环。不要提交 `bin/`、`obj/`、前端 build、数据库副本、镜像、Cookie、Token、私钥或密码。

合并前：

```powershell
git fetch origin --prune
git switch main
git pull --ff-only
git diff origin/main...codex/<task-name>
git log --oneline origin/main..codex/<task-name>
```

审查和验证通过后：

```powershell
git merge --no-ff codex/<task-name>
git push origin main
```

## 7. 合并后的清理

合并成功后，任务分支和 worktree 默认删除：

```powershell
git worktree remove ..\newGZCTF-<task-name>
git branch -d codex/<task-name>
git push origin --delete codex/<task-name>
git worktree prune
```

删除前必须确认：分支已推送、合并提交已经进入 `main`、worktree 干净、没有未交接修改，并且需要保留的运行版本已经有稳定标签。未合并分支不能删除；短期保留必须写明原因和负责人。

## 8. 服务器和凭据

现行文档只记录服务器角色、IP、端口、发布目录和备份位置，不记录明文密码、API token、Cookie、Agent token、WireGuard 私钥或 Registry 凭据。

当前两台关键服务器已配置本机 SSH 公钥：

```powershell
ssh -i "$env:USERPROFILE\.ssh\id_ed25519" whoami@10.24.0.27
ssh -i "$env:USERPROFILE\.ssh\id_ed25519" ubuntu@203.195.157.191
```

需要 sudo 或危险操作时，通过安全交互认证或人工授权完成。若要无人值守发布，应另建 Secret Manager、受限部署账号和最小权限 sudo 规则。

## 9. 会话结束

结束会话前：

1. 停止未完成的测试、部署和 SSH 会话。
2. 更新任务交接文件。
3. 执行 `git diff --check`。
4. 分别记录通过、失败、未执行和人工验收项。
5. 记录提交、推送、合并和 worktree 清理结果。
6. 涉及服务器时记录 release、Git SHA、迁移头、备份和冒烟结果。
7. 把新的真实缺口更新到 `current-state.md` 或现有缺口文档。

新 AI 交接的完成标准不是读过聊天记录，而是能够从 Git、现行文档、任务记录和服务器发布证据复现当前判断。
