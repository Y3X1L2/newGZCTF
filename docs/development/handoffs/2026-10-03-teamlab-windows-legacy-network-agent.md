# TeamLab 旧 Windows 可选网络宿主：Agent 集成交接

## 主任务真实客户机补充（2026-10-03）

### 用户批准后的部署准备

- 用户明确批准本次部署到10.24.0.27，并要求直传慢时改走GitHub。测试制品发布于tag teamlab-legacy-agent-8637e087-20261003，只有Agent release asset，无main合并；服务器约7秒下载，SHA256为2ed4a2114d7ef0b681fe66ad6cca62aee358395a362bdf2b6ca85aa69fda5bb2，和本机构建一致。
- 完整备份在/opt/gzctf/backups/teamlab-legacy-8637e087-20261003：gzctf.dump1608281102字节、Guacamole92146字节、附件187619398字节，两库全部压缩内容可读，5项摘要检查通过，备份后原主站启动命令成功。无迁移或数据库写入。
- 发起切换时SSH新channel打开失败，远程命令尚未执行；之后SSH与HTTP超时。用户VPN接口与内网路由仍在，已及时告知并请求恢复，不能宣称候选已部署。部署授权继续有效；恢复后先核对现场，再以服务器独立任务切换并读取结果。临时stage为/tmp/yinyu-legacy-8637e087-20261003，备份与传输片段均保留。
- 预备真实回归使用现代Windows模板507，正常平台新建双卡/实际QGA回读/重置/销毁；不安装兼容helper。Win10无需默认增加helper，但本轮无Win10实机正例。VM121新镜像和完整四机仍待验收。

- VM121的QGA100.0.0基本回应、CMD和input-data正常；Windows错误报告明确原PowerShell CLI失败为System.ComponentModel.Win32Exception，ConsoleControl.GetActiveScreenBufferHandle。引擎组件存在，后台ConsoleHost失败不能当作QGA离线。
- 已提交可审核的.NET2引擎宿主及build-host.cmd，位于scripts/guest-tools/windows-legacy-network；通过Runspace直接使用已有PowerShell引擎，不加载profile、注册服务、改策略或增加网络监听。空输入返回78及既有标记，输入限64KiB，异常/错误流返回非零；原生失败由实际网络脚本检查并抛出。
- 在原VM121正式路径C:\Program Files\YINYU-GuestTools\LegacyPowerShellHost.exe通过QGA运行实际Agent生成的只读脚本，退出0，中文网卡名、172.22.1.21/24、DNS172.22.1.2均正确。目录继承Program Files受保护权限，Users为RX/GR/GE，没有写入权限；管理员/SYSTEM有完全控制。宿主5120字节，SHA256为1f644854da4837f98b4cad7b4767979d77a61eed8b8cfb7cd8411f903e121afd；编译路径不同可能改变输出摘要，不能只拿旧原型摘要判断正式构建。
- 原型真实输入/读取、空输入、throw、Write-Error、netsh只读及检查原生失败用例通过；未改网络/题目业务。正式完整网络写入、新MAC、重启恢复、平台新建/重置/销毁仍未验收；VSS/COM+未修复，不能宣称在线冻结/备份支持。
- Agent代码合入任务分支：初始集成b1b80e51、工具源081fd83f、制作路径e8ff712a、保护路径集成8637e087。子代理最终全量单元1276/1276、定向112/112、Agent Release构建0警告0错误；主任务最终集成定向112/112及linux-x64单文件publish通过。数据库集成未执行（Docker daemon不可用、无迁移/数据库改动），前端无改动未重复门禁。
- 10.24.0.27现场仍为teamlab-managed-fa97187c-20261002/publish，主站/Agent active、Config200；实际与内置Agent摘要仍c7b66c3408e3328bcc56f2577a9e363abc06f129c597d6c7acd4009ebb52ede8。本次候选尚未部署，部署前按用户要求确认具体发布物。原镜像506–509、PVE源机、快照、旧release和实验产物保留。
- 仓库外证据及发布物在D:/Work/YINYU-Lab2-20260929/windows121-readiness-20261003；其中formal-host-ready.json、powershell-host-validation.json及powershell-fault-signatures.json记录上述验证。教学笔记仅在独立学习分支本地提交，不推送。实验VPN曾短暂中断，恢复后两端连通；后续连接异常应立即告知用户。

## 任务目标与基线

- 用户目标：继续平台静态网络开发；服务器部署前仍须确认。
- 本轮范围：Agent 只读选择固定可选 PowerShell 引擎宿主、沿用现有网络脚本与 QGA 输入、补充单元测试。无客户机、服务器、镜像、数据库或启动任务修改。
- 独立分支：`codex/teamlab-windows-legacy-network`。
- 独立 worktree：`D:/Work/newGZCTF-windows-legacy-network`。
- 从最新 `origin/main bc3599aa` 建立，再快进合入本地已部署功能分支 `codex/teamlab-managed-network 9751a728`。fetch 后远端该功能分支不存在，因此使用同一仓库中的确切本地提交；没有修改主工作区或其它任务 worktree。
- 当前状态：`IMPLEMENTED`，未部署；最终提交和推送身份见本任务 Git 记录及交接消息。

