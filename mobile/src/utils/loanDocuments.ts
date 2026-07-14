import * as FileSystem from "expo-file-system/legacy";
import * as IntentLauncher from "expo-intent-launcher";
import * as Sharing from "expo-sharing";
import { Platform } from "react-native";
import { api } from "../api/client";
import type { LoanDetail } from "../types/models";

function safeFileName(value: string) {
  return value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/[^a-zA-Z0-9]+/g, "-").replace(/^-|-$/g, "").toLowerCase();
}

async function ensureSharingAvailable() {
  if (!await Sharing.isAvailableAsync()) {
    throw new Error("Este dispositivo no permite compartir o guardar el archivo.");
  }
}

async function downloadVerifiedDocument(
  source: { uri: string; headers?: Record<string, string> },
  destination: string,
  expectedSignature: string,
  documentName: string
) {
  await FileSystem.deleteAsync(destination, { idempotent: true });
  const result = await FileSystem.downloadAsync(source.uri, destination, { headers: source.headers });

  if (result.status < 200 || result.status >= 300) {
    await FileSystem.deleteAsync(destination, { idempotent: true });
    throw new Error(`No se pudo descargar ${documentName} (${result.status}).`);
  }

  const info = await FileSystem.getInfoAsync(result.uri);
  if (!info.exists || !info.size) {
    throw new Error(`El archivo de ${documentName} llegó vacío.`);
  }

  const signature = await FileSystem.readAsStringAsync(result.uri, {
    encoding: FileSystem.EncodingType.Base64,
    position: 0,
    length: expectedSignature === "JVBERi0=" ? 5 : 2
  });

  if (signature !== expectedSignature) {
    await FileSystem.deleteAsync(destination, { idempotent: true });
    throw new Error(`El servidor no devolvió un archivo válido de ${documentName}. Vuelve a iniciar sesión e inténtalo otra vez.`);
  }

  return result.uri;
}

async function openOrShareDocument(uri: string, mimeType: string, dialogTitle: string, UTI: string) {
  if (Platform.OS === "android") {
    try {
      const contentUri = await FileSystem.getContentUriAsync(uri);
      await IntentLauncher.startActivityAsync("android.intent.action.VIEW", {
        data: contentUri,
        flags: 1,
        type: mimeType
      });
      return;
    } catch {
      // Some Android devices do not include a compatible viewer; sharing remains a reliable fallback.
    }
  }

  await ensureSharingAvailable();
  await Sharing.shareAsync(uri, { dialogTitle, mimeType, UTI });
}

export async function shareLoanAgreement(detail: LoanDetail) {
  const source = api.loanAgreementSource(detail.loan.id);
  const fileName = `acuerdo-${safeFileName(detail.loan.clientName || "prestamo")}.docx`;
  const destination = `${FileSystem.cacheDirectory}${fileName}`;
  const uri = await downloadVerifiedDocument(source, destination, "UEs=", "acuerdo");

  await openOrShareDocument(
    uri,
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    "Guardar o compartir acuerdo",
    "org.openxmlformats.wordprocessingml.document"
  );
}

export async function shareLoanPaymentPlan(detail: LoanDetail, clientPortal = false) {
  const source = api.loanPaymentTableSource(detail.loan.id, clientPortal);
  const displayName = detail.loan.referenceName
    ? `${detail.loan.clientName}-${detail.loan.referenceName}`
    : detail.loan.clientName;
  const fileName = `tabla-pagos-${safeFileName(displayName) || "prestamo"}.pdf`;
  const destination = `${FileSystem.cacheDirectory}${fileName}`;
  const uri = await downloadVerifiedDocument(source, destination, "JVBERi0=", "PDF");

  await openOrShareDocument(uri, "application/pdf", "Guardar o compartir tabla de pagos", "com.adobe.pdf");
}
