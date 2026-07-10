# Phase 0 Baseline Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 清除旧 IR/Scenario 独立系统和旧培训双轨，完成可校验的数据迁移并冻结后续阶段唯一术语。

**Architecture:** 采用一次性 `expand -> migrate -> contract` 切换。旧培训数据先迁入现有 `TrainingCourse` 聚合，核对数量和引用后删除旧 Controller、DTO、页面、实体和数据库表；IR/Scenario 没有可继续使用的业务入口，只执行备份、计数审计和删表。历史 EF migration 保留为数据库演进记录，当前模型快照必须只包含目标模型。

**Tech Stack:** .NET 10、EF Core 10、PostgreSQL、xUnit、Testcontainers.PostgreSql、React 19、TypeScript 6、Playwright e2e。

---

## 实施进度

更新时间：2026-07-10

- 当前状态：执行中，Task 1 至 Task 4 已完成，下一步实施 Task 5 总体验收。
- 工作分支：`codex/phase-0-baseline-cleanup`。
- 隔离工作区：`D:\newgz\newGZCTF-phase0`。
- 基线验证：`dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore` 通过，577 项测试全部通过；`pnpm check` 通过。
- 告警门禁：基线有 17 条 nullable warning；Task 2 当前构建为 13 条，未新增告警。
- Task 2 结果：`TrainingCourseController.BuildOverview` 已完全切换到课程、章节、课程提交和课程理论答卷事实，不再读取旧模块表。
- Task 2 结果：`TimeSlot`、`ScoringRule`、IR/Scenario 实体、旧培训 Controller、DTO、DbSet 和当前模型快照已清退；历史 migration 保留。
- Task 2 结果：PostgreSQL Testcontainers 已验证旧课程树、组报名、实践提交、两次理论作答和阅读百分比守恒，目标 EF 模型无 pending changes。
- Task 2 质量修正：理论重做改为显式状态转换，GET 不再自动创建下一次 attempt；章节完成策略进入新课程章节编辑合约，不保留旧培训配置入口。
- Task 3 结果：旧管理员培训页、旧 CTF/理论模块页和四个废弃 e2e 已删除；学员组 API 已拆出为独立 `StudentGroupApi`，没有把旧模块逻辑合并进新课程页面。
- Task 3 结果：新课程前端已接入章节完成策略、理论重做、答案显示策略和 attempt 信息；locale 校验、TypeScript strict check 与活动源码遗留扫描通过。
- Task 4 结果：活动源码和 e2e 的禁用术语、乱码、历史阶段注释与 `dry-run` 占位扫描通过；总纲、术语表和模块边界已更新为 Phase 0 完成后的唯一运行模型。
- 数据边界：本轮完成迁移代码、审计脚本和可重复集成验证；未收到部署指令前，不连接生产数据库、不执行生产备份、不应用 contract migration。

任务状态：

- [x] Task 1：建立遗留面清单和失败测试
- [x] Task 2：原子完成旧培训迁移和后端 contract 切换
- [x] Task 3：删除旧前端和失效 e2e
- [x] Task 4：冻结术语并清理活动文档
- [ ] Task 5：Phase 0 总体验收

## 0. 代码事实与退出边界

Phase 0 实施前的代码基线：

- `Models/Data/IREntities.cs` 仍定义 `IRCheckpoint` 和 `IRInstance`，`TimeSlot.cs`、`ScoringRule.cs` 仍保留同一废弃子系统的辅助实体，`AppDbContext` 仍暴露对应 DbSet。
- `Models/Data/ScenarioEntities.cs` 仍定义 Stage、timeline 和 instance 体系，当前没有有效 Controller 或前端入口。
- `TrainingController`、`TrainingAdminController`、`TrainingDirection/TrainingModule`、`TrainingModels` 和旧培训前端 API 仍可运行，不能直接删表。
- 新 `TrainingCourse` 体系已具备课程、章节、题目绑定、理论试卷、提交和进度实体，可以承接旧培训数据。
- `tests/e2e/ir-challenge.spec.ts`、`scenario-create.spec.ts`、`scenario-play.spec.ts` 和 `topology-editor.spec.ts` 仍验证废弃产品概念。

