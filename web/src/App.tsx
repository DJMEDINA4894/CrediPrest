import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { ApiRequestError, api, clearToken, getToken, setToken } from "./api/client";
import type { AppUser, Client, Dashboard, Installment, Loan, LoanDetail, LoginResponse, Notification } from "./types/models";

type View = "dashboard" | "clients" | "loans" | "payments" | "reports" | "users" | "settings" | "clientPortal";
type ConfirmDialogState = {
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel?: string;
  tone?: "danger" | "default";
  onConfirm: () => void | Promise<void>;
};

const currencyLabels = { 1: "C$", 2: "USD" };
const frequencyLabels = { 1: "Semanal", 2: "Quincenal", 3: "Mensual" };
const termLabels = { 1: "Cantidad de pagos semanales", 2: "Cantidad de pagos quincenales", 3: "Cantidad de pagos mensuales" };
const termPlaceholders = { 1: "Ej. 8 semanas", 2: "Ej. 6 quincenas", 3: "Ej. 6 meses" };
const statusLabels = { 1: "Activo", 2: "Cancelado", 3: "Vencido" };
const installmentStatusLabels = { 1: "Pendiente", 2: "Parcial", 3: "Pagada", 4: "Atrasada" };
const paymentPreferenceLabels: Record<string, string> = {
  cash: "Efectivo",
  bac: "BAC",
  lafise: "Lafise",
  bampro: "Bampro",
  kash: "Kash"
};
const longDateFormatter = new Intl.DateTimeFormat("es-NI", { day: "numeric", month: "long", year: "numeric" });
const identificationPattern = "\\d{3}-?\\d{6}-?\\d{4}[A-Za-z]";
const phonePattern = "\\+?[0-9 ()-]{8,20}";
const bankAccountPattern = "[0-9]{6,24}";
const kashPattern = "[A-Za-z0-9@._+\\- ]{3,80}";
const clientFieldLabels: Record<string, string> = {
  fullName: "Nombre completo",
  identificationNumber: "Cédula",
  phone: "Teléfono",
  email: "Correo",
  address: "Dirección",
  referencePhone1: "Teléfono referencia 1",
  preferredPaymentMethod: "Forma de pago preferida",
  paymentAccount: "Cuenta o Kash"
};

const clientFieldHints: Record<string, string> = {
  identificationNumber: "La cédula debe tener el formato 001-010101-0001A o 0010101010001A.",
  phone: "El teléfono debe tener de 8 a 15 dígitos. Puede usar +, espacios o guiones.",
  email: "El correo debe tener un formato válido, por ejemplo nombre@correo.com.",
  referencePhone1: "El teléfono de referencia 1 debe tener de 8 a 15 dígitos.",
  paymentAccount: "Las cuentas bancarias deben tener solo números de 6 a 24 dígitos. Kash debe tener de 3 a 80 caracteres."
};
const APP_FONT_SIZE_KEY = "crediprest.fontSize";
const DEFAULT_APP_FONT_SIZE = 16;

const emptyDashboard: Dashboard = {
  totalLoanedCordobas: 0,
  totalLoanedUsd: 0,
  totalRecoveredCordobas: 0,
  totalRecoveredUsd: 0,
  pendingCordobas: 0,
  pendingUsd: 0,
  estimatedInterestCordobas: 0,
  estimatedInterestUsd: 0,
  activeClients: 0,
  activeLoans: 0,
  overdueLoans: 0,
  overdueInstallments: 0,
  dueTodayInstallments: 0,
  dueThisWeekInstallments: 0,
  paidTodayCordobas: 0,
  paidTodayUsd: 0,
  paidThisWeekCordobas: 0,
  paidThisWeekUsd: 0,
  paidThisMonthCordobas: 0,
  paidThisMonthUsd: 0,
  paidTodayCount: 0,
  paidThisWeekCount: 0,
  paidThisMonthCount: 0
};

