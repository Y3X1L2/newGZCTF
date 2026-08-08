using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabReleaseImageClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResourcePublicId",
                table: "ImageDistributionReferences",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageDistributionReferences_Kind_PublicResource",
                table: "ImageDistributionReferences",
                columns: new[] { "Kind", "ResourcePublicId" },
                filter: "\"ResourcePublicId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ImageDistributionReferences_Record_Kind_PublicResource",
                table: "ImageDistributionReferences",
                columns: new[] { "DistributionRecordId", "Kind", "ResourcePublicId" },
                unique: true,
                filter: "\"ResourcePublicId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImageDistributionReferences_Kind_PublicResource",
                table: "ImageDistributionReferences");

            migrationBuilder.DropIndex(
                name: "UX_ImageDistributionReferences_Record_Kind_PublicResource",
                table: "ImageDistributionReferences");

            migrationBuilder.DropColumn(
                name: "ResourcePublicId",
                table: "ImageDistributionReferences");
        }
    }
}
