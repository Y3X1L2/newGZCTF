# Scenario API Contracts

**Base URL**: `/api/v1/scenarios`

## 1. 场景管理 (Admin)

### POST /api/v1/scenarios
创建新场景。

**Request Body**:
```json
{
  "title": "企业内网渗透实战",
  "description": "从外网 Web 漏洞入手，逐步渗透至内网域控...",
  "gameId": 5,
  "stages": [
    {
      "orderIndex": 1,
      "title": "外网入口",
      "skillDescription": "考察 Web 漏洞利用与信息搜集能力",
      "prerequisiteStageIds": [],
      "networkRules": [
        { "fromCidr": "10.0.1.0/24", "toCidr": "10.0.2.0/24", "action": "Allow", "protocol": "TCP", "portRange": "1-65535" }
      ],
      "environmentRefs": [
        { "imageTemplateId": 12, "resourceSpec": { "cpu": 2, "memoryMb": 2048, "diskGb": 40 } }
      ],
      "flag": "flag{web_entry_point}"
    },
    {
      "orderIndex": 2,
      "title": "内网扫描",
      "skillDescription": "考察内网探测与横向移动能力",
      "prerequisiteStageIds": [1],
      "networkRules": [
        { "fromCidr": "10.0.1.0/24", "toCidr": "10.0.3.0/24", "action": "Allow", "protocol": "TCP", "portRange": "1-65535" }
      ],
      "environmentRefs": [
        { "imageTemplateId": 15, "resourceSpec": { "cpu": 1, "memoryMb": 1024 } }
      ],
      "flag": "flag{internal_scan}"
    }
  ],
  "scoringRules": [
    { "submissionType": "Flag", "weight": 50, "verificationMode": "AutoExact", "maxAttempts": 10 },
    { "submissionType": "Writeup", "weight": 30, "verificationMode": "ManualReview", "maxAttempts": 3 },
    { "submissionType": "IP", "weight": 20, "verificationMode": "AutoExact", "maxAttempts": 5 }
  ]
}
```

**Response** (201 Created):
```json
{
  "id": 42,
  "title": "企业内网渗透实战",
  "status": "Draft",
  "stageCount": 2,
  "createdAt": "2026-05-16T10:00:00Z"
}
```

### GET /api/v1/scenarios/{id}
获取场景详情。

### PUT /api/v1/scenarios/{id}
更新场景配置（仅 Draft 状态可修改）。

### DELETE /api/v1/scenarios/{id}
删除场景（仅 Draft 状态）。

### GET /api/v1/scenarios
列表查询场景（支持 gameId 过滤、分页）。

**Query**: `?gameId=5&page=1&pageSize=20`

## 2. 场景实例 (Player)

### POST /api/v1/scenarios/{id}/instances
选手申请创建场景实例（加入场景）。

**Response** (201 Created):
```json
{
  "instanceId": "550e8400-e29b-41d4-a716-446655440000",
  "scenarioId": 42,
  "currentStageId": 1,
  "timeSlot": {
    "startTime": "2026-05-16T14:00:00Z",
    "endTime": "2026-05-16T18:00:00Z"
  },
  "environmentAccess": {
    "stageId": 1,
    "host": "10.0.1.100",
    "port": 2222,
    "protocol": "SSH",
    "credential": "player_token_xxxx"
  }
}
```

### GET /api/v1/scenarios/instances/{instanceId}/status
获取当前实例状态（当前阶段、解锁进度、剩余时间）。

### POST /api/v1/scenarios/instances/{instanceId}/stages/{stageId}/submit
提交阶段 Flag，触发阶段解锁检查。

**Request**: `{ "flag": "flag{web_entry_point}" }`

**Response**:
```json
{
  "correct": true,
  "stageUpdated": "Completed",
  "nextStageUnlocked": {
    "stageId": 2,
    "title": "内网扫描",
    "accessInfo": { "note": "需要通过阶段 1 的跳板访问 10.0.3.0/24 网段" }
  },
  "score": 100
}
```

## 3. 时间段预约

### GET /api/v1/scenarios/{id}/timeslots
查询可用时间段。

### POST /api/v1/scenarios/{id}/timeslots/{slotId}/reserve
预约时间段。

## 4. 实时通知 (SignalR)

**Hub**: `/hubs/scenario`

**Events**:
- `StageUnlocked`: 新阶段解锁通知 (stageId, title, accessInfo)
- `TimeWarning`: 时间段即将结束提醒 (remainingMinutes)
- `ScoreUpdated`: 得分更新
- `EnvironmentReady`: 环境创建完成
