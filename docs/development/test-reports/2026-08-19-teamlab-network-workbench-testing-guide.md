# 组网工作台：测试验收使用说明

- 交付对象：测试人员
- 日期：2026-08-19
- 环境：118 主站（`http://10.0.7.118:8080`）、125 节点
- 目标：不靠开发也能把组网工作台完整测一遍。先讲「正常使用」，再讲「按 API 调」，最后给「测试重点」清单。

> 这篇是给测试看的使用说明，不是开发设计文档。文中功能按最近一次验收报告（`2026-08-18-yinyu-teamlab-a-b-c-acceptance.md`）核过；接口路径按当前源码逐条核对过。没有物理设备的场景用容器/模拟器搭，测试环境已具备。
> 想更深看契约/设计，去查 `docs/commercialization/teamlab-external-control-plane-contract.md` 和在线 `/api-docs`。

---

## 目录

1. 组网工作台是做什么的
2. 正常使用：界面逐个讲
   - 2.1 场景库页（Library）
   - 2.2 拓扑设计台（Design）
   - 2.3 校验与发布（Validate / Publish）
   - 2.4 试运行与发布版本（Releases）
   - 2.5 运行时详情（Runtime）
   - 2.6 资源与能力（Resources）
3. 正常使用：一条完整链路走到底
   - 3.1 测试链路（试运行）
   - 3.2 比赛链路（正式部署）
4. 按 API 调用：底座接口全清单
   - 4.0 先说清楚怎么调
   - 4.1 认证与 scope
   - 4.2 能力与控制范围
   - 4.3 拓扑与发布
   - 4.4 镜像准备
   - 4.5 Rollout（批量部署）
   - 4.6 运行时与访问
   - 4.7 链路策略（损路/断连/网络隔离）
   - 4.8 连接器与设备包（虚实结合）
   - 4.9 流量、路径与抓包
   - 4.10 Webhook 事件通知
   - 4.11 资源池与远端会话
   - 4.12 分页、错误与幂等
   - 4.13 API 调用示例（照抄就能跑）
5. 测试重点
   - 5.1 正常全链路（测试链路 + 比赛链路）
   - 5.2 远程运维（Docker / Linux VM / Windows VM）
   - 5.3 高并发与性能
   - 5.4 扩展能力（协议模拟、虚实结合）
   - 5.5 失败与恢复路径
6. 测试小贴士（环境、凭据、常见坑）

---

## 1. 组网工作台是做什么的

组网工作台用来**搭建一张"比赛网络"**：里面有几台设备（Docker 容器、Linux 虚拟机、Windows 虚拟机），设备之间怎么连（哪台在哪个网段、路由怎么走），然后整套部署到一台或多台物理节点上，开放给选手访问，比赛结束后再整网拆除。

它分两块能力：

- **设计**：画拓扑（拖设备、拉网线、设网段、设启动顺序），保存成"场景"。
- **运行**：把场景发布成不可变版本 → 部署成"运行时" → 选手访问 → 查流量/抓包 → 拆除。

「不可变版本」意思是：发布那一刻的场景拍一张快照，之后的部署、比赛都按这张快照走，不会再被编辑改动影响。这是组网和普通 CTF 的一个关键区别：**发布后才能部署，编辑中的场景不能部署**。

---

## 2. 正常使用：界面逐个讲

> 登录 118 管理后台后，左侧菜单找「TeamLab 组网」相关入口。以下按页面说明。

### 2.1 场景库页（Library）

**作用**：项目（场景）的列表和管理。一个场景 = 一张拓扑图 + 它的发布版本 + 历史部署。

| 区域 | 是什么 | 怎么用 |
| --- | --- | --- |
| 场景列表 | 所有已保存的场景 | 点名称进入设计台；点「新建」建空场景 |
| 场景卡片/行 | 每个场景的名称、节点数、连接数、最后修订 | 排序、搜索、打开 |
| 「新建」按钮 | 创建空场景 | 填名称后进入设计台 |

**测试点**：新建 → 命名 → 列表能搜到 → 能再次打开。

### 2.2 拓扑设计台（Design）

这是核心画布页。先记住整体分区：

```
┌─────────────────────────────────────────────────────────────┐
│ 顶部工具栏： 场景名 | 连接类型(N) | 统计 | 校验 | 发布 | 保存 │
├──────────┬──────────────────────────────────┬──────────────┤
│ 左侧     │                                  │ 右侧         │
│ 节点库   │       画布（拓扑图）              │ 属性检查器   │
│ (拖入)   │   交换机/路由器/设备 + 连线       │ (点节点看/改)│
├──────────┴──────────────────────────────────┴──────────────┤
│ 画布左上角小工具条：撤销/重做/自动排版/专注模式/面板开关      │
│ 画布左下角：小地图 / 缩放按钮                                │
└─────────────────────────────────────────────────────────────┘
```

**左侧：节点库**

| 节点 | 是什么 | 拖到画布后的作用 |
| --- | --- | --- |
| 交换机 | 一个隔离网段 | 承载网段，设备挂到它下面形成同一网络 |
| 路由器 | 连接多个网段 | 让不同网段的设备能互相通信 |
| Docker | 容器设备 | 一个跑容器的资产，可设镜像、端口、资源 |
| Linux 虚拟机 | Linux 设备 | 一个跑 Linux 的资产 |
| Windows 虚拟机 | Windows 设备 | 一个跑 Windows 的资产 |

操作：直接**拖**到画布；点一下也可加到画布。画布上节点可以拖动、多选。

**画布中间**

