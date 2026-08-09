using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabRemoteAccessSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NativeIdentity",
                table: "TeamLabRuntimeAssets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImageTemplateRemoteAccesses",
                columns: table => new
                {
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Protocol = table.Column<byte>(type: "smallint", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CredentialMode = table.Column<byte>(type: "smallint", nullable: false),
                    ProtectedSecret = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageTemplateRemoteAccesses", x => x.ImageTemplateId);
                    table.CheckConstraint("CK_ImageTemplateRemoteAccesses_Port", "\"Port\" >= 1 AND \"Port\" <= 65535");
                    table.ForeignKey(
                        name: "FK_ImageTemplateRemoteAccesses_ImageTemplates_ImageTemplateId",
                        column: x => x.ImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationTeamLabOperatorGrants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permissions = table.Column<byte>(type: "smallint", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationTeamLabOperatorGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamLabOperatorGrants_AspNetUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamLabOperatorGrants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamLabOperatorGrants_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabRemoteSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    RuntimeAssetId = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Protocol = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RelayId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GuacamoleConnectionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GuacamoleUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRemoteSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRemoteSessions_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamLabRemoteSessions_TeamLabRuntimeAssets_RuntimeAssetId",
                        column: x => x.RuntimeAssetId,
                        principalTable: "TeamLabRuntimeAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamLabRemoteSessions_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamLabRemoteSessions_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeRemoteCredentials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    RuntimeAssetId = table.Column<int>(type: "integer", nullable: false),
                    Protocol = table.Column<byte>(type: "smallint", nullable: false),
                    Username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    Mode = table.Column<byte>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeRemoteCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeRemoteCredentials_TeamLabRuntimeAssets_Runtim~",
                        column: x => x.RuntimeAssetId,
                        principalTable: "TeamLabRuntimeAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeRemoteCredentials_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabRemoteAuditFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRemoteAuditFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRemoteAuditFiles_TeamLabRemoteSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TeamLabRemoteSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamLabOperatorGrants_GameId_UserId",
                table: "PenetrationTeamLabOperatorGrants",
                columns: new[] { "GameId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamLabOperatorGrants_GrantedByUserId",
                table: "PenetrationTeamLabOperatorGrants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamLabOperatorGrants_UserId",
                table: "PenetrationTeamLabOperatorGrants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRemoteAuditFiles_SessionId_RelativePath",
                table: "TeamLabRemoteAuditFiles",
                columns: new[] { "SessionId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRemoteSessions_PublicId",
                table: "TeamLabRemoteSessions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRemoteSessions_RequestedByUserId",
                table: "TeamLabRemoteSessions",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRemoteSessions_RuntimeAssetId",
                table: "TeamLabRemoteSessions",
                column: "RuntimeAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRemoteSessions_RuntimeId_Generation_RuntimeAssetId",
                table: "TeamLabRemoteSessions",
                columns: new[] { "RuntimeId", "Generation", "RuntimeAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRemoteSessions_Status_ExpiresAt",
                table: "TeamLabRemoteSessions",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRemoteSessions_WorkerNodeId",
                table: "TeamLabRemoteSessions",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeRemoteCredentials_RuntimeAssetId",
                table: "TeamLabRuntimeRemoteCredentials",
                column: "RuntimeAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeRemoteCredentials_RuntimeId_Generation_Runtim~",
                table: "TeamLabRuntimeRemoteCredentials",
                columns: new[] { "RuntimeId", "Generation", "RuntimeAssetId", "Protocol" },
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageTemplateRemoteAccesses");

            migrationBuilder.DropTable(
                name: "PenetrationTeamLabOperatorGrants");

            migrationBuilder.DropTable(
                name: "TeamLabRemoteAuditFiles");

            migrationBuilder.DropTable(
                name: "TeamLabRuntimeRemoteCredentials");

            migrationBuilder.DropTable(
                name: "TeamLabRemoteSessions");

            migrationBuilder.DropColumn(
                name: "NativeIdentity",
                table: "TeamLabRuntimeAssets");

        }
    }
}
