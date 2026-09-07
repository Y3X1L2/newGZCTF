\set ON_ERROR_STOP on

CREATE TEMP TABLE phase4_seed_config (
    participation_count integer NOT NULL,
    submission_count integer NOT NULL,
    training_count integer NOT NULL,
    theory_count integer NOT NULL,
    queue_count integer NOT NULL,
    log_count integer NOT NULL,
    flow_count integer NOT NULL
);

INSERT INTO phase4_seed_config
SELECT CASE WHEN :'profile' = 'commercial' THEN 500 ELSE 500 END,
       CASE WHEN :'profile' = 'commercial' THEN 3000000 ELSE 100000 END,
       CASE WHEN :'profile' = 'commercial' THEN 200000 ELSE 20000 END,
       CASE WHEN :'profile' = 'commercial' THEN 50000 ELSE 5000 END,
       CASE WHEN :'profile' = 'commercial' THEN 500000 ELSE 20000 END,
       CASE WHEN :'profile' = 'commercial' THEN 5000000 ELSE 100000 END,
       CASE WHEN :'profile' = 'commercial' THEN 10000000 ELSE 200000 END;

SET session_replication_role = replica;
TRUNCATE TABLE
    "Submissions", "Participations", "TrainingCourseProgresses",
    "TheoryQuestionTagBindings", "TheoryQuestionTags", "TheoryQuestionBankItems",
    "DeploymentQueueTickets", "Logs", "TeamLabTrafficFlows" CASCADE;

INSERT INTO "Participations" ("Id", "Status", "Token", "GameId", "TeamId")
SELECT value, value % 3, 'benchmark-' || value, 1 + value % 5, value
FROM generate_series(1, (SELECT participation_count FROM phase4_seed_config)) AS value;

INSERT INTO "Submissions"
    ("Id", "Answer", "Status", "SubmitTimeUtc", "SubmissionType", "AttemptNumber",
     "Score", "TeamId", "ParticipationId", "GameId", "ChallengeId")
SELECT value,
       '',
       CASE WHEN value % 20 = 0 THEN 'FlagSubmitted' ELSE 'WrongAnswer' END,
       CURRENT_TIMESTAMP - make_interval(secs => value % 2592000),
       'Flag',
       1,
       CASE WHEN value % 7 = 0 THEN 100 ELSE 0 END,
       1 + value % (SELECT participation_count FROM phase4_seed_config),
       1 + value % (SELECT participation_count FROM phase4_seed_config),
       1 + value % 5,
       1 + value % 150
FROM generate_series(1, (SELECT submission_count FROM phase4_seed_config)) AS value;

INSERT INTO "TrainingCourseProgresses"
    ("CourseId", "UserId", "Status", "CompletedChapterCount", "TotalChapterCount",
     "ChallengeSolvedCount", "ChallengeTotalCount", "UpdatedAt")
SELECT 1 + value % 10,
       ('00000000-0000-4000-8000-' || lpad(value::text, 12, '0'))::uuid,
       CASE value % 3 WHEN 0 THEN 'NotStarted' WHEN 1 THEN 'InProgress' ELSE 'Completed' END,
       value % 20, 20, value % 30, 30,
       CURRENT_TIMESTAMP - make_interval(secs => value % 2592000)
FROM generate_series(1, (SELECT training_count FROM phase4_seed_config)) AS value;

INSERT INTO "TheoryQuestionBankItems"
    ("Id", "Type", "BankName", "Title", "Content", "Options", "AnswerIndexes", "CreatedAt", "UpdatedAt")
SELECT value,
       CASE value % 3 WHEN 0 THEN 'SingleChoice' WHEN 1 THEN 'MultipleChoice' ELSE 'TrueFalse' END,
       'bank-' || value % 100,
       'Synthetic network question ' || value,
       '', '[]', '[]', CURRENT_TIMESTAMP,
       CURRENT_TIMESTAMP - make_interval(secs => value % 2592000)
