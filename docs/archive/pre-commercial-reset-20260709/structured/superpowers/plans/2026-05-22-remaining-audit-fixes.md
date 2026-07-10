# 审计剩余问题修复计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 修复 2026-05-22 综合审计中剩余的全部 P2/P3 级问题（4 个功能缺失 + 5 个代码卫生问题）

**Architecture:** 前端 3 个任务（Flag编辑、排行榜、死代码）+ 后端 2 个任务（模型清理、OrderIndex重排）+ 部署 1 个任务

**Tech Stack:** .NET 10 / EF Core 10 / React 19 + Mantine / TypeScript

**Audit Source:** `docs/superpowers/reviews/2026-05-22-comprehensive-audit-progress.md`

---

## File Map

| 操作 | 文件 | 职责 |
|------|------|------|
| 修改 | `ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/flags/index.tsx` | Flag 编辑功能：添加编辑按钮和编辑模态框 |
| 修改 | `ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/index.tsx` | Flag 编辑功能：主编辑页已支持，无需额外修改 |
| 修改 | `ClientApp/src/pages/game/Scoreboard.tsx` 或排行榜组件 | 排行榜展示多 Flag 完成进度 |
| 删除 | `ClientApp/src/utils/screenDemoData.ts` | 死代码清理 |
| 修改 | `Models/Data/GamePhase.cs` | 删除 IREnabled/ScenarioEnabled 残留字段 |
| 修改 | `Repositories/GameChallengeRepository.cs` | AddFlags 时自动分配 OrderIndex |
| 修改 | `Migrations/` | 生成迁移：删除 GamePhase 残留列 + TimeSlots 表 |

---

## Task 1: Flag 编辑功能

### 背景
`flags/index.tsx` 目前只能添加和展示 Flag，没有编辑按钮。用户需要编辑已有 Flag 的 ScoreMode、AnswerType、OrderIndex 等字段时，只能删除重建。

### 改动

**文件:** `ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/flags/index.tsx`

**Step 1: 添加编辑状态和模态框**

添加编辑状态：
```typescript
const [editingFlag, setEditingFlag] = useState<FlagInfo | null>(null)
const [editFlag, setEditFlag] = useState('')
const [editScoreMode, setEditScoreMode] = useState<string>('InheritDecay')
const [editFixedScore, setEditFixedScore] = useState(0)
// ... 其他编辑字段
```

**Step 2: 添加 PUT 请求函数**

```typescript
const handleUpdateFlag = async () => {
  if (!editingFlag) return
  const body = {
    flag: editFlag,
    orderIndex: editOrderIndex,
    description: editDescription,
    scoreMode: editScoreMode,
    fixedScore: editFixedScore,
    maxAttempts: editMaxAttempts,
    answerType: editAnswerType,
    customName: editCustomName,
  }
  const res = await fetch(
    `/api/edit/Games/${gameId}/Challenges/${challengeId}/Flags/${editingFlag.id}`,
    { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
  )
  if (res.ok) {
    notifications.show({ title: '更新成功', message: 'Flag 已更新', color: 'green' })
    setEditingFlag(null)
    load()
  }
}
```

**Step 3: 添加编辑按钮到 Flag 列表项**

在每个 Flag 的 Alert 组件中添加编辑按钮：
```tsx
<Button size="xs" variant="subtle" onClick={() => {
  setEditingFlag(f)
  setEditFlag(f.flag)
  setEditScoreMode(f.scoreMode ?? 'InheritDecay')
  setEditFixedScore(f.fixedScore ?? 0)
  setEditMaxAttempts(f.maxAttempts ?? 0)
  setEditOrderIndex(f.orderIndex ?? flags.indexOf(f))
  setEditDescription(f.description ?? '')
  setEditAnswerType(f.answerType ?? 'Flag')
  setEditCustomName(f.customName ?? '')
}}>编辑</Button>
```

**Step 4: 添加编辑模态框**

```tsx
<Modal opened={!!editingFlag} onClose={() => setEditingFlag(null)} title="编辑 Flag" size="lg">
  <Stack>
    <TextInput label="Flag" required value={editFlag} onChange={e => setEditFlag(e.currentTarget.value)} />
    <NumberInput label="顺序" value={editOrderIndex} onChange={v => setEditOrderIndex(Number(v) ?? 0)} />
    <Select label="评分模式" data={scoreModeOptions} value={editScoreMode}
      onChange={v => setEditScoreMode(v as string)} />
    {editScoreMode === 'FixedScore' && (
      <NumberInput label="固定分值" value={editFixedScore} onChange={v => setEditFixedScore(Number(v) ?? 0)} />
    )}
    <NumberInput label="最大尝试" value={editMaxAttempts} onChange={v => setEditMaxAttempts(Number(v) ?? 0)} />
    <Select label="答案类型" data={answerTypeOptions} value={editAnswerType}
      onChange={v => setEditAnswerType(v as string)} />
    <TextInput label="自定义名称" value={editCustomName} onChange={e => setEditCustomName(e.currentTarget.value)} />
    <Textarea label="描述" value={editDescription} onChange={e => setEditDescription(e.currentTarget.value)} />
    <Button onClick={handleUpdateFlag}>保存</Button>
  </Stack>
</Modal>
```

**Step 5: 提交**

```bash
git add src/GZCTF/ClientApp/src/pages/admin/games/\[id\]/challenges/\[challengeId\]/flags/index.tsx
git commit -m "feat(ui): add flag edit modal with PUT API support"
```

---

## Task 2: 排行榜多 Flag 进度展示

