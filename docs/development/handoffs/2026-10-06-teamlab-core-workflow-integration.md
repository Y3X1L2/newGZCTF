# TeamLab 场景制作与运行工作区集成交接（2026-10-06）

## 目标与代码身份

- 起点 `origin/main f21f5ef2`。按已批准的第一轮页面规划整合场景库、设计、版本与启动、全局运行环境及运行详情；草稿、不可变版本和实例继续为不同事实。
- 三线主体集成候选为 `2932c81cc6758d6c4f2fc1035f131efc59a50025`；场景库 1366px 冗余箭头修正为 `db4c8fc1`，390px 独立条目布局为 `56c51835`。包含最后 Windows 空工具区和 VM 电源详情收口 `13bff315`、本机 PostgreSQL 回归的最新功能树为 `3ace86e246ef59a66140c2ee0f61cadc13f11642`，最终 PR 检查结果待确认。
- 本任务分支与候选均未合并 `main`、未推送生产制品、未部署，也未修改生产服务器、节点、镜像、数据库或网关。没有数据库迁移或 Agent 改动。

## 实现范围

- 组网普通主导航只有场景与运行环境。场景库分别显示草稿相对发布状态、最新不可变版本与最近实例，提供设计、版本与启动、查看环境入口。旧实例路径在场景草稿加载前转到全局详情或版本页运行历史；全局检索筛选与游标在 URL，详情可返回原检索地址。旧设备/现场资源维护 URL 保留，节点缓存移到节点管理下的只读查询入口。
- 设计器保留机器、镜像、资源和逐网卡 DNS/网关/路由；属性面板按选择显示，高级设备/连接器现存绑定在常规流程只读。发布动作先保存草稿、服务端校验同一修订，再确认不可变发布；校验期间变更会使结果失效。
- 版本页选择明确的发布版本，历史默认折叠，运行历史同页；就绪阻断取服务端，SSH/RDP 模板配置列为可选且不代表实际连通。启动成功直接进入稳定全局实例路径。
- 运行详情独立于可编辑场景草稿，显示执行版本冻结名称、当前代次资产状态与逐网卡分配配置。只有持久化且对应代次的来宾回读才进入 observed；当前后端无可靠历史回读时为 null，界面不把分配地址或网段网关冒充实测。Windows/Linux/Docker 工具按系统和后台能力显示，Windows 不展示错误 SSH/文件工具。
- 业务端口、VPN 和平台操作权限分开；业务映射不代表服务可达，平台权限不对外部端口访问者自动鉴权。运行操作记录与高级事件/日志按需查看，缺 actor 的记录不伪造管理员。现有后端权限和受管运行生命周期限制保持生效。

## 已有验证