- **连线（网络连接模式）**：默认是「网络连接」。从一个**设备/交换机**拖线到**交换机**，表示"这台设备在这个网段里"；从**交换机**拖到**交换机**，表示"这两个网段之间路由互通"。第一张网卡默认标「主网卡」。
- **连线（启动依赖模式）**：切到「启动依赖」后，从一台设备拖到另一台，表示"先启动谁、后启动谁"，并可填启动条件（比如：等前面就绪/等 3 秒）。
- **网络区域（虚线圈）**：每个交换机自动套一个"网络区域"框，框住属于这个网段的节点。点区域标题可折叠/展开；有「按成员收拢」按钮可让区域收紧到刚好包住设备；区域右下角可拖拽调整大小（调整只影响布局，不影响网段本身）。
- **自动排版**：画布左上角小工具条里有「自动排版」图标，一键把整张图排整齐（从左到右按路由层级摆放）。**它只改布局，不改网络本身，也不产生新版本**——放心点。
- **小地图 + 缩放**：画布左下角，方便大图导航。

**右侧：属性检查器**（选中对象后出现）

- 选中**交换机**：可改网段名称、网段号（CIDR，如 `10.80.1.0/24`）、是不是"入口网段"（选手网络接入点）。
- 选中**设备**：可改名称、镜像、CPU/内存、对外端口、健康检查、监控方式、设备包绑定、连接器等。
- 选中**连线**：可看/改这条线的信息（主网卡、路由方向、启动条件）。

**顶部工具栏**

| 按钮 | 作用 |
| --- | --- |
| 连接类型切换 | 「网络连接」/「启动依赖」两种画线模式 |
| 统计 | 显示当前节点数、连线数 |
| 校验 | 检查这张图画得对不对（见 2.3） |
| 保存 | 保存编辑内容（形成新修订） |
| 发布新版本 | 把当前画布发布成不可变版本（见 2.3） |
| 撤销/重做 | 编辑历史 |

**键盘**：Ctrl/⌘+C 复制选中、Ctrl/⌘+V 粘贴、Delete 删除、Ctrl+A 全选、方向键微调。专注模式按钮可把右侧检查器收起，只看画布。

**测试点**：拖入 5 种节点各 1 个；交换机↔设备连线、交换机↔交换机路由连线、启动依赖连线；改网段 CIDR；折叠/展开区域；自动排版；保存后再打开内容还在。

### 2.3 校验与发布（Validate / Publish）

- **校验**：点上面的「校验」按钮，右侧会出现**校验抽屉**，列出所有"不能部署"的问题（比如：设备没接任何网段、网段没有出口、依赖顺序有环、网段 CIDR 冲突、镜像没指定……）。问题分 错误/警告。**有阻断项时不能发布**。
- **发布新版本**：校验通过后点「发布」，生成**不可变发布版本**（release）。之后编辑画布会产生**新修订**，需要再次发布成新版本才会被接下来部署使用。已部署的运行时永远用创建时指定的那个版本，不受后续编辑影响。

**测试点**：故意画一个有错拓扑 → 校验应报错且发布被禁；改对 → 校验通过 → 发布成功 → 出现新版本号；发布后再编辑 → 显示"已变更"状态。

### 2.4 试运行与发布版本（Releases）

- **发布版本列表**：每个场景有它所有发布版本，能看到版本号、创建时间、执行摘要（网络怎么建、设备怎么启动的摘要）。
- **试运行（Trial）**：不占用正式比赛资源、快速验证"这个版本能不能跑起来"的部署。默认是关闭了机会次数计费的本地试运行，管理员可发起。
- **版本计划/就绪**：发布版本可查看"镜像预分发计划"——在每个节点上需要哪些镜像、哪些节点已就绪。

**测试点**：对同一场景发布两个版本；对旧版本发起试运行；确认新版试运行不影响旧版。

### 2.5 运行时详情（Runtime）

部署起来的"实例"叫**运行时**。列表页看所有运行时；详情页是一个大面板：

| 区域 | 内容 |
| --- | --- |
| 状态卡 | 运行阶段（准备镜像→部署→就绪→运行→拆除）、generation（第几代，重置后 +1） |
| 拓扑视图 | 只读的小拓扑，当前各设备对应哪个节点 |
| 阶段时间线 | 每个部署步骤开始/结束时间 |
| 日志 | 该运行时的日志（按资产筛选） |
| 事件 | 状态事件 + 协议事件（设备上报的自定义事件） |
| 流量面板 | 会话（flows）、路径（paths）、按协议筛选（如只看 TCP:502） |
| 抓包 | 发起抓包、下载抓包文件 |
| 访问授权 | 生成选手/玩家的访问授权（WireGuard 配置等） |
| 链路策略 | 对某条链路施加"损路/断连/网络隔离"策略（见 2.6） |
| 远程运维 | 对 Docker / Linux VM / Windows VM 发起远程会话（SSH/RDP） |
| 生命周期按钮 | 重置 / 暂停 / 恢复 / 销毁 |

**测试点**：见第 5 章，这里是运行时相关所有操作的总入口。

### 2.6 资源与能力（Resources）

管理员页面，管理运行时要用的"外设能力"，是做「虚实结合」的地方：

| 项 | 是什么 |
| --- | --- |
| 设备包（Device Package） | 一段"设备程序"（服务包），绑到容器上让容器扮演真实设备（如 PLC、传感器），能生成协议事件 |
| 连接器（Connector） | 一个"外部物理/模拟设备"的占位条目，有容量；运行时可以"租用"它，模拟把真实设备挂进比赛网 |
| 资源池 / 节点缓存 | 查看各节点的资源槽位、镜像缓存占用 |
| 链路策略 | 运行时详情里的损路/断连/隔离策略（netem/断连、访问规则、NAT），属数据面真实执行 |

**测试点**：见 5.4 扩展能力。

---

## 3. 正常使用：一条完整链路走到底

### 3.1 测试链路（试运行，不正式占资源）

> 目的：验证"能不能跑起来、设备间能不能通信"。适合测试人员日常反复验证。

