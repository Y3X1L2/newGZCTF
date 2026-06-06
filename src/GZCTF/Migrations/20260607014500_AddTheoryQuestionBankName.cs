using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260607014500_AddTheoryQuestionBankName")]
    public partial class AddTheoryQuestionBankName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TheoryQuestionBankItems_Type",
                table: "TheoryQuestionBankItems");

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "TheoryQuestionBankItems",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "Default");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryQuestionBankItems_Type_BankName",
                table: "TheoryQuestionBankItems",
                columns: new[] { "Type", "BankName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TheoryQuestionBankItems_Type_BankName",
                table: "TheoryQuestionBankItems");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "TheoryQuestionBankItems");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryQuestionBankItems_Type",
                table: "TheoryQuestionBankItems",
                column: "Type");
        }
    }
}
