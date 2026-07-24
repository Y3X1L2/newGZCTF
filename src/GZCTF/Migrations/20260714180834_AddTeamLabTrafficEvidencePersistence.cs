using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabTrafficEvidencePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamLabObservationCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    DroppedCount = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabObservationCursors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabObservationCursors_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabObservationCursors_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTrafficCorrelationCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    LastObservationId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTrafficCorrelationCursors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficCorrelationCursors_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTrafficObservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    ObservationPointId = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSequence = table.Column<long>(type: "bigint", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourcePort = table.Column<int>(type: "integer", nullable: true),
                    DestinationIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DestinationPort = table.Column<int>(type: "integer", nullable: true),
                    Protocol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TcpFlags = table.Column<byte>(type: "smallint", nullable: true),
                    PacketLength = table.Column<int>(type: "integer", nullable: false),
                    PacketFingerprint = table.Column<byte[]>(type: "bytea", nullable: true),
                    FlowFingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    ProcessIdentityHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    EvidenceKind = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTrafficObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficObservations_TeamLabObservationPoints_Observa~",
                        column: x => x.ObservationPointId,
                        principalTable: "TeamLabObservationPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficObservations_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficObservations_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTrafficPaths",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<byte>(type: "smallint", nullable: false),
                    EvidenceFingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    SourceIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourcePort = table.Column<int>(type: "integer", nullable: true),
                    DestinationIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DestinationPort = table.Column<int>(type: "integer", nullable: true),
                    Protocol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTrafficPaths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficPaths_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTrafficPathHops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PathId = table.Column<long>(type: "bigint", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ObservationId = table.Column<long>(type: "bigint", nullable: true),
                    ObservationPointId = table.Column<int>(type: "integer", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvidenceKind = table.Column<byte>(type: "smallint", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourcePort = table.Column<int>(type: "integer", nullable: true),
                    DestinationIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DestinationPort = table.Column<int>(type: "integer", nullable: true),
                    Protocol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTrafficPathHops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficPathHops_TeamLabObservationPoints_Observation~",
                        column: x => x.ObservationPointId,
                        principalTable: "TeamLabObservationPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficPathHops_TeamLabTrafficObservations_Observati~",
                        column: x => x.ObservationId,
                        principalTable: "TeamLabTrafficObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficPathHops_TeamLabTrafficPaths_PathId",
                        column: x => x.PathId,
                        principalTable: "TeamLabTrafficPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservationCursors_RuntimeId_Generation_WorkerNodeId",
                table: "TeamLabObservationCursors",
                columns: new[] { "RuntimeId", "Generation", "WorkerNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservationCursors_WorkerNodeId",
                table: "TeamLabObservationCursors",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCorrelationCursors_RuntimeId_Generation",
                table: "TeamLabTrafficCorrelationCursors",
                columns: new[] { "RuntimeId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservations_PacketFingerprint",
                table: "TeamLabTrafficObservations",
                columns: new[] { "RuntimeId", "Generation", "PacketFingerprint", "ObservedAt" },
                filter: "\"PacketFingerprint\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservations_ProcessIdentity",
                table: "TeamLabTrafficObservations",
                columns: new[] { "RuntimeId", "Generation", "ProcessIdentityHash", "ObservedAt" },
                filter: "\"ProcessIdentityHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservations_Runtime_Generation_Time_Id",
                table: "TeamLabTrafficObservations",
                columns: new[] { "RuntimeId", "Generation", "ObservedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficObservations_ObservationPointId",
                table: "TeamLabTrafficObservations",
                column: "ObservationPointId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficObservations_RuntimeId_Generation_Observation~",
                table: "TeamLabTrafficObservations",
                columns: new[] { "RuntimeId", "Generation", "ObservationPointId", "SourceSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficObservations_WorkerNodeId",
                table: "TeamLabTrafficObservations",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficPathHops_ObservationId",
                table: "TeamLabTrafficPathHops",
                column: "ObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficPathHops_ObservationPointId",
                table: "TeamLabTrafficPathHops",
                column: "ObservationPointId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficPathHops_PathId_Ordinal",
                table: "TeamLabTrafficPathHops",
                columns: new[] { "PathId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabPaths_Runtime_Generation_Time_Id",
                table: "TeamLabTrafficPaths",
                columns: new[] { "RuntimeId", "Generation", "StartedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficPaths_PublicId",
                table: "TeamLabTrafficPaths",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficPaths_RuntimeId_Generation_EvidenceFingerprint",
                table: "TeamLabTrafficPaths",
                columns: new[] { "RuntimeId", "Generation", "EvidenceFingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamLabObservationCursors");

            migrationBuilder.DropTable(
                name: "TeamLabTrafficCorrelationCursors");

            migrationBuilder.DropTable(
                name: "TeamLabTrafficPathHops");

            migrationBuilder.DropTable(
                name: "TeamLabTrafficObservations");

            migrationBuilder.DropTable(
                name: "TeamLabTrafficPaths");
        }
    }
}
