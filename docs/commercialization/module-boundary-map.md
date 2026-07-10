# 平台模块边界图

版本：1.0

生效阶段：Phase 1 退出时

架构形态：模块化单体主站 + 独立 Agent 执行面

## 1. 分层规则

主站内部调用方向固定为：

`HTTP/Frontend -> Contracts -> Application -> Domain -> Infrastructure ports`

运行能力调用方向固定为：

`Business Application -> Runtime Application -> Fleet/VM/TeamLab ports -> AgentClient -> Agent`

约束：

1. Controller 只负责协议解析、认证授权、调用 application use case 和映射 HTTP 结果。
2. Application service 负责事务边界和用例编排，不拼接 Agent shell 命令。
3. Domain 只包含业务状态、规则和值对象，不依赖 ASP.NET Core、EF Core、Redis、Docker 或 libvirt。
4. Infrastructure 实现 repository、缓存、队列和外部客户端，不决定业务权限。
5. Agent 只执行已经校验的本机命令，不读取比赛、课程、题目和计分实体。
6. 跨模块读取通过公开 query contract；跨模块写入通过 application command，不直接获取对方 repository。
7. 同步调用能够完成的流程不引入事件总线；只有确实需要异步恢复的流程才产生持久化 operation 或领域事件。

## 2. 目标代码结构

```text
src/GZCTF/
  Modules/
    Identity/
      Contracts/
      Application/
      Domain/
      Infrastructure/
    Content/
    Ctf/
    Exercise/
    Training/
    Theory/
    Runtime/
    Vm/
    TeamLab/
    Penetration/
    Awdp/
    Audit/
  Infrastructure/
    Api/
    Persistence/
    Cache/
    Observability/
  Composition/
    ServiceRegistration.cs
```

Phase 1 迁入 Identity API token、外部 API 基础和 ImageTemplate 参考链路。其他模块在各自主责 Phase 迁移；架构测试从 Phase 1 起禁止增加新的跨界依赖，并为每项现存依赖绑定唯一清理 Phase。

## 3. 模块所有权

| 模块 | 拥有实体/事实 | 公开能力 | 禁止承担 |
| --- | --- | --- | --- |
| Identity | UserInfo、Team、API token、token grant、actor context | 用户查询、角色策略、token 签发与校验 | 比赛参与、课程报名、节点调度 |
| Content | Challenge 基础、QuestionPool、ImageTemplate、Attachment、FlagContext、镜像绑定查询 | 题目资产、镜像目录、附件和导入任务 | 比赛计分、课程进度、runtime 调度 |
| Ctf | Game、Participation、GameChallenge、Submission、Scoreboard | 比赛生命周期、参赛、普通 CTF 提交与计分 | Agent 调用、TeamLab 拓扑 |
| Exercise | ExerciseChallenge、ExerciseInstance、练习进度 | 常态练习生命周期 | 使用 Participation 表达练习状态 |
| Training | TrainingCourse、Chapter、Enrollment、课程题绑定、课程提交、课程进度 | 课程管理和学习流程 | 拥有 ImageTemplate 主副本、复用旧 TrainingModule |
| Theory | 理论题库、tag、试卷、答题卡、答案 | 理论题检索、组卷、判题 | 用题库名代替 tag |
| Runtime | DeploymentQueueTicket、容量预留、部署阶段、运行操作 | 统一排队、取消、状态、恢复和容量接口 | 题目计分、课程权限 |
| VM | VmInstance、VmAccessEndpoint、VM 初始化和探测状态 | VM 创建、访问、停止、销毁 | Windows 专属类型代表全部 VM |
| TeamLab | Topology、Release、Plan、Runtime、Shard、RuntimeNetwork、RuntimeAsset、Traffic、Capture | 组网验证、发布、计划、部署、访问、观测和清理 | Penetration 计分、比赛参与关系 |
| Penetration | PenetrationObjective、Submission、ResetPolicy、Workspace projection、TeamLab binding | 渗透赛制、目标、Flag、提交、计分 | 拥有 TeamLab 拓扑和 runtime 执行 |
| AWDP | Service、Instance、Round、Flag、Checker、Patch、Reset、Recovery | AWDP 轮次和态势事件 | 普通 CTF 或 TeamLab runtime |
| Audit | SystemLog、ApiOperation、治理审计、恢复记录 | 审计写入和可读查询 | 业务状态的唯一事实来源 |

## 4. 允许依赖矩阵

`A -> B` 表示 A 可以依赖 B 的 Contracts/Application public surface。

| 调用方 | 允许依赖 |
| --- | --- |
| Identity | Audit |
| Content | Identity、Audit |
| Ctf | Identity、Content、Runtime、VM、Audit |
| Exercise | Identity、Content、Runtime、VM、Audit |
| Training | Identity、Content、Exercise、Runtime、VM、Audit |
| Theory | Identity、Content、Audit |
| Runtime | Identity、Content query contracts、Audit |
| VM | Runtime contracts、Content image catalog、Audit |
| TeamLab | Runtime contracts、VM contracts、Content image catalog、Audit |
| Penetration | Ctf contracts、TeamLab contracts、Identity、Audit |
| AWDP | Ctf contracts、Runtime contracts、Identity、Audit |

