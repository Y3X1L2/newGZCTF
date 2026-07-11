using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class DecoupleImageTemplateOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PenetrationNodes_ImageTemplates_ImageTemplateId",
                table: "PenetrationNodes");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "ImageTemplates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrainingCourseImageTemplateBindings",
                columns: table => new
                {
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    AddedById = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseImageTemplateBindings", x => new { x.CourseId, x.ImageTemplateId });
                    table.ForeignKey(
                        name: "FK_TrainingCourseImageTemplateBindings_AspNetUsers_AddedById",
                        column: x => x.AddedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseImageTemplateBindings_ImageTemplates_ImageTem~",
                        column: x => x.ImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingCourseImageTemplateBindings_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "TrainingCourseImageTemplateBindings"
                    ("CourseId", "ImageTemplateId", "AddedById", "AddedAt")
                SELECT
                    template."TrainingCourseId",
                    template."Id",
                    course."CreatedById",
                    now()
                FROM "ImageTemplates" AS template
                INNER JOIN "TrainingCourses" AS course
                    ON course."Id" = template."TrainingCourseId"
                WHERE template."TrainingCourseId" IS NOT NULL
                ON CONFLICT ("CourseId", "ImageTemplateId") DO NOTHING;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ImageTemplates_TrainingCourses_TrainingCourseId",
                table: "ImageTemplates");

            migrationBuilder.DropIndex(
                name: "IX_ImageTemplates_TrainingCourseId",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "TrainingCourseId",
                table: "ImageTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplates_CreatedById",
                table: "ImageTemplates",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseImageTemplateBindings_AddedById",
                table: "TrainingCourseImageTemplateBindings",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseImageTemplateBindings_ImageTemplateId",
                table: "TrainingCourseImageTemplateBindings",
                column: "ImageTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_ImageTemplates_AspNetUsers_CreatedById",
                table: "ImageTemplates",
                column: "CreatedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PenetrationNodes_ImageTemplates_ImageTemplateId",
                table: "PenetrationNodes",
                column: "ImageTemplateId",
                principalTable: "ImageTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImageTemplates_AspNetUsers_CreatedById",
                table: "ImageTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_PenetrationNodes_ImageTemplates_ImageTemplateId",
                table: "PenetrationNodes");

            migrationBuilder.DropIndex(
                name: "IX_ImageTemplates_CreatedById",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "ImageTemplates");

            migrationBuilder.AddColumn<int>(
                name: "TrainingCourseId",
                table: "ImageTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ImageTemplates" AS template
                SET "TrainingCourseId" = binding."CourseId"
                FROM (
                    SELECT "ImageTemplateId", MIN("CourseId") AS "CourseId"
                    FROM "TrainingCourseImageTemplateBindings"
                    GROUP BY "ImageTemplateId"
                ) AS binding
                WHERE template."Id" = binding."ImageTemplateId";
                """);

            migrationBuilder.DropTable(
                name: "TrainingCourseImageTemplateBindings");

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplates_TrainingCourseId",
                table: "ImageTemplates",
                column: "TrainingCourseId");

            migrationBuilder.AddForeignKey(
                name: "FK_ImageTemplates_TrainingCourses_TrainingCourseId",
                table: "ImageTemplates",
                column: "TrainingCourseId",
                principalTable: "TrainingCourses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PenetrationNodes_ImageTemplates_ImageTemplateId",
                table: "PenetrationNodes",
                column: "ImageTemplateId",
                principalTable: "ImageTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