| 验证 | 证据与结果 |
| --- | --- |
| 中间集成完整前端 | `4ec3707600135f35c9e24ccb307b28a87f9a8ef0` 的 `pnpm build` 通过：locale、lint 0 warning/error、严格类型、架构、117 文件/391 测试、Vite build、223 文件/3,767,054 字节 manifest 与 bundle budget。完整日志在仓库外 `D:/Work/YINYU-TeamLab-Page-Plan-20261006/frontend-build-4ec37076.log`。 |
| 后续增量 | `2932c81c` 上镜像设置、逐网卡回读、访问权限、runtime adapter/详情的 5 文件/25 测试通过；`pnpm check`、`pnpm lint:check`、`pnpm check:architecture`、`vite build`、manifest（223 文件/3,768,366 字节）及 bundle budget 通过。分项完整日志在上述仓库外目录的 `frontend-*-2932c81c.log`。 |
| 本地视觉修正 | IAB 本地合成 fixture 截图发现 1366px 场景表末尾冗余箭头换行；`db4c8fc1` 移除后场景库 1 文件/6 测试、严格类型与 lint 通过。`ce17bf1c` 复拍操作列一行。 |
| 小屏场景库 | IAB 本地合成 fixture 截图发现 390px 桌面表格压窄；`56c51835` 改为逐场景条目，场景库 1 文件/7 测试、严格类型/lint/架构通过。`ce17bf1c` 复拍动作完整。 |
| PR head 前端制品 | 核验 ClientApp 源码与 `ce17bf1c` 同树后，设置该 git SHA/name 执行 Vite 构建、manifest 和 bundle budget，均通过。仓库外 `D:/Work/YINYU-TeamLab-Core-Workflow-20261006/frontend-ce17bf1c`：223 文件/3,772,962 字节，manifest SHA256 `a8f43b6b7bea8ddb70501cb1bb58f706de78c351cd7469a67e5ca279f4dc4659`；逐文件长度与 SHA256 核验通过。目录供同版 Main 候选组合，不是发布。 |
| 最新功能树前端制品 | `3ace86e2` 上 Vite、manifest 和 bundle budget 通过。仓库外 `D:/Work/YINYU-TeamLab-Core-Workflow-20261006/frontend-3ace86e2`：223 文件/3,773,732 字节，manifest gitSha 为完整 `3ace86e246ef59a66140c2ee0f61cadc13f11642`，manifest SHA256 `fe9b23f73b77094c072312d74c55cecc4d4e3be46935dac7b7bbe783ca228f07`；逐文件长度与 SHA256 核验通过，已交给后端候选组合。旧 ce17 目录保留作比较，不是最终 UI。 |
| 最后运行页收口 | `13bff315` 隐藏 Windows 空工具区，VM 宿主电源事实折叠按需展开并去掉 libvirt 术语；定向 6/6、严格类型/lint/diff 通过。该增量未能在 CUA IAB 复拍。 |

以上局部门禁没有把 4ec 之后的所有提交再次跑一遍完整 391 项；最终 GitHub PR CI 需在最终树验证。`git diff --check` 在文档提交后复核。

## 尚未验证与交接

- 本机 PostgreSQL 16 Testcontainer 的新增查询/投影回归 1/1 通过，证据见[合同 PostgreSQL 交接](2026-10-06-teamlab-core-contracts-postgres-ci.md)；它不证明真实来宾网络或远程接入。`a3bdb34a` 的 Quality run `37414453314` 与 `ce17bf1c` 的 PR Quality 均已通过；最新 `3ace86e2` 的 Quality CI 仍待最终结果，旧 run 不代替最新树。
- 有 IAB 的运行工作区代理已在 `ce17bf1c` 用真实页面组件和本地合成只读 fixture 检查 390/1366/1920/2560、日夜、键盘与 reduced-motion，四宽度无页面级横向溢出；390/1366 场景修正复拍通过。仓库外截图与限制记录：`D:/Work/YINYU-TeamLab-Page-Plan-20261006/visual-acceptance/TeamLab-集成版本地视觉验收.md`。最后 `13bff315` 修改后 CUA IAB 不可用，未复拍；该报告只证明 ce17 版本的页面，不能充当 3ace 最终视觉签收。制作代理 CUA 也无法访问 IAB。fixture 绕过登录壳层，只核组件布局与只读导航，不验证正式旧 URL、真实 API 或生产业务链。
- 真实 Docker/KVM、四机网络、来宾回读、RDP/SSH/SFTP、VPN、业务端口、故障恢复和销毁链路未在本任务执行；不可把代码实现或合成截图写成实机通过。用户未要求部署，后续仍须单独审批维护窗口。
- 全局搜索没有可信来源类型、显示所有者、到期字段；创建运行接口没有环境名称字段。页面不伪造这些事实，也没有扩展数据库 schema。场景复制/归档未加入无合同操作。

下一位接手者先确认最终集成提交和 PR CI，补入本地视觉复拍路径与真实 API/基础设施验收边界，再决定是否进入部署审批；不得将本候选直接当作已发布版本。
