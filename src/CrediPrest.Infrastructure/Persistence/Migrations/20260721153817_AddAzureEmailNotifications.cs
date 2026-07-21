using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrediPrest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureEmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailDispatchState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDispatchState", x => x.Id);
                    table.CheckConstraint("CK_EmailDispatchState_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "EmailNotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationVersion = table.Column<int>(type: "int", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailNotificationDeliveries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailNotificationDeliveries_NotificationId_NotificationVersion_RecipientEmail",
                table: "EmailNotificationDeliveries",
                columns: new[] { "NotificationId", "NotificationVersion", "RecipientEmail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailDispatchState");

            migrationBuilder.DropTable(
                name: "EmailNotificationDeliveries");
        }
    }
}