Phase 0 不删除历史 `Migrations/*.cs`；删除历史 migration 会破坏从空库升级的能力。阶段退出检查忽略历史 migration 和 `docs/archive`，但当前 `AppDbContextModelSnapshot` 不得保留旧表。

## 1. 数据迁移规则

### 1.1 旧培训映射

| 旧对象 | 新对象 | 确定性映射 |
| --- | --- | --- |
| TrainingDirection | 课程标签 | 写入 `direction:{Key}` 和 `training-type:{Type}`，不建立新分类实体。 |
| 根 TrainingModule | TrainingCourse | 每个根模块建立一门课程，保留标题、slug、摘要、封面、创建者和时间。 |
| 根模块及其后代 | TrainingCourseChapter | 每个模块建立一个章节，使用旧 `ParentId` 重建章节树。 |
| TrainingModuleChallenge | TrainingCourseChallenge + TrainingCourseChapterChallenge | 每个旧绑定同时建立课程级绑定和章节级绑定。 |
| TrainingCtfSubmission | TrainingCourseSubmission | 使用 module-to-course 和 module-to-chapter 映射写入。 |
| TheoryTrainingPlan | TrainingCourseChapterTheoryPaper | 每个理论计划归属其模块对应章节，并保留 `PassRate`、`AllowRetake` 和提交后答案可见策略。 |
| TheoryTrainingPlanQuestion | TrainingCourseTheoryQuestion + TrainingCourseChapterTheoryQuestion | 从 `TheoryQuestionBankItem` 复制课程题和试卷题快照，不能让课程继续依赖旧计划。 |
| TheoryTrainingSession | TrainingCourseChapterTheorySheet | 按用户、章节和尝试序号迁移全部 session，保留状态、得分、总分和提交时间。 |
| TheoryTrainingSessionQuestion | TrainingCourseChapterTheoryAnswer | 按迁移后的 paper question 映射保留作答和判题结果。 |
| TrainingArticleProgress | TrainingChapterProgress | 保留 `ReadPercent`；100 映射为 Completed，1-99 映射为 Learning，0 映射为 NotStarted。 |
| TrainingModuleProgress | TrainingCourseProgress | 按课程内所有章节、题目和理论 sheet 重新聚合，旧汇总值只用于迁移校验。 |
| TrainingModuleVisibility | EnrollmentPolicy + Enrollment | AllStudents 映射 Open；GroupOnly 为当前组员建立 Approved enrollment。 |

每个旧 module 迁入 chapter 时必须复制 `Title, Summary, ArticleContent -> Content, ArticleContentType -> ContentType, Order, IsPublished, CreatedById, UpdatedById, CreatedAt, UpdatedAt`。根 module 额外向 course 复制 `Title, Slug, Summary, CoverFileHash, CreatedById, UpdatedById, CreatedAt, UpdatedAt, PublishedAt`；其 direction/type 写入 course tags。`TrainingCompletionRule` 逐字段写入 chapter 的目标 `TrainingChapterCompletionPolicy`，不能按新系统默认值覆盖。

迁移前置约束：

1. 同一根模块子树的可见性集合必须一致，避免课程级报名扩大章节访问范围。
2. `EnvironmentTemplateId` 非空的模块必须存在引用同一模板的 `ExerciseChallenge` 绑定；旧字段本身不形成运行环境。
3. `TrainingModule.Slug` 在迁移后的课程范围内必须唯一；冲突时使用 `{slug}-{moduleId}`，空值使用 `course-{moduleId}`。
4. 缺失用户、题目、Flag 或理论题来源的外键必须在迁移前报告并修复，禁止静默丢行。
5. 每个 `TheoryTrainingSessionQuestion.SourceQuestionId` 必须能映射到同模块 plan question；无法映射的历史快照必须先修复，不能丢弃作答。
6. 同一用户同一模块的多次理论 session 按 `CreatedAt, Id` 生成从 1 开始的稳定 attempt number；目标表必须允许保留全部尝试。
7. 迁移完成后旧表只允许在同一个 migration 事务中被删除，不能留下双写窗口。

### 1.2 IR/Scenario 处理

