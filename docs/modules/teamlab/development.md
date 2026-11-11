# TeamLab 开发地图

功能入口见 [模块说明](README.md)，配置与上线顺序见 [接入手册](netlab-integration.md)。

## 调用链

| 职责 | 源码 |
| --- | --- |
| 工作区与连接设置入口 | [NetlabWorkspaceController](../../../src/GZCTF/Modules/TeamLab/Api/NetlabWorkspaceController.cs) |
| 用户、比赛和队伍访问范围 | [NetlabWorkspaceAccess](../../../src/GZCTF/Modules/TeamLab/Application/NetlabWorkspaceAccess.cs) |
| 配置、服务凭据与项目授权检查 | [NetlabConnection](../../../src/GZCTF/Modules/TeamLab/Infrastructure/Netlab/NetlabConnection.cs) |
| 同源 API、SSE、WebSocket 和文件流 | [NetlabProxy](../../../src/GZCTF/Modules/TeamLab/Infrastructure/Netlab/NetlabProxy.cs) |
| 业务使用的底座 API | [NetlabClient](../../../src/GZCTF/Modules/TeamLab/Infrastructure/Netlab/NetlabClient.cs) |
| 比赛绑定、部署、重建与退出清理 | [PenetrationTeamLabAdapter](../../../src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs) |
| 目标与动态 Flag | [PenetrationObjectiveService](../../../src/GZCTF/Modules/Penetration/Application/PenetrationObjectiveService.cs) |
| 上层工作区容器与 API adapter | [features/teamlab](../../../src/GZCTF/ClientApp/src/vnext/features/teamlab) |
| 管理入口 / 选手入口 | [TeamLabPage](../../../src/GZCTF/ClientApp/src/vnext/features/teamlab/TeamLabPage.tsx)、[games/teamlab](../../../src/GZCTF/ClientApp/src/vnext/features/games/teamlab) |

上层创建环境使用持久化的 `CreationRequestId`；重建沿用同一次请求标识并读取底座操作结果。比赛和队伍删除先请求销毁，底座返回 `destroyed` 后才删除本地业务记录。定时清理复用现有 Runtime 定时任务，处理比赛结束和退出队伍后的关联环境。

旧 TeamLab 实体、EF 映射和只读镜像引用检查用于保留待迁移数据；旧 Controller、Agent 执行器、调度 worker 与页面已由 Netlab 接入链替换。普通 CTF、培训、练习和 AWDP 继续使用原 Runtime/Agent。

## 契约与生成

底座 OpenAPI 保存在 [contracts/netlab/openapi.yaml](../../../contracts/netlab/openapi.yaml)。更新源契约后执行 [generate.ps1](../../../contracts/netlab/generate.ps1)，生成 `NetlabContract.g.cs`。上层公开 API 变化按仓库 NSwag 流程重新生成前端客户端。

场景组件源码位于 `features/teamlab/workspace`，随上层构建。API 实例按管理或比赛入口创建，HTTP、SSE、下载和 WebSocket 使用同一个上层地址前缀。业务入口校验用户与资源归属，底座收到配置的普通服务 Token。

教师资源归属读取环境的创建者和比赛关联，蓝图归属读取版本的来源环境；选手范围直接读取比赛队伍绑定。选手详情清除初始化参数和文件，SSE 作为失效通知驱动重新查询。资源状态、模板和可用操作来自底座公开接口。

## 页面布局

工作区沿用平台内容宽度，由页面统一向下滚动。环境列表、资产详情和模板目录随内容展开。控制台、SSH、串口及远程桌面在独立浏览器标签页打开，占满该页；原页面不再维护底部连接区域。拓扑保留独立画布尺寸，普通滚轮滚动页面，Ctrl 加滚轮缩放画布。流量分析和文件区按各自交互需要保留操作区域。

独立控制台入口为 `TeamLabConsolePage`，经过原工作区 API 获取环境，再由 `ConsolePage` 检查资产和连接权限。管理和比赛入口分别保留各自的 API 范围；比赛控制台使用队伍绑定环境。连接设置留在新页，URL 只带资源标识和连接类型。渲染继续复用原 noVNC、xterm 和 RDP 组件。

Mantine 样式限制在 `data-netlab-workspace` 区域内，主题容器 `data-netlab-theme` 位于其内部。弹层使用页面顶层的同类容器，避免路由动画影响定位；主题标记置于作用域内，确保下拉菜单及表单的日夜样式生效。

