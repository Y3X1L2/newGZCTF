# Submission & Scoring API Contracts

**Base URL**: `/api/v1/submissions`

## 1. 提交管理

### POST /api/v1/submissions
创建提交。

**Request Body**:
```json
{
  "challengeId": 42,
  "submissionType": "Flag",
  "content": {
    "value": "flag{web_entry_point}"
  }
}
```

**Multi-type submission** (Writeup):
```json
{
  "challengeId": 42,
  "submissionType": "Writeup",
  "content": {
    "text": "## 解题报告\n\n### 外网入口\n通过 SQL 注入...",
    "format": "markdown"
  }
}
```

**Multi-type submission** (File upload):
`POST /api/v1/submissions/upload` (multipart/form-data)
- `file`: PDF/Markdown 文件 (max 50MB)
- `challengeId`: int
- `submissionType`: "Writeup"

**Response** (200 OK):
```json
{
  "submissionId": "770e8400-e29b-41d4-a716-446655440002",
  "status": "Correct",
  "score": 100,
  "totalScore": 100,
  "detailScores": {
    "Flag": { "weight": 50, "score": 50, "status": "Correct" },
    "Writeup": { "weight": 30, "score": 0, "status": "PendingReview" },
    "IP": { "weight": 20, "score": 0, "status": "NotSubmitted" }
  }
}
```

### GET /api/v1/submissions?challengeId={id}&userId={uid}
查询提交记录（选手查自己的，管理员可查任意选手）。

## 2. 评分管理 (Admin)

### GET /api/v1/submissions/pending-review
获取待评审提交列表（仅 ManualReview 类型）。

**Query**: `?challengeId=42&submissionType=Writeup&page=1`

### POST /api/v1/submissions/{id}/review
人工评审提交。

**Request Body**:
```json
{
  "score": 8,
  "maxScore": 10,
  "comment": "攻击路径还原较完整，但缺少时间线分析"
}
```

### PUT /api/v1/scenarios/{id}/scoring-rules
更新评分配置。

### GET /api/v1/scenarios/{id}/leaderboard
获取场景排行榜。

**Response**:
```json
{
  "scenarioId": 42,
  "title": "企业内网渗透实战",
  "entries": [
    {
      "rank": 1,
      "userId": "...",
      "userName": "Team Alpha",
      "totalScore": 95,
      "detailScores": { "Flag": 50, "Writeup": 25, "IP": 20 },
      "stageCompletion": [1, 2],
      "completedAt": "2026-05-16T17:30:00Z"
    }
  ]
}
```

## 3. 评分引擎行为

评分计算规则:
1. 自动验证类型 (Flag/IP): 提交后立即出分，按 `ScoreDecay` 规则计算（None=满分/零分, Half=每次减半, Linear=线性递减）
2. 人工评审类型 (Writeup): 管理员在后台评分后计入总分
3. 综合总分 = Σ(每种类型得分 × 权重 / 100)
4. 排行榜按总分降序，同分按最后提交时间升序（先完成者排名靠前）

## 4. 实时通知 (SignalR)

**Events**:
- `ScoreUpdated`: 得分变更通知
- `SubmissionAcknowledged`: 提交已接收
- `ReviewCompleted`: 人工评审完成（仅被评审选手可见）
- `LeaderboardUpdated`: 排行榜更新
