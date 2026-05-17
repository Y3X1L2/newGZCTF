# 数据模型设计: CTF 场景化实战平台

**Feature**: 001-ctf-scenario-engine
**Date**: 2026-05-16

## Entity Relationship Overview

```
Game (GZCTF 现有) -1---*- Challenge (GZCTF 现有，扩展)
                                |
                    +-----------+-----------+
                    |                       |
              Scenario (子类型)        IRChallenge (子类型)
                    |                       |
                  1 *                     1 *
                    |                       |
                  Stage                 IRCheckpoint
                    |                       |
                  1 *                     1 *
                    |                       |
            ScenarioInstance           IRInstance
                    |                       |
                    1---*--- Submission ---*---1

TimeSlot *---1 Scenario    ImageTemplate *---* Stage
```

## Entities

### Scenario（场景）— 继承自 Challenge

真实世界攻击链场景，包含多个有序阶段。

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| Id | int (PK) | 自增主键 | Required |
| Title | string(256) | 场景名称 | Required, Max 256 |
| Description | string(4096) | 场景描述/故事背景 | Required |
| Stages | List\<Stage\> | 阶段列表 | Min 2, Max 20 |
| ScoringRules | List\<ScoringRule\> | 评分配置 | Weight sum = 100% |
| TimeWindow | TimeWindow | 赛事时间窗口（继承自 Game） | Required |
| GameId | int (FK) | 所属赛事 | Required |
| ChallengeType | enum | = Scenario | Discriminator |

**State transitions**: `Draft → Published → (Game starts) → Active → (Game ends) → Ended`

### Stage（场景阶段）

攻击链中的一个步骤。包含网络隔离规则和关联的题目环境。

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| Id | int (PK) | 自增主键 | Required |
| ScenarioId | int (FK) | 所属场景 | Required |
| OrderIndex | int | 阶段序号 (1-based) | Required, Unique per Scenario |
| Title | string(256) | 阶段名称 | Required |
| SkillDescription | string(1024) | 考察能力说明 | Required |
| PrerequisiteStages | List\<Stage\> | 前置依赖阶段 (DAG) | Max depth 10 |
| NetworkRules | List\<NetworkRule\> | 网络访问规则 | Optional |
| EnvironmentRefs | List\<EnvironmentRef\> | 关联题目环境 | Min 1 per stage |
| Flag | string(512) | 本阶段 Flag | Required (hashed) |

**State transitions** (per instance): `Locked → Unlocked → Completed`

**NetworkRule**: { FromIp/Cidr, ToIp/Cidr, Allow/Deny, Protocol (TCP/UDP/ICMP), PortRange }

### ScenarioInstance（场景实例）

选手在场景中的运行实例。

| Field | Type | Description |
|-------|------|-------------|
| Id | guid (PK) | 实例唯一标识 |
| ScenarioId | int (FK) | 所属场景 |
| UserId | guid (FK) | 选手 ID |
| CurrentStageId | int (FK) | 当前所在阶段 |
| StageStatuses | JSON | 各阶段状态 { StageId: Locked/Unlocked/Completed } |
| StageTimeline | JSON | 各阶段操作时间线 [{ StageId, UnlockedAt, CompletedAt, FirstBlood }] |
| EnvironmentCredentials | JSON | 环境访问凭证（阶段→凭证映射） |
| TimeSlotId | int (FK) | 关联时段 |
| CreatedAt | datetime | 创建时间 |
| Status | enum | Active/Paused/Completed/Expired |

### IRChallenge（应急响应题目）— 继承自 Challenge

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| Id | int (PK) | 自增主键 | Required |
| Title | string(256) | 题目名称 | Required |
| Description | string(4096) | 应急场景描述 | Required |
| OSType | enum | Linux / Windows | Required |
| AccessConfig | JSON | 访问配置（见下方） | Required |
| Checkpoints | List\<IRCheckpoint\> | 检查点列表 | Min 1 |
| ScoringRules | List\<ScoringRule\> | 评分配置 | Weight sum = 100% |
| GameId | int (FK) | 所属赛事 | Required |
| ChallengeType | enum | = IRChallenge | Discriminator |

**AccessConfig** (Linux): `{ Protocol: "SSH", Host, Port, Username, AuthType: "Password"|"PrivateKey", Credential }`
**AccessConfig** (Windows): `{ Protocol: "Guacamole", GuacConnectionId }`

### IRCheckpoint（检查点）

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| Id | int (PK) | 自增主键 | Required |
| ChallengeId | int (FK) | 所属 IR 题目 | Required |
| OrderIndex | int | 检查点序号 | Required |
| Description | string(1024) | 检查点描述 | Required |
| VerificationType | enum | AutoScript / AutoCommand / ManualAnswer / ManualReview | Required |
| VerificationConfig | JSON | 验证规则（见下方） | Required |
| Score | int | 本检查点分值 | Min 1 |
| IsRequired | bool | 是否必须完成 | Default true |