- 不把 `IRCheckpoint`、`IRInstance`、Stage 或 Scenario instance 自动转换成普通 CTF 题目；二者的数据语义不等价。
- 生产升级前使用 PostgreSQL `COPY ... TO STDOUT` 导出旧表，记录行数和 SHA-256。
- 数据库备份和导出确认后，由 Phase 0 contract migration 删除旧表和外键。
- IR 作为普通题目方向由现有 challenge category 或后续 QuestionPool tag 表达，不建立 IR 专属实体。

## Task 1: 建立遗留面清单和失败测试

**Files:**
- Create: `src/GZCTF.Test/UnitTests/Phase/LegacySurfaceRemovalTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/LegacyTrainingMigrationTests.cs`
- Create: `scripts/migrations/phase-00-legacy-data-audit.sql`
- Modify: `docs/platform-commercialization-audit-progress.md`

- [x] **Step 1: 写运行时遗留类型失败测试**

测试通过反射和 Controller route 扫描锁定必须删除的类型与路由：

```csharp
public class LegacySurfaceRemovalTests
{
    private static readonly string[] RemovedTypeNames =
    [
        "IRCheckpoint", "IRInstance", "ScenarioInstance", "ScenarioTimelineEntry",
        "TrainingDirection", "TrainingModule", "TrainingModuleVisibility",
        "TrainingModuleChallenge", "TrainingModuleProgress"
    ];

    [Fact]
    public void RuntimeAssembly_DoesNotContainRemovedLegacyTypes()
    {
        var names = typeof(Program).Assembly.GetTypes().Select(type => type.Name).ToHashSet();
        Assert.Empty(RemovedTypeNames.Where(names.Contains));
    }

    [Fact]
    public void Controllers_DoNotExposeLegacyTrainingRoutes()
    {
        var routes = typeof(Program).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetCustomAttributes<RouteAttribute>().Select(route => route.Template))
            .Where(template => template is not null)
            .ToArray();

        Assert.DoesNotContain("api/training", routes);
        Assert.DoesNotContain("api/admin/training", routes);
        Assert.Contains("api/training/courses", routes);
        Assert.Contains("api/admin/training/courses", routes);
    }
}
```

- [x] **Step 2: 运行测试确认当前失败**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~LegacySurfaceRemovalTests
```

Expected: FAIL，失败列表包含 IR、Scenario 和旧 Training 类型。

- [x] **Step 3: 写数据库审计 SQL**

`phase-00-legacy-data-audit.sql` 必须输出旧表行数、孤儿外键、可见性冲突和未绑定环境模板：

```sql
SELECT 'IRCheckpoints' AS table_name, count(*) AS row_count FROM "IRCheckpoints"
UNION ALL SELECT 'IRInstances', count(*) FROM "IRInstances"
UNION ALL SELECT 'ScenarioInstances', count(*) FROM "ScenarioInstances"
UNION ALL SELECT 'TrainingDirections', count(*) FROM "TrainingDirections"
UNION ALL SELECT 'TrainingModules', count(*) FROM "TrainingModules";

WITH RECURSIVE module_tree AS (
    SELECT m."Id" AS root_id, m."Id" AS module_id
    FROM "TrainingModules" m WHERE m."ParentId" IS NULL
    UNION ALL
    SELECT tree.root_id, child."Id"
    FROM module_tree tree
    JOIN "TrainingModules" child ON child."ParentId" = tree.module_id
), module_visibility AS (
    SELECT tree.root_id,
           tree.module_id,
           coalesce(string_agg(
               concat(v."VisibilityType", ':', coalesce(v."GroupId"::text, 'all')),
               ',' ORDER BY v."VisibilityType", v."GroupId"
           ), '') AS signature
    FROM module_tree tree
    LEFT JOIN "TrainingModuleVisibilities" v ON v."ModuleId" = tree.module_id
    GROUP BY tree.root_id, tree.module_id
), visibility_sets AS (
    SELECT root_id, count(DISTINCT signature) AS set_count
    FROM module_visibility
    GROUP BY root_id
)
SELECT root_id FROM visibility_sets WHERE set_count > 1;

