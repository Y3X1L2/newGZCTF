using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

/// <inheritdoc />
public partial class RemoveLegacyIrScenarioTraining : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TrainingCourseChapterTheorySheets_UserId_ChapterId",
            table: "TrainingCourseChapterTheorySheets");

        migrationBuilder.AddColumn<int>(
            name: "AttemptNumber",
            table: "TrainingCourseChapterTheorySheets",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<bool>(
            name: "AllowRetake",
            table: "TrainingCourseChapterTheoryPapers",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowCorrectAnswerAfterSubmit",
            table: "TrainingCourseChapterTheoryPapers",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<string>(
            name: "CompletionPolicy",
            table: "TrainingCourseChapters",
            type: "text",
            nullable: false,
            defaultValue: "{\"RequireContentRead\":true,\"RequireAllRequiredChallenges\":true,\"RequiredChallengeCount\":0,\"TheoryPassRate\":80}");

        migrationBuilder.AddColumn<int>(
            name: "ReadPercent",
            table: "TrainingChapterProgresses",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_TrainingCourseChapterTheorySheets_UserId_ChapterId_AttemptN~",
            table: "TrainingCourseChapterTheorySheets",
            columns: ["UserId", "ChapterId", "AttemptNumber"],
            unique: true);

        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
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
                        LEFT JOIN "TrainingModuleVisibilities" visibility
                               ON visibility."ModuleId" = tree.module_id
                        GROUP BY tree.root_id, tree.module_id
                    )
                    SELECT 1
                    FROM module_visibility
                    GROUP BY root_id
                    HAVING count(DISTINCT signature) > 1
                ) THEN
                    RAISE EXCEPTION 'Phase 0 visibility mismatch inside a legacy module tree';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM "TrainingModules" module
                    WHERE module."EnvironmentTemplateId" IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1
                          FROM "TrainingModuleChallenges" link
                          JOIN "ExerciseChallenges" challenge
                            ON challenge."Id" = link."ExerciseChallengeId"
                          WHERE link."ModuleId" = module."Id"
                            AND challenge."ImageTemplateId" = module."EnvironmentTemplateId"
                      )
                ) THEN
                    RAISE EXCEPTION 'Phase 0 found an unbound legacy environment template';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM "TheoryTrainingSessionQuestions" session_question
                    JOIN "TheoryTrainingSessions" session
                      ON session."Id" = session_question."SessionId"
                    LEFT JOIN "TheoryTrainingPlanQuestions" plan_question
                      ON plan_question."PlanId" = session."PlanId"
                     AND plan_question."SourceQuestionId" = session_question."SourceQuestionId"
                    WHERE session_question."SourceQuestionId" IS NULL
                       OR plan_question."SourceQuestionId" IS NULL
                ) THEN
                    RAISE EXCEPTION 'Phase 0 found an unmapped legacy theory answer snapshot';
                END IF;
            END $$;

            CREATE TEMP TABLE phase00_baseline (
                course_count bigint NOT NULL,
                chapter_count bigint NOT NULL,
                chapter_challenge_count bigint NOT NULL,
                submission_count bigint NOT NULL,
                paper_count bigint NOT NULL,
                paper_question_count bigint NOT NULL,
                sheet_count bigint NOT NULL,
                answer_count bigint NOT NULL
            ) ON COMMIT DROP;

            INSERT INTO phase00_baseline
            SELECT (SELECT count(*) FROM "TrainingCourses"),
                   (SELECT count(*) FROM "TrainingCourseChapters"),
                   (SELECT count(*) FROM "TrainingCourseChapterChallenges"),
                   (SELECT count(*) FROM "TrainingCourseSubmissions"),
                   (SELECT count(*) FROM "TrainingCourseChapterTheoryPapers"),
                   (SELECT count(*) FROM "TrainingCourseChapterTheoryQuestions"),
                   (SELECT count(*) FROM "TrainingCourseChapterTheorySheets"),
                   (SELECT count(*) FROM "TrainingCourseChapterTheoryAnswers");

            CREATE TEMP TABLE phase00_module_roots (
                old_module_id integer PRIMARY KEY,
                old_root_module_id integer NOT NULL
            ) ON COMMIT DROP;

            INSERT INTO phase00_module_roots
            WITH RECURSIVE module_tree AS (
                SELECT module."Id" AS old_module_id, module."Id" AS old_root_module_id
                FROM "TrainingModules" module
                WHERE module."ParentId" IS NULL
                UNION ALL
                SELECT child."Id", tree.old_root_module_id
                FROM module_tree tree
                JOIN "TrainingModules" child ON child."ParentId" = tree.old_module_id
            )
            SELECT old_module_id, old_root_module_id
            FROM module_tree;

            DO $$
            BEGIN
                IF (SELECT count(*) FROM phase00_module_roots) <> (SELECT count(*) FROM "TrainingModules") THEN
                    RAISE EXCEPTION 'Phase 0 legacy module hierarchy is cyclic or disconnected';
                END IF;
            END $$;

            CREATE TEMP TABLE phase00_course_map (
                old_root_module_id integer PRIMARY KEY,
                new_course_id integer NOT NULL UNIQUE
            ) ON COMMIT DROP;

            INSERT INTO phase00_course_map
            SELECT roots.old_root_module_id,
                   nextval(pg_get_serial_sequence('"TrainingCourses"', 'Id'))::integer
            FROM (
                SELECT DISTINCT old_root_module_id
                FROM phase00_module_roots
                ORDER BY old_root_module_id
            ) roots;

            INSERT INTO "TrainingCourses"
                ("Id", "Title", "Slug", "Summary", "Description", "CoverFileHash", "Tags", "Status",
                 "EnrollmentPolicy", "CreatedById", "UpdatedById", "CreatedAt", "UpdatedAt", "PublishedAt", "ArchivedAt")
            SELECT course_map.new_course_id,
                   root."Title",
                   CASE
                       WHEN count(*) OVER (PARTITION BY coalesce(nullif(btrim(root."Slug"), ''), concat('course-', root."Id"))) > 1
                           THEN concat(coalesce(nullif(btrim(root."Slug"), ''), 'course'), '-', root."Id")
                       ELSE coalesce(nullif(btrim(root."Slug"), ''), concat('course-', root."Id"))
                   END,
                   root."Summary",
                   root."Summary",
                   root."CoverFileHash",
                   jsonb_build_array(concat('direction:', direction."Key"), concat('training-type:', root."Type"))::text,
                   CASE WHEN root."IsPublished" THEN 'Published' ELSE 'Draft' END,
                   CASE WHEN EXISTS (
                       SELECT 1
                       FROM phase00_module_roots tree
                       JOIN "TrainingModuleVisibilities" visibility ON visibility."ModuleId" = tree.old_module_id
                       WHERE tree.old_root_module_id = root."Id"
                         AND visibility."VisibilityType" = 'AllStudents'
                   ) THEN 'AutoApprove' ELSE 'TeacherApproval' END,
                   root."CreatedById",
                   root."UpdatedById",
                   root."CreatedAt",
                   root."UpdatedAt",
                   CASE WHEN root."IsPublished" THEN coalesce(root."PublishedAt", root."UpdatedAt") ELSE NULL END,
                   NULL
            FROM phase00_course_map course_map
            JOIN "TrainingModules" root ON root."Id" = course_map.old_root_module_id
            JOIN "TrainingDirections" direction ON direction."Id" = root."DirectionId";

            INSERT INTO "TrainingCourseTeachers"
                ("CourseId", "TeacherId", "Role", "AssignedById", "AssignedAt")
            SELECT course_map.new_course_id, root."CreatedById", 'Owner', root."CreatedById", root."CreatedAt"
            FROM phase00_course_map course_map
            JOIN "TrainingModules" root ON root."Id" = course_map.old_root_module_id
            WHERE root."CreatedById" IS NOT NULL
            ON CONFLICT ("CourseId", "TeacherId") DO NOTHING;

            CREATE TEMP TABLE phase00_chapter_map (
                old_module_id integer PRIMARY KEY,
                new_course_id integer NOT NULL,
                new_chapter_id integer NOT NULL UNIQUE
            ) ON COMMIT DROP;

            INSERT INTO phase00_chapter_map
            SELECT roots.old_module_id,
                   course_map.new_course_id,
                   nextval(pg_get_serial_sequence('"TrainingCourseChapters"', 'Id'))::integer
            FROM phase00_module_roots roots
            JOIN phase00_course_map course_map
              ON course_map.old_root_module_id = roots.old_root_module_id
            ORDER BY roots.old_module_id;

            INSERT INTO "TrainingCourseChapters"
                ("Id", "CourseId", "ParentId", "Title", "Summary", "Content", "ContentType", "CompletionPolicy",
                 "VideoProvider", "VideoUrl", "VideoFileId", "Order", "IsPublished", "CreatedById", "UpdatedById",
                 "CreatedAt", "UpdatedAt")
            SELECT chapter_map.new_chapter_id,
                   chapter_map.new_course_id,
                   parent_map.new_chapter_id,
                   module."Title",
                   module."Summary",
                   module."ArticleContent",
                   module."ArticleContentType",
                   jsonb_build_object(
                       'RequireContentRead', coalesce((module."CompletionRule"::jsonb ->> 'RequireArticleRead')::boolean, true),
                       'RequireAllRequiredChallenges', coalesce((module."CompletionRule"::jsonb ->> 'RequireAllRequiredChallenges')::boolean, true),
                       'RequiredChallengeCount', coalesce((module."CompletionRule"::jsonb ->> 'RequiredChallengeCount')::integer, 0),
                       'TheoryPassRate', coalesce((module."CompletionRule"::jsonb ->> 'TheoryPassRate')::integer, 80)
                   )::text,
                   'None',
                   NULL,
                   NULL,
                   module."Order",
                   module."IsPublished",
                   module."CreatedById",
                   module."UpdatedById",
                   module."CreatedAt",
                   module."UpdatedAt"
            FROM phase00_chapter_map chapter_map
            JOIN "TrainingModules" module ON module."Id" = chapter_map.old_module_id
            LEFT JOIN phase00_chapter_map parent_map ON parent_map.old_module_id = module."ParentId";

            INSERT INTO "TrainingCourseEnrollments"
                ("CourseId", "UserId", "Status", "ApplyReason", "ReviewComment", "ReviewedById", "RequestedAt", "ReviewedAt", "UpdatedAt")
            SELECT DISTINCT course_map.new_course_id,
                   member."StudentId",
                   'Approved',
                   '',
                   'Migrated from legacy training group visibility',
                   root."CreatedById",
                   member."JoinedAt",
                   root."UpdatedAt",
                   root."UpdatedAt"
            FROM phase00_course_map course_map
            JOIN "TrainingModules" root ON root."Id" = course_map.old_root_module_id
            JOIN phase00_module_roots tree ON tree.old_root_module_id = course_map.old_root_module_id
            JOIN "TrainingModuleVisibilities" visibility
              ON visibility."ModuleId" = tree.old_module_id
             AND visibility."VisibilityType" = 'GroupOnly'
            JOIN "StudentGroupMembers" member ON member."GroupId" = visibility."GroupId"
            ON CONFLICT ("CourseId", "UserId") DO NOTHING;

            INSERT INTO "TrainingCourseChallenges"
                ("CourseId", "ExerciseChallengeId", "Order", "IsRequired", "DisplayTitle", "CreatedById", "CreatedAt")
            SELECT chapter_map.new_course_id,
                   link."ExerciseChallengeId",
                   min(link."Order"),
                   bool_or(link."IsRequired"),
                   (array_agg(link."DisplayTitle" ORDER BY link."Order", link."ModuleId"))[1],
                   (array_agg(link."CreatedById" ORDER BY link."Order", link."ModuleId"))[1],
                   min(link."CreatedAt")
            FROM "TrainingModuleChallenges" link
            JOIN phase00_chapter_map chapter_map ON chapter_map.old_module_id = link."ModuleId"
            GROUP BY chapter_map.new_course_id, link."ExerciseChallengeId";

            INSERT INTO "TrainingCourseChapterChallenges"
                ("ChapterId", "ExerciseChallengeId", "CourseId", "Order")
            SELECT chapter_map.new_chapter_id,
                   link."ExerciseChallengeId",
                   chapter_map.new_course_id,
                   link."Order"
            FROM "TrainingModuleChallenges" link
            JOIN phase00_chapter_map chapter_map ON chapter_map.old_module_id = link."ModuleId";

            INSERT INTO "TrainingCourseSubmissions"
                ("Id", "CourseId", "ChapterId", "ExerciseChallengeId", "UserId", "Status", "SubmittedAt",
                 "SubmittedAnswerHash", "FlagId", "IpAddress")
            SELECT nextval(pg_get_serial_sequence('"TrainingCourseSubmissions"', 'Id'))::bigint,
                   chapter_map.new_course_id,
                   chapter_map.new_chapter_id,
                   submission."ExerciseChallengeId",
                   submission."UserId",
                   submission."Status",
                   submission."SubmittedAt",
                   submission."SubmittedAnswerHash",
                   submission."FlagId",
                   submission."IpAddress"
            FROM "TrainingCtfSubmissions" submission
            JOIN phase00_chapter_map chapter_map ON chapter_map.old_module_id = submission."ModuleId";

            CREATE TEMP TABLE phase00_course_question_map (
                old_plan_id integer NOT NULL,
                source_question_id integer NOT NULL,
                new_question_id integer NOT NULL UNIQUE,
                PRIMARY KEY (old_plan_id, source_question_id)
            ) ON COMMIT DROP;

            INSERT INTO phase00_course_question_map
            SELECT plan_question."PlanId",
                   plan_question."SourceQuestionId",
                   nextval(pg_get_serial_sequence('"TrainingCourseTheoryQuestions"', 'Id'))::integer
            FROM "TheoryTrainingPlanQuestions" plan_question
            ORDER BY plan_question."PlanId", plan_question."Order", plan_question."SourceQuestionId";

            INSERT INTO "TrainingCourseTheoryQuestions"
                ("Id", "CourseId", "Type", "BankName", "Title", "Content", "Options", "AnswerIndexes",
                 "CreatedById", "UpdatedById", "CreatedAt", "UpdatedAt")
            SELECT question_map.new_question_id,
                   chapter_map.new_course_id,
                   source."Type",
                   coalesce(nullif(plan."BankName", ''), source."BankName"),
                   source."Title",
                   source."Content",
                   source."Options",
                   source."AnswerIndexes",
                   plan."CreatedById",
                   plan."UpdatedById",
                   plan."CreatedAt",
                   plan."UpdatedAt"
            FROM phase00_course_question_map question_map
            JOIN "TheoryTrainingPlans" plan ON plan."Id" = question_map.old_plan_id
            JOIN phase00_chapter_map chapter_map ON chapter_map.old_module_id = plan."ModuleId"
            JOIN "TheoryQuestionBankItems" source ON source."Id" = question_map.source_question_id;

            CREATE TEMP TABLE phase00_paper_map (
                old_plan_id integer PRIMARY KEY,
                new_paper_id integer NOT NULL UNIQUE
            ) ON COMMIT DROP;

            INSERT INTO phase00_paper_map
            SELECT plan."Id",
                   nextval(pg_get_serial_sequence('"TrainingCourseChapterTheoryPapers"', 'Id'))::integer
            FROM "TheoryTrainingPlans" plan
            ORDER BY plan."Id";

            INSERT INTO "TrainingCourseChapterTheoryPapers"
                ("Id", "CourseId", "ChapterId", "Title", "Description", "PassRate", "AllowRetake",
                 "ShowCorrectAnswerAfterSubmit", "IsPublished", "PublishedAt", "UpdatedById", "CreatedAt", "UpdatedAt")
            SELECT paper_map.new_paper_id,
                   chapter_map.new_course_id,
                   chapter_map.new_chapter_id,
                   plan."Title",
                   plan."Description",
                   plan."PassRate",
                   plan."AllowRetake",
                   plan."ShowCorrectAnswerAfterSubmit",
                   plan."IsPublished",
                   CASE WHEN plan."IsPublished" THEN plan."UpdatedAt" ELSE NULL END,
                   plan."UpdatedById",
                   plan."CreatedAt",
                   plan."UpdatedAt"
            FROM phase00_paper_map paper_map
            JOIN "TheoryTrainingPlans" plan ON plan."Id" = paper_map.old_plan_id
            JOIN phase00_chapter_map chapter_map ON chapter_map.old_module_id = plan."ModuleId";

            CREATE TEMP TABLE phase00_paper_question_map (
                old_plan_id integer NOT NULL,
                source_question_id integer NOT NULL,
                new_paper_question_id integer NOT NULL UNIQUE,
                PRIMARY KEY (old_plan_id, source_question_id)
            ) ON COMMIT DROP;

            INSERT INTO phase00_paper_question_map
            SELECT plan_question."PlanId",
                   plan_question."SourceQuestionId",
                   nextval(pg_get_serial_sequence('"TrainingCourseChapterTheoryQuestions"', 'Id'))::integer
            FROM "TheoryTrainingPlanQuestions" plan_question
            ORDER BY plan_question."PlanId", plan_question."Order", plan_question."SourceQuestionId";

            INSERT INTO "TrainingCourseChapterTheoryQuestions"
                ("Id", "PaperId", "SourceQuestionId", "Type", "Title", "Content", "Options", "AnswerIndexes", "Score", "Order")
            SELECT paper_question_map.new_paper_question_id,
                   paper_map.new_paper_id,
                   course_question_map.new_question_id,
                   source."Type",
                   source."Title",
                   source."Content",
                   source."Options",
                   source."AnswerIndexes",
                   plan_question."Score",
                   plan_question."Order"
            FROM phase00_paper_question_map paper_question_map
            JOIN phase00_paper_map paper_map ON paper_map.old_plan_id = paper_question_map.old_plan_id
            JOIN phase00_course_question_map course_question_map
              ON course_question_map.old_plan_id = paper_question_map.old_plan_id
             AND course_question_map.source_question_id = paper_question_map.source_question_id
            JOIN "TheoryTrainingPlanQuestions" plan_question
              ON plan_question."PlanId" = paper_question_map.old_plan_id
             AND plan_question."SourceQuestionId" = paper_question_map.source_question_id
            JOIN "TheoryQuestionBankItems" source ON source."Id" = paper_question_map.source_question_id;

            CREATE TEMP TABLE phase00_sheet_map (
                old_session_id integer PRIMARY KEY,
                new_sheet_id integer NOT NULL UNIQUE,
                attempt_number integer NOT NULL
            ) ON COMMIT DROP;

            INSERT INTO phase00_sheet_map
            SELECT session."Id",
                   nextval(pg_get_serial_sequence('"TrainingCourseChapterTheorySheets"', 'Id'))::integer,
                   row_number() OVER (
                       PARTITION BY session."UserId", session."ModuleId"
                       ORDER BY session."CreatedAt", session."Id"
                   )::integer
            FROM "TheoryTrainingSessions" session
            ORDER BY session."CreatedAt", session."Id";

            INSERT INTO "TrainingCourseChapterTheorySheets"
                ("Id", "CourseId", "ChapterId", "PaperId", "UserId", "AttemptNumber", "Status", "Score", "MaxScore",
                 "Passed", "CreatedAt", "UpdatedAt", "SubmittedAt")
            SELECT sheet_map.new_sheet_id,
                   chapter_map.new_course_id,
                   chapter_map.new_chapter_id,
                   paper_map.new_paper_id,
                   session."UserId",
                   sheet_map.attempt_number,
                   session."Status",
                   session."Score",
                   session."MaxScore",
                   session."Status" = 'Submitted' AND
                       (session."MaxScore" = 0 OR session."Score" * 100 >= session."MaxScore" * plan."PassRate"),
                   session."CreatedAt",
                   coalesce(session."SubmittedAt", session."CreatedAt"),
                   session."SubmittedAt"
            FROM phase00_sheet_map sheet_map
            JOIN "TheoryTrainingSessions" session ON session."Id" = sheet_map.old_session_id
            JOIN "TheoryTrainingPlans" plan ON plan."Id" = session."PlanId"
            JOIN phase00_paper_map paper_map ON paper_map.old_plan_id = plan."Id"
            JOIN phase00_chapter_map chapter_map ON chapter_map.old_module_id = session."ModuleId";

            INSERT INTO "TrainingCourseChapterTheoryAnswers"
                ("Id", "SheetId", "PaperQuestionId", "SelectedIndexes", "IsCorrect", "Score")
            SELECT nextval(pg_get_serial_sequence('"TrainingCourseChapterTheoryAnswers"', 'Id'))::integer,
                   sheet_map.new_sheet_id,
                   paper_question_map.new_paper_question_id,
                   session_question."SelectedIndexes",
                   session_question."IsCorrect",
                   CASE WHEN session_question."IsCorrect" THEN session_question."Score" ELSE 0 END
            FROM "TheoryTrainingSessionQuestions" session_question
            JOIN "TheoryTrainingSessions" session ON session."Id" = session_question."SessionId"
            JOIN phase00_sheet_map sheet_map ON sheet_map.old_session_id = session."Id"
            JOIN phase00_paper_question_map paper_question_map
              ON paper_question_map.old_plan_id = session."PlanId"
             AND paper_question_map.source_question_id = session_question."SourceQuestionId";

            INSERT INTO "TrainingChapterProgresses"
                ("ChapterId", "UserId", "CourseId", "Status", "ReadPercent", "StartedAt", "CompletedAt", "UpdatedAt")
            WITH progress_keys AS (
                SELECT "ModuleId", "UserId" FROM "TrainingArticleProgresses"
                UNION
                SELECT "ModuleId", "UserId" FROM "TrainingModuleProgresses"
            )
            SELECT chapter_map.new_chapter_id,
                   keys."UserId",
                   chapter_map.new_course_id,
                   CASE
                       WHEN module_progress."Status" = 'Completed' THEN 'Completed'
                       WHEN article_progress."ReadPercent" > 0 OR module_progress."Status" IN ('Reading', 'Practicing') THEN 'Learning'
                       ELSE 'NotStarted'
                   END,
                   CASE
                       WHEN article_progress."ReadPercent" IS NOT NULL THEN greatest(0, least(100, article_progress."ReadPercent"))
                       WHEN module_progress."Status" = 'Completed' THEN 100
                       ELSE 0
                   END,
                   coalesce(module_progress."StartedAt", article_progress."LastReadAt"),
                   CASE WHEN module_progress."Status" = 'Completed' THEN module_progress."CompletedAt" ELSE NULL END,
                   greatest(
                       coalesce(module_progress."UpdatedAt", '-infinity'::timestamptz),
                       coalesce(article_progress."LastReadAt", '-infinity'::timestamptz)
                   )
            FROM progress_keys keys
            JOIN phase00_chapter_map chapter_map ON chapter_map.old_module_id = keys."ModuleId"
            LEFT JOIN "TrainingArticleProgresses" article_progress
              ON article_progress."ModuleId" = keys."ModuleId" AND article_progress."UserId" = keys."UserId"
            LEFT JOIN "TrainingModuleProgresses" module_progress
              ON module_progress."ModuleId" = keys."ModuleId" AND module_progress."UserId" = keys."UserId";

            INSERT INTO "TrainingCourseProgresses"
                ("CourseId", "UserId", "Status", "CompletedChapterCount", "TotalChapterCount", "ChallengeSolvedCount",
                 "ChallengeTotalCount", "StartedAt", "CompletedAt", "UpdatedAt")
            WITH progress_keys AS (
                SELECT "CourseId", "UserId" FROM "TrainingChapterProgresses"
                WHERE "CourseId" IN (SELECT new_course_id FROM phase00_course_map)
                UNION
                SELECT "CourseId", "UserId" FROM "TrainingCourseSubmissions"
                WHERE "CourseId" IN (SELECT new_course_id FROM phase00_course_map)
                UNION
                SELECT "CourseId", "UserId" FROM "TrainingCourseChapterTheorySheets"
                WHERE "CourseId" IN (SELECT new_course_id FROM phase00_course_map)
            ), facts AS (
                SELECT keys."CourseId",
                       keys."UserId",
                       (SELECT count(*) FROM "TrainingCourseChapters" chapter
                        WHERE chapter."CourseId" = keys."CourseId" AND chapter."IsPublished")::integer AS total_chapters,
                       (SELECT count(*) FROM "TrainingChapterProgresses" progress
                        WHERE progress."CourseId" = keys."CourseId" AND progress."UserId" = keys."UserId"
                          AND progress."Status" = 'Completed')::integer AS completed_chapters,
                       (SELECT count(*) FROM "TrainingCourseChallenges" challenge
                        WHERE challenge."CourseId" = keys."CourseId")::integer AS total_challenges,
                       (SELECT count(DISTINCT submission."ExerciseChallengeId") FROM "TrainingCourseSubmissions" submission
                        WHERE submission."CourseId" = keys."CourseId" AND submission."UserId" = keys."UserId"
                          AND submission."Status" = 'Accepted')::integer AS solved_challenges,
                       (SELECT min(value) FROM (
                           SELECT progress."StartedAt" AS value FROM "TrainingChapterProgresses" progress
                           WHERE progress."CourseId" = keys."CourseId" AND progress."UserId" = keys."UserId"
                           UNION ALL
                           SELECT submission."SubmittedAt" FROM "TrainingCourseSubmissions" submission
                           WHERE submission."CourseId" = keys."CourseId" AND submission."UserId" = keys."UserId"
                           UNION ALL
                           SELECT sheet."CreatedAt" FROM "TrainingCourseChapterTheorySheets" sheet
                           WHERE sheet."CourseId" = keys."CourseId" AND sheet."UserId" = keys."UserId"
                       ) started_values) AS started_at,
                       (SELECT max(value) FROM (
                           SELECT progress."UpdatedAt" AS value FROM "TrainingChapterProgresses" progress
                           WHERE progress."CourseId" = keys."CourseId" AND progress."UserId" = keys."UserId"
                           UNION ALL
                           SELECT submission."SubmittedAt" FROM "TrainingCourseSubmissions" submission
                           WHERE submission."CourseId" = keys."CourseId" AND submission."UserId" = keys."UserId"
                           UNION ALL
                           SELECT sheet."UpdatedAt" FROM "TrainingCourseChapterTheorySheets" sheet
                           WHERE sheet."CourseId" = keys."CourseId" AND sheet."UserId" = keys."UserId"
                       ) updated_values) AS updated_at
                FROM progress_keys keys
            )
            SELECT facts."CourseId",
                   facts."UserId",
                   CASE
                       WHEN facts.total_chapters > 0 AND facts.completed_chapters >= facts.total_chapters THEN 'Completed'
                       WHEN facts.completed_chapters > 0 OR facts.solved_challenges > 0 OR facts.started_at IS NOT NULL THEN 'Learning'
                       ELSE 'NotStarted'
                   END,
                   facts.completed_chapters,
                   facts.total_chapters,
                   facts.solved_challenges,
                   facts.total_challenges,
                   facts.started_at,
                   CASE WHEN facts.total_chapters > 0 AND facts.completed_chapters >= facts.total_chapters
                        THEN facts.updated_at ELSE NULL END,
                   coalesce(facts.updated_at, now())
            FROM facts;

            DO $$
            DECLARE
                baseline phase00_baseline%ROWTYPE;
            BEGIN
                SELECT * INTO baseline FROM phase00_baseline;

                IF (SELECT count(*) FROM phase00_chapter_map) <> (SELECT count(*) FROM "TrainingModules") THEN
                    RAISE EXCEPTION 'Phase 0 chapter count mismatch';
                END IF;
                IF (SELECT count(*) FROM "TrainingCourses") - baseline.course_count
                   <> (SELECT count(*) FROM phase00_course_map) THEN
                    RAISE EXCEPTION 'Phase 0 course count mismatch';
                END IF;
                IF (SELECT count(*) FROM "TrainingCourseChapters") - baseline.chapter_count
                   <> (SELECT count(*) FROM "TrainingModules") THEN
                    RAISE EXCEPTION 'Phase 0 chapter target count mismatch';
                END IF;
                IF (SELECT count(*) FROM "TrainingCourseChapterChallenges") - baseline.chapter_challenge_count
                   <> (SELECT count(*) FROM "TrainingModuleChallenges") THEN
                    RAISE EXCEPTION 'Phase 0 chapter challenge count mismatch';
                END IF;
                IF (SELECT count(*) FROM "TrainingCourseSubmissions") - baseline.submission_count
                   <> (SELECT count(*) FROM "TrainingCtfSubmissions") THEN
                    RAISE EXCEPTION 'Phase 0 submission count mismatch';
                END IF;
                IF (SELECT count(*) FROM "TrainingCourseChapterTheoryPapers") - baseline.paper_count
                   <> (SELECT count(*) FROM "TheoryTrainingPlans") THEN
                    RAISE EXCEPTION 'Phase 0 theory paper count mismatch';
                END IF;
                IF (SELECT count(*) FROM "TrainingCourseChapterTheoryQuestions") - baseline.paper_question_count
                   <> (SELECT count(*) FROM "TheoryTrainingPlanQuestions") THEN
                    RAISE EXCEPTION 'Phase 0 theory question count mismatch';
                END IF;
                IF (SELECT count(*) FROM "TrainingCourseChapterTheorySheets") - baseline.sheet_count
                   <> (SELECT count(*) FROM "TheoryTrainingSessions") THEN
                    RAISE EXCEPTION 'Phase 0 theory session count mismatch';
                END IF;
                IF (SELECT count(*) FROM "TrainingCourseChapterTheoryAnswers") - baseline.answer_count
                   <> (SELECT count(*) FROM "TheoryTrainingSessionQuestions") THEN
                    RAISE EXCEPTION 'Phase 0 theory answer count mismatch';
                END IF;
            END $$;
            """);

        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS "IRCheckpoints";
            DROP TABLE IF EXISTS "IRInstances";
            DROP TABLE IF EXISTS "ScenarioInstances";
            DROP TABLE IF EXISTS "ScoringRules";
            DROP TABLE IF EXISTS "Stages";
            DROP TABLE IF EXISTS "TimeSlots";
            """);
        migrationBuilder.DropTable(name: "TheoryTrainingPlanQuestions");
        migrationBuilder.DropTable(name: "TheoryTrainingSessionQuestions");
        migrationBuilder.DropTable(name: "TrainingArticleProgresses");
        migrationBuilder.DropTable(name: "TrainingCtfSubmissions");
        migrationBuilder.DropTable(name: "TrainingModuleChallenges");
        migrationBuilder.DropTable(name: "TrainingModuleProgresses");
        migrationBuilder.DropTable(name: "TrainingModuleVisibilities");
        migrationBuilder.DropTable(name: "TheoryTrainingSessions");
        migrationBuilder.DropTable(name: "TheoryTrainingPlans");
        migrationBuilder.DropTable(name: "TrainingModules");
        migrationBuilder.DropTable(name: "TrainingDirections");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Phase 0 removes legacy data after a verified migration. Restore the pre-cutover database backup to roll back.");
}
