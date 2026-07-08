import { FormEvent, useEffect, useMemo, useState } from "react";
import { api, clearToken, getToken, setToken } from "./api/client";
import type { Client, Dashboard, Installment, Loan, LoanDetail, LoginResponse } from "./types/models";

type View = "dashboard" | "clients" | "loans" | "payments" | "reports" | "settings";
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

const emptyDashboard: Dashboard = {
  totalLoanedCordobas: 0,
  totalLoanedUsd: 0,
  totalRecoveredCordobas: 0,
  totalRecoveredUsd: 0,
  pendingCordobas: 0,
  pendingUsd: 0,
  estimatedInterestCordobas: 0,
  estimatedInterestUsd: 0,
  realInterestCollectedCordobas: 0,
  realInterestCollectedUsd: 0,
  activeClients: 0,
  activeLoans: 0,
  overdueLoans: 0,
  overdueInstallments: 0,
  dueTodayInstallments: 0,
  dueThisWeekInstallments: 0,
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

function defaultPaymentInstallment(installments: Installment[]) {
  const ordered = [...installments].sort((left, right) => dateInputValue(left.dueDate).localeCompare(dateInputValue(right.dueDate)));
  const today = dateInputValue();
  const oldestDueInstallment = ordered.find((installment) => dateInputValue(installment.dueDate) <= today);

  return oldestDueInstallment ?? ordered[0] ?? null;
}

function formValue(form: FormData, key: string) {
  return String(form.get(key) ?? "").trim();
}

function numberValue(form: FormData, key: string) {
  return Number(form.get(key) ?? 0);
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

export default function App() {
  const [tokenAvailable, setTokenAvailable] = useState(Boolean(getToken()));
  const [session, setSession] = useState<LoginResponse | null>(null);
  const [view, setView] = useState<View>("dashboard");
  const [dashboard, setDashboard] = useState<Dashboard>(emptyDashboard);
  const [clients, setClients] = useState<Client[]>([]);
  const [loans, setLoans] = useState<Loan[]>([]);
  const [loanDetail, setLoanDetail] = useState<LoanDetail | null>(null);
  const [editingClient, setEditingClient] = useState<Client | null>(null);
  const [editingLoan, setEditingLoan] = useState<Loan | null>(null);
  const [clientSearch, setClientSearch] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmDialog, setConfirmDialog] = useState<ConfirmDialogState | null>(null);

  async function refresh(search = clientSearch) {
    setLoading(true);
    setError(null);
    try {
      const [dashboardData, clientData, loanData] = await Promise.all([
        api.dashboard(),
        api.clients(search),
        api.loans()
      ]);
      setDashboard(dashboardData);
      setClients(clientData);
      setLoans(loanData);
      if (loanDetail) {
        if (loanData.some((loan) => loan.id === loanDetail.loan.id)) {
          setLoanDetail(await api.loanDetail(loanDetail.loan.id));
        } else {
          setLoanDetail(null);
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error inesperado");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (tokenAvailable) {
      refresh();
    }
  }, [tokenAvailable]);

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLoading(true);
    setError(null);
    const form = new FormData(event.currentTarget);

    try {
      const response = await api.login(formValue(form, "user"), formValue(form, "password"));
      setToken(response.token);
      setSession(response);
      setTokenAvailable(true);
      setView("dashboard");
      setLoanDetail(null);
      setEditingClient(null);
      setEditingLoan(null);
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
    setEditingClient(null);
    setEditingLoan(null);
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
      setLoading(true);
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
      setLoading(false);
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
    if (loan) {
      setView("loans");
    }
  }

  async function deleteLoan(id: string) {
    const loan = loans.find((item) => item.id === id);
    const loanLabel = loan ? `${loan.clientName} - ${money(loan.pendingBalance, currencyLabels[loan.currency])}` : "este préstamo";

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
      notes: formValue(form, "notes") || undefined
    };

    try {
      setLoading(true);
      setError(null);
      const detail = editingLoan
        ? await api.updateLoan(editingLoan.id, { ...payload, status: editingLoan.status })
        : await api.createLoan(payload);
      setLoanDetail(detail);
      setEditingLoan(null);
      formElement.reset();
      await refresh();
      setView("loans");
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el préstamo");
    } finally {
      setLoading(false);
    }
  }

  async function submitPayment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const installmentId = formValue(form, "installmentId");
    const payload = {
      loanId: formValue(form, "loanId"),
      installmentId: installmentId || null,
      paymentDate: formValue(form, "paymentDate"),
      amountPaid: numberValue(form, "amountPaid"),
      paymentMethod: numberValue(form, "paymentMethod"),
      referenceNumber: formValue(form, "referenceNumber") || undefined,
      notes: formValue(form, "notes") || undefined
    };

    try {
      setError(null);
      const detail = await api.registerPayment(payload);
      setLoanDetail(detail);
      formElement.reset();
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo registrar el pago");
    }
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

  const overdueLoans = useMemo(() => loans.filter((loan) => loan.status === 3), [loans]);
  const activeLoans = useMemo(() => loans.filter((loan) => loan.status === 1), [loans]);
  const activeClients = useMemo(() => clients.filter((client) => client.isActive), [clients]);

  if (!tokenAvailable) {
    return (
      <main className="login-shell">
        <section className="login-panel">
          <div>
            <span className="eyebrow">MVP privado</span>
            <h1>CrediPrestApp</h1>
            <p>Control de clientes, préstamos, cuotas, pagos e intereses en córdobas y dólares.</p>
          </div>
          <form onSubmit={handleLogin} className="login-form">
            <label>
              Usuario o correo
              <input name="user" autoComplete="username" defaultValue="admin" required />
            </label>
            <label>
              Contraseña
              <input name="password" type="password" autoComplete="current-password" defaultValue="Admin123*" required />
            </label>
            {error && <p className="alert">{error}</p>}
            <button type="submit" disabled={loading}>{loading ? "Entrando..." : "Entrar"}</button>
          </form>
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
          {(["dashboard", "clients", "loans", "payments", "reports", "settings"] as View[]).map((item) => (
            <button type="button" key={item} className={view === item ? "active" : ""} onClick={() => setView(item)}>
              {viewLabel(item)}
            </button>
          ))}
        </nav>
        <div className="sidebar-session">
          <div className="session-user">
            <span className="session-dot" aria-hidden="true" />
            <div>
              <small>Sesión activa</small>
              <strong>{session?.fullName ?? session?.userName ?? "Administrador"}</strong>
            </div>
          </div>
          <button type="button" className="ghost" onClick={logout}>Cerrar sesión</button>
        </div>
      </aside>

      <main className="content">
        <header className="topbar">
          <div>
            <span className="eyebrow">Administrador</span>
            <h1>{viewLabel(view)}</h1>
          </div>
          <div className="top-actions">
            <button type="button" onClick={() => refresh()} disabled={loading}>{loading ? "Actualizando..." : "Actualizar"}</button>
          </div>
        </header>

        {error && <div className="alert">{error}</div>}

        {view === "dashboard" && <DashboardView dashboard={dashboard} activeLoans={activeLoans} overdueLoans={overdueLoans} />}
        {view === "clients" && (
          <ClientsView
            clients={clients}
            editingClient={editingClient}
            clientSearch={clientSearch}
            setClientSearch={setClientSearch}
            refresh={refresh}
            submitClient={submitClient}
            setEditingClient={startEditingClient}
            isSaving={loading}
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
            isSaving={loading}
            submitLoan={submitLoan}
            openLoan={openLoan}
            setEditingLoan={startEditingLoan}
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
        {view === "settings" && <SettingsView />}
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
    settings: "Configuración"
  }[view];
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

function DashboardView({ dashboard, activeLoans, overdueLoans }: { dashboard: Dashboard; activeLoans: Loan[]; overdueLoans: Loan[] }) {
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
          <div className="inline-stats">
            <span>Pagos hoy <strong>{dashboard.paidTodayCount}</strong></span>
            <span>Semana <strong>{dashboard.paidThisWeekCount}</strong></span>
            <span>Mes <strong>{dashboard.paidThisMonthCount}</strong></span>
            <span>Vencen hoy <strong>{dashboard.dueTodayInstallments}</strong></span>
          </div>
        </Panel>
        <Panel title="Estado cartera">
          <div className="inline-stats">
            <span>Activos <strong>{activeLoans.length}</strong></span>
            <span>Vencidos <strong>{overdueLoans.length}</strong></span>
            <span>Interés estimado C$ <strong>{money(dashboard.estimatedInterestCordobas)}</strong></span>
            <span>Interés real C$ <strong>{money(dashboard.realInterestCollectedCordobas)}</strong></span>
            <span>Interés estimado USD <strong>{money(dashboard.estimatedInterestUsd, "USD")}</strong></span>
            <span>Interés real USD <strong>{money(dashboard.realInterestCollectedUsd, "USD")}</strong></span>
          </div>
        </Panel>
      </div>
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
            <div className="form-actions">
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
  isSaving: boolean;
  submitLoan: (event: FormEvent<HTMLFormElement>) => void;
  openLoan: (id: string) => void | Promise<void>;
  setEditingLoan: (loan: Loan | null) => void;
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
          <form className="grid-form" onSubmit={props.submitLoan} key={props.editingLoan?.id ?? "new-loan"}>
            <label className="field-label">
              Cliente
              <select name="clientId" defaultValue={props.editingLoan?.clientId ?? ""} disabled={Boolean(props.editingLoan)} required>
              <option value="">Cliente</option>
              {props.clients.map((client) => <option key={client.id} value={client.id}>{client.fullName}</option>)}
              </select>
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
              {termLabels[selectedTermKey]}
              <input name="termMonths" type="number" min="1" defaultValue={props.editingLoan?.termMonths ?? 1} placeholder={termPlaceholders[selectedTermKey]} required />
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
              Fecha de inicio
              <input name="startDate" type="date" defaultValue={dateInputValue(props.editingLoan?.startDate)} required />
            </label>
            <label className="field-label span-2">
              Observaciones
              <textarea name="notes" className="loan-notes-field" placeholder="Observaciones opcionales" defaultValue={props.editingLoan?.notes} rows={1} />
            </label>
            <div className="loan-submit-row span-3">
              <p className="form-hint loan-term-hint">El plazo se calcula por frecuencia: semanal crea pagos cada 7 días, quincenal cada 15 días y mensual cada mes.</p>
              <div className="form-actions">
                {props.editingLoan && <button type="button" className="ghost" onClick={() => props.setEditingLoan(null)}>Cancelar edición</button>}
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
                <th>Saldo</th>
                <th>Frecuencia</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
                {props.loans.length === 0 && (
                   <tr>
                      <td colSpan={8} className="empty-table-cell">
                        Todavía no hay préstamos registrados.
                      </td>
                    </tr>
                )}
                {props.loans.map((loan) => (
                  <tr key={loan.id} className={props.loanDetail?.loan.id === loan.id ? "selected-row" : undefined}>
                    <td>{loan.clientName}</td>
                    <td>{money(loan.principalAmount, currencyLabels[loan.currency])}</td>
                    <td>{loan.monthlyInterestRate}% mensual</td>
                    <td>{loan.termMonths}</td>
                    <td><span className={`badge status-${loan.status}`}>{statusLabels[loan.status]}</span></td>
                    <td>{money(loan.pendingBalance, currencyLabels[loan.currency])}</td>
                    <td>{frequencyLabels[loan.paymentFrequency]}</td>
                    <td className="row-actions">
                      <button type="button" className="ghost" onClick={() => props.openLoan(loan.id)}>Detalle</button>
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

  useEffect(() => {
    const installment = defaultPaymentInstallment(installments);
    setSelectedInstallmentId(installment?.id ?? "");
    setPaymentDate(installment ? dateInputValue(installment.dueDate) : dateInputValue());
  }, [installments]);

  return (
    <section className="two-col">
      <Panel title="Registrar pago">
        <form className="grid-form" onSubmit={props.submitPayment}>
          <label className="field-label span-2">
            Préstamo
            <select name="loanId" required value={props.loanDetail?.loan.id ?? ""} onChange={(event) => event.target.value && props.openLoan(event.target.value)}>
              <option value="">Selecciona préstamo</option>
              {props.loans.filter((loan) => loan.status !== 2).map((loan) => (
                <option key={loan.id} value={loan.id}>{loan.clientName} - {money(loan.pendingBalance, currencyLabels[loan.currency])}</option>
              ))}
            </select>
          </label>
          <label className="field-label span-2">
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
              <option value="">Aplicar al próximo saldo</option>
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
            <select name="paymentMethod" defaultValue="1">
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
          <label className="field-label span-2">
            Observaciones
            <textarea name="notes" placeholder="Observaciones opcionales" />
          </label>
          <button type="submit" className="span-2">Registrar pago</button>
        </form>
      </Panel>
      {props.loanDetail ? <LoanDetailPanel detail={props.loanDetail} /> : <Panel title="Detalle"><p className="muted">Selecciona un préstamo para ver sus cuotas pendientes.</p></Panel>}
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
                  <td>{loan.clientName}</td>
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

function SettingsView() {
  return (
    <Panel title="Configuración inicial">
      <div className="settings-grid">
        <div>
          <span className="eyebrow">Base de datos</span>
          <p>SQL Server LocalDB: <strong>CrediPrestAppDB</strong></p>
        </div>
        <div>
          <span className="eyebrow">Usuario semilla</span>
          <p><strong>admin</strong> / <strong>Admin123*</strong></p>
        </div>
        <div>
          <span className="eyebrow">API</span>
          <p>Swagger en <strong>http://localhost:5052/swagger</strong></p>
        </div>
      </div>
    </Panel>
  );
}

function LoanDetailPanel({ detail }: { detail: LoanDetail }) {
  return (
    <Panel title={`Tabla de pagos - ${detail.loan.clientName}`}>
      <div className="loan-summary">
        <span>Total {money(detail.loan.totalToPay, currencyLabels[detail.loan.currency])}</span>
        <span>Pagado {money(detail.loan.totalPaid, currencyLabels[detail.loan.currency])}</span>
        <span>Saldo {money(detail.loan.pendingBalance, currencyLabels[detail.loan.currency])}</span>
        <span>{frequencyLabels[detail.loan.paymentFrequency]}</span>
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
              <th>Saldo</th>
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
