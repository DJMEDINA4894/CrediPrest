using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrediPrest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLenderOwnershipAndClientAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LenderUserId",
                table: "Loans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LenderUserId",
                table: "Clients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Loans_LenderUserId",
                table: "Loans",
                column: "LenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_LenderUserId",
                table: "Clients",
                column: "LenderUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Clients_Users_LenderUserId",
                table: "Clients",
                column: "LenderUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Loans_Users_LenderUserId",
                table: "Loans",
                column: "LenderUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clients_Users_LenderUserId",
                table: "Clients");

            migrationBuilder.DropForeignKey(
                name: "FK_Loans_Users_LenderUserId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Loans_LenderUserId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Clients_LenderUserId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "LenderUserId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "LenderUserId",
                table: "Clients");
        }
    }
}
