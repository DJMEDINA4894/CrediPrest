import type { AppUser, Client, Dashboard, Loan, LoanDetail, LoanRecalculationPreview, LoginResponse, Notification, Payment } from "../types/models";

const PRODUCTION_API_URL = "https://creadiprest-c6a3e6dya2cbhtf9.centralus-01.azurewebsites.net/api";
const API_URL = process.env.EXPO_PUBLIC_API_URL ?? PRODUCTION_API_URL;
export const CONNECTION_ERROR_MESSAGE = "No se pudo conectar. Revisa tu conexión a internet o Wi-Fi e inténtalo nuevamente.";

export class ApiRequestError extends Error {
  constructor(message: string, public readonly statusCode: number) {
    super(message);
    this.name = "ApiRequestError";
  }
}

let authToken: string | null = null;

export function setApiToken(token: string | null) {
  authToken = token;
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  let response: Response;

  try {
    response = await fetch(`${API_URL}${path}`, {
      ...options,
      headers: {
        "Content-Type": "application/json",
        ...(authToken ? { Authorization: `Bearer ${authToken}` } : {}),
        ...options.headers
      }
    });
  } catch {
    throw new ApiRequestError(CONNECTION_ERROR_MESSAGE, 0);
  }

  if (!response.ok) {
    const fallbackMessage = response.status === 401
      ? "Tu sesion vencio o no tienes permiso para entrar."
      : `No se pudo completar la solicitud (${response.status}).`;
    const contentType = response.headers.get("content-type") ?? "";

    if (contentType.includes("application/json")) {
      const payload = await response.json().catch(() => null) as { error?: string; title?: string } | null;
      throw new ApiRequestError(payload?.error ?? payload?.title ?? fallbackMessage, response.status);
    }

    const text = await response.text().catch(() => "");
    throw new ApiRequestError(text.trim() || fallbackMessage, response.status);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

export const api = {
  login: (userOrEmail: string, password: string) =>
    request<LoginResponse>("/auth/login", {
      method: "POST",
      body: JSON.stringify({ userOrEmail, password })
    }),
  clientLogin: (identificationOrPhone: string) =>
    request<LoginResponse>("/auth/client-login", {
      method: "POST",
      body: JSON.stringify({ identificationOrPhone })
    }),
  dashboard: () => request<Dashboard>("/dashboard"),
  notifications: () => request<Notification[]>(`/notifications?fresh=${Date.now()}`, {
    cache: "no-store",
    headers: {
      "Cache-Control": "no-cache",
      Pragma: "no-cache"
    }
  }),
  markNotificationRead: (id: string) => request<void>(`/notifications/${id}/read`, { method: "POST" }),
  registerPushDevice: (payload: { expoPushToken: string; platform: string; deviceName?: string }) =>
    request<void>("/notifications/push-devices", { method: "POST", body: JSON.stringify(payload) }),
  unregisterPushDevice: (expoPushToken: string) =>
    request<void>("/notifications/push-devices/unregister", {
      method: "POST",
      body: JSON.stringify({ expoPushToken })
    }),
  clientPaymentPlans: () => request<LoanDetail[]>("/client-portal/payment-plans"),
  users: () => request<AppUser[]>("/users"),
  createUser: (payload: unknown) => request<AppUser>("/users", { method: "POST", body: JSON.stringify(payload) }),
  updateUser: (id: string, payload: unknown) => request<AppUser>(`/users/${id}`, { method: "PUT", body: JSON.stringify(payload) }),
  deleteUser: (id: string) => request<void>(`/users/${id}`, { method: "DELETE" }),
  clients: (search = "") => request<Client[]>(`/clients${search ? `?search=${encodeURIComponent(search)}` : ""}`),
  createClient: (payload: unknown) => request<Client>("/clients", { method: "POST", body: JSON.stringify(payload) }),
  updateClient: (id: string, payload: unknown) => request<Client>(`/clients/${id}`, { method: "PUT", body: JSON.stringify(payload) }),
  activateClient: (id: string) => request<Client>(`/clients/${id}/activate`, { method: "POST" }),
  deactivateClient: (id: string) => request<Client>(`/clients/${id}/deactivate`, { method: "POST" }),
  deleteClient: (id: string) => request<void>(`/clients/${id}`, { method: "DELETE" }),
  loans: () => request<Loan[]>("/loans"),
  loanDetail: (id: string) => request<LoanDetail>(`/loans/${id}`),
  loanAgreementSource: (id: string) => ({
    uri: `${API_URL}/loans/${id}/agreement`,
    headers: authToken ? { Authorization: `Bearer ${authToken}` } : undefined
  }),
  loanPaymentTableSource: (id: string, clientPortal = false) => ({
    uri: clientPortal
      ? `${API_URL}/client-portal/payment-plans/${id}/pdf`
      : `${API_URL}/loans/${id}/payment-plan.pdf`,
    headers: authToken ? { Authorization: `Bearer ${authToken}` } : undefined
  }),
  createLoan: (payload: unknown) => request<LoanDetail>("/loans", { method: "POST", body: JSON.stringify(payload) }),
  updateLoan: (id: string, payload: unknown) => request<LoanDetail>(`/loans/${id}`, { method: "PUT", body: JSON.stringify(payload) }),
  previewExtraordinaryPayment: (id: string, payload: unknown) =>
    request<LoanRecalculationPreview>(`/loans/${id}/extraordinary-payment/preview`, { method: "POST", body: JSON.stringify(payload) }),
  registerExtraordinaryPayment: (id: string, payload: unknown) =>
    request<LoanDetail>(`/loans/${id}/extraordinary-payment`, { method: "POST", body: JSON.stringify(payload) }),
  cancelLoan: (id: string) => request<void>(`/loans/${id}/cancel`, { method: "POST" }),
  deleteLoan: (id: string) => request<void>(`/loans/${id}`, { method: "DELETE" }),
  payments: (loanId: string) => request<Payment[]>(`/loans/${loanId}/payments`),
  paymentReceiptSource: (receiptId: string) => ({
    uri: `${API_URL}/payments/receipts/${receiptId}`,
    headers: authToken ? { Authorization: `Bearer ${authToken}` } : undefined
  }),
  registerPayment: (payload: unknown) =>
    request<LoanDetail>("/payments", {
      method: "POST",
      body: JSON.stringify(payload)
    })
};
