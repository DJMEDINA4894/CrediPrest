using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrediPrest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientPreferredPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredPaymentMethod",
                table: "Clients",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "cash");

            migrationBuilder.Sql("""
                UPDATE dbo.Clients
                SET PreferredPaymentMethod = CASE
                    WHEN KashAccount IS NOT NULL AND LTRIM(RTRIM(KashAccount)) <> '' THEN 'kash'
                    WHEN HasKash = 1 THEN 'kash'
                    WHEN BacAccountNumber IS NOT NULL AND LTRIM(RTRIM(BacAccountNumber)) <> '' THEN 'bac'
                    WHEN LafiseAccountNumber IS NOT NULL AND LTRIM(RTRIM(LafiseAccountNumber)) <> '' THEN 'lafise'
                    WHEN BamproAccountNumber IS NOT NULL AND LTRIM(RTRIM(BamproAccountNumber)) <> '' THEN 'bampro'
                    ELSE 'cash'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredPaymentMethod",
                table: "Clients");
        }
    }
}
