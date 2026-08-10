using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeImageDistributionReferenceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ImageDistributionReferences_Record_Kind_Resource",
                table: "ImageDistributionReferences");

            migrationBuilder.CreateIndex(
                name: "UX_ImageDistributionReferences_Record_Kind_Resource",
                table: "ImageDistributionReferences",
                columns: new[] { "DistributionRecordId", "Kind", "ResourceId" },
                unique: true,
                filter: "\"ResourcePublicId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ImageDistributionReferences_Record_Kind_Resource",
                table: "ImageDistributionReferences");

            migrationBuilder.CreateIndex(
                name: "UX_ImageDistributionReferences_Record_Kind_Resource",
                table: "ImageDistributionReferences",
                columns: new[] { "DistributionRecordId", "Kind", "ResourceId" },
                unique: true);
        }
    }
}