SELECT m."Id", m."Title", m."EnvironmentTemplateId"
FROM "TrainingModules" m
WHERE m."EnvironmentTemplateId" IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM "TrainingModuleChallenges" link
      JOIN "ExerciseChallenges" challenge ON challenge."Id" = link."ExerciseChallengeId"
      WHERE link."ModuleId" = m."Id"
        AND challenge."ImageTemplateId" = m."EnvironmentTemplateId"
  );
```

- [x] **Step 4: 提交遗留基线测试**

```powershell
git add src/GZCTF.Test/UnitTests/Phase/LegacySurfaceRemovalTests.cs src/GZCTF.Integration.Test/Tests/Database/LegacyTrainingMigrationTests.cs scripts/migrations/phase-00-legacy-data-audit.sql docs/platform-commercialization-audit-progress.md
git commit -m "test: define phase zero legacy removal gates"
```

## Task 2: 原子完成旧培训迁移和后端 contract 切换

**Files:**
- Create: `src/GZCTF/Migrations/20260710100000_RemoveLegacyIrScenarioTraining.cs`
- Create: `src/GZCTF/Migrations/20260710100000_RemoveLegacyIrScenarioTraining.Designer.cs`
- Modify: `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `src/GZCTF.Integration.Test/Tests/Database/LegacyTrainingMigrationTests.cs`
- Delete: `src/GZCTF/Models/Data/IREntities.cs`
- Delete: `src/GZCTF/Models/Data/ScenarioEntities.cs`
- Delete: `src/GZCTF/Models/Data/TimeSlot.cs`
- Delete: `src/GZCTF/Models/Data/ScoringRule.cs`
- Modify: `src/GZCTF/Models/Data/Training.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Delete: `src/GZCTF/Controllers/TrainingController.cs`
- Delete: `src/GZCTF/Controllers/TrainingAdminController.cs`
- Delete: `src/GZCTF/Models/Request/Training/TrainingModels.cs`
- Modify: `src/GZCTF/Controllers/TrainingCourseController.cs`
- Modify: `src/GZCTF/Models/Request/Training/CourseModels.cs`
- Modify: `src/GZCTF/Models/Data/ImageTemplate.cs`
- Modify: `src/GZCTF/Models/Data/Challenge.cs`
- Modify: `src/GZCTF/Utils/JsonSerializerContext.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`

- [x] **Step 1: 在 PostgreSQL integration test 中播种完整旧培训子树**

测试数据必须覆盖父子章节、组可见性、实践题提交、同一用户两次理论作答和部分文章进度：

integration fixture 先把 Testcontainers PostgreSQL migrate 到 Phase 0 前一 migration，再用参数化 SQL 插入旧表；测试项目可以定义私有 seed record，但生产程序集不得为测试保留旧 entity。

```csharp
[Fact]
public async Task ContractMigration_PreservesLegacyTrainingFactsAndDropsLegacyTables()
{
    await SeedLegacyTrainingTreeAsync();
    await ApplyPhaseZeroMigrationAsync();

    await using var context = CreateContext();
    var course = await context.TrainingCourses
        .Include(item => item.Chapters).ThenInclude(chapter => chapter.Challenges)
        .Include(item => item.Enrollments)
        .SingleAsync();

    Assert.Equal(2, course.Chapters.Count);
    Assert.False(course.Chapters.Single(item => item.Title == "Child").CompletionPolicy.RequireContentRead);
    Assert.Single(course.Enrollments, item => item.Status == TrainingCourseEnrollmentStatus.Approved);
    Assert.Single(await context.TrainingCourseSubmissions.ToListAsync());
    var sheets = await context.TrainingCourseChapterTheorySheets
        .OrderBy(item => item.AttemptNumber)
        .ToListAsync();
    Assert.Equal([1, 2], sheets.Select(item => item.AttemptNumber));
    Assert.Equal(37, await context.TrainingChapterProgresses
        .Where(item => item.UserId == seed.UserId)
        .Select(item => item.ReadPercent)
        .SingleAsync());
    Assert.False(await TableExistsAsync("TrainingModules"));
    Assert.False(await TableExistsAsync("IRInstances"));
    Assert.False(await TableExistsAsync("ScenarioInstances"));
}
```

- [x] **Step 2: 运行 integration test 确认缺少 migration**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~LegacyTrainingMigrationTests
```

Expected: FAIL，旧表仍存在且目标课程数据为空。

