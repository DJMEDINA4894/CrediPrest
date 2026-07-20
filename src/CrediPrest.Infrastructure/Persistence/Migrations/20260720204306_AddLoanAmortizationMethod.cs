using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrediPrest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanAmortizationMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AmortizationMethod",
                table: "Loans",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmortizationMethod",
                table: "Loans");
        }
    }
}
