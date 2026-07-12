-- Run with psql -X -v ON_ERROR_STOP=1 outside a transaction after Expand/Backfill.
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_TrainingCourseProgress_Course_Status_Updated_User"
    ON "TrainingCourseProgresses" ("CourseId", "Status", "UpdatedAt" DESC, "UserId");
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_TrainingCourseProgress_User_Updated"
    ON "TrainingCourseProgresses" ("UserId", "UpdatedAt" DESC);
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_TrainingChapterProgress_User_Updated"
    ON "TrainingChapterProgresses" ("UserId", "UpdatedAt" DESC);
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_TheoryQuestions_Type_Updated_Id"
    ON "TheoryQuestionBankItems" ("Type", "UpdatedAt" DESC, "Id" DESC);
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_TheoryQuestions_Title_Trgm"
    ON "TheoryQuestionBankItems" USING gin ("Title" gin_trgm_ops);
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_TheoryQuestions_Bank_Trgm"
    ON "TheoryQuestionBankItems" USING gin ("BankName" gin_trgm_ops);
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_TheoryAnswerSheets_Game_Status_Submitted_Id"
    ON "TheoryAnswerSheets" ("GameId", "Status", "SubmittedAt" DESC, "Id" DESC);
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Submissions_Challenge_Time_Id"
    ON "Submissions" ("ChallengeId", "SubmitTimeUtc" DESC, "Id" DESC);
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Submissions_Game_Time_Id"
    ON "Submissions" ("GameId", "SubmitTimeUtc" DESC, "Id" DESC);
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Submissions_Participation_Challenge"
    ON "Submissions" ("ParticipationId", "ChallengeId");
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Submissions_Team_Time_Id"
    ON "Submissions" ("TeamId", "SubmitTimeUtc" DESC, "Id" DESC);
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Submissions_Unchecked_Time_Id"
    ON "Submissions" ("Status", "SubmitTimeUtc" DESC, "Id" DESC) WHERE "Status" = 'FlagSubmitted';
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS "UX_Participations_Game_Team"
    ON "Participations" ("GameId", "TeamId");
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Participations_Game_Status_Division_Team"
    ON "Participations" ("GameId", "Status", "DivisionId", "TeamId");
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS "UX_ImageDistributionRecords_Template_Node"
    ON "ImageDistributionRecords" ("ImageTemplateId", "WorkerNodeId");
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_ImageDistributionRecords_Node_Status_Checked"
    ON "ImageDistributionRecords" ("WorkerNodeId", "Status", "LastCheckedAt");
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS "UX_DeploymentQueueTickets_ActiveIdentity"
    ON "DeploymentQueueTickets" ("ActiveIdentity") WHERE "Status" IN (0, 1, 2);
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_DeploymentQueueTickets_Status_Created_Id"
    ON "DeploymentQueueTickets" ("Status", "CreatedAt", "Id");
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_DeploymentQueueTickets_Node_Status_Created_Id"
    ON "DeploymentQueueTickets" ("TargetNodeId", "Status", "CreatedAt", "Id");
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_DeploymentQueueTickets_Terminal_Completed_Id"
    ON "DeploymentQueueTickets" ("Status", "CompletedAt" DESC, "Id" DESC) WHERE "Status" IN (3, 4, 5);