1. **建场景**：新建场景，命名如 `test-plc-scada`。
2. **画拓扑**：
   - 拖 1 个交换机（网段 `10.80.1.0/24`），作为入口；
   - 拖 1 个 Docker，绑 `modbus-slave` 镜像（模拟 PLC），连到交换机，IP 10.80.1.10；
   - 拖 1 个 Docker，绑 `scada-client` 镜像（模拟上位机），连到交换机，IP 10.80.1.20；
   - 网卡设主网卡；启动依赖：先 PLC 再 SCADA。
3. **校验**：应无阻断项。若报"镜像未指定/网段无出口"等，按提示改。
4. **发布**：生成版本 v1。
5. **发起试运行**：选该版本 → 部署。等状态到「就绪」。
6. **验证通信**：在运行时详情里开「远程会话」进 SCADA 容器，跑 `nc`/`modbus` 客户端读写 PLC；或在流量面板看是否有 MODBUS(TCP:502) 会话。
7. **抓包**：对这台运行时发起抓包 → 下载 → 用 Wireshark 看是否能解出 502 端口的 MODBUS 协议。
8. **收尾**：销毁运行时，确认节点槽位释放、镜像引用释放。

### 3.2 比赛链路（正式部署，用 Rollout）

> 区别：正式部署用 **Rollout（批量部署单）**，走完整生命周期，开放选手访问，比赛结束统一拆除。适合比赛日流程验证。

1. **场景 + 拓扑 + 校验 + 发布**（同 3.1 的 1~4）。
2. **做镜像准备**：对发布版本提交"镜像准备"，目标节点缓存好所需镜像；看计划状态到 `readyToStart`。
3. **建 Rollout**：设定目标（哪些节点跑哪些网段），提交 prepare。
4. **open-access**：部署全部就绪后开放选手访问，生成访问授权/入口配置。
5. **比赛运行中**：
   - 查 `flows`/`paths` 看选手流量；
   - 按需对某条链路加「损路 40% 丢包 / 延时 200ms / 断连」策略，观察选手侧真实受影响 → 可手动恢复；
   - 某台失败：对单个目标 `rebuild`（重建），不影响其他已就绪目标。
6. **比赛结束**：`close-access` 关访问 → `drain` 排空（关访问→停观测→销毁资产→释放容量）→ `archive` 归档 rollout。
7. **清理确认**：节点无残留运行时、镜像缓存引用释放。

---

## 4. 按 API 调用：底座接口全清单

> 这一章给「要自动化测试 / 要写脚本 / 要对齐第三方」的人用。**接口路径全部来自当前源码路由**，按 controller 逐个核对过，保证一个不漏。

### 4.0 先说清楚怎么调

- Base URL：`http://10.0.7.118:8080/api/open/v1`
- 认证：请求头 `Authorization: Bearer <token>`（token 在管理后台签发，见 4.1）
- 所有**异步写操作**必须带请求头 `Idempotency-Key`（一个不重复的字符串），成功受理返回 `202` 和 `operation`；拿 `GET /operations/{id}` 查进度。
- 列表分页：用不透明的 `after` 游标（`?limit=&after=`），不要自己拼页码。
- 失败统一返回 `application/problem+json`，以稳定 `code` 字段判断，别解析中文 detail。
- 在线接口文档：`http://10.0.7.118:8080/api-docs`（OpenAPI），机器契约 `/openapi/open-v1.json`。

### 4.1 认证与 scope

Token 决定两件事：**能干什么**（scope）+ **能管哪块**（`teamlab-scope:<scope-id>`）。

| Scope | 权限 |
| --- | --- |
| `teamlab.topologies:read` | 查能力、拓扑、发布版本 |
| `teamlab.topologies:write` | 建/改/校验/发布拓扑 |
| `teamlab.runtimes:read` | 查镜像准备、rollout、运行时、事件 |
| `teamlab.runtimes:write` | 准备镜像、管理 rollout/runtime、授权 |
| `teamlab.traffic:read` | 查流量、路径 |
| `teamlab.capture:read` | 查/下载抓包 |
| `teamlab.capture:write` | 创建/停止抓包 |
| `teamlab.resource-pools:read` | 查资源池/节点缓存 |
| `teamlab.device-packages:read` | 查设备包 |
| `teamlab.connectors:read` | 查连接器 |
| `teamlab.connectors:write` | 占用/释放连接器 |
| `teamlab.link-policies:read` | 查链路策略 |
| `teamlab.link-policies:write` | 应用/恢复链路策略 |
| `teamlab.remote-sessions:read` | 查远端会话 |
| `teamlab.remote-sessions:write` | 建/删远端会话 |

管理员可签发 `teamlab-scope:*`；普通签发者只能授权仍存在、未归档的具体 scope。scope 归档后只读，禁止写操作。

### 4.2 能力与控制范围

| 方法 | 路径 | 干什么 | scope |
| --- | --- | --- | --- |
| GET | `/teamlab/capabilities` | 查平台能力（节点支持 Docker/KVM/协议等） | topologies:read |
| GET/POST | `/teamlab/scopes` | 列/建控制范围 | 读：topologies:read；写：runtimes:write |
| POST | `/teamlab/scopes/{scopeId}/archive` | 归档范围（此后只读） | runtimes:write |

### 4.3 拓扑与发布

| 方法 | 路径 | 干什么 | 备注 |
| --- | --- | --- | --- |
| GET | `/teamlab/topologies` | 列场景 | 分页游标 |
| POST | `/teamlab/topologies` | 建场景 | |
| GET | `/teamlab/topologies/{id}` | 查场景 | 含编辑器布局 + 执行定义 |
| PUT | `/teamlab/topologies/{id}` | 更新场景（整体替换） | 保留已发布版本 |
| DELETE | `/teamlab/topologies/{id}` | 删场景 | 有已发布版本时受限 |
| POST | `/teamlab/topologies/{id}/clone` | 复制场景 | |
| POST | `/teamlab/topologies/{id}/validate` | 校验 | 返回阻断/警告项 |
| POST | `/teamlab/topologies/{id}/releases` | 发布新版本 | 异步写，需幂等键 |
| GET | `/teamlab/topologies/{id}/releases` | 列发布版本 | |
| GET | `/teamlab/topologies/{id}/releases/{releaseId}` | 查单个版本 | 版本快照 |
| POST | `/teamlab/topologies/{id}/releases/{releaseId}/plan` | 生成部署计划 | |
| POST | `/teamlab/topologies/{id}/releases/{releaseId}/archive` | 归档版本 | 释放预热引用 |