function money(value: number, currency = "C$") {
  return `${currency} ${value.toLocaleString("es-NI", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function clientDebt(client: Client) {
  if (client.pendingCordobas > 0 && client.pendingUsd > 0) {
    return `${money(client.pendingCordobas)} / ${money(client.pendingUsd, "USD")}`;
  }

  if (client.pendingUsd > 0) {
    return money(client.pendingUsd, "USD");
  }

  return money(client.pendingCordobas);
}

function portfolioMoney(cordobas: number, usd: number) {
  if (cordobas > 0 && usd > 0) {
    return `${money(cordobas)} / ${money(usd, "USD")}`;
  }

  if (usd > 0) {
    return money(usd, "USD");
  }

  return money(cordobas);
}

function loanDisplayName(loan: Loan) {
  return loan.referenceName?.trim() ? `${loan.clientName} - ${loan.referenceName}` : loan.clientName;
}

function dualMoney(cordobas = 0, usd = 0) {
  if (cordobas <= 0 && usd <= 0) {
    return `${money(0)} / ${money(0, "USD")}`;
  }

  return portfolioMoney(cordobas, usd);
}

function installmentPendingAmount(installment: Installment) {
  return Math.max(0, installment.paymentAmount - installment.amountPaid);
}

function dateOnly(value: string) {
  const datePart = value.split("T")[0];
  const [year, month, day] = datePart.split("-").map(Number);
  const date = year && month && day ? new Date(year, month - 1, day) : new Date(value);

  if (Number.isNaN(date.getTime())) {
    return value;
  }

  const parts = longDateFormatter.formatToParts(date);
  const formattedDay = parts.find((part) => part.type === "day")?.value ?? "";
  const formattedMonth = parts.find((part) => part.type === "month")?.value ?? "";
  const formattedYear = parts.find((part) => part.type === "year")?.value ?? "";
  const capitalizedMonth = formattedMonth.charAt(0).toUpperCase() + formattedMonth.slice(1);

  return `${formattedDay} ${capitalizedMonth} ${formattedYear}`.trim();
}

function dateInputValue(value?: string) {
  return value ? value.slice(0, 10) : new Date().toISOString().slice(0, 10);
}

function printableText(value: string) {
  return value
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[^\x20-\x7E]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function escapePdfText(value: string) {
  return printableText(value).replace(/\\/g, "\\\\").replace(/\(/g, "\\(").replace(/\)/g, "\\)");
}

function truncatePdfText(value: string, maxLength: number) {
  const text = printableText(value);
  return text.length > maxLength ? `${text.slice(0, Math.max(0, maxLength - 3))}...` : text;
}

function pdfText(x: number, y: number, value: string, size = 9, font = "F1") {
  return `BT /${font} ${size} Tf ${x.toFixed(2)} ${y.toFixed(2)} Td (${escapePdfText(value)}) Tj ET`;
}

function pdfLine(x1: number, y1: number, x2: number, y2: number) {
  return `${x1.toFixed(2)} ${y1.toFixed(2)} m ${x2.toFixed(2)} ${y2.toFixed(2)} l S`;
}

function buildPaymentTablePdf(detail: LoanDetail) {
  const pageWidth = 842;
  const pageHeight = 595;
  const margin = 32;
  const rowHeight = 18;
  const columns = [
    { title: "#", width: 28, max: 4 },
    { title: "Vence", width: 92, max: 18 },
    { title: "Capital", width: 90, max: 18 },
    { title: "Interes", width: 90, max: 18 },
    { title: "Cuota", width: 90, max: 18 },
    { title: "Pagado", width: 90, max: 18 },
    { title: "Pendiente", width: 95, max: 18 },
    { title: "Debe", width: 90, max: 18 },
    { title: "Estado", width: 85, max: 16 }
  ];
  const tableWidth = columns.reduce((sum, column) => sum + column.width, 0);
  const currency = currencyLabels[detail.loan.currency];
  const rows = detail.installments.map((installment) => [
    String(installment.installmentNumber),
    dateOnly(installment.dueDate),
    money(installment.principalAmount, currency),
    money(installment.interestAmount, currency),
    money(installment.paymentAmount, currency),
    money(installment.amountPaid, currency),
    money(installmentPendingAmount(installment), currency),
    money(installment.remainingBalance, currency),
    installmentStatusLabels[installment.status]
  ]);

  const pages: string[] = [];
  let rowIndex = 0;
  let pageNumber = 1;

  while (rowIndex < rows.length || pages.length === 0) {
    const lines: string[] = ["0.2 w"];
    let y = pageHeight - margin;

    lines.push(pdfText(margin, y, "CrediPrest - Tabla de pagos", 16, "F2"));
    lines.push(pdfText(pageWidth - 115, y, `Pagina ${pageNumber}`, 8));
    y -= 22;
    lines.push(pdfText(margin, y, `Cliente: ${detail.loan.clientName}`, 10, "F2"));
    lines.push(pdfText(360, y, `Frecuencia: ${frequencyLabels[detail.loan.paymentFrequency]}`, 10));
    y -= 16;
    if (detail.loan.lenderName) {
      lines.push(pdfText(margin, y, `Prestamista: ${detail.loan.lenderName}`, 9));
      y -= 16;
    }
    if (detail.loan.referenceName) {
      lines.push(pdfText(margin, y, `Referencia: ${detail.loan.referenceName}`, 9));
      y -= 16;
    }
    lines.push(pdfText(margin, y, `Prestado: ${money(detail.loan.principalAmount, currency)}`, 9));
    lines.push(pdfText(190, y, `Interes mensual: ${detail.loan.monthlyInterestRate}%`, 9));
    lines.push(pdfText(360, y, `Total: ${money(detail.loan.totalToPay, currency)}`, 9));
    lines.push(pdfText(520, y, `Pagado: ${money(detail.loan.totalPaid, currency)}`, 9));
    lines.push(pdfText(670, y, `Debe: ${money(detail.loan.pendingBalance, currency)}`, 9));
    y -= 22;
    if (detail.loan.lateFeesPending > 0) {
      lines.push(pdfText(margin, y, `Mora pendiente: ${money(detail.loan.lateFeesPending, currency)}`, 9, "F2"));
      y -= 18;
    }

    const tableLeft = margin;
    const headerTop = y + 6;
    const headerBottom = y - rowHeight + 5;
    lines.push(pdfLine(tableLeft, headerTop, tableLeft + tableWidth, headerTop));
    lines.push(pdfLine(tableLeft, headerBottom, tableLeft + tableWidth, headerBottom));

    let x = tableLeft + 4;
    columns.forEach((column) => {
      lines.push(pdfText(x, y - 7, column.title, 8, "F2"));
      x += column.width;
    });
    y -= rowHeight;

    while (rowIndex < rows.length && y > margin + 32) {
      x = tableLeft + 4;
      columns.forEach((column, columnIndex) => {
        lines.push(pdfText(x, y - 7, truncatePdfText(rows[rowIndex][columnIndex], column.max), 8));
        x += column.width;
      });
      lines.push(pdfLine(tableLeft, y - rowHeight + 5, tableLeft + tableWidth, y - rowHeight + 5));
      y -= rowHeight;
      rowIndex += 1;
    }

    lines.push(pdfText(margin, margin, `Generado: ${dateOnly(new Date().toISOString())}`, 8));
    pages.push(lines.join("\n"));
    pageNumber += 1;
  }

  const objects = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    `<< /Type /Pages /Kids [${pages.map((_, index) => `${5 + index * 2} 0 R`).join(" ")}] /Count ${pages.length} >>`,
    "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"
  ];

  pages.forEach((content, index) => {
    const contentObjectId = 6 + index * 2;
    objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${pageWidth} ${pageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents ${contentObjectId} 0 R >>`);
    objects.push(`<< /Length ${content.length} >>\nstream\n${content}\nendstream`);
  });

  let pdf = "%PDF-1.4\n";
  const offsets = [0];
  objects.forEach((object, index) => {
    offsets.push(pdf.length);
    pdf += `${index + 1} 0 obj\n${object}\nendobj\n`;
  });
  const xrefOffset = pdf.length;
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  offsets.slice(1).forEach((offset) => {
    pdf += `${String(offset).padStart(10, "0")} 00000 n \n`;
  });
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xrefOffset}\n%%EOF`;

  return new Blob([pdf], { type: "application/pdf" });
}

function downloadPaymentTablePdf(detail: LoanDetail) {
  const blob = buildPaymentTablePdf(detail);
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  const clientName = printableText(loanDisplayName(detail.loan)).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");

  link.href = url;
  link.download = `tabla-pagos-${clientName || "prestamo"}.pdf`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function downloadLoanAgreement(detail: LoanDetail) {
  return api.loanAgreement(detail.loan.id).then((blob) => {
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    const clientName = printableText(loanDisplayName(detail.loan)).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");

    link.href = url;
    link.download = `acuerdo-prestamo-${clientName || "prestamo"}.docx`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  });
}

function defaultPaymentInstallment(installments: Installment[]) {
  const ordered = [...installments].sort((left, right) => dateInputValue(left.dueDate).localeCompare(dateInputValue(right.dueDate)));
  const today = dateInputValue();
  const oldestDueInstallment = ordered.find((installment) => dateInputValue(installment.dueDate) <= today);

  return oldestDueInstallment ?? ordered[0] ?? null;
}

function formValue(form: FormData, key: string) {
  return String(form.get(key) ?? "").trim();
}

async function paymentReceiptPayload(value: FormDataEntryValue | null) {
  if (!(value instanceof File) || value.size === 0) {
    return {};
  }

  const allowedTypes = ["image/jpeg", "image/png", "image/webp"];
  if (!allowedTypes.includes(value.type)) {
    throw new Error("El comprobante debe ser una imagen JPG, PNG o WEBP.");
  }

  const maxBytes = 5 * 1024 * 1024;
  if (value.size > maxBytes) {
    throw new Error("El comprobante no puede superar 5 MB.");
  }

  const dataUrl = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error("No se pudo leer la imagen del comprobante."));
    reader.onload = () => typeof reader.result === "string"
      ? resolve(reader.result)
      : reject(new Error("No se pudo leer la imagen del comprobante."));
    reader.readAsDataURL(value);
  });

  return {
    receiptImageBase64: dataUrl,
    receiptFileName: value.name,
    receiptContentType: value.type
  };
}

function savedAppFontSize() {
  const value = Number(localStorage.getItem(APP_FONT_SIZE_KEY));
  return Number.isFinite(value) && value >= 14 && value <= 20 ? value : DEFAULT_APP_FONT_SIZE;
}

function numberValue(form: FormData, key: string) {
  return Number(form.get(key) ?? 0);
}

function GmailBrandIcon() {
  return (
    <svg viewBox="0 0 48 36" aria-hidden="true" focusable="false">
      <path d="M4 4h40v28H4z" fill="#fff" />
      <path d="M4 8.5 24 23 44 8.5V4L24 18.5 4 4z" fill="#ea4335" />
      <path d="M4 8.5V32h7V13.6z" fill="#4285f4" />
      <path d="M44 8.5V32h-7V13.6z" fill="#34a853" />
      <path d="M11 13.6 4 8.5V4l7 5.1z" fill="#c5221f" />
      <path d="M37 13.6 44 8.5V4l-7 5.1z" fill="#fbbc04" />
    </svg>
  );
}

function ClaroBrandIcon() {
  return (
    <svg viewBox="0 0 76 34" aria-hidden="true" focusable="false">
      <rect width="76" height="34" rx="8" fill="#da291c" />
      <text x="38" y="22" textAnchor="middle" fill="#fff" fontFamily="Arial, Helvetica, sans-serif" fontSize="18" fontWeight="800">Claro</text>
      <path d="M56 7l5-4M60 11l7-1M56 15l5 4" stroke="#fff" strokeWidth="2" strokeLinecap="round" />
    </svg>
  );
}

function TigoBrandIcon() {
  return (
    <svg viewBox="0 0 76 34" aria-hidden="true" focusable="false">
      <rect width="76" height="34" rx="8" fill="#003da6" />
      <text x="38" y="23" textAnchor="middle" fill="#fff" fontFamily="Arial, Helvetica, sans-serif" fontSize="20" fontWeight="800">tigo</text>
    </svg>
  );
}

function EyeIcon({ crossed }: { crossed: boolean }) {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
      <circle cx="12" cy="12" r="2.8" fill="none" stroke="currentColor" strokeWidth="1.8" />
      {crossed && <path d="M4 4l16 16" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />}
    </svg>
  );
}

function paymentAccountValue(client: Client | null) {
  if (!client) {
    return "";
  }

  switch (client.preferredPaymentMethod) {
    case "bac":
      return client.bacAccountNumber ?? "";
    case "lafise":
      return client.lafiseAccountNumber ?? "";
    case "bampro":
      return client.bamproAccountNumber ?? "";
    case "kash":
      return client.kashAccount ?? "";
    default:
      return "";
  }
}

function needsPaymentAccount(paymentMethod: string) {
  return paymentMethod !== "cash";
}

function paymentAccountLabel(paymentMethod: string) {
  return {
    bac: "Cuenta BAC",
    lafise: "Cuenta Lafise",
    bampro: "Cuenta Bampro",
    kash: "Kash"
  }[paymentMethod] ?? "Cuenta";
}

function paymentAccountPattern(paymentMethod: string) {
  return paymentMethod === "kash" ? kashPattern : bankAccountPattern;
}

function paymentAccountHint(paymentMethod: string) {
  return paymentMethod === "kash"
    ? "Entre 3 y 80 caracteres. Puede incluir letras, números, espacios, @, punto, guion, + o _."
    : "Solo números, entre 6 y 24 dígitos.";
}

function isValidatedFormControl(element: Element): element is HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement {
  return element instanceof HTMLInputElement || element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement;
}

function getClientFormError(form: HTMLFormElement) {
  const invalidControl = Array.from(form.elements).find((element): element is HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement => {
    return isValidatedFormControl(element) && !element.validity.valid;
  });

  if (!invalidControl) {
    return null;
  }

  const fieldName = invalidControl.name;
  const fieldLabel = clientFieldLabels[fieldName] ?? "Campo";

  if (invalidControl.validity.valueMissing) {
    return `${fieldLabel} es requerido.`;
  }

  if (invalidControl.validity.tooShort) {
    return `${fieldLabel} está muy corto.`;
  }

  if (invalidControl.validity.tooLong) {
    return `${fieldLabel} está muy largo.`;
  }

  if (invalidControl.validity.typeMismatch) {
    return clientFieldHints[fieldName] ?? `${fieldLabel} no tiene un formato válido.`;
  }

  if (invalidControl.validity.patternMismatch) {
    return clientFieldHints[fieldName] ?? `${fieldLabel} no tiene el formato correcto.`;
  }

  return invalidControl.validationMessage || "Revisa los campos marcados antes de guardar.";
}

function claimValue(payload: Record<string, unknown>, keys: string[]) {
  for (const key of keys) {
    const value = payload[key];
    if (Array.isArray(value)) {
      return String(value[0] ?? "");
    }

    if (typeof value === "string" || typeof value === "number") {
      return String(value);
    }
  }

  return "";
}

function sessionFromToken(token: string | null): LoginResponse | null {
  if (!token) {
    return null;
  }

  try {
    const [, payloadPart] = token.split(".");
    if (!payloadPart) {
      return null;
    }

    const normalizedPayload = payloadPart.replace(/-/g, "+").replace(/_/g, "/");
    const paddedPayload = normalizedPayload.padEnd(Math.ceil(normalizedPayload.length / 4) * 4, "=");
    const payload = JSON.parse(atob(paddedPayload)) as Record<string, unknown>;
    const expiration = Number(payload.exp ?? 0);

    if (expiration && expiration * 1000 <= Date.now()) {
      return null;
    }

    const role = claimValue(payload, [
      "role",
      "roles",
      "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
    ]);

    if (role !== "Admin" && role !== "Lender" && role !== "Client") {
      return null;
    }

    return {
      token,
      userId: claimValue(payload, [
        "sub",
        "nameid",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
      ]),
      userName: claimValue(payload, ["name", "unique_name"]),
      email: claimValue(payload, [
        "email",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"
      ]),
      fullName: claimValue(payload, ["fullName"]) || claimValue(payload, ["name", "unique_name"]),
      role,
      clientId: claimValue(payload, ["clientId"]) || undefined
    };
  } catch {
    return null;
  }
}

export default function App() {
  const restoredSession = sessionFromToken(getToken());
  const [tokenAvailable, setTokenAvailable] = useState(Boolean(restoredSession));
  const [session, setSession] = useState<LoginResponse | null>(restoredSession);
  const [loginMode, setLoginMode] = useState<"staff" | "client">("staff");
  const [showAccessPassword, setShowAccessPassword] = useState(false);
  const [view, setView] = useState<View>("dashboard");
  const [dashboard, setDashboard] = useState<Dashboard>(emptyDashboard);
  const [clients, setClients] = useState<Client[]>([]);
  const [loans, setLoans] = useState<Loan[]>([]);
  const [loanDetail, setLoanDetail] = useState<LoanDetail | null>(null);
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [clientPlans, setClientPlans] = useState<LoanDetail[]>([]);
  const [users, setUsers] = useState<AppUser[]>([]);
  const [editingClient, setEditingClient] = useState<Client | null>(null);
  const [editingLoan, setEditingLoan] = useState<Loan | null>(null);
  const [editingUser, setEditingUser] = useState<AppUser | null>(null);
  const [newLoanClientId, setNewLoanClientId] = useState("");
  const [clientSearch, setClientSearch] = useState("");
  const [appFontSize, setAppFontSize] = useState(savedAppFontSize);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmDialog, setConfirmDialog] = useState<ConfirmDialogState | null>(null);
  const refreshInFlight = useRef(false);

  async function refresh(search = clientSearch) {
    if (refreshInFlight.current) {
      return;
    }

    refreshInFlight.current = true;
    setRefreshing(true);
    setError(null);
    try {
      if (session?.role === "Client") {
        const [notificationData, planData] = await Promise.all([
          api.notifications(),
          api.clientPaymentPlans()
        ]);
        setNotifications(notificationData);
        setClientPlans(planData);
        setClients([]);
        setLoans([]);
        setDashboard(emptyDashboard);
      } else {
        const [dashboardData, clientData, loanData, notificationData, userData] = await Promise.all([
          api.dashboard(),
          api.clients(search),
          api.loans(),
          api.notifications(),
          session?.role === "Admin" ? api.users() : Promise.resolve([] as AppUser[])
        ]);
        setDashboard(dashboardData);
        setClients(clientData);
        setLoans(loanData);
        setNotifications(notificationData);
        setUsers(userData);
        if (loanDetail) {
          if (loanData.some((loan) => loan.id === loanDetail.loan.id)) {
            setLoanDetail(await api.loanDetail(loanDetail.loan.id));
          } else {
            setLoanDetail(null);
          }
        }
      }
    } catch (err) {
      if (err instanceof ApiRequestError && err.statusCode === 401) {
        logout();
        setError(err.message);
      } else {
        setError(err instanceof Error ? err.message : "No se pudo actualizar la información.");
      }
    } finally {
      refreshInFlight.current = false;
      setRefreshing(false);
    }
  }

  useEffect(() => {
    if (tokenAvailable) {
      refresh();
    }
  }, [tokenAvailable, session?.role]);

  useEffect(() => {
    document.documentElement.style.fontSize = `${appFontSize}px`;
    localStorage.setItem(APP_FONT_SIZE_KEY, String(appFontSize));
  }, [appFontSize]);

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLoading(true);
    setError(null);
    const form = new FormData(event.currentTarget);

    try {
      const response = loginMode === "client"
        ? await api.clientLogin(formValue(form, "clientAccessKey"))
        : await api.login(formValue(form, "accessUser"), formValue(form, "accessCode"));
      setToken(response.token);
      setSession(response);
      setTokenAvailable(true);
      setView(response.role === "Client" ? "clientPortal" : "dashboard");
      setLoanDetail(null);
      setEditingClient(null);
      setEditingLoan(null);
      setEditingUser(null);
      setNewLoanClientId("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo iniciar sesión");
    } finally {
      setLoading(false);
    }
  }

  function logout() {
    clearToken();
    setTokenAvailable(false);
    setSession(null);
    setDashboard(emptyDashboard);
    setClients([]);
    setLoans([]);
    setLoanDetail(null);
    setNotifications([]);
    setClientPlans([]);
    setUsers([]);
    setEditingClient(null);
    setEditingLoan(null);
    setEditingUser(null);
    setNewLoanClientId("");
    setClientSearch("");
    setView("dashboard");
  }

  function startEditingClient(client: Client | null) {
    setError(null);
    setEditingClient(client);
  }

  async function submitClient(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formElement = event.currentTarget;
    const validationError = getClientFormError(formElement);

    if (validationError) {
      setError(validationError);
      formElement.reportValidity();
      return;
    }

    const form = new FormData(formElement);
    const preferredPaymentMethod = formValue(form, "preferredPaymentMethod") || "cash";
    const paymentAccount = formValue(form, "paymentAccount");
    const payload = {
      hasKash: preferredPaymentMethod === "kash" && Boolean(paymentAccount),
      fullName: formValue(form, "fullName"),
      identificationNumber: formValue(form, "identificationNumber"),
      phone: formValue(form, "phone"),
      address: formValue(form, "address"),
      email: formValue(form, "email") || undefined,
      personalReference1: formValue(form, "personalReference1") || undefined,
      referencePhone1: formValue(form, "referencePhone1") || undefined,
      personalReference2: undefined,
      referencePhone2: undefined,
      bacAccountNumber: preferredPaymentMethod === "bac" ? paymentAccount || undefined : undefined,
      lafiseAccountNumber: preferredPaymentMethod === "lafise" ? paymentAccount || undefined : undefined,
      bamproAccountNumber: preferredPaymentMethod === "bampro" ? paymentAccount || undefined : undefined,
      preferredPaymentMethod,
      kashAccount: preferredPaymentMethod === "kash" ? paymentAccount || undefined : undefined,
      notes: formValue(form, "notes") || undefined,
      isActive: true
    };

    try {
      setSaving(true);
      setError(null);
      if (editingClient) {
        await api.updateClient(editingClient.id, { ...payload, isActive: editingClient.isActive });
      } else {
        await api.createClient(payload);
      }
      setEditingClient(null);
      formElement.reset();
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el cliente");
    } finally {
      setSaving(false);
    }
  }

  async function setClientActive(id: string, isActive: boolean) {
    try {
      setLoading(true);
      setError(null);
      if (isActive) {
        await api.activateClient(id);
      } else {
        await api.deactivateClient(id);
      }
      if (editingClient?.id === id) {
        setEditingClient(null);
      }
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo actualizar el estado del cliente");
    } finally {
      setLoading(false);
    }
  }

  async function deleteClient(id: string) {
    const client = clients.find((item) => item.id === id);
    const confirmationName = client?.fullName ?? "este cliente";

    setConfirmDialog({
      title: "Eliminar cliente",
      message: `Eliminar ${confirmationName} borrará sus préstamos, cuotas y pagos. Esta acción no se puede deshacer.`,
      confirmLabel: "Eliminar definitivamente",
      cancelLabel: "Cancelar",
      tone: "danger",
      onConfirm: async () => {
        setConfirmDialog(null);
        try {
          setLoading(true);
          setError(null);
          await api.deleteClient(id);
          if (editingClient?.id === id) {
            setEditingClient(null);
          }
          if (loanDetail?.loan.clientId === id) {
            setLoanDetail(null);
          }
          if (editingLoan?.clientId === id) {
            setEditingLoan(null);
          }
          await refresh();
        } catch (err) {
          setError(err instanceof Error ? err.message : "No se pudo eliminar el cliente");
        } finally {
          setLoading(false);
        }
      }
    });
  }

  function startEditingLoan(loan: Loan | null) {
    setError(null);
    setEditingLoan(loan);
    setNewLoanClientId("");
    if (loan) {
      setView("loans");
    }
  }

  function startNewLoan(clientId = "") {
    setError(null);
    setEditingLoan(null);
    setNewLoanClientId(clientId);
    setView("loans");
  }

  async function deleteLoan(id: string) {
    const loan = loans.find((item) => item.id === id);
    const loanLabel = loan ? `${loanDisplayName(loan)} - ${money(loan.pendingBalance, currencyLabels[loan.currency])}` : "este préstamo";

    setConfirmDialog({
      title: "Eliminar préstamo",
      message: `Eliminar ${loanLabel} borrará sus cuotas y pagos asociados. Esta acción no se puede deshacer.`,
      confirmLabel: "Eliminar definitivamente",
      cancelLabel: "Cancelar",
      tone: "danger",
      onConfirm: async () => {
        setConfirmDialog(null);
        try {
          setLoading(true);
          setError(null);
          await api.deleteLoan(id);
          if (loanDetail?.loan.id === id) {
            setLoanDetail(null);
          }
          if (editingLoan?.id === id) {
            setEditingLoan(null);
          }
          await refresh();
        } catch (err) {
          setError(err instanceof Error ? err.message : "No se pudo eliminar el préstamo");
        } finally {
          setLoading(false);
        }
      }
    });
  }

  async function submitLoan(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const payload = {
      clientId: formValue(form, "clientId"),
      principalAmount: numberValue(form, "principalAmount"),
      currency: numberValue(form, "currency"),
      monthlyInterestRate: numberValue(form, "monthlyInterestRate"),
      termMonths: numberValue(form, "termMonths"),
      paymentFrequency: numberValue(form, "paymentFrequency"),
      startDate: formValue(form, "startDate"),
      referenceName: formValue(form, "referenceName") || undefined,
      notes: formValue(form, "notes") || undefined,
      agreementCity: formValue(form, "agreementCity") || undefined,
      lateFeeDescription: formValue(form, "lateFeeDescription") || undefined
    };

    try {
      setSaving(true);
      setError(null);
      const detail = editingLoan
        ? await api.updateLoan(editingLoan.id, { ...payload, status: editingLoan.status })
        : await api.createLoan(payload);
      setLoanDetail(detail);
      setEditingLoan(null);
      setNewLoanClientId("");
      formElement.reset();
      await refresh();
      setView("loans");
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el préstamo");
    } finally {
      setSaving(false);
    }
  }

  async function submitPayment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const installmentId = formValue(form, "installmentId");

    try {
      setError(null);
      const receipt = await paymentReceiptPayload(form.get("receiptImage"));
      const payload = {
        loanId: formValue(form, "loanId"),
        installmentId: installmentId || null,
        paymentDate: formValue(form, "paymentDate"),
        amountPaid: numberValue(form, "amountPaid"),
        paymentMethod: numberValue(form, "paymentMethod"),
        referenceNumber: formValue(form, "referenceNumber") || undefined,
        notes: formValue(form, "notes") || undefined,
        ...receipt
      };
      const detail = await api.registerPayment(payload);
      setLoanDetail(detail);
      formElement.reset();
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo registrar el pago");
    }
  }

  async function submitUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const password = formValue(form, "lenderAccessCode");
    const confirmPassword = formValue(form, "lenderAccessConfirm");

    if (password !== confirmPassword) {
      setError("La contraseña y la confirmación no coinciden.");
      return;
    }

    if (password && password.length <= 8) {
      setError("La contraseña debe tener más de 8 caracteres.");
      return;
    }

    const payload = {
      clientId: null,
      userName: formValue(form, "lenderAlias"),
      email: formValue(form, "email"),
      fullName: formValue(form, "fullName"),
      phone: formValue(form, "phone"),
      identificationNumber: formValue(form, "identificationNumber"),
      password,
      confirmPassword,
      role: 2,
      isActive: editingUser?.isActive ?? true
    };

    try {
      setSaving(true);
      setError(null);
      if (editingUser) {
        await api.updateUser(editingUser.id, {
          clientId: payload.clientId,
          email: payload.email,
          fullName: payload.fullName,
          phone: payload.phone,
          identificationNumber: payload.identificationNumber,
          password: password || null,
          confirmPassword: password ? confirmPassword : null,
          role: 2,
          isActive: payload.isActive
        });
      } else {
        await api.createUser(payload);
      }
      setEditingUser(null);
      formElement.reset();
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el usuario");
    } finally {
      setSaving(false);
    }
  }

  async function setUserActive(user: AppUser, isActive: boolean) {
    try {
      setLoading(true);
      setError(null);
      await api.updateUser(user.id, {
        clientId: null,
        email: user.email,
        fullName: user.fullName,
        phone: user.phone ?? null,
        identificationNumber: user.identificationNumber ?? null,
        password: null,
        confirmPassword: null,
        role: 2,
        isActive
      });
      if (editingUser?.id === user.id) {
        setEditingUser({ ...user, isActive });
      }
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo actualizar el usuario");
    } finally {
      setLoading(false);
    }
  }

  async function deleteUser(user: AppUser) {
    setConfirmDialog({
      title: "Eliminar usuario",
      message: `Eliminar ${user.fullName} quitará su acceso. Si tiene datos asociados, se desactivará para conservar el historial.`,
      confirmLabel: "Eliminar usuario",
      cancelLabel: "Cancelar",
      tone: "danger",
      onConfirm: async () => {
        setConfirmDialog(null);
        try {
          setLoading(true);
          setError(null);
          await api.deleteUser(user.id);
          if (editingUser?.id === user.id) {
            setEditingUser(null);
          }
          await refresh();
        } catch (err) {
          setError(err instanceof Error ? err.message : "No se pudo eliminar el usuario");
        } finally {
          setLoading(false);
        }
      }
    });
  }

  async function openLoan(id: string, nextView: View | null = "loans") {
    setLoading(true);
    try {
      setError(null);
      setLoanDetail(await api.loanDetail(id));
      if (nextView) {
        setView(nextView);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo abrir el préstamo");
    } finally {
      setLoading(false);
    }
  }

  async function markNotificationRead(id: string) {
    try {
      await api.markNotificationRead(id);
      setNotifications((items) => items.map((item) => item.id === id ? { ...item, isRead: true } : item));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo actualizar la notificación");
    }
  }

  const overdueLoans = useMemo(() => loans.filter((loan) => loan.status === 3), [loans]);
  const activeLoans = useMemo(() => loans.filter((loan) => loan.status === 1), [loans]);
  const activeClients = useMemo(() => clients.filter((client) => client.isActive), [clients]);
  const navigationItems: View[] = session?.role === "Client"
    ? ["clientPortal"]
    : session?.role === "Admin"
      ? ["dashboard", "clients", "loans", "payments", "reports", "users", "settings"]
      : ["dashboard", "clients", "loans", "payments", "reports"];

  if (!tokenAvailable) {
    return (
      <main className="login-shell">
        <section className="login-panel">
          <div className="login-copy">
            <span className="eyebrow">Gestión financiera</span>
            <h1>CrediPrest</h1>
            <p>Control de clientes, préstamos, cuotas, pagos e intereses en córdobas y dólares.</p>
            <div className="login-info-stack">
              <div className="login-info-card">
                <strong>Acceso para prestamistas</strong>
                <span>Solicita tu usuario directamente al administrador. Las cuentas se crean y activan solo después de revisión.</span>
              </div>
              <div className="login-info-card">
                <strong>Privacidad y seguridad</strong>
                <span>Cada cliente solo puede consultar sus propios préstamos. La información se protege con acceso autenticado, roles y trazabilidad.</span>
              </div>
            </div>
          </div>
          <form onSubmit={handleLogin} className="login-form" autoComplete="off">
            {loginMode === "staff" ? (
              <>
                <label>
                  Usuario o correo
                  <input name="accessUser" autoComplete="off" spellCheck={false} required />
                </label>
                <label>
                  Contraseña
                  <div className="password-field">
                    <input name="accessCode" type={showAccessPassword ? "text" : "password"} autoComplete="new-password" required />
                    <button
                      type="button"
                      className="password-toggle"
                      aria-label={showAccessPassword ? "Ocultar contraseña" : "Ver contraseña"}
                      title={showAccessPassword ? "Ocultar contraseña" : "Ver contraseña"}
                      onClick={() => setShowAccessPassword((value) => !value)}
                    >
                      <EyeIcon crossed={showAccessPassword} />
                    </button>
                  </div>
                </label>
              </>
            ) : (
              <>
                <div className="login-context">
                  <strong>Consulta de plan de pago</strong>
                  <span>Ingresa tu cédula o teléfono registrado.</span>
                </div>
                <label>
                  Cédula o teléfono
                  <input name="clientAccessKey" autoComplete="off" placeholder="001-010101-0001A o 88888888" required />
                </label>
              </>
            )}
            {error && <p className="alert">{error}</p>}
            <button type="submit" disabled={loading}>
              {loading ? "Procesando..." : loginMode === "client" ? "Consultar plan" : "Entrar"}
            </button>
            <div className="login-link-row">
              {loginMode !== "staff" && (
                <button
                  type="button"
                  className="login-secondary-action"
                  onClick={() => {
                    setError(null);
                    setLoginMode("staff");
                  }}
                >
                  Volver al acceso principal
                </button>
              )}
              {loginMode !== "client" && (
                <button
                  type="button"
                  className="login-secondary-action"
                  onClick={() => {
                    setError(null);
                    setLoginMode("client");
                  }}
                >
                  Consultar mi plan de pago
                </button>
              )}
            </div>
            <p className="login-contact-note">
              ¿Necesitas acceso como prestamista? Ponte en contacto con el administrador para validar tus datos y activar tu usuario.
            </p>
          </form>
          <div className="login-contact-strip">
            <strong>Contacto directo</strong>
            <div className="contact-grid">
              <a href="mailto:denisjmedinac4894@gmail.com" className="contact-link">
                <span className="contact-brand-icon gmail-brand"><GmailBrandIcon /></span>
                <span>
                  <small>Correo Gmail</small>
                  denisjmedinac4894@gmail.com
                </span>
              </a>
              <a href="https://wa.me/50558210655" className="contact-link" target="_blank" rel="noreferrer">
                <span className="contact-brand-icon claro-brand"><ClaroBrandIcon /></span>
                <span>
                  <small>WhatsApp / Claro</small>
                  58210655
                </span>
              </a>
              <a href="tel:+50584517258" className="contact-link">
                <span className="contact-brand-icon tigo-brand"><TigoBrandIcon /></span>
                <span>
                  <small>Línea Tigo</small>
                  84517258
                </span>
              </a>
            </div>
          </div>
        </section>
      </main>
    );
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div>
          <span className="eyebrow">CrediPrest</span>
          <h2>Finanzas</h2>
        </div>
        <nav>
          {navigationItems.map((item) => (
            <button type="button" key={item} className={view === item ? "active" : ""} onClick={() => setView(item)}>
              {viewLabel(item)}
            </button>
          ))}
        </nav>
        <div className="sidebar-session">
          <div className="session-user">
            <span className="session-dot" aria-hidden="true" />
            <div>
              <small>{session?.role === "Client" ? "Cliente" : session?.role === "Lender" ? "Prestamista" : "Administrador"}</small>
              <strong>{session?.fullName ?? session?.userName ?? "Administrador"}</strong>
            </div>
          </div>
          <button type="button" className="ghost" onClick={logout}>Cerrar sesión</button>
        </div>
      </aside>

      <main className="content">
        <header className="topbar">
          <div>
            <span className="eyebrow">{session?.role === "Client" ? "Cliente" : session?.role === "Lender" ? "Prestamista" : "Administrador"}</span>
            <h1>{viewLabel(view)}</h1>
          </div>
          <div className="top-actions">
            <button
              type="button"
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                void refresh();
              }}
              disabled={refreshing || loading || saving}
            >
              {refreshing ? "Actualizando..." : "Actualizar"}
            </button>
          </div>
        </header>

        {error && <div className="alert">{error}</div>}
        {notifications.length > 0 && <NotificationsPanel notifications={notifications} markAsRead={markNotificationRead} />}

        {view === "dashboard" && <DashboardView dashboard={dashboard} activeLoans={activeLoans} overdueLoans={overdueLoans} navigate={setView} />}
        {view === "clientPortal" && <ClientPortalView plans={clientPlans} />}
        {view === "clients" && (
          <ClientsView
            clients={clients}
            editingClient={editingClient}
            clientSearch={clientSearch}
            setClientSearch={setClientSearch}
            refresh={refresh}
            submitClient={submitClient}
            setEditingClient={startEditingClient}
            isSaving={saving}
            setClientActive={setClientActive}
            deleteClient={deleteClient}
          />
        )}
        {view === "loans" && (
          <LoansView
            clients={activeClients}
            loans={loans}
            loanDetail={loanDetail}
            editingLoan={editingLoan}
            newLoanClientId={newLoanClientId}
            isSaving={saving}
            submitLoan={submitLoan}
            openLoan={openLoan}
            setEditingLoan={startEditingLoan}
            startNewLoan={startNewLoan}
            deleteLoan={deleteLoan}
            cancelLoan={async (id) => {
              await api.cancelLoan(id);
              await refresh();
            }}
          />
        )}
        {view === "payments" && (
          <PaymentsView loans={loans} loanDetail={loanDetail} openLoan={(id) => openLoan(id, null)} submitPayment={submitPayment} />
        )}
        {view === "reports" && <ReportsView loans={loans} clients={activeClients} overdueLoans={overdueLoans} />}
        {view === "users" && (
          <UserManagementView
            users={users}
            editingUser={editingUser}
            setEditingUser={setEditingUser}
            submitUser={submitUser}
            setUserActive={setUserActive}
            deleteUser={deleteUser}
            isSaving={saving}
          />
        )}
        {view === "settings" && (
          <SettingsView
            fontSize={appFontSize}
            setFontSize={setAppFontSize}
          />
        )}
      </main>
      {confirmDialog && (
        <ConfirmDialog
          dialog={confirmDialog}
          onCancel={() => setConfirmDialog(null)}
        />
      )}
    </div>
  );
}

function viewLabel(view: View) {
  return {
    dashboard: "Dashboard",
    clients: "Clientes",
    loans: "Préstamos",
    payments: "Pagos",
    reports: "Reportes",
    users: "Usuarios",
    settings: "Configuración",
    clientPortal: "Mi plan de pago"
  }[view];
}

function NotificationsPanel({ notifications, markAsRead }: { notifications: Notification[]; markAsRead: (id: string) => void }) {
  const unreadCount = notifications.filter((notification) => !notification.isRead).length;

  return (
    <Panel title={`Notificaciones${unreadCount > 0 ? ` (${unreadCount})` : ""}`}>
      <div className="notification-list">
        {notifications.slice(0, 5).map((notification) => (
          <div key={notification.id} className={`notification-item ${notification.isRead ? "read" : ""}`}>
            <div>
              <strong>{notification.title}</strong>
              <p>{notification.message}</p>
              <small>{dateOnly(notification.createdAtUtc)}</small>
            </div>
            {!notification.isRead && (
              <button type="button" className="ghost" onClick={() => markAsRead(notification.id)}>
                Marcar leída
              </button>
            )}
          </div>
        ))}
      </div>
    </Panel>
  );
}

function ConfirmDialog({ dialog, onCancel }: { dialog: ConfirmDialogState; onCancel: () => void }) {
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onCancel}>
      <div className="confirm-modal" role="dialog" aria-modal="true" aria-labelledby="confirm-title" onMouseDown={(event) => event.stopPropagation()}>
        <div>
          <span className={`modal-icon ${dialog.tone === "danger" ? "danger" : ""}`} aria-hidden="true">!</span>
        </div>
        <div className="confirm-modal-body">
          <h2 id="confirm-title">{dialog.title}</h2>
          <p>{dialog.message}</p>
          <div className="confirm-modal-actions">
            <button type="button" className="ghost" onClick={onCancel}>{dialog.cancelLabel ?? "Cancelar"}</button>
            <button type="button" className={dialog.tone === "danger" ? "danger" : ""} onClick={() => dialog.onConfirm()}>
              {dialog.confirmLabel}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function DashboardView({
  dashboard,
  activeLoans,
  overdueLoans,
  navigate
}: {
  dashboard: Dashboard;
  activeLoans: Loan[];
  overdueLoans: Loan[];
  navigate: (view: View) => void;
}) {
  return (
    <section className="stack">
      <div className="metric-grid">
        <Metric title="Prestado C$" value={money(dashboard.totalLoanedCordobas)} />
        <Metric title="Prestado USD" value={money(dashboard.totalLoanedUsd, "USD")} />
        <Metric title="Pendiente C$" value={money(dashboard.pendingCordobas)} tone="warn" />
        <Metric title="Pendiente USD" value={money(dashboard.pendingUsd, "USD")} tone="warn" />
        <Metric title="Recuperado C$" value={money(dashboard.totalRecoveredCordobas)} tone="good" />
        <Metric title="Recuperado USD" value={money(dashboard.totalRecoveredUsd, "USD")} tone="good" />
        <Metric title="Clientes activos" value={String(dashboard.activeClients)} />
        <Metric title="Cuotas vencidas" value={String(dashboard.overdueInstallments)} tone="danger" />
      </div>
      <div className="two-col">
        <Panel title="Actividad">
          <div className="action-stats">
            <button type="button" className="stat-action" onClick={() => navigate("payments")}>
              <span>Pagos hoy</span>
              <strong>{dashboard.paidTodayCount}</strong>
              <small>{dualMoney(dashboard.paidTodayCordobas, dashboard.paidTodayUsd)}</small>
            </button>
            <button type="button" className="stat-action" onClick={() => navigate("payments")}>
              <span>Últimos 7 días</span>
              <strong>{dashboard.paidThisWeekCount}</strong>
              <small>{dualMoney(dashboard.paidThisWeekCordobas, dashboard.paidThisWeekUsd)}</small>
            </button>
            <button type="button" className="stat-action" onClick={() => navigate("reports")}>
              <span>Este mes</span>
              <strong>{dashboard.paidThisMonthCount}</strong>
              <small>{dualMoney(dashboard.paidThisMonthCordobas, dashboard.paidThisMonthUsd)}</small>
            </button>
            <button type="button" className="stat-action warn" onClick={() => navigate("payments")}>
              <span>Vencen hoy</span>
              <strong>{dashboard.dueTodayInstallments}</strong>
              <small>Ir a pagos</small>
            </button>
          </div>
        </Panel>
        <Panel title="Estado cartera">
          <div className="inline-stats">
            <span>Activos <strong>{activeLoans.length}</strong></span>
            <span>Vencidos <strong>{overdueLoans.length}</strong></span>
            <span>Cuotas vencidas <strong>{dashboard.overdueInstallments}</strong></span>
            <span>Vencen esta semana <strong>{dashboard.dueThisWeekInstallments}</strong></span>
            <span>Interés estimado C$ <strong>{money(dashboard.estimatedInterestCordobas)}</strong></span>
            <span>Interés estimado USD <strong>{money(dashboard.estimatedInterestUsd, "USD")}</strong></span>
          </div>
        </Panel>
      </div>
      <Panel title="Guía rápida">
        <div className="inline-stats">
          <span>Por cobrar <strong>{dualMoney(dashboard.pendingCordobas, dashboard.pendingUsd)}</strong></span>
          <span>Recuperado <strong>{dualMoney(dashboard.totalRecoveredCordobas, dashboard.totalRecoveredUsd)}</strong></span>
          <span>Clientes activos <strong>{dashboard.activeClients}</strong></span>
          <span>Préstamos activos <strong>{dashboard.activeLoans}</strong></span>
        </div>
      </Panel>
    </section>
  );
}

function ClientsView(props: {
  clients: Client[];
  editingClient: Client | null;
  clientSearch: string;
  setClientSearch: (value: string) => void;
  refresh: (search?: string) => void;
  submitClient: (event: FormEvent<HTMLFormElement>) => void;
  setEditingClient: (client: Client | null) => void;
  isSaving: boolean;
  setClientActive: (id: string, isActive: boolean) => Promise<void>;
  deleteClient: (id: string) => Promise<void>;
}) {
  const [selectedPaymentMethod, setSelectedPaymentMethod] = useState(props.editingClient?.preferredPaymentMethod ?? "cash");
  const showPaymentAccount = needsPaymentAccount(selectedPaymentMethod);

  useEffect(() => {
    setSelectedPaymentMethod(props.editingClient?.preferredPaymentMethod ?? "cash");
  }, [props.editingClient?.id, props.editingClient?.preferredPaymentMethod]);

  return (
    <section className="stack clients-page">
      <div className="client-form-row">
        <Panel title={props.editingClient ? "Editar cliente" : "Crear cliente"}>
          <form className="grid-form" onSubmit={props.submitClient} key={props.editingClient?.id ?? "new-client"} noValidate>
          <label className="field-label">
            Nombre completo
            <input name="fullName" placeholder="Nombre completo" minLength={3} maxLength={180} defaultValue={props.editingClient?.fullName} required />
          </label>
          <label className="field-label">
            Cédula
            <input name="identificationNumber" placeholder="001-010101-0001A" pattern={identificationPattern} title="Formato esperado: 001-010101-0001A o 0010101010001A" maxLength={16} defaultValue={props.editingClient?.identificationNumber} required />
          </label>
          <label className="field-label">
            Teléfono
            <input name="phone" type="tel" inputMode="tel" placeholder="8888-8888" pattern={phonePattern} title="Debe tener de 8 a 15 dígitos. Puede usar +, espacios o guiones." maxLength={20} defaultValue={props.editingClient?.phone} required />
          </label>
          <label className="field-label">
            Correo
            <input name="email" type="email" inputMode="email" autoComplete="email" placeholder="Correo opcional" title="Debe ser un correo válido, por ejemplo nombre@correo.com" maxLength={160} defaultValue={props.editingClient?.email} />
          </label>
          <label className="field-label span-2">
            Dirección
            <input name="address" placeholder="Dirección" maxLength={320} defaultValue={props.editingClient?.address} required />
          </label>
          <label className="field-label">
            Referencia personal 1
            <input name="personalReference1" placeholder="Nombre de referencia" maxLength={180} defaultValue={props.editingClient?.personalReference1} />
          </label>
          <label className="field-label">
            Teléfono referencia 1
            <input name="referencePhone1" type="tel" inputMode="tel" placeholder="Teléfono referencia 1" pattern={phonePattern} title="Debe tener de 8 a 15 dígitos. Puede usar +, espacios o guiones." maxLength={20} defaultValue={props.editingClient?.referencePhone1} />
          </label>
          <label className="field-label">
            Forma de pago preferida
            <select name="preferredPaymentMethod" value={selectedPaymentMethod} onChange={(event) => setSelectedPaymentMethod(event.target.value)} required>
              <option value="cash">Efectivo</option>
              <option value="bac">Transferencia BAC</option>
              <option value="lafise">Transferencia Lafise</option>
              <option value="bampro">Transferencia Bampro</option>
              <option value="kash">Kash</option>
            </select>
          </label>
          {showPaymentAccount && (
            <label className="field-label">
              {paymentAccountLabel(selectedPaymentMethod)}
              <input
                name="paymentAccount"
                key={selectedPaymentMethod}
                inputMode={selectedPaymentMethod === "kash" ? "text" : "numeric"}
                placeholder={selectedPaymentMethod === "kash" ? "Usuario, teléfono o cuenta Kash" : "Número de cuenta"}
                pattern={paymentAccountPattern(selectedPaymentMethod)}
                title={paymentAccountHint(selectedPaymentMethod)}
                maxLength={selectedPaymentMethod === "kash" ? 80 : 24}
                defaultValue={paymentAccountValue(props.editingClient)}
                required
              />
            </label>
          )}
          <label className="field-label">
            Observaciones
            <textarea className="client-notes-field" name="notes" placeholder="Observaciones" maxLength={1200} defaultValue={props.editingClient?.notes} rows={1} />
          </label>
          <div className="client-submit-row span-3">
            <p className="form-hint">Cédula: 001-010101-0001A. Teléfonos: 8 a 15 dígitos. Si seleccionas transferencia o Kash, agrega el dato correspondiente.</p>
            <div className="form-actions span-2">
              {props.editingClient && <button type="button" className="ghost" onClick={() => props.setEditingClient(null)}>Cancelar edición</button>}
              <button type="submit" disabled={props.isSaving}>{props.isSaving ? "Guardando..." : props.editingClient ? "Guardar cambios" : "Crear cliente"}</button>
            </div>
          </div>
          </form>
        </Panel>
      </div>

      <Panel title="Clientes">
        <form
          className="search-row"
          onSubmit={(event) => {
            event.preventDefault();
            props.refresh(props.clientSearch);
          }}
        >
          <input value={props.clientSearch} onChange={(event) => props.setClientSearch(event.target.value)} placeholder="Buscar por nombre, cédula, teléfono o cuenta" />
          <button type="submit">Buscar</button>
        </form>
        <div className="table-wrap clients-table-wrap">
          <table>
            <thead>
              <tr>
                <th>Cliente</th>
                <th>Teléfono</th>
                <th>Cuentas</th>
                <th>Estado</th>
                <th>Préstamos</th>
                <th>Debe</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {props.clients.length === 0 && (
                <tr>
                  <td colSpan={7} className="empty-table-cell">
                    No hay clientes registrados.
                  </td>
                </tr>
              )}
              {props.clients.map((client) => (
                <tr key={client.id}>
                  <td><strong>{client.fullName}</strong><small>{client.identificationNumber}</small></td>
                  <td>{client.phone}</td>
                  <td>
                    <AccountSummary client={client} />
                  </td>
                  <td><span className={`badge client-${client.isActive ? "active" : "inactive"}`}>{client.isActive ? "Activo" : "Inactivo"}</span></td>
                  <td>{client.activeLoans}</td>
                  <td>{clientDebt(client)}</td>
                  <td className="row-actions">
                    <button type="button" className="ghost" onClick={() => props.setEditingClient(client)}>Editar</button>
                    {client.isActive ? (
                      <button type="button" className="ghost" onClick={() => props.setClientActive(client.id, false)}>Desactivar</button>
                    ) : (
                      <button type="button" className="ghost" onClick={() => props.setClientActive(client.id, true)}>Activar</button>
                    )}
                    <button type="button" className="danger" onClick={() => props.deleteClient(client.id)}>Eliminar</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>
    </section>
  );
}

function LoansView(props: {
  clients: Client[];
  loans: Loan[];
  loanDetail: LoanDetail | null;
  editingLoan: Loan | null;
  newLoanClientId: string;
  isSaving: boolean;
  submitLoan: (event: FormEvent<HTMLFormElement>) => void;
  openLoan: (id: string) => void | Promise<void>;
  setEditingLoan: (loan: Loan | null) => void;
  startNewLoan: (clientId?: string) => void;
  cancelLoan: (id: string) => Promise<void>;
  deleteLoan: (id: string) => void | Promise<void>;
}) {
  const [selectedFrequency, setSelectedFrequency] = useState<number>(props.editingLoan?.paymentFrequency ?? 3);
  const selectedTermKey = selectedFrequency as keyof typeof termLabels;

  useEffect(() => {
    setSelectedFrequency(props.editingLoan?.paymentFrequency ?? 3);
  }, [props.editingLoan?.id, props.editingLoan?.paymentFrequency]);

  return (
    <section className="stack loans-page">
      <div className="loan-form-row">
        <Panel title={props.editingLoan ? "Editar préstamo" : "Crear préstamo"}>
          {props.editingLoan && (
            <div className="edit-context">
              <span>Editando {loanDisplayName(props.editingLoan)}</span>
              <button type="button" className="ghost" onClick={() => props.startNewLoan(props.editingLoan?.clientId)}>
                Crear otro préstamo para este cliente
              </button>
            </div>
          )}
          <form className="grid-form" onSubmit={props.submitLoan} key={props.editingLoan?.id ?? `new-loan-${props.newLoanClientId}`}>
            <label className="field-label">
              Cliente
              <select name="clientId" defaultValue={props.editingLoan?.clientId ?? props.newLoanClientId} disabled={Boolean(props.editingLoan)} required>
              <option value="">Cliente</option>
              {props.clients.map((client) => <option key={client.id} value={client.id}>{client.fullName}</option>)}
              </select>
            </label>
            <label className="field-label">
              Referencia del préstamo
              <input name="referenceName" placeholder="Ej. Moto, negocio, emergencia" maxLength={120} defaultValue={props.editingLoan?.referenceName} />
            </label>
            <label className="field-label">
              Monto prestado
              <input name="principalAmount" type="number" min="1" step="0.01" placeholder="Ej. 5000" defaultValue={props.editingLoan?.principalAmount} required />
            </label>
            <label className="field-label">
              Moneda
              <select name="currency" defaultValue={String(props.editingLoan?.currency ?? 1)}>
                <option value="1">Córdobas C$</option>
                <option value="2">Dólares USD</option>
              </select>
            </label>
            <label className="field-label">
              Interés mensual
              <input name="monthlyInterestRate" type="number" min="0" step="0.01" placeholder="Ej. 10" defaultValue={props.editingLoan?.monthlyInterestRate ?? 10} required />
            </label>
            <label className="field-label">
              Frecuencia de pago
              <select name="paymentFrequency" defaultValue={String(props.editingLoan?.paymentFrequency ?? 3)} onChange={(event) => setSelectedFrequency(Number(event.target.value))}>
                <option value="1">Semanal</option>
                <option value="2">Quincenal</option>
                <option value="3">Mensual</option>
              </select>
            </label>
            <label className="field-label">
              {termLabels[selectedTermKey]}
              <input name="termMonths" type="number" min="1" defaultValue={props.editingLoan?.termMonths ?? 1} placeholder={termPlaceholders[selectedTermKey]} required />
            </label>
            <label className="field-label">
              Fecha de inicio
              <input name="startDate" type="date" defaultValue={dateInputValue(props.editingLoan?.startDate)} required />
            </label>
            <label className="field-label">
              Observaciones
              <textarea name="notes" className="loan-notes-field" placeholder="Observaciones opcionales" defaultValue={props.editingLoan?.notes} rows={1} />
            </label>
            <label className="field-label">
              Ciudad del acuerdo
              <input name="agreementCity" placeholder="Ej. Managua" maxLength={120} defaultValue={props.editingLoan?.agreementCity} />
            </label>
            <label className="field-label">
              Recargo por mora
              <input name="lateFeeDescription" placeholder="Ej. 50" maxLength={220} defaultValue={props.editingLoan?.lateFeeDescription} />
            </label>
            <div className="loan-submit-row span-3">
              <p className="form-hint loan-term-hint">El plazo se calcula por frecuencia: semanal crea pagos cada 7 días, quincenal cada 15 días y mensual cada mes.</p>
              <div className="form-actions">
                {props.editingLoan && <button type="button" className="ghost" onClick={() => props.startNewLoan(props.editingLoan?.clientId)}>Cancelar edición</button>}
                <button type="submit" disabled={props.isSaving}>{props.isSaving ? "Guardando..." : props.editingLoan ? "Guardar cambios" : "Crear y generar cuotas"}</button>
              </div>
            </div>
          </form>
        </Panel>
      </div>
      <Panel title="Préstamos">
          <div className="table-wrap loans-table-wrap">
            <table>
              <thead>
                <tr>
                <th>Cliente</th>
                <th>Monto</th>
                <th>Interés</th>
                <th>Cantidad pagos</th>
                <th>Estado</th>
                <th>Debe</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
                {props.loans.length === 0 && (
                   <tr>
                      <td colSpan={7} className="empty-table-cell">
                        Todavía no hay préstamos registrados.
                      </td>
                    </tr>
                )}
                {props.loans.map((loan) => (
                  <tr key={loan.id} className={props.loanDetail?.loan.id === loan.id ? "selected-row" : undefined}>
                    <td>{loan.clientName}{loan.referenceName && <small>{loan.referenceName}</small>}</td>
                    <td>{money(loan.principalAmount, currencyLabels[loan.currency])}</td>
                    <td>{loan.monthlyInterestRate}% mensual</td>
                    <td>{loan.termMonths}</td>
                    <td><span className={`badge status-${loan.status}`}>{statusLabels[loan.status]}</span></td>
                    <td>{money(loan.pendingBalance, currencyLabels[loan.currency])}</td>
                    <td className="row-actions">
                      <button type="button" className="ghost" onClick={() => props.openLoan(loan.id)}>Detalle</button>
                      <button type="button" className="ghost" onClick={() => props.startNewLoan(loan.clientId)}>Nuevo</button>
                      {loan.status !== 2 && <button type="button" className="ghost" onClick={() => props.setEditingLoan(loan)}>Editar</button>}
                      {loan.status !== 2 && <button type="button" className="danger" onClick={() => props.cancelLoan(loan.id)}>Cancelar</button>}
                      <button type="button" className="danger" onClick={() => props.deleteLoan(loan.id)}>Eliminar</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>
      {props.loanDetail && <LoanDetailPanel detail={props.loanDetail} />}
    </section>
  );
}

function AccountSummary({ client }: { client: Client }) {
  const preferredMethod = paymentPreferenceLabels[client.preferredPaymentMethod] ?? "Efectivo";
  const accounts = [
    `Preferido: ${preferredMethod}`,
    client.bacAccountNumber ? `BAC ${client.bacAccountNumber}` : null,
    client.lafiseAccountNumber ? `Lafise ${client.lafiseAccountNumber}` : null,
    client.bamproAccountNumber ? `Bampro ${client.bamproAccountNumber}` : null,
    client.hasKash ? `Kash${client.kashAccount ? ` ${client.kashAccount}` : ""}` : null
  ].filter((account): account is string => Boolean(account));

  return (
    <div className="account-tags">
      {accounts.map((account) => (
        <span key={account}>{account}</span>
      ))}
    </div>
  );
}

function PaymentsView(props: {
  loans: Loan[];
  loanDetail: LoanDetail | null;
  openLoan: (id: string) => void | Promise<void>;
  submitPayment: (event: FormEvent<HTMLFormElement>) => void;
}) {
  const installments = useMemo(
    () => props.loanDetail?.installments.filter((installment) => installment.status !== 3) ?? [],
    [props.loanDetail]
  );
  const [selectedInstallmentId, setSelectedInstallmentId] = useState("");
  const [paymentDate, setPaymentDate] = useState(dateInputValue());
  const [paymentMethod, setPaymentMethod] = useState(1);

  useEffect(() => {
    const installment = defaultPaymentInstallment(installments);
    setSelectedInstallmentId(installment?.id ?? "");
    setPaymentDate(installment ? dateInputValue(installment.dueDate) : dateInputValue());
  }, [installments]);

  return (
    <section className="stack payments-page">
      <div className="payment-form-row">
        <Panel title="Registrar pago">
          <form className="grid-form" onSubmit={props.submitPayment}>
          <label className="field-label">
            Préstamo
            <select name="loanId" required value={props.loanDetail?.loan.id ?? ""} onChange={(event) => event.target.value && props.openLoan(event.target.value)}>
              <option value="">Selecciona préstamo</option>
              {props.loans.filter((loan) => loan.status !== 2).map((loan) => (
                <option key={loan.id} value={loan.id}>{loanDisplayName(loan)} - {money(loan.pendingBalance, currencyLabels[loan.currency])}</option>
              ))}
            </select>
          </label>
          <label className="field-label">
            Cuota a pagar
            <select
              name="installmentId"
              value={selectedInstallmentId}
              onChange={(event) => {
                const installmentId = event.target.value;
                const installment = installments.find((item) => item.id === installmentId);

                setSelectedInstallmentId(installmentId);
                setPaymentDate(installment ? dateInputValue(installment.dueDate) : dateInputValue(defaultPaymentInstallment(installments)?.dueDate));
              }}
            >
              <option value="">Aplicar al próximo monto pendiente</option>
              {installments.map((installment) => (
                <option key={installment.id} value={installment.id}>
                  Cuota {installment.installmentNumber} - vence {dateOnly(installment.dueDate)} - pendiente {money(installmentPendingAmount(installment), currencyLabels[props.loanDetail?.loan.currency ?? 1])}
                </option>
              ))}
            </select>
          </label>
          <label className="field-label">
            Fecha de pago
            <input name="paymentDate" type="date" value={paymentDate} onChange={(event) => setPaymentDate(event.target.value)} required />
          </label>
          <label className="field-label">
            Monto pagado
            <input name="amountPaid" type="number" step="0.01" min="0.01" placeholder="Ej. 500" required />
          </label>
          <label className="field-label">
            Método de pago
            <select name="paymentMethod" value={paymentMethod} onChange={(event) => setPaymentMethod(Number(event.target.value))}>
              <option value="1">Efectivo</option>
              <option value="2">Transferencia</option>
              <option value="3">Depósito</option>
              <option value="4">Otro</option>
            </select>
          </label>
          <label className="field-label">
            Referencia
            <input name="referenceNumber" placeholder="Referencia o comprobante" />
          </label>
          {(paymentMethod === 2 || paymentMethod === 3) && (
            <label className="field-label">
              Imagen del comprobante
              <input name="receiptImage" type="file" accept="image/jpeg,image/png,image/webp" />
              <small className="form-hint">Opcional. JPG, PNG o WEBP de hasta 5 MB.</small>
            </label>
          )}
          <label className="field-label span-2">
            Observaciones
            <textarea name="notes" placeholder="Observaciones opcionales" />
          </label>
          <div className="payment-submit-row span-3">
            <p className="form-hint">Si hay cuotas atrasadas, el pago se aplica primero a lo vencido, luego a mora pendiente y después a cuotas actuales.</p>
            <div className="form-actions">
              <button type="submit">Registrar pago</button>
            </div>
          </div>
          </form>
        </Panel>
      </div>
      {props.loanDetail ? <LoanDetailPanel detail={props.loanDetail} /> : <Panel title="Detalle"><p className="muted">Selecciona un préstamo para ver sus cuotas pendientes.</p></Panel>}
    </section>
  );
}

function ClientPortalView({ plans }: { plans: LoanDetail[] }) {
  if (plans.length === 0) {
    return (
      <Panel title="Mi plan de pago">
        <p className="muted">No tienes préstamos activos registrados para mostrar.</p>
      </Panel>
    );
  }

  return (
    <section className="stack">
      <div className="metric-grid">
        <Metric title="Préstamos" value={String(plans.length)} />
        <Metric title="Debe C$" value={money(plans.filter((plan) => plan.loan.currency === 1).reduce((sum, plan) => sum + plan.loan.pendingBalance, 0))} tone="warn" />
        <Metric title="Debe USD" value={money(plans.filter((plan) => plan.loan.currency === 2).reduce((sum, plan) => sum + plan.loan.pendingBalance, 0), "USD")} tone="warn" />
        <Metric title="Cuotas atrasadas" value={String(plans.flatMap((plan) => plan.installments).filter((installment) => installment.status === 4).length)} tone="danger" />
      </div>
      {plans.map((plan) => (
        <LoanDetailPanel key={plan.loan.id} detail={plan} />
      ))}
    </section>
  );
}

function ReportsView({ loans, clients, overdueLoans }: { loans: Loan[]; clients: Client[]; overdueLoans: Loan[] }) {
  const pendingCordobas = loans
    .filter((loan) => loan.currency === 1)
    .reduce((sum, loan) => sum + loan.pendingBalance, 0);
  const pendingUsd = loans
    .filter((loan) => loan.currency === 2)
    .reduce((sum, loan) => sum + loan.pendingBalance, 0);

  return (
    <section className="stack">
      <div className="metric-grid">
        <Metric title="Préstamos activos" value={String(loans.filter((loan) => loan.status === 1).length)} />
        <Metric title="Préstamos vencidos" value={String(overdueLoans.length)} tone="danger" />
        <Metric title="Clientes registrados" value={String(clients.length)} />
        <Metric title="Cartera pendiente" value={portfolioMoney(pendingCordobas, pendingUsd)} tone="warn" />
      </div>
      <Panel title="Reporte de préstamos">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Cliente</th>
                <th>Moneda</th>
                <th>Total</th>
                <th>Pagado</th>
                <th>Pendiente</th>
                <th>Estado</th>
              </tr>
            </thead>
            <tbody>
              {loans.length === 0 && (
                <tr>
                  <td colSpan={6} className="empty-table-cell">
                    No hay datos de préstamos para mostrar.
                  </td>
                </tr>
              )}
              {loans.map((loan) => (
                <tr key={loan.id}>
                  <td>{loan.clientName}{loan.referenceName && <small>{loan.referenceName}</small>}</td>
                  <td>{currencyLabels[loan.currency]}</td>
                  <td>{money(loan.totalToPay, currencyLabels[loan.currency])}</td>
                  <td>{money(loan.totalPaid, currencyLabels[loan.currency])}</td>
                  <td>{money(loan.pendingBalance, currencyLabels[loan.currency])}</td>
                  <td>{statusLabels[loan.status]}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>
    </section>
  );
}

function UserManagementView({
  users,
  editingUser,
  setEditingUser,
  submitUser,
  setUserActive,
  deleteUser,
  isSaving
}: {
  users: AppUser[];
  editingUser: AppUser | null;
  setEditingUser: (user: AppUser | null) => void;
  submitUser: (event: FormEvent<HTMLFormElement>) => void;
  setUserActive: (user: AppUser, isActive: boolean) => void;
  deleteUser: (user: AppUser) => void;
  isSaving: boolean;
}) {
  const lenderUsers = users.filter((user) => user.role === 2);
  const [showLenderPassword, setShowLenderPassword] = useState(false);
  const [showLenderConfirmPassword, setShowLenderConfirmPassword] = useState(false);

  return (
    <section className="stack">
      <Panel title="Crear prestamista">
        <form className="grid-form" onSubmit={submitUser} key={editingUser?.id ?? "new-user"} autoComplete="off">
          <label className="field-label">
            Nombre completo
            <input name="fullName" placeholder="Nombre completo" defaultValue={editingUser?.fullName ?? ""} autoComplete="off" required />
          </label>
          <label className="field-label">
            Usuario o NickName
            <input name="lenderAlias" placeholder="Ej. jmedina" defaultValue={editingUser?.userName ?? ""} autoComplete="off" spellCheck={false} required disabled={Boolean(editingUser)} />
          </label>
          <label className="field-label">
            Correo
            <input name="email" type="email" placeholder="correo@dominio.com" defaultValue={editingUser?.email ?? ""} autoComplete="off" required />
          </label>
          <label className="field-label">
            Teléfono
            <input name="phone" placeholder="88888888" pattern={phonePattern} defaultValue={editingUser?.phone ?? ""} autoComplete="off" required />
          </label>
          <label className="field-label">
            Cédula
            <input name="identificationNumber" placeholder="001-010101-0001A" pattern={identificationPattern} defaultValue={editingUser?.identificationNumber ?? ""} autoComplete="off" required />
          </label>
          <label className="field-label">
            Contraseña
            <div className="password-field">
              <input
                name="lenderAccessCode"
                type={showLenderPassword ? "text" : "password"}
                autoComplete="new-password"
                minLength={9}
                placeholder={editingUser ? "Dejar vacía para conservarla" : "Más de 8 caracteres"}
                required={!editingUser}
              />
              <button
                type="button"
                className="password-toggle"
                aria-label={showLenderPassword ? "Ocultar contraseña" : "Ver contraseña"}
                title={showLenderPassword ? "Ocultar contraseña" : "Ver contraseña"}
                onClick={() => setShowLenderPassword((value) => !value)}
              >
                <EyeIcon crossed={showLenderPassword} />
              </button>
            </div>
          </label>
          <label className="field-label">
            Confirmar contraseña
            <div className="password-field">
              <input
                name="lenderAccessConfirm"
                type={showLenderConfirmPassword ? "text" : "password"}
                autoComplete="new-password"
                minLength={9}
                placeholder={editingUser ? "Repetir solo si la cambias" : "Repite la contraseña"}
                required={!editingUser}
              />
              <button
                type="button"
                className="password-toggle"
                aria-label={showLenderConfirmPassword ? "Ocultar confirmación" : "Ver confirmación"}
                title={showLenderConfirmPassword ? "Ocultar confirmación" : "Ver confirmación"}
                onClick={() => setShowLenderConfirmPassword((value) => !value)}
              >
                <EyeIcon crossed={showLenderConfirmPassword} />
              </button>
            </div>
          </label>
          <p className="form-hint span-2">
            Estos usuarios podrán entrar como prestamistas y solo verán sus clientes, préstamos, pagos, reportes y dashboard.
          </p>
          <div className="form-actions span-2">
            {editingUser && (
              <button type="button" className="ghost" onClick={() => setEditingUser(null)} disabled={isSaving}>
                Cancelar edición
              </button>
            )}
            <button type="submit" disabled={isSaving}>{isSaving ? "Guardando..." : editingUser ? "Guardar cambios" : "Crear prestamista"}</button>
          </div>
        </form>
      </Panel>
      <Panel title="Prestamistas registrados">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Prestamista</th>
                <th>Usuario</th>
                <th>Correo</th>
                <th>Teléfono</th>
                <th>Cédula</th>
                <th>Estado</th>
                <th className="actions-column">Acciones</th>
              </tr>
            </thead>
            <tbody>
              {lenderUsers.length === 0 && (
                <tr>
                  <td colSpan={7} className="empty-table-cell">
                    No hay prestamistas registrados.
                  </td>
                </tr>
              )}
              {lenderUsers.map((user) => (
                <tr key={user.id}>
                  <td><strong>{user.fullName}</strong></td>
                  <td>{user.userName}</td>
                  <td>{user.email}</td>
                  <td>{user.phone ?? "-"}</td>
                  <td>{user.identificationNumber ?? "-"}</td>
                  <td><span className={`badge client-${user.isActive ? "active" : "inactive"}`}>{user.isActive ? "Activo" : "Inactivo"}</span></td>
                  <td>
                    <div className="row-actions centered">
                      <button type="button" className="ghost" onClick={() => setEditingUser(user)}>
                        Editar
                      </button>
                      <button type="button" className="ghost" onClick={() => setUserActive(user, !user.isActive)}>
                        {user.isActive ? "Desactivar" : "Activar"}
                      </button>
                      <button type="button" className="ghost danger" onClick={() => deleteUser(user)}>
                        Eliminar
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>
    </section>
  );
}

function SettingsView({ fontSize, setFontSize }: { fontSize: number; setFontSize: (value: number) => void }) {
  return (
    <section className="stack">
      <Panel title="Preferencias de interfaz">
        <div className="settings-grid">
          <div className="setting-control">
            <span className="eyebrow">Tamaño de letra</span>
            <p>Ajusta el tamaño general de la app para trabajar más cómodo.</p>
            <div className="range-row">
              <input
                type="range"
                min="14"
                max="20"
                step="1"
                value={fontSize}
                onChange={(event) => setFontSize(Number(event.target.value))}
              />
              <strong>{fontSize}px</strong>
            </div>
            <div className="form-actions">
              <button type="button" className="ghost" onClick={() => setFontSize(DEFAULT_APP_FONT_SIZE)}>
                Restablecer
              </button>
            </div>
          </div>
          <div className="font-preview">
            <span className="eyebrow">Vista previa</span>
            <h3>CrediPrest</h3>
            <p>Texto de ejemplo para revisar cómo se verá la información en tablas, formularios y paneles.</p>
          </div>
        </div>
      </Panel>
    </section>
  );
}

function LoanDetailPanel({ detail }: { detail: LoanDetail }) {
  const [agreementError, setAgreementError] = useState("");

  return (
    <Panel title={`Tabla de pagos - ${loanDisplayName(detail.loan)}`}>
      <div className="panel-toolbar">
        <button type="button" className="ghost" onClick={() => downloadPaymentTablePdf(detail)}>
          Descargar PDF
        </button>
        <button
          type="button"
          className="ghost"
          onClick={() => {
            setAgreementError("");
            downloadLoanAgreement(detail).catch((error) => {
              setAgreementError(error instanceof Error ? error.message : "No se pudo descargar el acuerdo.");
            });
          }}
        >
          Descargar acuerdo
        </button>
      </div>
      {agreementError && <p className="form-error">{agreementError}</p>}
      <div className="loan-summary">
        {detail.loan.referenceName && <span>Referencia {detail.loan.referenceName}</span>}
        <span>Total {money(detail.loan.totalToPay, currencyLabels[detail.loan.currency])}</span>
        <span>Pagado {money(detail.loan.totalPaid, currencyLabels[detail.loan.currency])}</span>
        {detail.loan.lateFeesPending > 0 && <span>Mora pendiente {money(detail.loan.lateFeesPending, currencyLabels[detail.loan.currency])}</span>}
        <span>Debe {money(detail.loan.pendingBalance, currencyLabels[detail.loan.currency])}</span>
      </div>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>#</th>
              <th>Vence</th>
              <th>Capital</th>
              <th>Interés</th>
              <th>Cuota</th>
              <th>Pagado</th>
              <th>Pendiente</th>
              <th>Debe</th>
              <th>Estado</th>
            </tr>
          </thead>
          <tbody>
            {detail.installments.map((installment: Installment) => (
              <tr key={installment.id}>
                <td>{installment.installmentNumber}</td>
                <td>{dateOnly(installment.dueDate)}</td>
                <td>{money(installment.principalAmount, currencyLabels[detail.loan.currency])}</td>
                <td>{money(installment.interestAmount, currencyLabels[detail.loan.currency])}</td>
                <td>{money(installment.paymentAmount, currencyLabels[detail.loan.currency])}</td>
                <td>{money(installment.amountPaid, currencyLabels[detail.loan.currency])}</td>
                <td>{money(installmentPendingAmount(installment), currencyLabels[detail.loan.currency])}</td>
                <td>{money(installment.remainingBalance, currencyLabels[detail.loan.currency])}</td>
                <td><span className={`badge installment-${installment.status}`}>{installmentStatusLabels[installment.status]}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {detail.charges.length > 0 && (
        <div className="loan-charges-section">
          <h3>Moras aplicadas</h3>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Periodo</th>
                  <th>Desde</th>
                  <th>Hasta</th>
                  <th>Monto</th>
                  <th>Pagado</th>
                  <th>Pendiente</th>
                  <th>Detalle</th>
                </tr>
              </thead>
              <tbody>
                {detail.charges.map((charge) => (
                  <tr key={charge.id}>
                    <td>{charge.periodNumber}</td>
                    <td>{dateOnly(charge.periodStartDate)}</td>
                    <td>{dateOnly(charge.periodEndDate)}</td>
                    <td>{money(charge.amount, currencyLabels[detail.loan.currency])}</td>
                    <td>{money(charge.amountPaid, currencyLabels[detail.loan.currency])}</td>
                    <td>{money(charge.pendingAmount, currencyLabels[detail.loan.currency])}</td>
                    <td>{charge.notes ?? "Mora por atraso"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </Panel>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="panel">
      <h2>{title}</h2>
      {children}
    </section>
  );
}

function Metric({ title, value, tone }: { title: string; value: string; tone?: "good" | "warn" | "danger" }) {
  return (
    <div className={`metric ${tone ?? ""}`}>
      <span>{title}</span>
      <strong>{value}</strong>
    </div>
  );
}
