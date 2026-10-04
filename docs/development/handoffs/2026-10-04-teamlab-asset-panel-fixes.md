# TeamLab 资产连接区域修正

## 目标与基线

用户现场反馈：连续选择不同资产后出现多组 VNC；连接与服务左右排列挤压访问地址；概览和资产图标重复。

从 `origin/main e5a4add2` 创建 `codex/teamlab-asset-panel-fixes`，复用本会话 worktree。原主工作区与学习工作树保留。用户已明确批准合并与部署。状态：**complete / VERIFIED（前端修正及限定现场验收）**。

## 原因与修改

- `RuntimeRemoteAccessPanel` 与 `ServiceAccessPanel` 同级且都使用 `key={asset.id}`。React 对重复 key 的更新行为不受保证，切换后残留远程连接区域。回归测试在旧代码实测两个远程区域，而且前资产未完成的控制台连接未被正常补偿关闭。
- 将资产身份放在两面板共同容器的 key，含 runtime、generation、asset；移除重复子 key。切换资产/代次时整组正确卸载与重建，沿用现有远程会话清理逻辑。
- 连接与开放服务始终上下排列。访问地址不在冒号/端口处折行；小屏继续使用表格内部横向滚动。
- 概览用 `LayoutDashboard`，资产保留 `Boxes`。

仅改前端组件、CSS Module 与测试，不改 API/后端/Agent/迁移。

## 验证

- 新增两个有实际故障覆盖的工作区回归：多次切换始终一组 VNC/服务；切换时关闭前资产尚未完成的控制台，避免旧连接进入新资产。两项在原代码失败，修复后通过。
- 定向 3 文件/7 测试通过；完整前端 114 文件/378 测试通过。locale、lint、严格类型、架构、生产构建、manifest 和体积预算均通过。无后端变更，本地未重复后端全量测试；候选 GitHub Quality CI 37206599605 全通过，单元 1284/1284、集成 304/304。
- 本地 Vite 前端通过代理读取 `.27` 真实数据，仅管理员登录及只读页面浏览。九次切换四资产，每次远程区域/VNC/服务区域数量均为 1，真实 runtime 仍 generation 4；没有重启、重置、销毁或开放端口。
- 当前真实服务列表为空。只在本地浏览器对服务列表 GET 提供 `203.0.113.10:30000` 样本响应用于排版，不在服务器创建记录，不将其声称为可访问服务；截图明确属于本地布局样本。拦截已清除、页面重新加载恢复真实数据。
- 浏览器 requested 390/1366/1920/2560 宽度（滚动条/捕获缩放会影响实际内容宽度）日夜检查：上下顺序正确、地址 nowrap、document 无横向溢出。减少动画开启检查、键盘切换和浏览器 error/warn 0；具体数值及截图保存在仓库外。
- 证据与候选前端制品在 `D:/Work/YINYU-TeamLab-Asset-Fixes-20261004`。不提交截图、发布包、Cookie 或凭据；学习笔记继续只留本地。

## 合并、部署与回退

- PR #12 已正常合并到 main：`cc58169d3f445a44c14f9f696d8c15ba9d510987`。候选 `8fd26c2263c6b9d47ff8722b90fad36ad4884563` 与合并提交同树：`ed9df332370ffff6d6330a2cc6c36a3d45557573`。CI：<https://github.com/Y3X1L2/newGZCTF/actions/runs/37206599605>。
- 当前活动目录：`/opt/gzctf/releases/teamlab-asset-ui-8fd26c22-20261004/publish`。前端 8fd26c22，后端仍使用 3090b98a 二进制（本次没有后端源码变化），Agent 保持 cc6c5737；release manifest 明确记录三个身份及 main merge SHA。
- 压缩包 1,506,940 字节，SHA256 `51b33b37156fe686bd30c17d7a0960a4c950c67bba71217d3162b0776d010e44`；前端 224 文件长度与摘要逐项校验。非 wwwroot 文件、符号链接、权限和所有者与原发布一致；`files` 仍指向 shared/files。
- 新鲜备份在 `/opt/gzctf/backups/teamlab-asset-ui-8fd26c22-20261004`：两个完整 dump 全文可读、附件 tar 可列出、五摘要通过；配置只留服务器受保护备份。备份主站暂停约 5 分钟，Agent/VM 持续运行。没有数据库迁移，历史仍 150 条；未新建验证库、未删除日志或原备份。
- 首次切换因发布脚本将启动阶段 curl 连接拒绝作为立即异常，自动切回旧目录；旧版恢复且 Agent PID 保持。修正临时脚本的有界启动等待逻辑，重新校验并复用已准备目录后，最终原子切换 12.58 秒成功。该错误发生在仓库外发布脚本，没有再改平台代码。保留 `deploy.first-attempt.log` 与 `deployment-failed.json`。
- 回退目标为 `/opt/gzctf/releases/teamlab-admin-3090b98a-20261004/publish`；主站停下、原子切回、启动并检查 Config。`/var/tmp/yinyu-asset-fixes-20261004/rollback.sh` 已 bash 语法检查。数据库/Agent无新版本回退要求；首次自动回退已实际验证。

## 部署后验收与证据

- 正式 `.27` 页面复用正常管理员会话，九次切换资产，每次远程区域/VNC/服务区域均为 1；实际上下顺序、概览 `LayoutDashboard`/资产 `Boxes` 图标通过。requested 390/1366/1920/2560 四宽度无 document 横向溢出，日夜、键盘 Enter 选择旧 Windows、浏览器 error/warn 0。现场没有注入样本，也没有开放或撤销服务；带地址布局沿用前述本地样本证据。
- Main/Agent active、NRestarts 0；首页/Config 200，前端 manifest 8fd26c22，主站 DLL SHA256仍 `2846d6f795a5e8796b7c3dca6997e30735ed52c62cde2f7639cc1ff68b0f332a`；Agent SHA256仍 `9b2142e9731d77495322e20a100432a728cf3d86f87dc67abebf85ed58a9b3e8`、同一 PID 1650376。队列 0，三节点在线且可调度，模板519–522 Ready。
- 原 Lab2 runtime `01a1022f-7e91-7ad3-8309-833af4771430` 保持 generation4/ready/四资产；部署前后 libvirt 域名、运行 ID 和状态列表一致。本轮未新建/重置/销毁实例，未修改网络，也未重复 QGA 或桌面业务验收。
- 最终启动时点附近 systemd journal 中 Exception/fail 行计数为0，不作为所有日志系统全量审计。
- 服务器脱敏回执：`/var/tmp/yinyu-asset-fixes-20261004/{deployment-proof,api-acceptance,final-preflight,log-summary,merge-proof}.json`。本地副本在 `D:/Work/YINYU-TeamLab-Asset-Fixes-20261004/evidence`，正式页面截图 `deployed-final.jpg`、UI结果 `deployed-ui-proof.json`；这些产物没有进 Git。
- 记录分支 `codex/teamlab-asset-panel-rollout` 从合并后 main 建立，原工作区、来源分支和工作树保留。学习资料仍只留本地。后续部署先读现场目录/manifest，不把本次三个不同发布身份合写成同一个 SHA。
