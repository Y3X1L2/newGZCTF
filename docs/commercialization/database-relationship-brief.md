# YINYU 现有数据库关系与复用讨论简报

日期：2026-09-15。这份简报介绍现有数据库的结构，供讨论新平台要复用哪些内容，以及第一阶段要完成什么。

按 `f389d26c` 的模型快照统计，数据库包含 **152 张逻辑表、310 条外键关系**。图表依据源码整理，不含生产业务数据。生产库可能有迁移版本差异和遗留表，实际结构还需另行核对。

## 总览

现有平台已经有用户、战队、参赛、提交和计分相关的数据与功能，建议继续使用现有的 PostgreSQL 架构，先理清各模块负责哪些数据、对外提供哪些接口。新平台需要先确定：是沿用表结构、共享现有数据，还是通过 API 调用现有功能。

队伍方面，系统已把战队当前成员和每场比赛的参赛成员分开保存，可以在这个基础上继续设计。计分要结合题目规则、解题记录、时间和赛制计算，复制一张积分表还不够。建议先确认队伍和计分规则，再确定表关系、接口范围和旧数据迁移方案。按需求方的意见，组网独立 API 的优先级较低。

## 当前数据库如何分工

业务数据主要保存在同一个 PostgreSQL 库里，不同内容放在不同表中。下表按业务分类，便于查找；这些分类目前并不对应独立的数据库或服务。

| 业务范围 | 代表表 | 保存的内容 |
| --- | --- | --- |
| 身份与战队 | AspNetUsers、Teams、TeamUserInfo | 账号、战队、当前队伍成员关系 |
| 比赛与参赛 | Games、Divisions、Participations、UserParticipations | 比赛、分组、战队参赛、个人参赛关系 |
| CTF 题目与解题 | GameChallenges、FlagContexts、Submissions、FirstSolves | 题目及规则、Flag 元数据、提交、首次成功解题 |
| 理论考试 | TheoryQuestionBankItems、TheoryPapers、TheoryAnswerSheets | 题库、试卷、答卷及成绩 |
| 培训与练习 | TrainingCourses、TrainingCourseEnrollments、ExerciseChallenges、ExerciseSubmissions | 课程、报名、练习定义、练习提交 |
| 内容与文件 | Files、Attachments、ImageTemplates | 文件元数据、附件关系、镜像模板 |
| 运行与组网 | DeploymentQueueTickets、WorkerNodes、TeamLabTopologies、TeamLabRuntimes | 统一部署任务、节点、场景和运行 |
| 攻防与审计 | AwdpRounds、AwdpCheckerTasks、Logs、DataGovernanceRuns | 攻防轮次与检测、日志、数据清理记录 |

Redis 用于缓存、协调和暂存高频数据。附件、镜像和实例磁盘的实际内容保存在文件或镜像存储中，数据库 `Files` 只记录 hash、文件名、大小等信息，存储子目录由 hash 计算。查看服务器磁盘占用时，需要把这些文件与数据库本身分开统计。

## 图一 用户战队和参赛关系

![用户战队和参赛关系](database-diagrams/team-participation.png)

实线表示现有外键，箭头从被引用的主表指向保存外键的表。图中只画主要关系，完整外键见附录 CSV。这是现有模型的关系图，字段和关联均按当前模型绘制，不是拟新增的表结构。

| 表 | 记录什么 | 复用时要注意什么 |
| --- | --- | --- |
| Teams 与 TeamUserInfo | 战队本身及当前成员关系 | 可供不同比赛使用，需要明确成员变动规则 |
| Participations | 某支战队参加某场比赛；包含审核状态和分组引用 | `(GameId, TeamId)` 有唯一约束，同队同场不重复报名 |
| UserParticipations | 个人属于该场比赛的哪支参赛队伍 | `(UserId, GameId)` 有唯一约束；另直接引用 TeamId、GameId |

同一支战队可以参加多场比赛，每场都有自己的 Participation，参赛成员通过 UserParticipation 关联。这样就能区分“当前队伍成员”和“本场参赛成员”。不过，仅凭这些表关系，还不能确认过去的姓名、队名和成员关系会永久冻结保存；历史记录如何保留、成员能否更换、数据何时允许删除，仍需确认。

Teams.CaptainId 还引用用户，UserParticipations 也直接引用 GameId、TeamId。这几条外键为便于阅读未画在图中。

## 图二 提交记录如何形成积分榜

![解题事实和计分流程](database-diagrams/scoring-facts.png)

上半图的实线表示数据库外键。下半图的虚线表示代码如何计算和查询榜单，其中的计算步骤和结果对象都不是新建的数据库表。

1. Submissions 记录提交状态、时间，以及对应的参赛关系和题目。表中虽然有 Score 字段，CTF 榜单并不直接累加所有 Submission.Score。
2. FirstSolves 记录某个参赛关系对某题、某 Flag 的首次成功解题，并引用对应 Submission。组合主键为 `(ParticipationId, ChallengeId, FlagId)`，SubmissionId 有唯一索引。“首次”是针对这个参赛关系而言，不是“整场比赛只有第一支解出题的队伍才有一条记录”。
3. `GameRepository.GenScoreboard` 读取首次解题、提交时间、题目及 Flag 计分配置、参赛状态和分组权限，计算动态分值及一二三血奖励，再生成榜单。
4. 当前普通／混合榜单的总分由 CTF、AWDP、Penetration 三部分组成，按赛制加入攻防或渗透结果。理论考试使用独立的答卷和成绩查询流程，各业务的计分规则不能预先视为相同。

