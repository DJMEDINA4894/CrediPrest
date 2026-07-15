export type CurrencyType = 1 | 2;
export type PaymentFrequency = 1 | 2 | 3;
export type LoanStatus = 1 | 2 | 3;
export type InstallmentStatus = 1 | 2 | 3 | 4;
export type PaymentMethod = 1 | 2 | 3 | 4 | 5;

export interface LoginResponse {
  token: string;
  userId: string;
  userName: string;
  email: string;
  fullName: string;
  role: "Admin" | "Lender" | "Client";
  clientId?: string;
}

export interface Client {
  id: string;
  fullName: string;
  identificationNumber: string;
  phone: string;
  address: string;
  email?: string;
  personalReference1?: string;
  referencePhone1?: string;
  personalReference2?: string;
  referencePhone2?: string;
  bacAccountNumber?: string;
  lafiseAccountNumber?: string;
  bamproAccountNumber?: string;
  preferredPaymentMethod: string;
  hasKash: boolean;
  kashAccount?: string;
  notes?: string;
  isActive: boolean;
  registeredAtUtc: string;
  activeLoans: number;
  pendingCordobas: number;
  pendingUsd: number;
  paidPrincipalCordobas: number;
  paidInterestCordobas: number;
  paidPrincipalUsd: number;
  paidInterestUsd: number;
}

export interface Loan {
  id: string;
  clientId: string;
  clientName: string;
  clientIdentificationNumber: string;
  lenderName?: string;
  lenderIdentificationNumber?: string;
  referenceName?: string;
  principalAmount: number;
  currency: CurrencyType;
  monthlyInterestRate: number;
  termMonths: number;
  paymentFrequency: PaymentFrequency;
  startDate: string;
  endDate: string;
  status: LoanStatus;
  totalInterest: number;
  totalToPay: number;
  totalPaid: number;
  paidPrincipal: number;
  paidInterest: number;
  lateFeesTotal: number;
  lateFeesPaid: number;
  lateFeesPending: number;
  pendingBalance: number;
  notes?: string;
  agreementCity?: string;
  lateFeeDescription?: string;
}

export interface Installment {
  id: string;
  installmentNumber: number;
  dueDate: string;
  principalAmount: number;
  interestAmount: number;
  paymentAmount: number;
  remainingBalance: number;
  status: InstallmentStatus;
  paidAtUtc?: string;
  amountPaid: number;
}

export interface LoanCharge {
  id: string;
  type: number;
  periodNumber: number;
  periodStartDate: string;
  periodEndDate: string;
  amount: number;
  amountPaid: number;
  pendingAmount: number;
  notes?: string;
  calculatedAtUtc?: string;
  allocations?: LoanChargeAllocation[];
}

export interface LoanChargeAllocation {
  installmentId: string;
  amount: number;
  amountPaid: number;
  pendingAmount: number;
}

export interface LoanDetail {
  loan: Loan;
  installments: Installment[];
  charges: LoanCharge[];
}

export interface LoanRecalculationPreview {
  loanId: string;
  mode: 1 | 2 | 3;
  effectiveDate: string;
  firstDueDate: string;
  outstandingPrincipal: number;
  extraordinaryPaymentAmount: number;
  principalAfterPayment: number;
  currentInstallmentAmount: number;
  newInstallmentAmount: number;
  paidInstallments: number;
  currentRemainingInstallments: number;
  newRemainingInstallments: number;
  currentPendingInterest: number;
  newInterest: number;
  interestSavings: number;
  newPendingTotal: number;
}

export interface Payment {
  id: string;
  loanId: string;
  installmentId?: string;
  loanChargeId?: string;
  paymentDate: string;
  amountPaid: number;
  type: 1 | 2;
  paymentMethod: PaymentMethod;
  referenceNumber?: string;
  notes?: string;
  receiptId?: string;
  receiptFileName?: string;
  recalculationMode?: 1 | 2 | 3;
  previousOutstandingPrincipal?: number;
  newOutstandingPrincipal?: number;
  previousInstallmentAmount?: number;
  newInstallmentAmount?: number;
  previousInstallmentCount?: number;
  newInstallmentCount?: number;
  previousPendingInterest?: number;
  newPendingInterest?: number;
}

export interface Dashboard {
  totalLoanedCordobas: number;
  totalLoanedUsd: number;
  totalRecoveredCordobas: number;
  totalRecoveredUsd: number;
  pendingCordobas: number;
  pendingUsd: number;
  estimatedInterestCordobas: number;
  estimatedInterestUsd: number;
  activeClients: number;
  activeLoans: number;
  overdueLoans: number;
  overdueInstallments: number;
  dueTodayInstallments: number;
  dueThisWeekInstallments: number;
  paidTodayCordobas: number;
  paidTodayUsd: number;
  paidThisWeekCordobas: number;
  paidThisWeekUsd: number;
  paidThisMonthCordobas: number;
  paidThisMonthUsd: number;
  paidTodayCount: number;
  paidThisWeekCount: number;
  paidThisMonthCount: number;
}

export interface Notification {
  id: string;
  type: 1 | 2 | 3 | 4 | 5;
  title: string;
  message: string;
  isRead: boolean;
  createdAtUtc: string;
  relatedEntityId: string;
  relatedLoanId?: string;
  dueDate?: string;
}

export interface AppUser {
  id: string;
  clientId?: string;
  userName: string;
  email: string;
  fullName: string;
  phone?: string;
  identificationNumber?: string;
  role: 1 | 2 | 3;
  isActive: boolean;
}
