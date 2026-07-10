# 平台商业化领域术语表

版本：1.0

生效阶段：Phase 0 退出时

适用范围：主站、前端、Agent、数据库、OpenAPI、日志、测试和运维文档

## 1. 术语使用规则

1. 一个术语只能有一个领域所有者；其他模块只能通过公开契约引用该对象。
2. 数据库实体名、C# 类型名、TypeScript 类型名、API 资源名和界面文案必须表达同一业务概念。
3. 历史命名不能通过 alias、兼容 DTO 或重复路由长期保留。
4. 迁移期名称转换只允许存在于对应 Phase 的数据迁移代码中，阶段退出前删除运行时适配代码。
5. 本表标记为“目标”的术语在对应 Phase 实现前不能伪装成现有能力。
6. 已删除术语只允许用于历史 migration、迁移验证、负向删除门禁、禁用术语登记和审计记录，不得形成可执行兼容面。

## 2. 身份与参与关系

| 术语 | 定义 | 所有者 | 当前代码事实 |
| --- | --- | --- | --- |
| User | 平台账号和权限主体。 | Identity | `UserInfo`。 |
| Team | 多名用户组成的比赛队伍。 | Identity | `Team`。 |
| Participation | 队伍参加某场比赛的关系，拥有比赛内状态。 | CTF | `Participation`。 |
| Actor | 发起 API 或治理动作的主体，取值为登录用户或 API token 代表的用户。 | Identity | 目标术语；当前日志主要从 `HttpContext.User` 取用户。 |

## 3. 题目与内容资产

| 术语 | 定义 | 所有者 | 使用约束 |
| --- | --- | --- | --- |
| Challenge | 可校验答案并可选绑定运行环境的题目定义。 | Content | 当前 `Challenge` 是 `GameChallenge` 和 `ExerciseChallenge` 的公共基类；不能用它表达比赛参与状态。 |
| GameChallenge | 某场比赛拥有的题目快照。 | CTF | 删除比赛可以删除该快照；不能删除其引用的全局镜像主副本。 |
| ExerciseChallenge | 常态练习拥有的题目定义。 | Exercise | 不通过 `Participation` 表达练习进度。 |
| TrainingCourseChallenge | 课程对 `ExerciseChallenge` 的显式绑定。 | Training | 当前实体已存在；删除课程删除课程拥有的 ExerciseChallenge 快照、绑定和提交，不删除全局来源题目或 ImageTemplate。 |
| QuestionPool | 平台级可复用题目资产集合。 | Content | 目标术语，当前代码没有对应实体；由 Phase 10 实现。 |
| TheoryQuestion | 理论题资产，包含题型、题干、选项、答案和 tag。 | Theory | tag 是正式关系和索引条件，不能用题库名代替 tag。 |
| Attachment | 题目或课程引用的文件资产。 | Content | 通过显式引用管理生命周期。 |
| FlagContext | 运行期或题目校验使用的 Flag 事实。 | Content/Runtime | 不在日志、API 错误和队列详情中输出明文。 |

## 4. 镜像与运行资产

| 术语 | 定义 | 所有者 | 使用约束 |
| --- | --- | --- | --- |
| ImageTemplate | Docker 或 VM 镜像的全局模板元数据，存储服务器保存主副本。 | Content | 课程、比赛、练习和 TeamLab 只能建立引用；删除业务绑定不能删除仍被引用的模板。 |
| ImageBinding | 业务对象对 `ImageTemplate` 的显式引用关系。 | 引用方模块 | 禁止使用通用多态外键绕过数据库约束。 |
| RuntimeAsset | 已部署到 WorkerNode 的容器、VM、路由命名空间、DNS/DHCP 服务或 WireGuard 资源事实。 | Runtime | 目标统一术语；必须记录来源模板、节点、状态和清理结果。 |
| VmInstance | 已创建 VM 的生命周期事实。 | VM | OS 类型和访问协议必须分离。 |
| VmAccessEndpoint | VM 对选手或管理员提供的访问端点。 | VM | 目标术语；Windows 通常为 RDP/Guacamole，Linux 为 SSH 或受控图形协议。 |
| DeploymentQueueTicket | 环境创建、重置或销毁的排队和执行事实。 | Fleet | 不能为 TeamLab 再建立平行部署队列。 |

## 5. 培训

| 术语 | 定义 | 所有者 | 当前状态 |
| --- | --- | --- | --- |
| TrainingCourse | 可发布、报名、授课和追踪学习进度的课程聚合根。 | Training | 当前唯一运行课程模型。 |
| TrainingCourseChapter | 课程内可嵌套的章节，承载正文、视频、题目绑定和理论试卷。 | Training | 当前实体已存在。 |
| Enrollment | 用户与课程的报名关系。 | Training | 审核策略属于课程，不属于全局用户。 |
| CourseProgress | 用户在课程内的聚合进度。 | Training | 由章节、实践题和理论测试事实计算。 |
| TrainingDirection | 旧培训分类聚合。 | 无 | 已删除；仅历史 migration、迁移验证、负向删除门禁、禁用术语登记和审计记录可引用。 |
| TrainingModule | 旧培训内容聚合。 | 无 | 已删除；仅历史 migration、迁移验证、负向删除门禁、禁用术语登记和审计记录可引用。 |

