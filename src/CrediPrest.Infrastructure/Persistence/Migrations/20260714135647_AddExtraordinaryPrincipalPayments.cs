using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrediPrest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExtraordinaryPrincipalPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NewInstallmentAmount",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NewInstallmentCount",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NewOutstandingPrincipal",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NewPendingInterest",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousInstallmentAmount",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousInstallmentCount",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousOutstandingPrincipal",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousPendingInterest",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecalculationMode",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewInstallmentAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "NewInstallmentCount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "NewOutstandingPrincipal",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "NewPendingInterest",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PreviousInstallmentAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PreviousInstallmentCount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PreviousOutstandingPrincipal",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PreviousPendingInterest",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RecalculationMode",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Payments");
        }
    }
}