FROM generate_series(1, (SELECT theory_count FROM phase4_seed_config)) AS value;

INSERT INTO "TheoryQuestionTags" ("Id", "DisplayName", "NormalizedName", "CreatedAt")
SELECT value, 'tag-' || value, 'TAG-' || value, CURRENT_TIMESTAMP
FROM generate_series(1, (SELECT theory_count FROM phase4_seed_config)) AS value;
INSERT INTO "TheoryQuestionTagBindings" ("QuestionId", "TagId")
SELECT value, value
FROM generate_series(1, (SELECT theory_count FROM phase4_seed_config)) AS value;

INSERT INTO "DeploymentQueueTickets"
    ("Id", "Kind", "Status", "DockerSlots", "VmSlots", "ActiveIdentity", "TenantKey", "FairnessKey",
     "SubjectConcurrencyKey", "NotBeforeAt", "CreatedAt", "CompletedAt")
SELECT ('10000000-0000-4000-8000-' || lpad(value::text, 12, '0'))::uuid,
       1 + value % 4,
       value % 6,
       value % 3,
       value % 2,
       'benchmark-ticket-' || value,
       'benchmark-tenant-' || value % 100,
       'benchmark-fairness-' || value % 100,
       'benchmark-subject-' || value,
       CASE value % 5
           WHEN 0 THEN CURRENT_TIMESTAMP + make_interval(secs => value % 3600)
           WHEN 1 THEN CURRENT_TIMESTAMP - make_interval(secs => value % 3600)
       END,
       CURRENT_TIMESTAMP - make_interval(secs => value % 15552000),
       CASE WHEN value % 6 >= 3 THEN CURRENT_TIMESTAMP - make_interval(secs => value % 15552000) END
FROM generate_series(1, (SELECT queue_count FROM phase4_seed_config)) AS value;

INSERT INTO "Logs" ("TimeUtc", "Id", "Level", "Logger", "Message")
SELECT date_trunc('month', CURRENT_TIMESTAMP) + make_interval(secs => value % 2500000),
       value,
       CASE value % 4 WHEN 0 THEN 'Error' WHEN 1 THEN 'Warning' ELSE 'Information' END,
       'Synthetic.Benchmark.' || value % 20,
       ''
FROM generate_series(1, (SELECT log_count FROM phase4_seed_config)) AS value;

INSERT INTO "TeamLabTrafficFlows"
    ("CapturedAt", "Id", "RuntimeId", "Generation", "SourceCursor", "SourceIp", "SourcePrefix",
     "SourcePort", "DestinationIp", "DestinationPrefix", "DestinationPort", "Protocol",
     "Bytes", "Packets", "FirstSeenAt", "LastSeenAt", "Fingerprint")
SELECT date_trunc('day', CURRENT_TIMESTAMP) + make_interval(secs => value % 86400),
       value,
       1 + value % 100,
       1,
       value,
       '10.10.' || value % 250 || '.' || (1 + value % 250),
       '10.10.' || value % 250 || '.0/24',
       1024 + value % 50000,
       '192.168.' || value % 250 || '.' || (1 + value % 250),
       '192.168.' || value % 250 || '.0/24',
       CASE WHEN value % 2 = 0 THEN 80 ELSE 443 END,
       'TCP',
       64 + value % 1500,
       1,
       date_trunc('day', CURRENT_TIMESTAMP) + make_interval(secs => value % 86400),
       date_trunc('day', CURRENT_TIMESTAMP) + make_interval(secs => value % 86400),
       digest(value::text, 'sha256')
FROM generate_series(1, (SELECT flow_count FROM phase4_seed_config)) AS value;

SET session_replication_role = origin;
ANALYZE "Participations";
ANALYZE "Submissions";
ANALYZE "TrainingCourseProgresses";
ANALYZE "TheoryQuestionTags";
ANALYZE "TheoryQuestionTagBindings";
ANALYZE "DeploymentQueueTickets";
ANALYZE "Logs";
ANALYZE "TeamLabTrafficFlows";
