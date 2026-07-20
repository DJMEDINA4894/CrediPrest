using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrediPrest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientNotificationRecipients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_Type_RelatedEntityId",
                table: "Notifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE notification
                SET notification.ClientId = appUser.ClientId,
                    notification.UserId = NULL
                FROM Notifications AS notification
                INNER JOIN Users AS appUser ON appUser.Id = notification.UserId
                WHERE appUser.Role = 3 AND appUser.ClientId IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ClientId_Type_RelatedEntityId",
                table: "Notifications",
                columns: new[] { "ClientId", "Type", "RelatedEntityId" },
                unique: true,
                filter: "[ClientId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Type_RelatedEntityId",
                table: "Notifications",
                columns: new[] { "UserId", "Type", "RelatedEntityId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Clients_ClientId",
                table: "Notifications",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Clients_ClientId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_ClientId_Type_RelatedEntityId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_Type_RelatedEntityId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "Notifications");

            migrationBuilder.Sql("DELETE FROM Notifications WHERE UserId IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Type_RelatedEntityId",
                table: "Notifications",
                columns: new[] { "UserId", "Type", "RelatedEntityId" },
                unique: true);
        }
    }
}
