using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class ExpandPhaseSevenObservabilityAuditRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "Logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeploymentTicketId",
                table: "Logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCategory",
                table: "Logs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "Logs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventCode",
                table: "Logs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceDisplayName",
                table: "Logs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceId",
                table: "Logs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceType",
                table: "Logs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "Logs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkerNodeId",
                table: "Logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkerNodeName",
                table: "Logs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ErrorCategory",
                table: "ImageDistributionRecords",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastCorrelationId",
                table: "ImageDistributionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Retryable",
                table: "ImageDistributionRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "ErrorCategory",
                table: "DeploymentQueueTickets",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "DeploymentQueueTickets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Retryable",
                table: "DeploymentQueueTickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "DeploymentQueueTickets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceState",
                table: "DeploymentQueueTickets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperationalEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EventCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Severity = table.Column<byte>(type: "smallint", nullable: false),
                    Outcome = table.Column<byte>(type: "smallint", nullable: false),
                    ErrorCategory = table.Column<byte>(type: "smallint", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DetailJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerTeamId = table.Column<int>(type: "integer", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: true),
                    CourseId = table.Column<int>(type: "integer", nullable: true),
                    ChallengeId = table.Column<int>(type: "integer", nullable: true),
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeploymentTicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamLabRuntimeId = table.Column<int>(type: "integer", nullable: true),
                    VmInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SubjectDisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResourceDisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Logs_Correlation_Time_Id",
                table: "Logs",
                columns: new[] { "CorrelationId", "TimeUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Logs_Event_Time_Id",
                table: "Logs",
                columns: new[] { "EventCode", "TimeUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Logs_Node_Time_Id",
                table: "Logs",
                columns: new[] { "WorkerNodeId", "TimeUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_Code_Outcome_Time_Id",
                table: "OperationalEvents",
                columns: new[] { "EventCode", "Outcome", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_Correlation_Time_Id",
                table: "OperationalEvents",
                columns: new[] { "CorrelationId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_Course_Time_Id",
                table: "OperationalEvents",
                columns: new[] { "CourseId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_Game_Time_Id",
                table: "OperationalEvents",
                columns: new[] { "GameId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_Node_Time_Id",
                table: "OperationalEvents",
                columns: new[] { "WorkerNodeId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_Team_Time_Id",
                table: "OperationalEvents",
                columns: new[] { "OwnerTeamId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_Template_Time_Id",
                table: "OperationalEvents",
                columns: new[] { "ImageTemplateId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_Ticket_Time_Id",
                table: "OperationalEvents",
                columns: new[] { "DeploymentTicketId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_Time_Id",
                table: "OperationalEvents",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationalEvents");

            migrationBuilder.DropIndex(
                name: "IX_Logs_Correlation_Time_Id",
                table: "Logs");

            migrationBuilder.DropIndex(
                name: "IX_Logs_Event_Time_Id",
                table: "Logs");

            migrationBuilder.DropIndex(
                name: "IX_Logs_Node_Time_Id",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "DeploymentTicketId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "ErrorCategory",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "EventCode",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "ResourceDisplayName",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "ResourceType",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "WorkerNodeId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "WorkerNodeName",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "ErrorCategory",
                table: "ImageDistributionRecords");

            migrationBuilder.DropColumn(
                name: "LastCorrelationId",
                table: "ImageDistributionRecords");

            migrationBuilder.DropColumn(
                name: "Retryable",
                table: "ImageDistributionRecords");

            migrationBuilder.DropColumn(
                name: "ErrorCategory",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "Retryable",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "TraceState",
                table: "DeploymentQueueTickets");
        }
    }
}
