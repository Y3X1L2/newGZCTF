# IR Challenge API Contracts

**Base URL**: `/api/v1/ir-challenges`

## 1. IR 题目管理 (Admin)

### POST /api/v1/ir-challenges
创建 IR 题目。

**Request Body**:
```json
{
  "title": "勒索病毒应急响应",
  "description": "一台 Windows Server 2019 被勒索病毒感染，数据库被加密...",
  "gameId": 5,
  "osType": "Windows",
  "accessConfig": {
    "protocol": "Guacamole",
    "templateId": 22
  },
  "checkpoints": [
    {
      "orderIndex": 1,
      "description": "恢复被加密的 PostgreSQL 数据库",
      "verificationType": "AutoCommand",
      "verificationConfig": {
        "command": "systemctl is-active postgresql",
        "expectedOutput": "active",
        "timeout": 30
      },
      "score": 100,
      "isRequired": true
    },
    {
      "orderIndex": 2,
      "description": "从 Web 日志中找出攻击者 IP",
      "verificationType": "ManualAnswer",
      "verificationConfig": {
        "expectedAnswer": "203.0.113.45",
        "matchMode": "Exact"
      },
      "score": 50,
      "isRequired": true
    },
    {
      "orderIndex": 3,
      "description": "还原攻击路径（提交报告）",
      "verificationType": "ManualReview",
      "verificationConfig": {},
      "score": 80,
      "isRequired": false
    }
  ],
  "scoringRules": [
    { "submissionType": "Flag", "weight": 40, "verificationMode": "AutoExact", "maxAttempts": 5 },
    { "submissionType": "Writeup", "weight": 30, "verificationMode": "ManualReview" },
    { "submissionType": "IP", "weight": 30, "verificationMode": "AutoExact", "maxAttempts": 3 }
  ]
}
```

**Response** (201 Created): 同 Scenario 格式。

### GET /api/v1/ir-challenges/{id}
获取 IR 题目详情（含检查点配置）。

## 2. IR 实例 (Player)

### POST /api/v1/ir-challenges/{id}/instances
选手申请创建 IR 实例（加入 IR 挑战）。

**Response** (201 Created):
```json
{
  "instanceId": "660e8400-e29b-41d4-a716-446655440001",
  "challengeId": 15,
  "accessDetails": {
    "linux": {
      "protocol": "SSH",
      "host": "192.168.10.50",
      "port": 2222,
      "username": "player",
      "credential": "temp_password_xxxx"
    },
    "windows": {
      "protocol": "Guacamole",
      "connectionUrl": "/guacamole/#/client/xxxxx",
      "token": "guac_token_xxxxx"
    }
  },
  "timeSlot": {
    "startTime": "2026-05-16T14:00:00Z",
    "endTime": "2026-05-16T16:00:00Z"
  }
}
```

### GET /api/v1/ir-challenges/instances/{instanceId}/status
获取当前实例状态和检查点完成进度。

**Response**:
```json
{
  "instanceId": "660e8400...",
  "status": "Active",
  "remainingTime": "01:23:45",
  "checkpoints": [
    { "id": 1, "description": "恢复 PostgreSQL 数据库", "completed": false, "score": 100 },
    { "id": 2, "description": "找出攻击者 IP", "completed": true, "score": 50 },
    { "id": 3, "description": "还原攻击路径", "completed": false, "score": 0 }
  ],
  "totalScore": 50
}
```

### POST /api/v1/ir-challenges/instances/{instanceId}/checkpoints/{checkpointId}/submit
提交检查点答案（用于 ManualAnswer 类型）。

**Request**: `{ "answer": "203.0.113.45" }`

### POST /api/v1/ir-challenges/instances/{instanceId}/reset
请求环境重置。

**Response**:
```json
{
  "resetRequested": true,
  "estimatedTime": 45,
  "message": "环境重置中，预计 45 秒后恢复至初始状态"
}
```

## 3. 检查点自动验证

系统定期检测（每 30 秒）IR 环境中 AutoScript/AutoCommand 类型检查点的目标状态。一旦检测到状态满足预期，自动标记检查点为完成并通过 SignalR 推送 `CheckpointCompleted` 事件。

## 4. 实时通知 (SignalR)

**Hub**: `/hubs/scenario` (复用)

**Events**:
- `CheckpointCompleted`: 检查点自动验证通过
- `EnvironmentResetComplete`: 环境重置完成
- `ShellLogUpdated`: 操作日志更新（摘要）