## 实现行为

- 仅 Windows `ManagedStatic` 在原 `guest-ready` 准备窗口内探测固定文件 `C:\Program Files\YINYU-GuestTools\LegacyPowerShellHost.exe`，QGA `guest-file-open` 使用 `rb`，随后关闭句柄，不读取二进制。
- 探测最多 5 秒，关闭句柄使用独立最多 5 秒清理期限。调用者取消继续传播；探测前后校验原生 VM 身份。
- 明确缺文件、`CommandNotFound` 或 `CommandDisabled` 返回不存在，继续原 `powershell.exe`。权限等未知错误不被当作缺文件，仍由现有安全映射报告 `guest_network_control_failed`。
- 文件存在时本次回读、应用和独立复核全部使用无参数固定宿主；脚本正文与原现代 Windows 路径完全相同，继续以 UTF-8 标准输入发送并受 QGA 64 KiB 上限约束。没有失败后切换宿主或第二次写入。
- 网络匹配、MAC、接口名、地址、DNS、路由、真实回读、输入能力标记、Typed failure、30/120 秒执行期限及隐私边界保持现有行为。宿主路径加入既有 Windows 超时/取消清理分类，使用 `taskkill /PID ... /T /F`，并保留清理前原生身份检查。
- 无数据库迁移、公开 DTO、执行协议、第二队列或前端修改。准备契约写入 `docs/operations/teamlab-managed-network-authoring.md`。

## 验证

| 检查 | 结果 |
| --- | --- |
| 原定向测试：ManagedVmNetwork、VmGuestControl、GuestNetworkDeadlineAndFailure | 110/110 通过；后续加入“domain not found 不能当作缺文件”分类用例后由完整测试覆盖 |
| `dotnet build src/GZCTF.slnx -c Release -v minimal` | 通过，0 错误；42 条已有依赖/测试警告 |
| `dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build -p:CollectCoverage=false` | 1275/1275 通过，0 跳过 |
| `git diff --check` | 通过 |
| Docker 检查 | 本机 `dockerDesktopLinuxEngine` 管道不存在，daemon 未运行 |

完整数据库集成测试未运行：本轮没有数据库或控制面变更，且本机 Docker 引擎不可用。前端门禁未运行：无前端改动。真实 Server 2008 R2 宿主编译、错误流、原生工具和实际生成脚本验收由主任务负责；本轮 Agent 测试不能替代真实执行面验收。

首次尝试 solution build 使用 `--no-restore` 因新 worktree 的 Integration.Test/AppHost 缺少资产文件失败；随后普通 build 完成还原并全部通过，不是源码编译故障。

## 宿主源审计与后续

- 原型位于仓库外，由镜像准备主任务拥有；本轮不复制、修改或提交二进制和原型。
- PowerShell 2 SDK 宿主应通过 `RunspaceFactory.CreateRunspace` / `CreatePipeline` 执行，不依赖 `powershell.exe` ConsoleHost，也不加载 profile。SDK 没有 `Pipeline.HadErrors`，应使用 `pipeline.Error.ReadToEnd()` 数量判定错误；`Invoke` 异常和错误流均须返回非零。
- 实际网络脚本不依赖显式 `exit 0`，正常脚本结束后宿主返回 0；原生网络命令失败由脚本检查并抛出异常，仍应成为非零。空输入须保留 `GZCTF_GUEST_STDIN_UNAVAILABLE` 标记，输出须保留原始 UTF-8/XML。不能把该专用工具扩大成任意 shell 执行兼容层。
- 下一步：主任务冻结并签收正式工具源/制作步骤，在独立镜像副本验证实际生成脚本、缺输入、异常、错误流、超时和取消；需要服务器部署时向用户确认具体发布物与范围。
- 工作分支与 worktree 保留供审查和合并；未合并前不得删除。生产 `main` 未修改。

## 保护路径修正

- 后续安全审查发现旧 `C:\YINYU-QGA` 目录可能继承普通用户创建文件权限。宿主不存在时普通用户可抢先创建可选文件，不能将该目录作为可信可选执行入口。
- 固定路径已改成标准管理员管理位置 `C:\Program Files\YINYU-GuestTools\LegacyPowerShellHost.exe`，不增加 DTO、任意路径设置或 hash 子系统。身份检查、一次宿主选择、同一脚本和失败不重试写操作继续保持。
- 测试覆盖包含空格的完整 QGA `path` 传递，以及大小写不敏感的 Windows 超时清理判断；旧位置不再被当作固定宿主路径。
- 仅本地源码、测试和契约修改；真实目录 ACL、正式安装和含空格路径的 QGA 执行由父任务验收，没有在本 worktree 任务修改客户机或部署。
- 修正后验证：上述三组定向测试 112/112、完整单元测试 1276/1276 通过；Agent Release 构建 0 警告、0 错误；`git diff --check` 通过。数据库集成和前端门禁本次仍未运行，范围和限制与上文相同。
