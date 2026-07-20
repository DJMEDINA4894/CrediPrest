import type { AppUser, Client, Loan } from "../types/models";

export type RootStackParamList = {
  Login: undefined;
  Home: undefined;
  Dashboard: undefined;
  Reports: undefined;
  Users: undefined;
  UserForm: { user?: AppUser } | undefined;
  Settings: undefined;
  Notifications: undefined;
  Clients: undefined;
  ClientForm: { client?: Client } | undefined;
  Loans: undefined;
  LoanForm: { loan?: Loan; clientId?: string } | undefined;
  LoanDetail: { loanId: string };
  LoanRecalculation: { loanId: string };
  Payments: { loan?: Loan; loanId?: string } | undefined;
  ClientPortal: undefined;
};