禁止关系：

- Content 不依赖 Ctf、Training、Exercise、Penetration 或 TeamLab。
- TeamLab 不依赖 Penetration entities、Controller DTO 或比赛计分服务。
- Runtime 不依赖具体 Challenge 派生类型；请求必须携带规范化资源需求。
- AgentClient 不返回数据库实体；只使用 Agent contract DTO。
- 前端生成 API 类型不进入 Domain 或 Infrastructure。

## 5. 跨模块引用与删除规则

### 5.1 ImageTemplate

- `ImageTemplate` 是 Content 全局资产，存储服务器保存唯一主副本。
- 创建者和管理员可以管理模板元数据；业务模块只拥有显式 binding。
- `TrainingCourseId` 不能继续作为模板所有权字段。Phase 1 将其迁移成课程 binding。
- 删除模板前由 Content 查询 CTF、Exercise、Training 和 TeamLab 的公开引用检查接口。
- 任一有效引用或运行实例存在时返回 `asset_in_use`，不能依赖数据库 cascade 删除。

### 5.2 Challenge

- `GameChallenge` 是比赛快照，`ExerciseChallenge` 是练习定义，二者不能通过修改同一行切换类型。
- 课程通过 `TrainingCourseChallenge` 绑定 ExerciseChallenge。`ExerciseChallenge.TrainingCourseId` 非空表示课程拥有的隔离快照，删除课程时显式删除该快照、实例、Flag、附件和提交。
- 全局 ExerciseChallenge 和 Phase 10 QuestionPool 是来源资产；导入课程必须创建课程拥有的 snapshot，后续修改互不影响。
- 无论删除课程 snapshot 还是全局题目，都不能级联删除仍被其他对象引用的 ImageTemplate 主副本。

### 5.3 TeamLab

- TeamLab release 只保存 ImageTemplate ID 和发布时摘要，不持有镜像文件。
- Penetration 使用 binding 表关联 Game/Team 与 TeamLab runtime。
- 删除比赛先销毁所有绑定 runtime，再删除 Penetration binding 和玩法事实；TeamLab release 的删除按独立引用计数处理。

## 6. API 与事件边界

| 模块 | Command 示例 | Query 示例 | 持久化事件/operation |
| --- | --- | --- | --- |
| Identity | IssueApiToken、RevokeApiToken | GetActor、ListTokenGrants | token.issued、token.revoked |
| Content | ImportImage、DeleteImage | GetImage、CheckImageReferences | image.import、image.delete |
| Runtime | EnqueueDeployment、CancelDeployment | GetQueueStatus | deployment lifecycle |
| TeamLab | PublishTopology、CreateRuntime、DestroyRuntime | GetPlan、GetRuntime、GetTraffic | TeamLabEvent + ApiOperation |
| Penetration | SubmitObjective、ResetWorkspace | GetWorkspace、GetScoreboard | submission + gameplay audit |

事件命名只用于持久化审计或异步恢复，不要求引入消息中间件。进程内同步流程直接调用 application contract。

## 7. 架构门禁

Phase 1 新增 `ArchitectureDependencyTests`，至少约束：

```csharp
[Fact]
public void TeamLab_DoesNotDependOn_PenetrationDomain()
{
    var result = Types.InAssembly(typeof(Program).Assembly)
        .That().ResideInNamespace("GZCTF.Modules.TeamLab", true)
        .ShouldNot().HaveDependencyOn("GZCTF.Modules.Penetration")
        .GetResult();

    Assert.True(result.IsSuccessful,
        string.Join(", ", result.FailingTypes.Select(type => type.FullName)));
}

[Fact]
public void Controllers_DoNotDependOn_AgentClient()
{
    var result = Types.InAssembly(typeof(Program).Assembly)
        .That().ResideInNamespace("GZCTF.Controllers", true)
        .ShouldNot().HaveDependencyOn("GZCTF.Services.Fleet.AgentClient")
        .GetResult();

    Assert.True(result.IsSuccessful,
        string.Join(", ", result.FailingTypes.Select(type => type.FullName)));
}
```

门禁策略：

1. Phase 1 创建当前依赖快照，任何新增违规立即失败。
2. 每项现存违规必须在总纲中已有主责 Phase；不能写入无截止时间的 allow-list。
3. 对 Phase 1 已迁移的 Identity/Content reference slice 不允许基线豁免。
4. Phase 3 退出时 TeamLab -> Penetration 违规数量必须为零。
5. Phase 14 退出时所有目标模块必须满足完整矩阵，临时快照文件删除。

## 8. 组合根

- DI 注册集中在 Composition，模块公开一个 `Add<Module>()` 方法。
- 模块不得在运行过程中通过 `HttpContext.RequestServices` 定位其他模块服务。
- `AppDbContext` 可以作为单库事实存储，但业务 Controller 不直接使用它操作其他模块实体。
- 跨模块事务由调用方 application service 组织；repository 不自行创建嵌套 transaction。
- 后续只有在独立扩缩容、独立故障域或独立数据所有权产生可测收益时才拆微服务。
