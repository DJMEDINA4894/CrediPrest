using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrediPrest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpoPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PushVersion",
                table: "Notifications",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ExpoPushDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpoPushDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationVersion = table.Column<int>(type: "int", nullable: false),
                    ExpoTicketId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    AttemptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpoPushDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpoPushDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpoPushToken = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpoPushDevices", x => x.Id);
                    table.CheckConstraint("CK_ExpoPushDevices_Recipient", "([UserId] IS NOT NULL AND [ClientId] IS NULL) OR ([UserId] IS NULL AND [ClientId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ExpoPushDevices_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpoPushDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpoPushDeliveries_NotificationId_ExpoPushDeviceId_NotificationVersion",
                table: "ExpoPushDeliveries",
                columns: new[] { "NotificationId", "ExpoPushDeviceId", "NotificationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpoPushDevices_ClientId",
                table: "ExpoPushDevices",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpoPushDevices_ExpoPushToken",
                table: "ExpoPushDevices",
                column: "ExpoPushToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpoPushDevices_UserId",
                table: "ExpoPushDevices",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpoPushDeliveries");

            migrationBuilder.DropTable(
                name: "ExpoPushDevices");

            migrationBuilder.DropColumn(
                name: "PushVersion",
                table: "Notifications");
        }
    }
}
