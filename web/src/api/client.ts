import type { Client, Dashboard, Loan, LoanDetail, LoginResponse, Payment } from "../types/models";

const API_URL = import.meta.env.VITE_API_URL ?? "http://localhost:5052/api";
const TOKEN_KEY = "crediprest.token";

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
    const payload = await response.json().catch(() => ({ error: "Error inesperado" }));
    throw new Error(payload.error ?? "Error inesperado");
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
  dashboard: () => request<Dashboard>("/dashboard"),
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
  payments: (loanId: string) => request<Payment[]>(`/loans/${loanId}/payments`),
  registerPayment: (payload: unknown) => request<LoanDetail>("/payments", { method: "POST", body: JSON.stringify(payload) })
};
