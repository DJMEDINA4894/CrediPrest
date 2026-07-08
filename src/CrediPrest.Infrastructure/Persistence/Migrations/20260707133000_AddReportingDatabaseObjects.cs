using CrediPrest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrediPrest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260707133000_AddReportingDatabaseObjects")]
    public partial class AddReportingDatabaseObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR ALTER FUNCTION dbo.fn_LoanPendingBalance (@LoanId uniqueidentifier)
                RETURNS decimal(18, 2)
                AS
                BEGIN
                    DECLARE @Pending decimal(18, 2);

                    SELECT @Pending = ISNULL(l.TotalToPay, 0) - ISNULL(SUM(p.AmountPaid), 0)
                    FROM dbo.Loans AS l
                    LEFT JOIN dbo.Payments AS p ON p.LoanId = l.Id
                    WHERE l.Id = @LoanId
                    GROUP BY l.TotalToPay;

                    RETURN ISNULL(@Pending, 0);
                END
                """);

            migrationBuilder.Sql("""
                CREATE OR ALTER VIEW dbo.vw_LoanPortfolioSummary
                AS
                SELECT
                    l.Id AS LoanId,
                    c.Id AS ClientId,
                    c.FullName AS ClientName,
                    l.Currency,
                    l.Status,
                    l.PrincipalAmount,
                    l.TotalInterest,
                    l.TotalToPay,
                    ISNULL(SUM(p.AmountPaid), 0) AS TotalPaid,
                    l.TotalToPay - ISNULL(SUM(p.AmountPaid), 0) AS PendingBalance,
                    MIN(CASE WHEN i.Status <> 3 THEN i.DueDate END) AS NextDueDate
                FROM dbo.Loans AS l
                INNER JOIN dbo.Clients AS c ON c.Id = l.ClientId
                LEFT JOIN dbo.Payments AS p ON p.LoanId = l.Id
                LEFT JOIN dbo.Installments AS i ON i.LoanId = l.Id
                GROUP BY
                    l.Id,
                    c.Id,
                    c.FullName,
                    l.Currency,
                    l.Status,
                    l.PrincipalAmount,
                    l.TotalInterest,
                    l.TotalToPay;
                """);

            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.sp_GetOverdueInstallments
                AS
                BEGIN
                    SET NOCOUNT ON;

                    SELECT
                        i.Id AS InstallmentId,
                        i.LoanId,
                        c.FullName AS ClientName,
                        c.Phone,
                        i.InstallmentNumber,
                        i.DueDate,
                        i.PaymentAmount,
                        i.AmountPaid,
                        i.PaymentAmount - i.AmountPaid AS PendingAmount
                    FROM dbo.Installments AS i
                    INNER JOIN dbo.Loans AS l ON l.Id = i.LoanId
                    INNER JOIN dbo.Clients AS c ON c.Id = l.ClientId
                    WHERE i.Status <> 3
                        AND CONVERT(date, i.DueDate) < CONVERT(date, SYSUTCDATETIME())
                    ORDER BY i.DueDate, c.FullName;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.sp_GetOverdueInstallments;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_LoanPortfolioSummary;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.fn_LoanPendingBalance;");
        }
    }
}
