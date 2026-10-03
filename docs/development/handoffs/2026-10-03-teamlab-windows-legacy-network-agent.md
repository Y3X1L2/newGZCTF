# TeamLab 旧 Windows 可选网络宿主：Agent 集成交接

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