### 4.4 镜像准备

| 方法 | 路径 | 干什么 |
| --- | --- | --- |
| GET | `/teamlab/preparations/releases/{releaseId}` | 查某版本的镜像准备状态 |
| POST | `/teamlab/preparations/releases/{releaseId}` | 提交镜像准备（异步） |
| DELETE | `/teamlab/preparations/releases/{releaseId}` | 取消/清理准备 |

状态：`planAvailable` → `preparing` → `readyToStart` / `blocked`。返回每个镜像在适配节点的就绪/准备中/失败数量。

### 4.5 Rollout（批量部署）

| 方法 | 路径 | 干什么 |
| --- | --- | --- |
| GET/POST | `/teamlab/rollouts` | 列 / 建批量部署单 |
| GET | `/teamlab/rollouts/{id}` | 查 |
| GET/PUT | `/teamlab/rollouts/{id}/targets` | 列 / 覆盖目标列表 |
| POST | `/teamlab/rollouts/{id}/prepare` | 准备全部目标（异步） |
| POST | `/teamlab/rollouts/{id}/open-access` | 开放访问 |
| POST | `/teamlab/rollouts/{id}/close-access` | 关闭访问 |
| POST | `/teamlab/rollouts/{id}/drain` | 排空（关访问→停观测→销毁→释放） |
| POST | `/teamlab/rollouts/{id}/archive` | 归档 |
| POST | `/teamlab/rollouts/{id}/pause` / `resume` | 暂停 / 恢复整个 rollout |
| POST | `/teamlab/rollouts/{id}/targets/{targetId}/rebuild` | 重建单个目标 |
| POST | `/teamlab/rollouts/{id}/targets/{targetId}/pause` / `resume` / `restart` | 暂停/恢复/重启单目标 |

> 一个目标失败不会牵连其他已就绪目标；恢复方式由调用方显式选（重建 / 移除目标 / 排空）。

### 4.6 运行时与访问

| 方法 | 路径 | 干什么 |
| --- | --- | --- |
| POST | `/teamlab/runtimes` | 创建运行时（异步） |
| GET | `/teamlab/runtimes/{id}` | 查运行时详情 |
| DELETE | `/teamlab/runtimes/{id}` | 销毁（幂等收敛到终态） |
| POST | `/teamlab/runtimes/{id}/reset` | 重置（新一代） |
| POST | `/teamlab/runtimes/{id}/pause` / `resume` | 暂停 / 恢复 |
| POST | `/teamlab/runtimes/{id}/protocol-events` | 设备模拟器主动上报协议事件 |
| GET | `/teamlab/runtimes/{id}/events` | 查事件（可按 generation/stage 过滤） |
| POST | `/teamlab/runtimes/{id}/access-grants` | 生成访问授权 |
| GET | `/teamlab/runtimes/{id}/access-grants/{grantId}/download` | 下载授权配置 |
| DELETE | `/teamlab/runtimes/{id}/access-grants/{grantId}` | 撤销授权 |

> 暂停保留节点、地址、网络和磁盘；恢复不重新调度、不重下镜像。

### 4.7 链路策略（损路 / 断连 / 网络隔离）

在**运行时链路**上真实执行数据面策略（作用于宿主机 side veth 或用 OVN）：丢包/延时/抖动/限速/断连、访问规则（allow/deny）、NAT（DNAT/SNAT）。

| 方法 | 路径 | 干什么 | scope |
| --- | --- | --- | --- |
| POST | `/teamlab/link-policies` | 应用一条策略 | link-policies:write |
| GET | `/teamlab/link-policies?runtimeId=` | 列该运行时策略（可按 status 过滤） | link-policies:read |
| POST | `/teamlab/link-policies/{policyId}/recover` | 手工恢复策略 | link-policies:write |

策略种类（`kind`）：
- `packet-loss` / `latency` / `jitter` / `duplication` / `bandwidth-limit` / `link-break`：损路/断连
- `access-rule`：对指定地址/端口 allow/deny
- `nat`：DNAT/SNAT

同参数重复应用幂等；不同参数需先 `recover` 再应用。

**已真实验收过的行为**（对照表）：
- 丢包 40% → 实测丢包；延时 200ms → 实测 200ms 级；恢复后复原。
- `deny` TCP 到 PLC → 真实超时；恢复后立即可通。
- DNAT `外网IP:端口 → 内网设备` 真实可达；SNAT 源地址转换真实生效。

### 4.8 连接器与设备包（虚实结合）

| 方法 | 路径 | 干什么 | scope |
| --- | --- | --- | --- |
| GET | `/teamlab/device-packages` | 列设备包 | device-packages:read |
| GET | `/teamlab/device-packages/{id}` | 查设备包 | device-packages:read |
| GET | `/teamlab/connectors?scopeId=` | 列连接器（含占用状态，不暴露接入地址） | connectors:read |
| GET | `/teamlab/connectors/{id}` | 查连接器 | connectors:read |
| POST | `/teamlab/connectors/{id}/leases` | 运行时占用连接器（租约） | connectors:write |
| POST | `/teamlab/connectors/{id}/leases/release` | 释放连接器 | connectors:write |

管理员侧（`api/admin/teamlab`）另有：建/停用/归档设备包、建连接器、连接器健康检查、强制撤销租约。

