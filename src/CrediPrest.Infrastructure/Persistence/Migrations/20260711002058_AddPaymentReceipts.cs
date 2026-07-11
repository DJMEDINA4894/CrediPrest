using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrediPrest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReceipts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReceiptId",
                table: "Payments",
                column: "ReceiptId");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_PaymentReceipts_ReceiptId",
                table: "Payments",
                column: "ReceiptId",
                principalTable: "PaymentReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_PaymentReceipts_ReceiptId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "PaymentReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ReceiptId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReceiptId",
                table: "Payments");
        }
    }
}
