import type { Client, Loan } from "../types/models";

export type RootStackParamList = {
  Login: undefined;
  Home: undefined;
  Dashboard: undefined;
  Reports: undefined;
  Notifications: undefined;
  Clients: undefined;
  ClientForm: { client?: Client } | undefined;
  Loans: undefined;
  LoanForm: { loan?: Loan; clientId?: string } | undefined;
  LoanDetail: { loanId: string };
  Payments: { loan?: Loan } | undefined;
  ClientPortal: undefined;
};