**已真实验收过的行为**：注册连接器 `sim-plc`（容量 1）→ 申请租约占 slot=1 → 释放 → `releasedAt` 置位。设备包绑到容器后，容器会按服务包产生真实协议事件（见 4.6 的 protocol-events）。

### 4.9 流量、路径与抓包

| 方法 | 路径 | 干什么 |
| --- | --- | --- |
| GET | `/teamlab/runtimes/{id}/traffic/flows` | 查会话（支持 cursor/关键字/协议/端口过滤） |
| GET | `/teamlab/runtimes/{id}/traffic/paths` | 查路径（跨资产转发链） |
| GET | `/teamlab/runtimes/{id}/traffic/paths/{pathId}` | 查单条路径 |
| POST | `/teamlab/runtimes/{id}/captures` | 发起抓包（异步，返回 captureId） |
| GET | `/teamlab/runtimes/{id}/captures` | 列抓包 |
| GET | `/teamlab/runtimes/{id}/captures/{captureId}` | 查抓包状态/分段 |
| POST | `/teamlab/runtimes/{id}/captures/{captureId}/stop` | 停止 |
| GET | `/teamlab/runtimes/{id}/captures/{captureId}/download` | 下载归档（pcap） |

> 抓包每次有时间和大小上限；下载只包含平台已验证的分片。抓包用 OVS 镜像到专用捕获口 + tcpdump 实现（`2026-08-18` 报告中确认归档含真实协议帧，如 MODBUS/TCP 502 的 330 帧）。

### 4.10 Webhook 事件通知

| 方法 | 路径 | 干什么 |
| --- | --- | --- |
| GET/POST | `/teamlab/webhooks` | 列 / 建 webhook |
| GET/DELETE | `/teamlab/webhooks/{id}` | 查 / 删 |
| POST | `/teamlab/webhooks/{id}/replay` | 重放历史事件 |

约束：endpoint 只接受可公开解析的 HTTPS，且指向外网（拒绝内网/回环/链路本地/平台自身）。投递为至少一次，带 `X-TeamLab-Event-Id`、时间戳和 HMAC-SHA256 签名；失败有上限退避；webhook 失败不影响部署状态；重放不产生新的业务操作。

### 4.11 资源池与远端会话

| 方法 | 路径 | 干什么 | scope |
| --- | --- | --- | --- |
| GET | `/teamlab/resource-pools` | 查资源池 | resource-pools:read |
| GET | `/teamlab/resource-pools/node-cache` | 查节点镜像缓存 | resource-pools:read |
| GET | `/teamlab/runtimes/{id}/remote-access` | 查可远程接入的资产清单 | remote-sessions:read |
| POST | `/teamlab/runtimes/{id}/assets/{assetId}/remote-sessions` | 对资产创建远程会话（Docker exec / SSH / RDP） | remote-sessions:write |
| GET | `/teamlab/remote-sessions/{sessionId}` | 查会话状态 | remote-sessions:read |
| DELETE | `/teamlab/remote-sessions/{sessionId}` | 结束会话 | remote-sessions:write |

> 这是测试「远程运维」的接口面。前端运维台（`api/admin/teamlab`）还有 `connect` / `terminal` 这类即时交互入口。

### 4.12 分页、错误与幂等（测试必懂）

- **分页**：所有列表用 `?limit=&after=`，`after` 是返回里给的不透明游标。
- **错误 code**（按稳定 code 判断）：
  - `scope_archived` —— 范围已归档，不能写
  - `topology_revision_conflict` —— 场景在你编辑期间被改过（保存冲突）
  - `topology_invalid` —— 校验未通过
  - `runtime_operation_in_progress` —— 该运行时已有生命周期操作在跑
  - `resume_blocked` —— 暂停期间有动作阻止恢复
  - `rollout_not_drained` —— 没排空就试图归档
  - `idempotency_conflict` —— 同一幂等键但请求体不同
- **幂等**：写操作带 `Idempotency-Key`；同一身份+路由+key+请求体重复提交返回同一 operation；key 相同但请求体不同返回 409。断线后 `GET /api/open/v1/operations/{operationId}` 恢复进度，不要盲目重提。

---

### 4.13 API 调用示例（照抄就能跑）

> 这批示例走一条完整链路：**建拓扑 → 校验 → 发布 → 建 rollout → 部署 → 开放访问 → 查事件 → 加链路策略 → 抓包**。每一段都给可以复制的命令。下面假设：
> - Base URL：`http://10.0.7.118:8080/api/open/v1`
> - Token：`<TOKEN>`（替换成你自己的）
> - Idempotency-Key：每个写操作给一个不重复的字符串，如 `test-20260819-0001`
>
> 命令行示例用 `curl`；Windows 下装 curl 可用，PowerShell 也可用 `curl.exe`。示例只给「关键请求」，响应的字段名以实际返回为准（字段大部分已在下文列出）。

### 4.13.1 先拿一个能用的 token（管理员签发）

在管理后台签发 API token 后，假设 token 是 `abc123`：

```bash
# 查能力——token 通不通，一眼就能看出来
curl -s http://10.0.7.118:8080/api/open/v1/teamlab/capabilities \
  -H "Authorization: Bearer abc123"
```

返回里能看到平台支持的能力（例如支持哪些资产类型、协议模拟/观测点是否开）。这一步通了，说明认证没问题。

> ⚠️ **枚举字段取值**：下面示例里的 `kind` / `direction` / `endpointObservation` 等，实际取值以在线接口文档 `/api-docs` 的 **description 列** 为准（枚举一般显示为数字 + 英文名，如 `AssetKind` 只有 `Docker(0)` / `Vm(1)`，具体是 Docker 还是 Linux/Windows VM 由**镜像模板**决定；观测模式是 `Disabled(0)/Optional(1)/Required(2)`；路由方向是 `FromTo(0)/Bidirectional(1)`）。示例用文字便于阅读，提交前如果报枚举不识别，就用数字或看 `/api-docs` 里的精确枚举名。

