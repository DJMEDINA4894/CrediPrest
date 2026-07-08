namespace CrediPrest.Application.DTOs.Dashboard;

public sealed record DashboardDto(
    decimal TotalLoanedCordobas,
    decimal TotalLoanedUsd,
    decimal TotalRecoveredCordobas,
    decimal TotalRecoveredUsd,
    decimal PendingCordobas,
    decimal PendingUsd,
    decimal EstimatedInterestCordobas,
    decimal EstimatedInterestUsd,
    decimal RealInterestCollectedCordobas,
    decimal RealInterestCollectedUsd,
    int ActiveClients,
    int ActiveLoans,
    int OverdueLoans,
    int OverdueInstallments,
    int DueTodayInstallments,
    int DueThisWeekInstallments,
    int PaidTodayCount,
    int PaidThisWeekCount,
    int PaidThisMonthCount);