**VerificationConfig examples**:
- AutoScript: `{ ScriptPath: "/scripts/check_db_restored.sh", Timeout: 30 }`
- AutoCommand: `{ Command: "systemctl is-active postgresql", ExpectedOutput: "active" }`
- ManualAnswer: `{ ExpectedAnswer: "192.168.1.105", MatchMode: "Exact"|"Regex" }`
- ManualReview: `{}` (管理员人工评审)

### IRInstance（应急响应实例）

| Field | Type | Description |
|-------|------|-------------|
| Id | guid (PK) | 实例唯一标识 |
| ChallengeId | int (FK) | 所属 IR 题目 |
| UserId | guid (FK) | 选手 ID |
| EnvironmentStatus | enum | Creating/Ready/Error/Destroyed |
| CheckpointResults | JSON | 检查点完成状态 [{ CheckpointId, Completed, Score, VerifiedAt }] |
| ShellLog | JSON/text | 操作命令日志 (Linux: bash history, Windows: PS log) |
| ResetCount | int | 环境重置次数 |
| AccessDetails | JSON | 当前访问信息（SSH host:port 或 Guac URL） |
| TimeSlotId | int (FK) | 关联时段 |
| CreatedAt / EndedAt | datetime | 时间戳 |

### Submission（提交）— 扩展 GZCTF 现有

| Field | Type | Description |
|-------|------|-------------|
| Id | guid (PK) | 提交唯一标识 |
| UserId | guid (FK) | 选手 ID |
| ChallengeId | int (FK) | 关联题目 (Scenario/IR/Standard) |
| SubmissionType | enum | Flag / Writeup / IP / Credential / Custom |
| Content | JSON/text | 提交内容 |
| Status | enum | Correct / Incorrect / Partial / PendingReview |
| Score | int | 本次提交得分 |
| ReviewedBy | guid (FK)? | 评审员 ID（人工评审时） |
| ReviewComment | string(1024)? | 评审反馈 |
| AttemptNumber | int | 第几次尝试 |
| SubmittedAt | datetime | 提交时间 |

### ScoringRule（评分规则）

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| Id | int (PK) | 自增主键 | Required |
| ChallengeId | int (FK) | 关联题目 | Required |
| SubmissionType | enum | Flag / Writeup / IP / Credential / Custom | Required, Unique per Challenge |
| Weight | decimal(5,2) | 权重百分比 (0-100) | Required, sum(all rules) = 100 |
| VerificationMode | enum | AutoExact / AutoRegex / AutoScript / ManualReview | Required |
| MaxAttempts | int | 最大尝试次数 | 0 = 无限制 |
| ScoreDecay | enum | None / Half / Linear | 递减评分策略 |

### TimeSlot（时间段）

| Field | Type | Description |
|-------|------|-------------|
| Id | int (PK) | 自增主键 |
| ScenarioId | int (FK) | 关联场景 |
| StartTime | datetime | 开始时间 |
| EndTime | datetime | 结束时间 |
| MaxParticipants | int | 最大人数 (default 20) |
| CurrentParticipants | int | 当前已预约人数 |

### ImageTemplate（环境模板镜像）

| Field | Type | Description |
|-------|------|-------------|
| Id | int (PK) | 自增主键 |
| Name | string(256) | 模板名称 |
| OSType | enum | Linux / Windows |
| ImageType | enum | Docker / Qcow2 / Ova / Vmdk |
| RegistryUrl | string(512)? | Docker Registry URL (Linux only) |
| RegistryAuth | string(512)? | Registry 认证凭证 (encrypted) |
| LocalFilePath | string(512)? | 本地存储路径 (Windows only) |
| FileSize | long | 文件大小 (bytes) |
| UploadedAt | datetime | 上传时间 |
| Status | enum | Ready / Importing / Error |

## Database Migration Strategy

由于 Scenario 和 IRChallenge 继承自 GZCTF 现有 Challenge 实体：

1. **EF Core TPH (Table-Per-Hierarchy)** 策略：在 `Challenges` 表中增加 `ChallengeType` 判别列，新增字段作为 nullable 列
2. 新建表：`Stages`、`ScenarioInstances`、`IRCheckpoints`、`IRInstances`、`ScoringRules`、`TimeSlots`、`ImageTemplates`
3. 扩展 `Submissions` 表：增加 `SubmissionType`、`ReviewedBy`、`ReviewComment` 列
4. 所有迁移通过 EF Core Migration 进行，保持 GZCTF 现有数据结构不变

## Indexing Strategy

- `ScenarioInstances`: (UserId, ScenarioId) 联合索引 — 高频查询选手实例状态
- `IRInstances`: (UserId, ChallengeId) 联合索引
- `Submissions`: (UserId, ChallengeId, SubmissionType) 联合索引 — 提交去重和查重
- `Stages`: (ScenarioId, OrderIndex) 联合索引 — 按序查询阶段
- `TimeSlots`: (ScenarioId, StartTime) 联合索引 — 时段查询