### 4.13.2 建一个场景（拓扑）

`POST /teamlab/topologies`，请求体是最小可用的"两个网段 + 两台 Docker 设备 + 跨网段路由连接"的拓扑定义：

```bash
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/topologies \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0001" \
  -d '{
    "name": "two-net-demo",
    "schemaVersion": 2,
    "networks": [
      { "key": "net-entry", "name": "入口网段", "addressPool": { "poolCidr": "10.80.1.0/24", "runtimePrefixLength": 24 }, "isEntry": true, "orderIndex": 0 },
      { "key": "net-core",  "name": "核心网段", "addressPool": { "poolCidr": "10.80.2.0/24", "runtimePrefixLength": 24 }, "isEntry": false, "orderIndex": 1 }
    ],
    "assets": [
      { "key": "plc",  "name": "PLC",  "kind": "docker", "imageTemplateId": 116, "resources": { "cpuUnits": 1, "memoryMiB": 256, "storageMiB": 256 },
        "interfaces": [ { "key": "plc-eth0", "networkKey": "net-entry", "hostOffset": 10, "primary": true, "orderIndex": 0 } ],
        "exposePort": 502, "endpointObservation": 2 },
      { "key": "scada", "name": "SCADA", "kind": "docker", "imageTemplateId": 117, "resources": { "cpuUnits": 1, "memoryMiB": 256, "storageMiB": 256 },
        "interfaces": [ { "key": "scada-eth0", "networkKey": "net-core", "hostOffset": 20, "primary": true, "orderIndex": 0 } ] }
    ],
    "connections": [
      { "key": "route-entry-core", "fromNetworkKey": "net-entry", "toNetworkKey": "net-core", "direction": 1 }
    ]
  }'
```

成功返回 `202` 和一个 `operation`（异步受理），形如：

```json
{ "id": "0b7f...", "status": "queued", "resourceUrl": "/api/open/v1/operations/0b7f..." }
```

**拿这个 `operation.id` 轮询**（见 4.13.13），等它结束就能拿到新拓扑的 `topologyId`。

> 小提示：`imageTemplateId` 是镜像模板库里的 ID（管理端「镜像模板」里查）；`hostOffset` 决定设备在这个网段里的具体主机位（如 10 → `10.80.1.10`）。
> 想省事也可只在**设计台**里画好拓扑点保存，再在 UI 里看它生成的等效 JSON；设计台和 API 是同一套底座（前台的"改 kind / 设 VM 类型"实际落在镜像模板上）。

### 4.13.3 校验场景

`POST /teamlab/topologies/{topologyId}/validate`（无请求体，或给空 `{}`）：

```bash
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/topologies/{topologyId}/validate \
  -H "Authorization: Bearer abc123"
```

返回：
```json
{ "valid": true, "issues": [] }
```
`valid: false` 时 `issues` 会给出每条问题的 `code` / `path`（定位到哪个网段/资产）/ `message`。

### 4.13.4 发布不可变版本

`POST /teamlab/topologies/{topologyId}/releases`，请求体给当前修订号（`revision` 在拓扑详情里）：

```bash
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/topologies/{topologyId}/releases \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0002" \
  -d '{ "revision": 1 }'
```

成功返回 `202` + operation，轮询结束后得到 `releaseId`（版本 ID）。

### 4.13.5 镜像准备

`POST /teamlab/preparations/releases/{releaseId}`：

```bash
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/preparations/releases/{releaseId} \
  -H "Authorization: Bearer abc123" \
  -H "Idempotency-Key: test-20260819-0003"
```

```bash
# 查准备进度
curl -s http://10.0.7.118:8080/api/open/v1/teamlab/preparations/releases/{releaseId} \
  -H "Authorization: Bearer abc123"
```

状态从 `planAvailable` → `preparing` → `readyToStart`（或 `blocked`）。`blocked` 时看返回里的每镜像失败数量/原因。

### 4.13.6 建 rollout（批量部署单）

`GET/POST /teamlab/rollouts`：

```bash
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/rollouts \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0004" \
  -d '{
    "controlScopeId": "{scopeId}",
    "releaseId": "{releaseId}",
    "externalReference": "match-20260819",
    "targets": [
      { "externalSubject": "team-01", "displayName": "红队一号" },
      { "externalSubject": "team-02", "displayName": "红队二号" }
    ]
  }'
```

返回 `202` + operation → 轮询得到 `rolloutId`。

> `controlScopeId`：控制范围 ID（在 `GET /teamlab/scopes` 里查）。一个 rollout = 一批部署目标；每个目标最终会成为一个"运行时"。想只给一个场景部署一队，`targets` 放一条即可。

### 4.13.7 部署 rollout（prepare）

```bash
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/rollouts/{rolloutId}/prepare \
  -H "Authorization: Bearer abc123" \
  -H "Idempotency-Key: test-20260819-0005"
```

轮询期间可随时：

```bash
curl -s http://10.0.7.118:8080/api/open/v1/teamlab/rollouts/{rolloutId} \
  -H "Authorization: Bearer abc123"
```

返回里的 `counts`（`total/pending/provisioning/ready/failed/draining/...`）就是实时进度；每个目标还能单独查：

```bash
curl -s http://10.0.7.118:8080/api/open/v1/teamlab/rollouts/{rolloutId}/targets \
  -H "Authorization: Bearer abc123"
```

### 4.13.8 开放访问 / 关闭访问

全部目标就绪后开放选手访问：

```bash
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/rollouts/{rolloutId}/open-access \
  -H "Authorization: Bearer abc123" \
  -H "Idempotency-Key: test-20260819-0006"
```

比赛结束：

```bash
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/rollouts/{rolloutId}/close-access \
  -H "Authorization: Bearer abc123" \
  -H "Idempotency-Key: test-20260819-0007"
```

### 4.13.9 查运行时与事件

