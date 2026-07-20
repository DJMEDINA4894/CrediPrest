import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useCallback, useState } from "react";
import { Alert, Image, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import * as ImagePicker from "expo-image-picker";
import { api } from "../api/client";
import { DateField } from "../components/DateField";
import { Card, EmptyState, ErrorText, Field, GhostButton, InfoTooltip, PrimaryButton, Screen, SelectField, Text } from "../components/ui";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { LoanDetail, LoanRecalculationPreview } from "../types/models";
import { currencyLabels, dateInputValue, dateOnly, money } from "../utils/format";

type Props = NativeStackScreenProps<RootStackParamList, "LoanRecalculation">;

const modes = [
  { value: "1", label: "Reducir cuota y conservar pagos" },
  { value: "2", label: "Conservar cuota y reducir pagos" },
  { value: "3", label: "Elegir nueva cantidad de pagos" },
  { value: "4", label: "Liquidar préstamo" }
];
const paymentMethods = [
  { value: "1", label: "Efectivo" },
  { value: "2", label: "Transferencia" },
  { value: "3", label: "Depósito" },
  { value: "5", label: "Kash" },
  { value: "4", label: "Otro" }
];

export function LoanRecalculationScreen({ route, navigation }: Props) {
  const [detail, setDetail] = useState<LoanDetail | null>(null);
  const [mode, setMode] = useState("1");
  const [effectiveDate, setEffectiveDate] = useState(dateInputValue());
  const [amount, setAmount] = useState("");
  const [newInstallmentCount, setNewInstallmentCount] = useState("");
  const [paymentMethod, setPaymentMethod] = useState("1");
  const [referenceNumber, setReferenceNumber] = useState("");
  const [receipt, setReceipt] = useState<{ base64: string; fileName: string; contentType: string; uri: string } | null>(null);
  const [notes, setNotes] = useState("");
  const [preview, setPreview] = useState<LoanRecalculationPreview | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError("");
      setDetail(await api.loanDetail(route.params.loanId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cargar el prestamo.");
    } finally {
      setLoading(false);
    }
  }, [route.params.loanId]);

  useFocusEffect(useCallback(() => {
    void load();
  }, [load]));

  function requestPayload(overrides?: { mode?: string; effectiveDate?: string; amount?: string }) {
    const requestedMode = overrides?.mode ?? mode;
    return {
      mode: Number(requestedMode),
      effectiveDate: overrides?.effectiveDate ?? effectiveDate,
      amount: Number(overrides?.amount ?? amount),
      newInstallmentCount: requestedMode === "3" ? Number(newInstallmentCount) : null
    };
  }

  async function calculatePreview(overrides?: { mode?: string; effectiveDate?: string; amount?: string }) {
    const requestedMode = overrides?.mode ?? mode;
    const requestedDate = overrides?.effectiveDate ?? effectiveDate;
    const requestedAmount = overrides?.amount ?? amount;
    if (!/^\d{4}-\d{2}-\d{2}$/.test(requestedDate)) {
      setError("La fecha del abono debe usar el formato AAAA-MM-DD.");
      return;
    }
    if (requestedMode !== "4" && (!Number.isFinite(Number(requestedAmount)) || Number(requestedAmount) <= 0)) {
      setError("Ingresa un monto de abono mayor que cero.");
      return;
    }
    if (requestedMode === "3" && (!Number.isInteger(Number(newInstallmentCount)) || Number(newInstallmentCount) < 1 || Number(newInstallmentCount) > 120)) {
      setError("La nueva cantidad de pagos debe estar entre 1 y 120.");
      return;
    }

    try {
      setLoading(true);
      setError("");
      const result = await api.previewExtraordinaryPayment(
        route.params.loanId,
        requestPayload({ mode: requestedMode, effectiveDate: requestedDate, amount: requestedAmount })
      );
      setPreview(result);
      if (requestedMode === "4") {
        setAmount(result.totalSettlementAmount.toFixed(2));
      }
    } catch (err) {
      setPreview(null);
      setError(err instanceof Error ? err.message : "No se pudo calcular la vista previa.");
    } finally {
      setLoading(false);
    }
  }

  async function applyExtraordinaryPayment() {
    const needsReceipt = paymentMethod === "2" || paymentMethod === "3" || paymentMethod === "5";
    if (needsReceipt && !referenceNumber.trim()) {
      setError("Ingresa la referencia de la transferencia, depósito o pago por Kash.");
      return;
    }

    try {
      setSaving(true);
      setError("");
      const updated = await api.registerExtraordinaryPayment(route.params.loanId, {
        ...requestPayload(),
        paymentMethod: Number(paymentMethod),
        referenceNumber: referenceNumber.trim() || null,
        notes: notes.trim() || null,
        ...(needsReceipt ? {
          receiptImageBase64: receipt?.base64,
          receiptFileName: receipt?.fileName,
          receiptContentType: receipt?.contentType
        } : {})
      });
      Alert.alert(
        mode === "4" ? "Préstamo liquidado" : "Abono registrado",
        mode === "4"
          ? "El préstamo quedó cancelado y su historial fue conservado."
          : "El abono se aplicó al capital y el nuevo plan quedó guardado.",
        [{ text: "Ver tabla", onPress: () => navigation.replace("LoanDetail", { loanId: updated.loan.id }) }]
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo registrar el abono extraordinario.");
    } finally {
      setSaving(false);
    }
  }

  async function chooseReceipt(source: "library" | "camera") {
    try {
      const permission = source === "camera"
        ? await ImagePicker.requestCameraPermissionsAsync()
        : await ImagePicker.requestMediaLibraryPermissionsAsync();
      if (!permission.granted) {
        setError("Necesitas permitir el acceso para adjuntar el comprobante.");
        return;
      }

      const result = source === "camera"
        ? await ImagePicker.launchCameraAsync({ allowsEditing: true, base64: true, quality: 0.7 })
        : await ImagePicker.launchImageLibraryAsync({ mediaTypes: ["images"], allowsEditing: true, base64: true, quality: 0.7 });
      if (result.canceled || !result.assets[0]?.base64) {
        return;
      }

      const asset = result.assets[0];
      const base64 = asset.base64;
      if (!base64) {
        return;
      }
      const contentType = asset.mimeType ?? "image/jpeg";
      if (!["image/jpeg", "image/png", "image/webp"].includes(contentType)) {
        setError("El comprobante debe ser una imagen JPG, PNG o WEBP.");
        return;
      }
      if (asset.fileSize && asset.fileSize > 5 * 1024 * 1024) {
        setError("El comprobante no puede superar 5 MB.");
        return;
      }

      setReceipt({
        base64,
        fileName: asset.fileName ?? `comprobante-${Date.now()}.jpg`,
        contentType,
        uri: asset.uri
      });
      setError("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo adjuntar el comprobante.");
    }
  }

  const currency = detail ? currencyLabels[detail.loan.currency] : "C$";
  const needsReceipt = paymentMethod === "2" || paymentMethod === "3" || paymentMethod === "5";

  return (
    <Screen>
      <ScrollView keyboardShouldPersistTaps="handled" refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        {detail ? (
          <>
            <Card title={`${mode === "4" ? "Liquidar préstamo" : "Abono extraordinario"} - ${detail.loan.clientName}`}>
              <Text style={styles.help}>
                {mode === "4"
                  ? "Se conserva el historial, se cobra lo generado hasta la fecha y se descuenta el interés futuro."
                  : "El dinero se aplica directamente al capital. Las cuotas pagadas se conservan y solo se reemplaza el plan futuro."}
              </Text>
              <Field
                label={mode === "4" ? "Monto total a liquidar" : "Monto del abono"}
                value={amount}
                onChangeText={(value) => {
                  setAmount(value);
                  setPreview(null);
                }}
                placeholder={mode === "4" ? "Se calcula automáticamente" : "Ej. 1000"}
                keyboardType="decimal-pad"
                editable={mode !== "4"}
              />
              <SelectField
                label="Modalidad"
                value={mode}
                options={modes}
                inputAccessory={(
                  <InfoTooltip
                    title="Modalidades del nuevo plan"
                    message={"Reducir cuota: conserva la cantidad de pagos restantes y calcula una cuota menor.\n\nReducir pagos: mantiene una cuota aproximada a la actual y termina el préstamo antes.\n\nElegir cantidad: permite definir los pagos. Menos pagos elevan la cuota y suelen reducir el interés; más pagos bajan la cuota, pero pueden aumentar el interés total.\n\nLiquidar préstamo: cobra capital pendiente, interés generado hasta la fecha y mora pendiente; el interés futuro se descuenta."}
                  />
                )}
                onChange={(value) => {
                  setMode(value);
                  setPreview(null);
                  if (value === "4") {
                    setAmount("");
                    void calculatePreview({ mode: value, amount: "0" });
                  }
                }}
              />
              {mode === "3" ? (
                <Field
                  label="Nueva cantidad de pagos"
                  value={newInstallmentCount}
                  onChangeText={(value) => {
                    setNewInstallmentCount(value);
                    setPreview(null);
                  }}
                  placeholder="Ej. 8"
                  keyboardType="number-pad"
                />
              ) : null}
              <DateField
                label="Fecha del abono"
                value={effectiveDate}
                onChange={(value) => {
                  setEffectiveDate(value);
                  setPreview(null);
                  if (mode === "4") {
                    void calculatePreview({ mode: "4", effectiveDate: value, amount: "0" });
                  }
                }}
                maximumDate={dateInputValue()}
              />
              <SelectField
                label="Método de pago"
                value={paymentMethod}
                options={paymentMethods}
                onChange={(value) => {
                  setPaymentMethod(value);
                  if (value !== "2" && value !== "3" && value !== "5") {
                    setReceipt(null);
                  }
                  setError("");
                }}
              />
              <Field
                label="Referencia"
                value={referenceNumber}
                onChangeText={setReferenceNumber}
                placeholder={paymentMethod === "2" || paymentMethod === "3" || paymentMethod === "5" ? "Obligatoria" : "Opcional"}
              />
              {needsReceipt ? (
                <View style={styles.receiptSection}>
                  <Text style={styles.receiptLabel}>{paymentMethod === "5" ? "Comprobante de Kash" : "Imagen del comprobante"}</Text>
                  <Text style={styles.receiptHint}>Opcional. JPG, PNG o WEBP de hasta 5 MB.</Text>
                  <View style={styles.receiptActions}>
                    <GhostButton title="Elegir imagen" onPress={() => void chooseReceipt("library")} />
                    <GhostButton title="Tomar foto" onPress={() => void chooseReceipt("camera")} />
                  </View>
                  {receipt ? (
                    <View style={styles.receiptPreview}>
                      <Image source={{ uri: receipt.uri }} style={styles.receiptImage} />
                      <View style={styles.receiptInfo}>
                        <Text numberOfLines={1} style={styles.receiptName}>{receipt.fileName}</Text>
                        <GhostButton title="Quitar" onPress={() => setReceipt(null)} />
                      </View>
                    </View>
                  ) : null}
                </View>
              ) : null}
              <Field label="Observaciones" value={notes} onChangeText={setNotes} placeholder="Opcional" multiline />
              <Text style={styles.rule}>
                {mode === "4"
                  ? "La liquidación incluye capital pendiente, interés generado hasta la fecha y mora pendiente."
                  : "El préstamo debe estar al día, sin cuotas parciales, vencidas ni mora pendiente."}
              </Text>
              <PrimaryButton
                title={mode === "4" ? "Actualizar liquidación" : "Calcular vista previa"}
                onPress={() => void calculatePreview()}
                loading={loading}
              />
            </Card>

            {preview ? (
              <Card title={mode === "4" ? "Resumen de liquidación" : "Vista previa del abono y nuevo plan"}>
                {mode === "4" ? (
                  <>
                    <PreviewRow label="Capital pendiente" value={money(preview.outstandingPrincipal, currency)} />
                    <PreviewRow label="Interés generado" value={money(preview.accruedInterest, currency)} />
                    <PreviewRow label="Mora pendiente" value={money(preview.pendingLateFees, currency)} />
                    <PreviewRow label="Interés futuro descontado" value={money(preview.futureInterestDiscount, currency)} />
                    <PreviewRow label="Total exacto a liquidar" value={money(preview.totalSettlementAmount, currency)} highlight />
                  </>
                ) : (
                  <>
                    <PreviewRow label="Capital antes" value={money(preview.outstandingPrincipal, currency)} />
                    <PreviewRow label="Abono a capital" value={money(preview.extraordinaryPaymentAmount, currency)} highlight />
                    <PreviewRow label="Capital después" value={money(preview.principalAfterPayment, currency)} />
                    <PreviewRow label="Cuota actual" value={money(preview.currentInstallmentAmount, currency)} />
                    <PreviewRow label="Nueva cuota" value={money(preview.newInstallmentAmount, currency)} highlight />
                    <PreviewRow label="Pagos restantes" value={`${preview.currentRemainingInstallments} -> ${preview.newRemainingInstallments}`} />
                    <PreviewRow label="Interés pendiente actual" value={money(preview.currentPendingInterest, currency)} />
                    <PreviewRow label="Nuevo interés" value={money(preview.newInterest, currency)} />
                    <PreviewRow label={preview.interestSavings >= 0 ? "Ahorro de interés" : "Aumento de interés"} value={money(Math.abs(preview.interestSavings), currency)} />
                    <PreviewRow label="Nuevo pendiente" value={money(preview.newPendingTotal, currency)} />
                    <PreviewRow label="Primera cuota" value={dateOnly(preview.firstDueDate)} />
                  </>
                )}
                <View style={styles.confirm}>
                  <PrimaryButton
                    title={mode === "4" ? "Confirmar liquidación" : "Confirmar abono y nuevo plan"}
                    onPress={() => void applyExtraordinaryPayment()}
                    loading={saving}
                  />
                </View>
              </Card>
            ) : null}
          </>
        ) : (
          <EmptyState text="Cargando prestamo..." />
        )}
      </ScrollView>
    </Screen>
  );
}

function PreviewRow({ label, value, highlight }: { label: string; value: string; highlight?: boolean }) {
  return (
    <View style={styles.previewRow}>
      <Text style={styles.previewLabel}>{label}</Text>
      <Text style={[styles.previewValue, highlight && styles.previewHighlight]}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  help: {
    color: colors.muted,
    lineHeight: 20,
    marginBottom: spacing.md
  },
  rule: {
    backgroundColor: colors.soft,
    borderColor: colors.border,
    borderRadius: 8,
    borderWidth: 1,
    color: colors.primaryDark,
    lineHeight: 19,
    marginBottom: spacing.md,
    padding: spacing.sm
  },
  receiptSection: {
    marginBottom: spacing.md
  },
  receiptLabel: {
    color: colors.text,
    fontWeight: "800",
    marginBottom: spacing.xs
  },
  receiptHint: {
    color: colors.muted,
    fontSize: 12,
    marginBottom: spacing.sm
  },
  receiptActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.sm,
    marginBottom: spacing.sm
  },
  receiptPreview: {
    alignItems: "center",
    backgroundColor: colors.soft,
    borderColor: colors.border,
    borderRadius: 8,
    borderWidth: 1,
    flexDirection: "row",
    gap: spacing.sm,
    padding: spacing.sm
  },
  receiptImage: {
    borderRadius: 6,
    height: 72,
    width: 72
  },
  receiptInfo: {
    flex: 1,
    gap: spacing.xs
  },
  receiptName: {
    color: colors.text,
    fontWeight: "700"
  },
  previewRow: {
    alignItems: "center",
    borderBottomColor: colors.border,
    borderBottomWidth: 1,
    flexDirection: "row",
    justifyContent: "space-between",
    paddingVertical: spacing.sm
  },
  previewLabel: {
    color: colors.muted,
    flex: 1
  },
  previewValue: {
    color: colors.text,
    fontWeight: "900"
  },
  previewHighlight: {
    color: colors.primary
  },
  confirm: {
    marginTop: spacing.md
  }
});