### 背景
后端 `ScoreboardModel.ChallengeInfo` 已有 `TotalFlags` 字段，`ChallengeItem` 已有 `FlagId` 字段，但前端排行榜未使用这些字段展示多 Flag 完成进度。

### 改动

**文件:** 查找并修改排行榜前端组件（通常为 `ClientApp/src/pages/game/` 或 `ClientApp/src/components/` 下的排行榜组件）

**Step 1: 找到排行榜组件**

```bash
grep -rn "ScoreboardItem\|ChallengeItem\|totalFlags\|solvedFlags" src/GZCTF/ClientApp/src --include="*.tsx" --include="*.ts"
```

**Step 2: 在挑战卡片/行中添加 Flag 完成进度**

如果是表格形式：
```tsx
<Table.Td>
  {item.solvedCount ?? 0}/{item.totalFlags ?? 1} Flags
</Table.Td>
```

如果是卡片形式：
```tsx
<Text size="xs" c="dimmed">
  {item.solvedFlags ?? 0}/{item.totalFlags ?? 1} Flags Solved
</Text>
```

**Step 3: 确认后端数据传递**

检查 `ScoreboardModel.ChallengeInfo` 是否有 `TotalFlags` 字段（已确认第 360 行存在），`ChallengeItem` 是否有 `SolvedFlags` 或类似字段。如果后端未填充 `solvedFlags`，需要在 `GenScoreboard` 中计算并填充。

**Step 4: 提交**

```bash
git commit -m "feat(ui): show multi-flag completion progress on leaderboard"
```

---

## Task 3: 死代码清理

### 改动

**文件 1:** `ClientApp/src/utils/screenDemoData.ts`（如果存在）

**Step 1: 检查引用**

```bash
grep -rn "screenDemoData\|DemoScenario\|useDemoScreenData" src/GZCTF/ClientApp/src --include="*.tsx" --include="*.ts"
```

**Step 2: 删除或清理**

如果只有 `screenDemoData.ts` 自身引用，直接删除：
```bash
git rm src/GZCTF/ClientApp/src/utils/screenDemoData.ts
```

如果有其他文件引用，移除引用后再删除。

**文件 2:** `Models/Data/GamePhase.cs` — `IREnabled` / `ScenarioEnabled` 残留字段

**Step 3: 删除残留字段**

在 `Models/Data/GamePhase.cs` 中：
```csharp
// 删除以下两行:
public bool IREnabled { get; set; } = true;
public bool ScenarioEnabled { get; set; } = true;
```

**Step 4: 生成迁移**

```bash
dotnet ef migrations add RemoveGamePhaseDeadColumns
```

**Step 5: 提交**

```bash
git commit -m "chore: remove dead code - screenDemoData, GamePhase IR/Scenario columns"
```

---

## Task 4: Flag OrderIndex 自动重排

### 背景
`AddFlags` 方法不自动分配 `OrderIndex`，所有 Flag 默认 `OrderIndex=0`。手动管理容易冲突。

### 改动

**文件:** `Repositories/GameChallengeRepository.cs`

**Step 1: 在 AddFlags 中自动分配 OrderIndex**

修改 `AddFlags` 方法：
```csharp
public async Task AddFlags(GameChallenge challenge, FlagCreateModel[] models, bool save = true,
    CancellationToken token = default)
{
    var maxOrder = await Context.FlagContexts
        .Where(f => f.Challenge == challenge)
        .MaxAsync(f => (int?)f.OrderIndex, token) ?? -1;

    foreach (var model in models)
    {
        maxOrder++;
        var flag = new FlagContext
        {
            Challenge = challenge,
            Flag = model.Flag,
            OrderIndex = model.OrderIndex > 0 ? model.OrderIndex : maxOrder,
            // ... other fields
        };
        Context.FlagContexts.Add(flag);
    }
    // ...
}
```

**Step 2: 提交**

```bash
git commit -m "feat: auto-assign OrderIndex for new flags when not explicitly set"
```

---

## Task 5: 数据库迁移与部署

### 改动

**Step 1: 删除 TimeSlots 残留表（如存在）**

检查 DB 中是否有 `TimeSlots` 表，如果有则在迁移中 DROP：
```csharp
migrationBuilder.Sql("DROP TABLE IF EXISTS \"TimeSlots\"");
```

**Step 2: 生成最终迁移**

```bash
dotnet ef migrations add FinalAuditCleanup
dotnet build -c Release
dotnet test
pnpm build
```

**Step 3: 部署**

```bash
python scripts/deploy.py
```

**Step 4: 提交**

```bash
git commit -m "chore: final audit cleanup migration"
```

---

## Execution Order

```
Task 3 (死代码) → Task 4 (OrderIndex) → Task 1 (Flag编辑) → Task 2 (排行榜) → Task 5 (最终迁移部署)
```

Task 3 和 4 独立，可并行。Task 1 和 2 依赖前端构建。Task 5 收尾。

---

## 架构级缺失（不在本次计划范围，需单独立项）

| 编号 | 问题 | 影响 | 需独立 spec |
|------|------|------|------------|
| A1 | Agent 程序未实现 | 节点 CPU/内存指标永远为 0 | 是 |
| A2 | QueueManager 只分配不执行 | 部署队列永远是 Pending | 是 |
| A3 | FleetManager/调度器未接入 | 节点选择算法未被使用 | 是 |
| A4 | VM 生命周期不完整 | VmInstance 状态不更新 | 是 |
| A5 | TimeSlots 表残留 | 无功能影响，浪费存储 | 否（本次清理） |
| A6 | sshpass 在 Windows 不可用 | NodeDeployService 无法在 Windows 上运行 | 是 |