单个部署目标对应的运行时 ID 从 rollout 的 target 里拿（`runtimeId`）：

```bash
# 运行时详情
curl -s http://10.0.7.118:8080/api/open/v1/teamlab/runtimes/{runtimeId} \
  -H "Authorization: Bearer abc123"

# 事件（含协议事件；可按 stage/generation 过滤）
curl -s "http://10.0.7.118:8080/api/open/v1/teamlab/runtimes/{runtimeId}/events?stage=protocol" \
  -H "Authorization: Bearer abc123"
```

### 4.13.10 链路策略：损路 / 断连 / 访问规则 / NAT

`POST /teamlab/link-policies`，`parameters` 随 `kind` 变化：

```bash
# 例1：对 net-entry 网段施加 40% 丢包
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/link-policies \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0008" \
  -d '{
    "runtimeId": "{runtimeId}",
    "networkKey": "net-entry",
    "kind": "packet-loss",
    "parameters": { "percent": 40 }
  }'

# 例2：延时 200ms
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/link-policies \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0009" \
  -d '{
    "runtimeId": "{runtimeId}",
    "networkKey": "net-entry",
    "kind": "latency",
    "parameters": { "delayMs": 200 }
  }'

# 例3：拒绝访问某资产的 502 端口（对指定资产链路）
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/link-policies \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0010" \
  -d '{
    "runtimeId": "{runtimeId}",
    "networkKey": "net-entry",
    "assetKey": "plc",
    "kind": "access-rule",
    "parameters": { "action": "deny", "protocol": "tcp", "port": 502 }
  }'

# 例4：DNAT（外网地址映射到内网设备）
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/link-policies \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0011" \
  -d '{
    "runtimeId": "{runtimeId}",
    "networkKey": "net-entry",
    "assetKey": "plc",
    "kind": "nat",
    "parameters": { "mode": "dnat", "externalAddress": "10.96.0.13", "externalPort": 80, "internalAddress": "10.80.1.10", "internalPort": 502 }
  }'
```

查策略与恢复：

```bash
# 列策略（按运行时）
curl -s "http://10.0.7.118:8080/api/open/v1/teamlab/link-policies?runtimeId={runtimeId}&status=active" \
  -H "Authorization: Bearer abc123"

# 恢复某条策略
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/link-policies/{policyId}/recover \
  -H "Authorization: Bearer abc123" \
  -H "Idempotency-Key: test-20260819-0012"
```

> 说明：`packet-loss`、`latency` 等字段名以平台 `/teamlab/link-policies` 在线文档实际字段为准——示例给的是平台已实测过的（40% 丢包、200ms 延时、deny TCP:502、DNAT）。
> 同参数重复应用幂等；换参数前必须先 `recover`。

### 4.13.11 连接器（虚实结合）

```bash
# 列连接器
curl -s "http://10.0.7.118:8080/api/open/v1/teamlab/connectors" \
  -H "Authorization: Bearer abc123"

# 运行时占用一个连接器（租约）
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/connectors/{connectorId}/leases \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0013" \
  -d '{ "runtimeId": "{runtimeId}" }'

# 释放
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/connectors/{connectorId}/leases/release \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0014" \
  -d '{ "runtimeId": "{runtimeId}" }'
```

### 4.13.12 抓包

```bash
# 发起抓包（scope=network，最长 60 秒）
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/runtimes/{runtimeId}/captures \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0015" \
  -d '{ "scope": "network", "maxSeconds": 60, "maxBytes": 536870912, "expiresInSeconds": 3600 }'

# 查状态（等它完成）
curl -s http://10.0.7.118:8080/api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId} \
  -H "Authorization: Bearer abc123"

# 停止（提前收工）
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}/stop \
  -H "Authorization: Bearer abc123" \
  -H "Idempotency-Key: test-20260819-0016"

# 下载归档（pcap）
curl -s -o capture.pcap http://10.0.7.118:8080/api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}/download \
  -H "Authorization: Bearer abc123"
```

> 抓包完成后再下载；下载文件是 pcap/归档，用 Wireshark 打开。若抓到 MODBUS/TCP:502，说明协议模拟链路通了。

### 4.13.13 直接创建一个运行时（不走 rollout）

如果只想"单个场景单个实例"（比如调试），可以直接 `POST /teamlab/runtimes`，给一个发布版本 ID 就能建：

```bash
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/runtimes \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0017" \
  -d '{
    "releaseId": "{releaseId}",
    "externalReference": "debug-instance-01"
  }'
```

返回 `202` + operation → 轮询得到 `runtimeId`。之后查详情/事件/链路策略/抓包都拿这个 `runtimeId`（见 4.13.9~4.13.12）。

> 用 rollout 与直接用 runtimes 是同一个底座：rollout 是"批目标"的调度外壳，runtime 是"单个实例"。日常调试直接用 runtimes 更省事。

### 4.13.14 建 webhook（事件通知）

把运行时/部署事件推到自己的收件服务（HTTPS endpoint）：

```bash
curl -s -X POST http://10.0.7.118:8080/api/open/v1/teamlab/webhooks \
  -H "Authorization: Bearer abc123" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: test-20260819-0018" \
  -d '{
    "controlScopeId": "{scopeId}",
    "endpointUrl": "https://your-server.example.com/teamlab-events",
    "eventTypes": ["runtime.ready", "rollout.ready", "runtime.failed"],
    "enabled": true
  }'
```

- 创建后返回的 `signingSecret` **只在创建响应里出现一次**，用于校验推送的 HMAC-SHA256 签名，先存好。
- `eventTypes` 可以从 `GET /teamlab/webhooks` / 在线文档里看支持的事件类型清单。
- 已建 webhook 可 `GET/DELETE /teamlab/webhooks/{id}`，失败重放用 `POST .../{id}/replay`。
- 注意：endpoint 必须是**可公开解析的 HTTPS** 且指向外网（平台拒绝内网/回环/链路本地）。

