import * as FileSystem from "expo-file-system/legacy";
import * as Print from "expo-print";
import * as Sharing from "expo-sharing";
import { api } from "../api/client";
import type { LoanDetail } from "../types/models";
import { currencyLabels, dateOnly, installmentPendingAmount, lateFeeAllocation, money } from "./format";

function safeFileName(value: string) {
  return value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/[^a-zA-Z0-9]+/g, "-").replace(/^-|-$/g, "").toLowerCase();
}

function escapeHtml(value: string) {
  return value.replace(/[&<>"']/g, (character) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "\"": "&quot;",
    "'": "&#039;"
  })[character] ?? character);
}

async function ensureSharingAvailable() {
  if (!await Sharing.isAvailableAsync()) {
    throw new Error("Este dispositivo no permite compartir o guardar el archivo.");
  }
}

export async function shareLoanAgreement(detail: LoanDetail) {
  await ensureSharingAvailable();
  const source = api.loanAgreementSource(detail.loan.id);
  const fileName = `acuerdo-${safeFileName(detail.loan.clientName || "prestamo")}.docx`;
  const destination = `${FileSystem.cacheDirectory}${fileName}`;
  const result = await FileSystem.downloadAsync(source.uri, destination, { headers: source.headers });

  await Sharing.shareAsync(result.uri, {
    dialogTitle: "Guardar o compartir acuerdo",
    mimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    UTI: "org.openxmlformats.wordprocessingml.document"
  });
}

export async function shareLoanPaymentPlan(detail: LoanDetail) {
  await ensureSharingAvailable();
  const currency = currencyLabels[detail.loan.currency];
  const rows = detail.installments.map((installment) => {
    const mora = lateFeeAllocation(detail, installment);
    const pending = installmentPendingAmount(installment) + mora.pendingAmount;
    const status = installment.amountPaid >= installment.paymentAmount
      ? "Pagada"
      : installment.dueDate.slice(0, 10) < new Date().toISOString().slice(0, 10)
        ? "Retrasada"
        : "Pendiente";

    return `<tr>
      <td>${installment.installmentNumber}</td>
      <td>${escapeHtml(dateOnly(installment.dueDate))}</td>
      <td>${escapeHtml(money(installment.principalAmount, currency))}</td>
      <td>${escapeHtml(money(installment.interestAmount, currency))}</td>
      <td>${escapeHtml(money(installment.paymentAmount, currency))}</td>
      <td>${escapeHtml(money(mora.amount, currency))}</td>
      <td>${escapeHtml(money(installment.amountPaid + mora.amountPaid, currency))}</td>
      <td>${escapeHtml(money(pending, currency))}</td>
      <td>${status}</td>
    </tr>`;
  }).join("");

  const html = `<!doctype html>
  <html lang="es">
    <head>
      <meta charset="utf-8" />
      <style>
        body { color: #162232; font-family: Arial, sans-serif; padding: 24px; }
        h1 { color: #10383f; font-size: 22px; margin: 0 0 6px; }
        p { margin: 4px 0; }
        .summary { background: #eef6f5; border: 1px solid #d8e4ea; margin: 18px 0; padding: 12px; }
        table { border-collapse: collapse; font-size: 9px; width: 100%; }
        th { background: #176f64; color: white; padding: 7px 4px; text-align: left; }
        td { border-bottom: 1px solid #d8e4ea; padding: 7px 4px; }
      </style>
    </head>
    <body>
      <h1>Tabla de pagos - ${escapeHtml(detail.loan.clientName)}</h1>
      <p>${escapeHtml(detail.loan.referenceName ?? "Préstamo")}</p>
      <div class="summary">
        <strong>Total:</strong> ${escapeHtml(money(detail.loan.totalToPay, currency))} &nbsp;
        <strong>Pagado:</strong> ${escapeHtml(money(detail.loan.totalPaid, currency))} &nbsp;
        <strong>Mora:</strong> ${escapeHtml(money(detail.loan.lateFeesPending, currency))} &nbsp;
        <strong>Debe:</strong> ${escapeHtml(money(detail.loan.pendingBalance, currency))}
      </div>
      <table>
        <thead><tr><th>#</th><th>Vence</th><th>Capital</th><th>Interés</th><th>Cuota</th><th>Mora</th><th>Pagado</th><th>Pendiente</th><th>Estado</th></tr></thead>
        <tbody>${rows}</tbody>
      </table>
    </body>
  </html>`;

  const result = await Print.printToFileAsync({ html });
  await Sharing.shareAsync(result.uri, {
    dialogTitle: "Guardar o compartir tabla de pagos",
    mimeType: "application/pdf",
    UTI: "com.adobe.pdf"
  });
}
