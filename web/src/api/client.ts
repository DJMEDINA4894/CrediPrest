import type { AppUser, Client, Dashboard, Loan, LoanDetail, LoginResponse, Notification, Payment } from "../types/models";

const LOCAL_API_URL = "http://localhost:5052/api";
const PRODUCTION_API_URL = "https://creadiprest-c6a3e6dya2cbhtf9.centralus-01.azurewebsites.net/api";
const API_URL = import.meta.env.VITE_API_URL ?? (import.meta.env.PROD ? PRODUCTION_API_URL : LOCAL_API_URL);
const TOKEN_KEY = "crediprest.token";

export class ApiRequestError extends Error {
  constructor(message: string, public readonly statusCode: number) {
    super(message);
    this.name = "ApiRequestError";
  }
}

export function getToken() {
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string) {
  localStorage.setItem(TOKEN_KEY, token);
}

export function clearToken() {
  localStorage.removeItem(TOKEN_KEY);
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = getToken();
  const response = await fetch(`${API_URL}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options.headers
    }
  });

  if (!response.ok) {
    const contentType = response.headers.get("content-type") ?? "";
    const fallbackMessage = response.status === 401
      ? "Tu sesión venció o no tienes permiso para entrar. Inicia sesión nuevamente."
      : `No se pudo completar la solicitud (${response.status}).`;

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
  notifications: () => request<Notification[]>("/notifications"),
  markNotificationRead: (id: string) => request<void>(`/notifications/${id}/read`, { method: "POST" }),
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
  createLoan: (payload: unknown) => request<LoanDetail>("/loans", { method: "POST", body: JSON.stringify(payload) }),
  updateLoan: (id: string, payload: unknown) => request<LoanDetail>(`/loans/${id}`, { method: "PUT", body: JSON.stringify(payload) }),
  cancelLoan: (id: string) => request<void>(`/loans/${id}/cancel`, { method: "POST" }),
  deleteLoan: (id: string) => request<void>(`/loans/${id}`, { method: "DELETE" }),
  payments: (loanId: string) => request<Payment[]>(`/loans/${loanId}/payments`),
  registerPayment: (payload: unknown) => request<LoanDetail>("/payments", { method: "POST", body: JSON.stringify(payload) })
};
