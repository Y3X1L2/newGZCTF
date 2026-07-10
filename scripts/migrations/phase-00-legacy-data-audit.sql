\set ON_ERROR_STOP on

SELECT current_database() AS database_name,
       current_timestamp AS audited_at,
       version() AS postgres_version;

SELECT "MigrationId" AS latest_migration
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC
LIMIT 1;

SELECT 'IRCheckpoints' AS table_name, count(*) AS row_count FROM "IRCheckpoints"
UNION ALL SELECT 'IRInstances', count(*) FROM "IRInstances"
UNION ALL SELECT 'Stages', count(*) FROM "Stages"
UNION ALL SELECT 'ScenarioInstances', count(*) FROM "ScenarioInstances"
UNION ALL SELECT 'ScoringRules', count(*) FROM "ScoringRules"
UNION ALL SELECT 'TimeSlots', count(*) FROM "TimeSlots"
UNION ALL SELECT 'TrainingDirections', count(*) FROM "TrainingDirections"
UNION ALL SELECT 'TrainingModules', count(*) FROM "TrainingModules"
UNION ALL SELECT 'TrainingModuleVisibilities', count(*) FROM "TrainingModuleVisibilities"
UNION ALL SELECT 'TrainingModuleChallenges', count(*) FROM "TrainingModuleChallenges"
UNION ALL SELECT 'TrainingCtfSubmissions', count(*) FROM "TrainingCtfSubmissions"
UNION ALL SELECT 'TheoryTrainingPlans', count(*) FROM "TheoryTrainingPlans"
UNION ALL SELECT 'TheoryTrainingPlanQuestions', count(*) FROM "TheoryTrainingPlanQuestions"
UNION ALL SELECT 'TheoryTrainingSessions', count(*) FROM "TheoryTrainingSessions"
UNION ALL SELECT 'TheoryTrainingSessionQuestions', count(*) FROM "TheoryTrainingSessionQuestions"
UNION ALL SELECT 'TrainingArticleProgresses', count(*) FROM "TrainingArticleProgresses"
UNION ALL SELECT 'TrainingModuleProgresses', count(*) FROM "TrainingModuleProgresses"
ORDER BY table_name;

WITH RECURSIVE module_tree AS (
    SELECT module."Id" AS root_id, module."Id" AS module_id
    FROM "TrainingModules" module
    WHERE module."ParentId" IS NULL

    UNION ALL

    SELECT tree.root_id, child."Id"
    FROM module_tree tree
    JOIN "TrainingModules" child ON child."ParentId" = tree.module_id
), module_visibility AS (
    SELECT tree.root_id,
           tree.module_id,
           coalesce(string_agg(
               concat(visibility."VisibilityType", ':', coalesce(visibility."GroupId"::text, 'all')),
               ',' ORDER BY visibility."VisibilityType", visibility."GroupId"
           ), '') AS signature
    FROM module_tree tree
    LEFT JOIN "TrainingModuleVisibilities" visibility ON visibility."ModuleId" = tree.module_id
    GROUP BY tree.root_id, tree.module_id
), visibility_sets AS (
    SELECT root_id, count(DISTINCT signature) AS set_count
    FROM module_visibility
    GROUP BY root_id
)
SELECT root_id, set_count
FROM visibility_sets
WHERE set_count > 1
ORDER BY root_id;

SELECT module."Id", module."Title", module."EnvironmentTemplateId"
FROM "TrainingModules" module
WHERE module."EnvironmentTemplateId" IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM "TrainingModuleChallenges" link
      JOIN "ExerciseChallenges" challenge ON challenge."Id" = link."ExerciseChallengeId"
      WHERE link."ModuleId" = module."Id"
        AND challenge."ImageTemplateId" = module."EnvironmentTemplateId"
  )
ORDER BY module."Id";

SELECT coalesce(nullif(btrim(root."Slug"), ''), concat('course-', root."Id")) AS target_slug,
       array_agg(root."Id" ORDER BY root."Id") AS module_ids
FROM "TrainingModules" root
WHERE root."ParentId" IS NULL
GROUP BY target_slug
HAVING count(*) > 1
ORDER BY target_slug;

SELECT session_question."Id" AS session_question_id,
       session_question."SessionId",
       session_question."SourceQuestionId"
FROM "TheoryTrainingSessionQuestions" session_question
JOIN "TheoryTrainingSessions" session ON session."Id" = session_question."SessionId"
LEFT JOIN "TheoryTrainingPlanQuestions" plan_question
       ON plan_question."PlanId" = session."PlanId"
      AND plan_question."SourceQuestionId" = session_question."SourceQuestionId"
WHERE session_question."SourceQuestionId" IS NULL
   OR plan_question."SourceQuestionId" IS NULL
ORDER BY session_question."Id";

SELECT 'TrainingModules.DirectionId' AS relation_name, count(*) AS orphan_count
FROM "TrainingModules" child
LEFT JOIN "TrainingDirections" parent ON parent."Id" = child."DirectionId"
WHERE parent."Id" IS NULL
UNION ALL
SELECT 'TrainingModules.ParentId', count(*)
FROM "TrainingModules" child
LEFT JOIN "TrainingModules" parent ON parent."Id" = child."ParentId"
WHERE child."ParentId" IS NOT NULL AND parent."Id" IS NULL
UNION ALL
SELECT 'TrainingModuleChallenges.ModuleId', count(*)
FROM "TrainingModuleChallenges" child
LEFT JOIN "TrainingModules" parent ON parent."Id" = child."ModuleId"
WHERE parent."Id" IS NULL
UNION ALL
SELECT 'TrainingCtfSubmissions.ModuleId', count(*)
FROM "TrainingCtfSubmissions" child
LEFT JOIN "TrainingModules" parent ON parent."Id" = child."ModuleId"
WHERE parent."Id" IS NULL
UNION ALL
SELECT 'TheoryTrainingSessions.PlanId', count(*)
FROM "TheoryTrainingSessions" child
LEFT JOIN "TheoryTrainingPlans" parent ON parent."Id" = child."PlanId"
WHERE parent."Id" IS NULL
ORDER BY relation_name;