复用积分功能时，需要明确分数依据哪些记录、采用什么规则、何时重新计算，以及如何查询排名。跨比赛累计积分、人工调分和不可变成绩快照是否需要，也要由新平台的需求决定。

## 审计发现和处理进度

| 发现的问题 | 依据 | 处理进度或建议 |
| --- | --- | --- |
| 目前不能仅凭表多决定拆库或合表 | 152 张表记录不同内容，尚未采集现网体积和负载 | 先测数据库大小、增长率和查询表现，再决定如何优化 |
| 改一处数据结构可能影响多个模块 | 部分 Controller 直接操作多个模块的数据，一部分映射仍集中在 AppDbContext | 明确各表由哪个模块负责，把读写逻辑放到对应模块，再对外提供接口 |
| 流量分区清理存在 SQL 错误 | 检查 SQL 中有多余反斜杠，会中断该周期后续清理 | 独立审计分支已修复，当时版本的 PostgreSQL 专项 4/4；尚未部署，需同步最新 main 后重新验证 |
| 还有几项清理和迁移问题待处理 | 每轮单批清理的处理量、空窗口扫描、Blob GC（无引用文件自动回收）、历史迁移来源 | 已记录为后续任务，尚未全部解决 |

审计修复提交为 `293b6d2d`，位于 `codex/database-maintainability-review` 分支；文件存储说明随后在 `44dd2f0` 中修订。当时构建通过，单测为 1,121/1,123，这些结果只对应当时的代码版本。后续 main 已增加分区处理数量限制和锁超时，合并修复前还要适配这些改动并重新测试。

本次关系图分支只提供文档和生成工具，没有改动计分逻辑、数据库或生产环境。

## 需要确认的五个问题

| 事项 | 建议问法 | 答复会影响什么 |
| --- | --- | --- |
| 首批业务 | 新平台第一版是什么类型，需要支持哪几种比赛或赛制？ | 模块范围和计分规则 |
| 复用方式 | 是沿用表结构、沿用现有账号和战队数据，还是通过 API 调用现有功能？是否要求共用一个库？ | 数据如何分开或共享、接口和迁移方式 |
| 队伍规则 | 需要复用成员、队长、报名审核、分组中的哪些内容？比赛中能否换人，历史名单如何保留？ | 战队与参赛模型 |
| 积分规则 | 需要哪些得分方式？是否涉及动态分值、攻防加减分、人工调整、并列排名或跨比赛累计？ | 要保存的计分记录、规则和查询接口 |
| 本周验收 | 本周先确认结构和接口方案，还是要完成开发、数据迁移并让新平台实际调用？谁负责验收？ | 本周具体需要完成的工作 |

等这些问题确认后，再提交一份对照方案，列出保留哪些表、调整哪些关系、提供哪些接口，以及旧数据怎么迁移，供对方评审。这份简报展示的是现状，还不是经过确认的新平台数据库设计。

## 可直接转发的说明

> 附件是现有数据库关系图和审计摘要，主要介绍用户、战队、参赛、提交和积分榜是怎样关联的。现有结构可以继续作为复用基础，计分部分还需要一起考虑赛制和计算规则，单独复制几张表不够。
>
> 想先确认新平台第一版支持哪些赛制、需要共享哪些数据、队伍和积分有哪些规则，以及本周要验收什么。确定后，再给出调整后的表结构、API 和迁移方案。组网 API 的独立产品化范围可以后续再确认。

## 技术核对附录

- [完整逻辑表清单](database-diagrams/tables.csv)：152 张表及对应实体和主键。
- [完整外键清单](database-diagrams/foreign-keys.csv)：310 条关系，记录子表、外键列、被引用表、删除行为和基数；基数结合主键、无条件唯一索引推导，不包含 hash 等非外键引用或仅在业务代码中检查的规则。
- [完整关系图 Mermaid 源文件](database-diagrams/full-schema.mmd)：可按模块筛选后渲染；全部节点同时展示较密，不作为汇报主图。
- 来源：`src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`、`Models/AppDbContext.cs`、`Models/Data/Team.cs`、`UserParticipation.cs`、`FirstSolve.cs`、`Modules/Ctf/Infrastructure/Persistence/CtfQueryEntityConfigurations.cs`、`Repositories/GameRepository.cs`、`Models/Request/Game/ScoreboardModel.cs`、`Controllers/TheoryPlayerController.cs`。
- 生成和检查：`python scripts/documentation/build_database_relationship_brief.py`，需 Python 与 Pillow。Windows 默认使用微软雅黑，其他环境可通过 `--font` 指定中文字体。工具仅读取源码快照，不连接数据库；如快照生成格式变化会报错，需要复核解析器。生成统计保存在 [manifest.json](database-diagrams/manifest.json)，含快照 SHA-256，便于确认材料对应的版本。
