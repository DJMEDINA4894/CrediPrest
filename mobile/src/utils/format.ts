import type { CurrencyType, Installment } from "../types/models";

export const currencyLabels: Record<CurrencyType, string> = {
  1: "C$",
  2: "USD"
};

export const statusLabels = {
  1: "Activo",
  2: "Cancelado",
  3: "Vencido"
};

export const installmentStatusLabels = {
  1: "Pendiente",
  2: "Parcial",
  3: "Pagada",
  4: "Atrasada"
};

export function money(value: number, currency: string = "C$") {
  return `${currency} ${value.toLocaleString("es-NI", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

export function dateOnly(value: string) {
  const datePart = value.split("T")[0];
  const [year, month, day] = datePart.split("-").map(Number);
  const date = year && month && day ? new Date(year, month - 1, day) : new Date(value);

  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("es-NI", { day: "numeric", month: "long", year: "numeric" }).format(date);
}

export function installmentPendingAmount(installment: Installment) {
  return Math.max(0, installment.paymentAmount - installment.amountPaid);
}

export function dateInputValue(value?: string) {
  return value ? value.slice(0, 10) : new Date().toISOString().slice(0, 10);
}
