using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class FleetNodeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgentPort",
                table: "WorkerNodes",
                type: "integer",
                nullable: false,
                defaultValue: 5001);

            migrationBuilder.AddColumn<bool>(
                name: "IsLocal",
                table: "WorkerNodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSchedulable",
                table: "WorkerNodes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NodeId",
                table: "VmInstances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NodeId",
                table: "Containers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VmInstances_NodeId",
                table: "VmInstances",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Containers_NodeId",
                table: "Containers",
                column: "NodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Containers_WorkerNodes_NodeId",
                table: "Containers",
                column: "NodeId",
                principalTable: "WorkerNodes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VmInstances_WorkerNodes_NodeId",
                table: "VmInstances",
                column: "NodeId",
                principalTable: "WorkerNodes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Containers_WorkerNodes_NodeId",
                table: "Containers");

            migrationBuilder.DropForeignKey(
                name: "FK_VmInstances_WorkerNodes_NodeId",
                table: "VmInstances");

            migrationBuilder.DropIndex(
                name: "IX_VmInstances_NodeId",
                table: "VmInstances");

            migrationBuilder.DropIndex(
                name: "IX_Containers_NodeId",
                table: "Containers");

            migrationBuilder.DropColumn(
                name: "AgentPort",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "IsLocal",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "IsSchedulable",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "NodeId",
                table: "VmInstances");

            migrationBuilder.DropColumn(
                name: "NodeId",
                table: "Containers");
        }
    }
}