## 6. TeamLab 组网基座

| 术语 | 定义 | 所有者 | 使用约束 |
| --- | --- | --- | --- |
| TeamLabTopology | 与调用方业务无关的组网草稿，拥有网段、资产、网卡和连通关系。 | TeamLab | Phase 3 建立；不能引用 Penetration 计分实体。 |
| TeamLabRelease | 从拓扑发布的不可变版本，包含 schema version、规范化快照和内容摘要。 | TeamLab | runtime 只能消费 release，不能消费可变草稿。 |
| TeamLabPlan | 对某个 release 和部署请求生成的确定性资源、分片、地址和路由计划。 | TeamLab | 相同 release、能力快照和约束必须产生相同计划。 |
| TeamLabRuntime | 某个 release 的运行实例，是组网生命周期聚合根。 | TeamLab | Phase 3 后不再以 `GameId + TeamId` 作为领域主身份。 |
| TeamLabRuntimeBinding | 平台业务对象与 TeamLab runtime 的显式绑定。 | 调用方模块 | Penetration 通过独立绑定表关联 Game、Team 和 Runtime。 |
| TeamLabShard | 一个 runtime 在某个 WorkerNode 上的执行分片。 | TeamLab | 一个网段首版只归属一个 shard。 |
| TeamLabRuntimeNetwork | runtime 内实际分配的网段、网关、bridge 和节点归属事实。 | TeamLab | 不从编辑器坐标或 UI 状态推导。 |
| TeamLabRuntimeAsset | runtime 内实际创建的 Docker、VM 或基础设施资产事实。 | TeamLab | 与 `ImageTemplate` 通过来源 ID 关联，不拥有镜像主副本。 |
| TeamLabAccessGrant | runtime 对玩家或外部调用方签发的受控访问配置。 | TeamLab | WireGuard 私钥必须受保护存储并支持撤销。 |
| TeamLabTrafficFlow | 解密后内网侧采集的聚合流量元数据。 | TeamLab | Phase 5 定义批写和保留，Phase 9 完成商业闭环。 |
| TeamLabCaptureJob | 有时间、大小、范围和保留期限的按需 PCAP 任务。 | TeamLab | 下载必须单独授权并记录审计。 |

## 7. Penetration 赛制

| 术语 | 定义 | 所有者 | 使用约束 |
| --- | --- | --- | --- |
| PenetrationGame | 使用 TeamLab 环境开展多阶段渗透的比赛模式。 | Penetration | 负责比赛资格、玩法、目标、计分、提交和重置政策。 |
| PenetrationObjective | 绑定到 TeamLab asset key 的得分目标。 | Penetration | 负责 Flag 规则、分值、前置目标和可见性，不进入 TeamLab 拓扑模型。 |
| PenetrationSubmission | 队伍对目标提交答案的事实。 | Penetration | 保留当前计分语义，改为引用 `PenetrationObjective`。 |
| PenetrationWorkspace | 组合比赛目标和 TeamLab runtime 查询结果的选手视图。 | Penetration | 只能通过 TeamLab 查询契约获取网络和访问事实。 |

## 8. 禁用术语

| 禁用术语 | 处理规则 |
| --- | --- |
| 独立 IRChallenge/IRInstance | 删除。IR 只作为普通 CTF 题目方向或内容标签。 |
| ScenarioInstance/Stage | 删除。不得用 Scenario 旧实体表达 TeamLab 拓扑、训练章节或题目阶段。 |
| 攻击图、迷雾、公网目标 | 不进入当前 TeamLab 商业主线，不保留兼容字段。 |
| WindowsVM 作为全部 VM 类型 | Phase 8 拆成 VM 类型、OS 类型和访问协议。 |
| 有效 token 等同管理员 | Phase 1 删除该授权语义。 |
| PenetrationConfig 作为 TeamLab 拓扑 | Phase 3 完成数据迁移后删除该依赖。 |

## 9. 命名检查

Phase 0 退出前运行：

```powershell
$legacySurface = 'IRChallenge|IRCheckpoint|IRInstance|ScenarioInstance|ScenarioTimelineEntry|TrainingDirection|TrainingModule|TrainingCtfSubmission|TheoryTrainingPlan|TheoryTrainingSession|Training(Admin)?Controller|api/training/(catalog|overview|modules|ctf/modules|theory/modules)'
rg -ni -g '!src/GZCTF/Migrations/**' -g '!src/GZCTF/wwwroot/**' -g '!artifacts/**' `
  $legacySurface `
  src/GZCTF src/GZCTF.Agent tests/e2e
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~LegacySurfaceRemovalTests
```

预期结果：文本扫描无产品运行代码或活动 e2e 命中，负向删除门禁通过。大小写不敏感扫描覆盖 PascalCase 类型、camelCase DTO/UI 字段、旧控制器名和旧 API 子路由；反射门禁精确覆盖 `Stage` 等通用词命名的已删除类型和控制器根路由。历史 EF migration、Phase 0 迁移验证、负向删除门禁、禁用术语登记和明确标记的审计记录可以引用旧名称，但不得形成可执行兼容面。