- [x] **Step 3: 先把运行时代码切到唯一目标模型**

从 `Training.cs` 删除旧实体区并删除 IR/Scenario 文件、旧 Controller、DTO、DbSet 和 ModelBuilder 配置。目标模型同时补齐迁移所需的非丢失字段：

```csharp
public class TrainingCourseChapterTheoryPaper
{
    public bool AllowRetake { get; set; } = true;
    public bool ShowCorrectAnswerAfterSubmit { get; set; } = true;
}

[Index(nameof(UserId), nameof(ChapterId), nameof(AttemptNumber), IsUnique = true)]
public class TrainingCourseChapterTheorySheet
{
    public int AttemptNumber { get; set; } = 1;
}

public class TrainingChapterProgress
{
    public int ReadPercent { get; set; }
}

public class TrainingCourseChapter
{
    public TrainingChapterCompletionPolicy CompletionPolicy { get; set; } = new();
}

public class TrainingChapterCompletionPolicy
{
    public bool RequireContentRead { get; set; } = true;
    public bool RequireAllRequiredChallenges { get; set; } = true;
    public int RequiredChallengeCount { get; set; }
    public int TheoryPassRate { get; set; } = 80;
}
```

`TrainingCourseController` 查询当前试卷时选择最新 attempt；已提交且试卷允许重做时创建 `AttemptNumber + 1`，否则返回最新记录。删除当前 `(UserId, ChapterId)` 唯一索引，禁止通过覆盖旧 sheet 实现重做。章节完成判定统一读取 CompletionPolicy：按策略校验阅读百分比、全部必做题或要求数量以及理论通过率，禁止继续硬编码“全部必做题 + 已发布试卷”。

- [x] **Step 4: 生成并完善 contract migration**

```powershell
dotnet ef migrations add RemoveLegacyIrScenarioTraining --project src/GZCTF/GZCTF.csproj --startup-project src/GZCTF/GZCTF.csproj
```

修改目标模型和删除旧 runtime 类型后再 scaffold migration，确保 migration、当前 EF model 和 snapshot 在同一个提交内一致。在生成的 `Up` 中先创建临时 ID 映射表，再依次迁移 course、chapter、binding、submission、paper、sheet、answer 和 progress，最后删除临时映射表和旧业务表。整个 `Up` 使用 PostgreSQL migration transaction；禁止调用外部服务。

关键映射表结构固定为：

```sql
CREATE TEMP TABLE phase00_course_map (
    old_root_module_id integer PRIMARY KEY,
    new_course_id integer NOT NULL UNIQUE
) ON COMMIT DROP;

CREATE TEMP TABLE phase00_chapter_map (
    old_module_id integer PRIMARY KEY,
    new_course_id integer NOT NULL,
    new_chapter_id integer NOT NULL UNIQUE
) ON COMMIT DROP;
```

迁移完成后在删除旧表前执行以下不变量，任一不满足必须让 migration 失败并回滚：

```sql
DO $$
BEGIN
    IF (SELECT count(*) FROM phase00_chapter_map) <> (SELECT count(*) FROM "TrainingModules") THEN
        RAISE EXCEPTION 'Phase 0 chapter count mismatch';
    END IF;
    IF (SELECT count(*) FROM "TrainingCourseSubmissions")
       <> (SELECT count(*) FROM "TrainingCtfSubmissions") THEN
        RAISE EXCEPTION 'Phase 0 submission count mismatch';
    END IF;
    IF (SELECT count(*) FROM "TrainingCourseChapterTheorySheets")
       <> (SELECT count(*) FROM "TheoryTrainingSessions") THEN
        RAISE EXCEPTION 'Phase 0 theory session count mismatch';
    END IF;
    IF (SELECT count(*) FROM "TrainingCourseChapterTheoryAnswers")
       <> (SELECT count(*) FROM "TheoryTrainingSessionQuestions") THEN
        RAISE EXCEPTION 'Phase 0 theory answer count mismatch';
    END IF;
END $$;
```

- [x] **Step 5: 清理仍有效字段中的遗留语义**

保留通用字段但删除 IR 专属注释：`ImageTemplate.ContainsMalware` 定义为镜像安全分类，`Challenge.OsType` 定义为目标环境操作系统提示。不得改变字段存储语义。