资产视图只保留一份列表，通过“资产 / 网段”切换，网段列表同时保留路由器入口；拓扑不再常驻左侧对象栏，“定位对象”位于视图切换右侧，仅拓扑显示。浮层输入名称、IP 或网段即可定位，未输入时仅显示本次使用中的最近 4 项或提示；不提供分类目录。多网卡对象只列一次，显示全部地址；路由器与外部设备仍可定位。结果附带所属网段，方向键选择、Enter 定位、Esc 关闭，Ctrl+K 打开。选中后关闭浮层、定位节点并打开右侧详情，保留正常选中样式。工具栏独立于下方画布与检查器，收放详情不移动入口。列表复用 Mantine ScrollArea，不显示滚动条，保留滚轮、触摸与键盘滚动。流量首页先显示全部机器的趋势，再显示通信图，点击图例可单独查看某台机器的曲线，再次点击恢复全部。点击通信图里的机器或连线，画布同步收窄并聚焦，右侧直接显示相关报文；关闭报文卡片恢复完整通信图，小屏幕上下排列。单条报文复用详情弹窗，查看摘要、协议层与原始字节。“筛选与导出”和告警保留二级视图，由 `traffic` 查询参数标识，返回时保留查询条件和选中项。

组件仍使用迁入的 Mantine、React Flow、noVNC 和 xterm。字体、间距、圆角和主要按钮跟随平台 Token；卡片保持中性，蓝色用于链接与选中提示，绿色表示运行状态。样式限定在工作区内。当前改版的验收与发布状态见 [布局任务记录](../../records/2026-11-11-teamlab-page-layout.md)。

## 轻量动画参考

当前组网改版仍在本地验收，尚未上线。动画只帮助理解操作，不随着监控数据刷新反复播放。

- 滑块、菜单、弹窗和“模板与硬件”折叠，使用 Mantine 自带动画，时长统一为 180 毫秒。
- 右侧详情使用项目已有的 Motion；`AnimatedPanel` 同步收放占位空间与透明度，内容只偏移 20 像素滑入，进入 180 毫秒、退出 160 毫秒，桌面收放宽度，小屏收放高度。间距一起收起，面板内的文字不会被挤变形。关闭时由 `AnimatePresence` 等动画结束再卸载，期间禁止操作退出中的面板。
- 定位浮层复用 Mantine Combobox 的键盘和焦点管理，使用 6 像素纵向位移与 140 毫秒淡入淡出。
- 拓扑定位沿用 React Flow 的视口接口，由 Motion 在同一段过渡中读取正在变化的画布尺寸，避免详情打开后再移动一次。定位机器保留缩放比例，网段过大时只做必要缩小。
- 主视图、资产/网段列表和不同对象详情，共用 `Motion.module.css` 的淡入样式。只在用户切换时触发，详情内容切换为 120 毫秒，容器保持稳定；实时数据更新不会重建组件。
- 按钮、链接的颜色过渡放在组网样式作用域中。系统开启“减少动态效果”时，动画立即完成。

没有新增动画依赖、手写关闭计时器或复制一份“正在退出的对象”状态。业务仍只维护当前视图和选中对象；动画只负责显示。新增区域先用组件自带能力，再复用上述组件或样式，不另起一套动画逻辑。全平台推广列为 [UX-01](../../backlog.md)，需要另行分模块验收。

## 验证

- [NetlabProxyTests](../../../src/GZCTF.Integration.Test/Tests/Runtime/NetlabProxyTests.cs)：真实 HTTP/WebSocket，覆盖 API 路径、凭据边界、上传、SSE、文件下载与响应重定向。
- [NetlabCompetitionTests](../../../src/GZCTF.Integration.Test/Tests/Runtime/NetlabCompetitionTests.cs)：真实 PostgreSQL 前向迁移、历史绑定保留、创建复用、队伍 Flag、重建重试及删除前资源清理。
- 前端执行 locale、lint、TypeScript、架构检查、测试、构建及工作区日夜/宽窄屏检查。

本轮验收聚焦上层调用链、授权和界面。底座并发调度与物理执行沿用底座验证；现有数据迁移和生产业务冒烟随上线进行，证据记录在 [任务记录](../../records/2026-11-10-netlab-integration.md)。
