using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

public partial class ExpandPhaseSixRuntimeSchedulingConcurrency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("AgentBinarySha256", "WorkerNodes", maxLength: 128,
            type: "character varying(128)", nullable: true);
        migrationBuilder.AddColumn<string>("AgentVersion", "WorkerNodes", maxLength: 64,
            type: "character varying(64)", nullable: true);
        migrationBuilder.AddColumn<int>("CapabilityManifestSchemaVersion", "WorkerNodes",
            type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("CapabilityManifestJson", "WorkerNodes", maxLength: 8192,
            type: "character varying(8192)", nullable: false, defaultValue: "{}");
        migrationBuilder.AddColumn<string>("CapabilityHash", "WorkerNodes", maxLength: 64,
            type: "character varying(64)", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("CapabilityObservedAt", "WorkerNodes",
            type: "timestamp with time zone", nullable: true);

        migrationBuilder.AddColumn<bool>("IsEntry", "TeamLabRuntimeNetworks", type: "boolean",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("PlacementGroupKey", "TeamLabRuntimeNetworks", maxLength: 256,
            type: "character varying(256)", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("PlacementGroupKey", "TeamLabRuntimeAssets", maxLength: 256,
            type: "character varying(256)", nullable: false, defaultValue: "");

        migrationBuilder.AddColumn<int>("AttemptCount", "ImageDistributionRecords", type: "integer",
            nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTimeOffset>("ClaimExpiresAt", "ImageDistributionRecords",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("ClaimOwner", "ImageDistributionRecords", maxLength: 256,
            type: "character varying(256)", nullable: true);
        migrationBuilder.AddColumn<string>("LastErrorCode", "ImageDistributionRecords", maxLength: 128,
            type: "character varying(128)", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("NextAttemptAt", "ImageDistributionRecords",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<byte>("Operation", "ImageDistributionRecords", type: "smallint",
            nullable: false, defaultValue: (byte)0);
        migrationBuilder.AddColumn<DateTimeOffset>("ProgressUpdatedAt", "ImageDistributionRecords",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<byte>("Stage", "ImageDistributionRecords", type: "smallint",
            nullable: false, defaultValue: (byte)0);

        migrationBuilder.AddColumn<int>("AttemptCount", "DeploymentQueueTickets", type: "integer",
            nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>("AwdpServiceInstanceId", "DeploymentQueueTickets", type: "integer",
            nullable: true);
        migrationBuilder.AddColumn<string>("BlockedReasonCode", "DeploymentQueueTickets", maxLength: 64,
            type: "character varying(64)", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("ClaimExpiresAt", "DeploymentQueueTickets",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("ClaimOwner", "DeploymentQueueTickets", maxLength: 128,
            type: "character varying(128)", nullable: true);
        migrationBuilder.AddColumn<int>("ExtensionSeconds", "DeploymentQueueTickets", type: "integer",
            nullable: true);
        migrationBuilder.AddColumn<int>("Generation", "DeploymentQueueTickets", type: "integer",
            nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<DateTimeOffset>("NotBeforeAt", "DeploymentQueueTickets",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<byte>("Operation", "DeploymentQueueTickets", type: "smallint",
            nullable: false, defaultValue: (byte)1);
        migrationBuilder.AddColumn<byte>("Stage", "DeploymentQueueTickets", type: "smallint",
            nullable: false, defaultValue: (byte)0);
        migrationBuilder.AddColumn<string>("StageMessage", "DeploymentQueueTickets", maxLength: 512,
            type: "character varying(512)", nullable: true);
        migrationBuilder.AddColumn<string>("SubjectConcurrencyKey", "DeploymentQueueTickets", maxLength: 256,
            type: "character varying(256)", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("PayloadHash", "DeploymentQueueTickets", maxLength: 128,
            type: "character varying(128)", nullable: true);
        migrationBuilder.AddColumn<string>("ProtectedPayload", "DeploymentQueueTickets", type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            "FleetCapacityReservations",
            table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DeploymentQueueTicketId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                DockerSlots = table.Column<int>(type: "integer", nullable: false),
                VmSlots = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<byte>(type: "smallint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FleetCapacityReservations", x => x.Id);
                table.ForeignKey("FK_FleetCapacityReservations_DeploymentQueueTickets_Deployment~",
                    x => x.DeploymentQueueTicketId, "DeploymentQueueTickets", "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_FleetCapacityReservations_WorkerNodes_WorkerNodeId",
                    x => x.WorkerNodeId, "WorkerNodes", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_ImageDistributionRecords_Work_Claim", "ImageDistributionRecords",
            new[] { "Status", "NextAttemptAt", "ClaimExpiresAt", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_FleetCapacityReservations_Node_Status_Expires",
            "FleetCapacityReservations", new[] { "WorkerNodeId", "Status", "ExpiresAt" });
        migrationBuilder.CreateIndex("IX_FleetCapacityReservations_Ticket_Status",
            "FleetCapacityReservations", new[] { "DeploymentQueueTicketId", "Status" });
        migrationBuilder.CreateIndex("UX_FleetCapacityReservations_Ticket_Node",
            "FleetCapacityReservations", new[] { "DeploymentQueueTicketId", "WorkerNodeId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("FleetCapacityReservations");
        migrationBuilder.DropIndex("IX_ImageDistributionRecords_Work_Claim", "ImageDistributionRecords");
        foreach (var column in new[]
                 {
                     "AgentBinarySha256", "AgentVersion", "CapabilityManifestSchemaVersion",
                     "CapabilityManifestJson", "CapabilityHash", "CapabilityObservedAt"
                 })
            migrationBuilder.DropColumn(column, "WorkerNodes");
        migrationBuilder.DropColumn("IsEntry", "TeamLabRuntimeNetworks");
        migrationBuilder.DropColumn("PlacementGroupKey", "TeamLabRuntimeNetworks");
        migrationBuilder.DropColumn("PlacementGroupKey", "TeamLabRuntimeAssets");
        foreach (var column in new[]
                 {
                     "AttemptCount", "ClaimExpiresAt", "ClaimOwner", "LastErrorCode", "NextAttemptAt",
                     "Operation", "ProgressUpdatedAt", "Stage"
                 })
            migrationBuilder.DropColumn(column, "ImageDistributionRecords");
        foreach (var column in new[]
                 {
                     "AttemptCount", "AwdpServiceInstanceId", "BlockedReasonCode", "ClaimExpiresAt",
                     "ClaimOwner", "ExtensionSeconds", "Generation", "NotBeforeAt", "Operation", "Stage",
                     "StageMessage", "SubjectConcurrencyKey", "PayloadHash", "ProtectedPayload"
                 })
            migrationBuilder.DropColumn(column, "DeploymentQueueTickets");
    }
}