- [x] **Step 6: 运行 migration、后端和边界测试**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~LegacyTrainingMigrationTests
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~LegacySurfaceRemovalTests|FullyQualifiedName~TrainingCourseAccessPolicyTests"
```

Expected: PASS，目标事实数量与旧数据一致，全部理论尝试仍可查询，旧表和旧 runtime 类型不存在。

- [x] **Step 7: 提交原子 contract 切换**

```powershell
git add src/GZCTF src/GZCTF.Test src/GZCTF.Integration.Test
git commit -m "refactor: migrate and remove legacy training runtime"
```

## Task 3: 删除旧前端和失效 e2e

**Files:**
- Modify: `src/GZCTF/ClientApp/src/utils/TrainingApi.ts`
- Create: `src/GZCTF/ClientApp/src/utils/StudentGroupApi.ts`
- Delete: `src/GZCTF/ClientApp/src/pages/admin/training.tsx`
- Delete: `src/GZCTF/ClientApp/src/pages/training/ctf/modules/[moduleId]/challenges.tsx`
- Delete: `src/GZCTF/ClientApp/src/pages/training/theory/modules/[moduleId]/session.tsx`
- Modify: `src/GZCTF/ClientApp/src/components/admin/UserEditModal.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/Users.tsx`
- Modify: `src/GZCTF/ClientApp/src/components/training/TrainingChapterEditor.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/training/courses/[courseId]/chapters/[chapterId]/theory.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/training/courses/[courseId]/chapters/[chapterId]/theory-edit.tsx`
- Delete: `tests/e2e/ir-challenge.spec.ts`
- Delete: `tests/e2e/scenario-create.spec.ts`
- Delete: `tests/e2e/scenario-play.spec.ts`
- Delete: `tests/e2e/topology-editor.spec.ts`

- [x] **Step 1: 删除旧 TrainingApi 类型和方法**

删除所有请求 `/api/training/catalog`、`/api/training/overview`、`/api/training/modules`、`/api/training/ctf/modules` 和 `/api/training/theory/modules` 的 DTO 与方法。保留 `/api/training/courses` 和 `/api/admin/training/courses` 方法。

- [x] **Step 2: 删除文件路由页面和废弃 e2e**

删除旧模块页面、混合旧模块管理与学员组管理的管理员页面及四个废弃用例，不添加重定向页面。通用学员组 API 独立拆分，旧方向、模块、可见性、统计和理论 session 逻辑不迁移。TeamLab 当前有效测试由 `src/GZCTF.Test/UnitTests/TeamLab` 和 Phase 3 新 e2e 承担。

- [x] **Step 3: 校验前端类型和 locale**

```powershell
pnpm --dir src/GZCTF/ClientApp validate:locales
pnpm --dir src/GZCTF/ClientApp check
```

Expected: 两条命令退出码均为 0。

- [x] **Step 4: 提交前端清理**

```powershell
git add src/GZCTF/ClientApp tests/e2e
git commit -m "refactor: remove legacy training and scenario ui"
```

## Task 4: 冻结术语并清理活动文档

**Files:**
- Modify: `docs/commercialization/domain-glossary.md`
- Modify: `docs/commercialization/module-boundary-map.md`
- Modify: `docs/commercialization/phase-00-baseline-cleanup.md`
- Modify: `docs/platform-commercialization-master-plan.md`
- Modify: `docs/platform-commercialization-audit-progress.md`
- Modify: `src/GZCTF/Models/Internal/Configs.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Modify: `src/GZCTF.Test/UnitTests/Phase/LegacySurfaceRemovalTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/PublicUdpGatewayProviderTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabCommandBuilderTests.cs`

- [x] **Step 1: 扫描乱码和历史阶段文案**

```powershell
$badEncoding = ([char]0x951f) + '|' + ([char]0x9225) + '|' + ([char]0xfffd)
rg -n -g '!docs/archive/**' -g '!src/GZCTF/Migrations/**' -g '!src/GZCTF/wwwroot/**' $badEncoding src docs tests
rg -n -g '!docs/archive/**' -g '!src/GZCTF/Migrations/**' -g '!src/GZCTF/wwwroot/**' `
  "Phase [0-9]|dry-run|IRChallenge|ScenarioInstance" src docs tests
