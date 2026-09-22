# 联赛第一阶段 yhr 交接

## 范围与基线

- 任务：T0 运行接口对齐、T2 双队 TeamLab 场景接入、T6 访问关闭和环境回收。
- 分支：`codex/league-phase1-yhr-20260922`。
- 起点：`origin/codex/league-phase1-lxy-20260921` 的 `2ec37d8`，包含 lxy 的 T0、T1 和 T4。
- 未合并 main，未部署生产，未执行真实双队场景。

## 实现

联赛准备复用 TeamLab Rollout。一个 preparation 对应一个 Rollout，两个入选队伍各对应一个 target，并从同一 release 创建两个相互独立的 runtime。运行创建、镜像准备、节点调度、Docker/VM/OVN 执行和销毁继续使用 TeamLab 现有服务和 `DeploymentQueueTicket`，没有新增联赛队列或运行状态表。

核心材料通过 `ILeagueCoreMaterialPort` 按 `materialId` 获取资产键、Secret 名和值。适配器将其转换为 TeamLab Secret overlay，Flag 明文不写入联赛实体、Rollout target、HTTP DTO 或日志。材料提供器仍由 lmr 的 T3 实现；未接入时准备明确失败。

开赛只修改本场 Rollout 的访问许可。场次进入 Running 后，固定参赛成员可通过 `POST /api/league/matches/{matchId}/attack-access` 领取对方 runtime 的 WireGuard 授权，并通过返回的一次性地址下载配置。目标 runtime 完全由服务端根据固定名单确定，请求不能指定或替换目标。

终局复用 Rollout drain。Rollout 先关闭访问，再经统一部署队列销毁两个 target 的 runtime；重复清理沿用原操作编号和原 Rollout。清理能力不再依赖 Flag 材料服务，避免材料服务不可用时阻断资源回收。另一场比赛使用不同 external reference，不在本次清理范围内。

## 验证

- 联赛定向测试 6/6 通过。
- 覆盖同一 preparation 复用一个 Rollout、两个队伍 target、Flag Secret overlay、访问和清理只修改本场 Rollout、准备依赖不可用时清理仍可执行、非 Running 场次和非参赛成员拒绝领取访问配置。
- 无数据库 OpenAPI 契约测试 1/1 通过，前端生成客户端已包含访问授权和配置下载方法；TypeScript 严格检查及前端架构检查通过。
- 全解决方案 Release 构建通过，0 错误；存在仓库已有的 `Microsoft.Build.Tasks.Git 8.0.0` NU1902 警告。
- PostgreSQL 集成用例因本机 Docker 引擎未运行而未执行到测试主体，未计为产品失败或通过。

## 待联调

- lmr 提供真实 `ILeagueCoreMaterialPort`、`ILeagueFlagPort` 和金币实现后，才能跑通真实准备。
- 需要固定比赛 release 和两队测试账号，验证 Docker/VM/OVN、Secret 注入、双方 WireGuard 访问隔离、单队失败、主站重启续跑和终局无残留。
- 前端联赛页面和 Flag 提交不在本分支范围。
