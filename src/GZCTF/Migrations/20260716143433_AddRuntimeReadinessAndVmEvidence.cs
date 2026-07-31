using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeReadinessAndVmEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentOperationId",
                table: "TeamLabRuntimeAssets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AgentSignalSequence",
                table: "TeamLabRuntimeAssets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "DomainCreateDurationMs",
                table: "ImageTemplateCapabilityCertifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FullProbeDurationMs",
                table: "ImageTemplateCapabilityCertifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GuestReadyDurationMs",
                table: "ImageTemplateCapabilityCertifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentRuntimeSignals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Stage = table.Column<byte>(type: "smallint", nullable: false),
                    Outcome = table.Column<byte>(type: "smallint", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResourceKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false),
                    FactsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRuntimeSignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRuntimeSignals_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentRuntimeSignals_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeAssets_AgentOperationId",
                table: "TeamLabRuntimeAssets",
                column: "AgentOperationId",
                unique: true,
                filter: "\"AgentOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuntimeSignals_Operation_Generation_Sequence",
                table: "AgentRuntimeSignals",
                columns: new[] { "OperationId", "Generation", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuntimeSignals_Runtime_Generation_Received",
                table: "AgentRuntimeSignals",
                columns: new[] { "RuntimeId", "Generation", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_AgentRuntimeSignals_Node_Operation_Sequence",
                table: "AgentRuntimeSignals",
                columns: new[] { "WorkerNodeId", "OperationId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRuntimeSignals");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeAssets_AgentOperationId",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "AgentOperationId",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "AgentSignalSequence",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "DomainCreateDurationMs",
                table: "ImageTemplateCapabilityCertifications");

            migrationBuilder.DropColumn(
                name: "FullProbeDurationMs",
                table: "ImageTemplateCapabilityCertifications");

            migrationBuilder.DropColumn(
                name: "GuestReadyDurationMs",
                table: "ImageTemplateCapabilityCertifications");
        }
    }
}