```

逐条判断命中：运行时乱码直接修正；当前总纲中的 Phase 标题保留；历史过程说明移入 `docs/archive/pre-commercial-reset-20260709`。

- [x] **Step 2: 检查禁用术语**

```powershell
$legacySurface = 'IRChallenge|IRCheckpoint|IRInstance|ScenarioInstance|ScenarioTimelineEntry|TrainingDirection|TrainingModule|TrainingCtfSubmission|TheoryTrainingPlan|TheoryTrainingSession|Training(Admin)?Controller|api/training/(catalog|overview|modules|ctf/modules|theory/modules)'
rg -ni -g '!src/GZCTF/Migrations/**' -g '!src/GZCTF/wwwroot/**' -g '!artifacts/**' `
  $legacySurface `
  src/GZCTF src/GZCTF.Agent tests/e2e
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~LegacySurfaceRemovalTests
```

Expected: 文本扫描无产品运行代码或活动 e2e 命中，负向删除门禁通过。大小写不敏感扫描覆盖类型、camelCase DTO/UI 字段、旧控制器名和旧 API 子路由；反射门禁精确覆盖 `Stage` 等通用词命名的已删除类型和控制器根路由。历史 migration、Phase 0 迁移验证、负向删除门禁、禁用术语登记和审计记录是升级、迁移与防回流证据，不参与运行时门禁，也不得提供可执行兼容面。

- [x] **Step 3: 提交术语冻结**

```powershell
git add docs src tests
git commit -m "docs: freeze commercialization domain terminology"
```

## Task 5: Phase 0 总体验收

**Files:**
- Modify: `docs/platform-commercialization-audit-progress.md`

- [ ] **Step 1: 验证 EF 模型和 migration**

```powershell
dotnet ef migrations script --idempotent --project src/GZCTF/GZCTF.csproj --startup-project src/GZCTF/GZCTF.csproj --output artifacts/phase-00-idempotent.sql
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~LegacyTrainingMigrationTests
```

Expected: migration script 生成成功，integration test PASS。

- [ ] **Step 2: 运行后端、前端和 e2e 静态门禁**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj
pnpm --dir src/GZCTF/ClientApp build
git diff --check
```

Expected: 全部退出码为 0。

- [ ] **Step 3: 生产切换前执行数据保护**

```bash
pg_dump --format=custom --file=phase00-before-cleanup.dump "$ConnectionStrings__Database"
psql "$ConnectionStrings__Database" --file scripts/migrations/phase-00-legacy-data-audit.sql
```

必须保存备份文件 SHA-256、审计 SQL 输出、migration 版本和恢复演练结果。任何前置约束不满足时禁止应用 contract migration。

- [ ] **Step 4: 更新进度并提交阶段验收记录**

```powershell
git add docs/platform-commercialization-audit-progress.md
git commit -m "docs: record phase zero acceptance"
```

## Phase 0 退出门槛

- 当前 runtime assembly 不含 IR/Scenario/旧 Training 类型。
- 当前数据库模型快照不含旧表，历史 migration 保持可从空库执行。
- 旧培训课程、章节、实践提交、理论作答和进度通过数量及引用校验。
- 旧 API、前端路由和 e2e 已删除，没有长期重定向或兼容 DTO。
- `domain-glossary.md` 已成为后续 Phase 的命名约束。
- 全量单元测试、PostgreSQL migration integration test、前端 build 和 `git diff --check` 通过。

## 切换与回滚

1. 切换前进入维护模式，阻止旧培训提交和课程编辑，执行数据库 custom-format backup 与旧表数据导出。
2. 应用包含 contract migration 的新制品，运行 migration 校验、课程抽样和登录后课程访问验收，再解除维护模式。
3. migration 事务失败时数据库自动回滚，继续运行旧制品并修复前置数据。
4. contract migration 成功但应用验收失败时，停止新制品，恢复切换前数据库 backup，再部署旧制品；不能只回滚应用而保留已删旧表的数据库。
5. 恢复演练必须验证旧培训提交数、理论作答数和课程访问结果，不以“服务能启动”作为恢复成功标准。
