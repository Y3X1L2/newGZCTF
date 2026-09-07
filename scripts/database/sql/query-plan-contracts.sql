\set ON_ERROR_STOP on

\echo __PLAN__:submissions
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
SELECT "Id", "SubmitTimeUtc", "Status", "TeamId", "ChallengeId"
FROM "Submissions"
WHERE "GameId" = 1 AND ("SubmitTimeUtc", "Id") < (CURRENT_TIMESTAMP, 2147483647)
ORDER BY "SubmitTimeUtc" DESC, "Id" DESC
LIMIT 100;

\echo __PLAN__:participations
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
SELECT "Id", "TeamId", "Status", "DivisionId"
FROM "Participations"
WHERE "GameId" = 1 AND "Status" = 1
ORDER BY "TeamId"
LIMIT 100;

\echo __PLAN__:training-progress
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
SELECT "UserId", "Status", "UpdatedAt"
FROM "TrainingCourseProgresses"
WHERE "CourseId" = 1 AND "Status" = 'InProgress'
ORDER BY "UpdatedAt" DESC, "UserId"
LIMIT 100;

\echo __PLAN__:theory-tags
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
SELECT binding."QuestionId"
FROM "TheoryQuestionTags" tag
JOIN "TheoryQuestionTagBindings" binding ON binding."TagId" = tag."Id"
WHERE tag."NormalizedName" = 'TAG-42'
ORDER BY binding."QuestionId"
LIMIT 100;

\echo __PLAN__:deployment-queue
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
SELECT "Id", "Kind", "Status", "CreatedAt"
FROM "DeploymentQueueTickets"
WHERE "Status" = 0 AND "NotBeforeAt" IS NOT NULL AND "NotBeforeAt" <= CURRENT_TIMESTAMP
ORDER BY "NotBeforeAt", "CreatedAt", "Id"
LIMIT 100;

\echo __PLAN__:teamlab-flow
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
SELECT "Id", "CapturedAt", "SourceIp", "DestinationIp", "Protocol", "Bytes"
FROM "TeamLabTrafficFlows"
WHERE "RuntimeId" = 1 AND "Generation" = 1
  AND "CapturedAt" >= date_trunc('day', CURRENT_TIMESTAMP)
  AND "CapturedAt" < date_trunc('day', CURRENT_TIMESTAMP) + interval '1 day'
ORDER BY "CapturedAt" DESC, "Id" DESC
LIMIT 100;

\echo __PLAN__:logs
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
SELECT "Id", "TimeUtc", "Level", "Logger"
FROM "Logs"
WHERE "Level" = 'Error'
  AND "TimeUtc" >= date_trunc('month', CURRENT_TIMESTAMP)
  AND "TimeUtc" < date_trunc('month', CURRENT_TIMESTAMP) + interval '1 month'
ORDER BY "TimeUtc" DESC, "Id" DESC
LIMIT 100;