### 4.13.15 轮询异步操作（通用范式）

几乎所有写操作都返回 `202` + `operation`。用这个接口等它结束：

```bash
# 假设上一步返回 { "id": "0b7f...", "resourceUrl": ".../operations/0b7f..." }
curl -s http://10.0.7.118:8080/api/open/v1/operations/{operationId} \
  -H "Authorization: Bearer abc123"
```

响应里有 `status`（如 `queued` / `running` / `completed` / `failed`）、`resourceId`（完成后给创建出的资源 ID）和 `error`。循环轮询直到 `status` 变成终态即可。**客户端杀掉 / 断线后重连，也是拿这个接口恢复，不重新提交。**

### 4.13.16 常见失败怎么读（对照 4.12）

- `401`：token 无效/没带。
- `403`：scope 没权限，或资源不属于你的 scope（按不存在处理）。
- `409 idempotency_conflict`：同一 Idempotency-Key 但请求体变了。
- `409 topology_revision_conflict`：保存时 revision 过期（先 `GET` 详情拿最新 revision 再提交）。
- `422` / `400`：请求体字段不对，看 `problem+json` 的 `detail`。
- 异步操作 `failed`：看 `operation.error`，再按 4.12 的恢复动作处理。

---

## 5. 测试重点

### 5.1 正常全链路（测试链路 + 比赛链路）

**测试链路（试运行）**
1. 建场景 → 画混合拓扑（Docker + Linux VM + Windows VM 各至少一个，两个网段，中间路由器连起来）。
2. 校验全绿 → 发布 v1 → 试运行 → 状态到「就绪」。
3. 跨网段 ping 通（通过路由器）；同网段互通。
4. 流量面板出现对应会话；抓一份包下载并确认内容。
5. 销毁 → 节点无残留、镜像引用释放（节点缓存可查）。

**比赛链路（Rollout，正式）**
1. 同场景发 v2（改一点东西）。
2. 镜像准备 → `readyToStart`；建 rollout → prepare → open-access。
3. 用 4.5 全部动作过一遍：pause/resume、单目标 rebuild、recover。
4. close-access → drain → archive → 确认终态收敛、可重复 drain 幂等。
5. **边跑比赛边编辑场景**：确认不影响已在运行的实例（不可变版本隔离）。

### 5.2 远程运维（Docker / Linux VM / Windows VM）——管理员功能

| 资产类型 | 远程运维方式 | 要测 |
| --- | --- | --- |
| Docker | 容器内终端（exec） | 打开终端、跑命令、看日志、结束会话 |
| Linux VM | SSH 远程会话 | 登录、执行命令、断开 |
| Windows VM | RDP / 远程会话 | 连接、操作桌面、断开 |

**统一要验证**：会话创建 → 连接可用 → 会话列表能看到 → 结束会话后资源释放；权限校验（无 scope 的 token 应被拒）。

### 5.3 高并发与性能

- **同场景多次部署/销毁**循环（创建→就绪→销毁 ×N），确认无资源泄漏、无槽位占死。
- **并发建多个运行时**：多目标 rollout 并发就绪，确认队列/锁不串。
- **大批量场景**：大拓扑（数十节点）开 1 次设计台不卡（自动排版要求毫秒级）；长时间编辑无内存爆炸。
- **抓包/流量**：高流量下 flows 落库稳定、抓包归档大小受控。
- 参考历史报告数据：`2026-08-17` V1/V2 性能对比报告、`2026-08-18` A/B/C 验收（MODBUS 1200 帧、32 网段布局 2.4ms）。

### 5.4 扩展能力（协议模拟、虚实结合）

- **协议模拟**：容器绑 `modbus-slave`（模拟 PLC，端口 502）→ 用 `scada-client` 读写 → 平台流量面板出现 `TCP:502` 会话 → 抓包解出 MODBUS 帧；设备产生 `protocol-events` → 事件面板/接口能读回。
- **虚实结合**：
  - 注册连接器（如 `sim-plc`，容量 1）→ 运行时申请租约（占用）→ 释放（归还）；
  - 设备包绑定到资产 → 容器按包行为运行、上报事件；
  - 连接器健康检查、强制撤销租约（管理员侧）也过一遍。

### 5.5 失败与恢复路径

- 某节点镜像没准备好就部署 → 应有明确 `blocked` 状态和恢复动作。
- 部署中某单目标失败 → 其它目标仍就绪 → `rebuild` 单目标恢复。
- 运行中 `reset` → generation+1、旧授权失效、新授权可用。
- 中途断网/重试 → 用幂等键 + `GET operations/{id}` 恢复，无重复部署。
- `drain`/`destroy` 重复执行 → 收敛到相同终态（幂等）。

---

## 6. 测试小贴士

- **环境**：主站 `http://10.0.7.118:8080`；节点 118/125。在线接口文档 `/api-docs`。
- **Token**：管理后台签发；测试用 `teamlab-scope:*` + 全读写 scope 最省事，要做权限测试再换成具体 scope。
- **镜像**：测试用内置 `modbus-slave:v1`、`scada-client:v1`（已推入内部 registry `10.0.7.118:5000`）；自己导入镜像走管理端。
- **别做**：不要带外直接 `docker restart` 节点上的业务容器（会破坏平台管理的网络）；破坏/恢复用平台的生命周期按钮或 API。
- **界面小 bug（已知，待修）**：自动排版后偶发"连线需要再点一下才出现"或"线跑到别处"，不影响数据面正确性，正在修复中。测试遇到可刷新视野或再点一次自动排版绕过，并记录复现步骤给我。
- **实话实说的边界**：Windows 目前是 Opaque 模板（可部署、可远程运维，未走 AD 域集成）；玩家公网 WireGuard 入口在测试环境按内网准入方式验证。这两点是已知边界，不是本次交付范围。

---

（完）
