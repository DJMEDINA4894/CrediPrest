using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrediPrest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeRateBuySell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BuyCordobasPerUsd",
                table: "ExchangeRates",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SellCordobasPerUsd",
                table: "ExchangeRates",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE ExchangeRates
                SET BuyCordobasPerUsd = CordobasPerUsd,
                    SellCordobasPerUsd = CordobasPerUsd
                WHERE BuyCordobasPerUsd = 0 OR SellCordobasPerUsd = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyCordobasPerUsd",
                table: "ExchangeRates");

            migrationBuilder.DropColumn(
                name: "SellCordobasPerUsd",
                table: "ExchangeRates");
        }
    }
}
