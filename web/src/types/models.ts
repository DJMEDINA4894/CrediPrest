export type CurrencyType = 1 | 2;
export type PaymentFrequency = 1 | 2 | 3;
export type LoanStatus = 1 | 2 | 3;
export type InstallmentStatus = 1 | 2 | 3 | 4;
export type PaymentMethod = 1 | 2 | 3 | 4;

export interface LoginResponse {
  token: string;
  userId: string;
  userName: string;
  email: string;
  fullName: string;
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
}

export interface Loan {
  id: string;
  clientId: string;
  clientName: string;
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
  pendingBalance: number;
  notes?: string;
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

export interface LoanDetail {
  loan: Loan;
  installments: Installment[];
}

export interface Payment {
  id: string;
  loanId: string;
  installmentId: string;
  paymentDate: string;
  amountPaid: number;
  paymentMethod: PaymentMethod;
  referenceNumber?: string;
  notes?: string;
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
