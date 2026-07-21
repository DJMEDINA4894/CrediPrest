namespace CrediPrest.Domain.Enums;

public enum NotificationType
{
    OverdueInstallment = 1,
    DueTodayInstallment = 2,
    LateFeeWarning = 3,
    LateFeeApplied = 4,
    LateFeeRateChanged = 5,
    PaymentReceived = 6,
    ClientCreated = 7,
    LoanCreated = 8
}
