import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useCallback, useState } from "react";
import { Alert, Image, Modal, Pressable, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import * as ImagePicker from "expo-image-picker";
import { api } from "../api/client";
import { DateField } from "../components/DateField";
import { PaidBreakdownInfo } from "../components/PaidBreakdownInfo";
import { Card, EmptyState, ErrorText, Field, GhostButton, InfoTooltip, PrimaryButton, Screen, SelectField, Text } from "../components/ui";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { Installment, Loan, LoanDetail, Payment } from "../types/models";
import { currencyLabels, dateInputValue, dateOnly, lateFeePolicyText, money } from "../utils/format";

type Props = NativeStackScreenProps<RootStackParamList, "Payments">;

export function PaymentsScreen({ route, navigation }: Props) {
  const [loans, setLoans] = useState<Loan[]>([]);
  const [selectedLoan, setSelectedLoan] = useState<Loan | null>(route.params?.loan ?? null);
  const [loanDetail, setLoanDetail] = useState<LoanDetail | null>(null);
  const [selectedInstallmentId, setSelectedInstallmentId] = useState("");
  const [payments, setPayments] = useState<Payment[]>([]);
  const [selectedReceiptId, setSelectedReceiptId] = useState<string | null>(null);
  const [paymentDate, setPaymentDate] = useState(dateInputValue());
  const [amountPaid, setAmountPaid] = useState("");
  const [referenceNumber, setReferenceNumber] = useState("");
  const [notes, setNotes] = useState("");
  const [paymentMethod, setPaymentMethod] = useState<1 | 2 | 3 | 4 | 5>(1);
  const [receipt, setReceipt] = useState<{ base64: string; fileName: string; contentType: string; uri: string } | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const selectLoan = useCallback(async (loan: Loan) => {
    setSelectedLoan(loan);

    try {
      const [detail, loanPayments] = await Promise.all([api.loanDetail(loan.id), api.payments(loan.id)]);
      setLoanDetail(detail);
      setPayments([...loanPayments].sort((left, right) => right.paymentDate.localeCompare(left.paymentDate)));
      const orderedInstallments = [...detail.installments]
        .filter((installment) => installment.status !== 3)
        .sort((left, right) => dateInputValue(left.dueDate).localeCompare(dateInputValue(right.dueDate)));
      const today = dateInputValue();
      const suggestedInstallment = orderedInstallments.find((installment) => dateInputValue(installment.dueDate) <= today)
        ?? orderedInstallments[0];

      if (suggestedInstallment) {
        setSelectedInstallmentId(suggestedInstallment.id);
        setPaymentDate(dateInputValue(suggestedInstallment.dueDate));
      } else {
        setSelectedInstallmentId("");
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo preparar la fecha de pago.");
    }
  }, []);

  const load = useCallback(async () => {
    try {
      setError("");
      setLoading(true);
      const activeLoans = (await api.loans()).filter((loan) => loan.status !== 2);
      setLoans(activeLoans);
      const preferredLoan = route.params?.loan ?? activeLoans.find((loan) => loan.id === route.params?.loanId);
      if (preferredLoan) {
        await selectLoan(preferredLoan);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudieron cargar los prestamos.");
    } finally {
      setLoading(false);
    }
  }, [route.params?.loan, route.params?.loanId, selectLoan]);

  useFocusEffect(useCallback(() => {
    void load();
  }, [load]));

  async function submit() {
    if (!selectedLoan) {
      setError("Selecciona un prestamo.");
      return;
    }

    if (!selectedInstallmentId) {
      setError("Selecciona la cuota hasta la cual se aplicará el pago.");
      return;
    }

    const amount = Number(amountPaid);
    if (!Number.isFinite(amount) || amount <= 0) {
      setError("Ingresa un monto valido.");
      return;
    }

    try {
      setError("");
      setSaving(true);
      const detail = await api.registerPayment({
        loanId: selectedLoan.id,
        installmentId: selectedInstallmentId,
        paymentDate,
        amountPaid: amount,
        paymentMethod,
        referenceNumber: referenceNumber || undefined,
        notes: notes || undefined,
        ...(paymentMethod === 2 || paymentMethod === 3 || paymentMethod === 5 ? {
          receiptImageBase64: receipt?.base64,
          receiptFileName: receipt?.fileName,
          receiptContentType: receipt?.contentType
        } : {})
      });
      setAmountPaid("");
      setReferenceNumber("");
      setNotes("");
      setReceipt(null);
      Alert.alert("Pago registrado", "El pago se aplico correctamente.", [
        { text: "Ver detalle", onPress: () => navigation.navigate("LoanDetail", { loanId: detail.loan.id }) },
        { text: "OK" }
      ]);
      setSelectedLoan(detail.loan);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo registrar el pago.");
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

  const currency = selectedLoan ? currencyLabels[selectedLoan.currency] : "C$";
  const needsReceipt = paymentMethod === 2 || paymentMethod === 3 || paymentMethod === 5;
  const paymentMethodLabels = { 1: "Efectivo", 2: "Transferencia", 3: "Depósito", 4: "Otro", 5: "Kash" } as const;
  const loanOptions = loans.map((loan) => ({
    value: loan.id,
    label: `${loan.clientName}${loan.referenceName ? ` - ${loan.referenceName}` : ""} - ${money(loan.pendingBalance, currencyLabels[loan.currency])}`
  }));
  const payableInstallments = [...(loanDetail?.installments ?? [])]
    .filter((installment) => installment.status !== 3)
    .sort((left, right) => left.installmentNumber - right.installmentNumber);
  const installmentOptions = payableInstallments.map((installment) => ({
    value: installment.id,
    label: `Cuota ${installment.installmentNumber} · ${dateOnly(installment.dueDate)} · pendiente ${money(installmentPendingAmount(installment), currency)}`
  }));

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content} refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        <SelectField
          label="Prestamo activo"
          value={selectedLoan?.id ?? ""}
          options={loanOptions}
          onChange={(loanId) => {
            const loan = loans.find((item) => item.id === loanId);
            if (loan) void selectLoan(loan);
          }}
          placeholder="Selecciona un prestamo"
        />
        <Card title="Prestamo">
          {selectedLoan ? (
            <>
              <Text style={styles.loanName}>{selectedLoan.clientName}</Text>
              {selectedLoan.referenceName ? <Text style={styles.muted}>{selectedLoan.referenceName}</Text> : null}
              <View style={styles.dueRow}>
                <Text style={styles.due}>Debe: {money(selectedLoan.pendingBalance, currency)}</Text>
                <PaidBreakdownInfo principal={selectedLoan.paidPrincipal} interest={selectedLoan.paidInterest} currency={currency} />
              </View>
              {selectedLoan.lateFeeDescription ? (
                <View style={styles.lateFeeSummary}>
                  <Text style={styles.muted}>Mora configurada: {selectedLoan.lateFeeDescription}</Text>
                  <InfoTooltip
                    title="Cómo se calcula la mora"
                    message={lateFeePolicyText(selectedLoan.paymentFrequency, selectedLoan.principalAmount, selectedLoan.monthlyInterestRate, Number(selectedLoan.lateFeeDescription.replace("%", "")) || 50, currency, selectedLoan.termMonths)}
                  />
                </View>
              ) : null}
              {selectedLoan.lateFeesPending > 0 ? <Text style={styles.late}>Mora: {money(selectedLoan.lateFeesPending, currency)}</Text> : null}
            </>
          ) : (
            <EmptyState text="Selecciona un prestamo arriba." />
          )}
        </Card>

        <Card title="Registrar pago">
          <SelectField
            label="Aplicar pago hasta la cuota"
            value={selectedInstallmentId}
            options={installmentOptions}
            onChange={(installmentId) => {
              setSelectedInstallmentId(installmentId);
              const installment = payableInstallments.find((item) => item.id === installmentId);
              if (installment) setPaymentDate(dateInputValue(installment.dueDate));
            }}
            placeholder="Selecciona una cuota"
          />
          <DateField label="Fecha de pago" value={paymentDate} onChange={setPaymentDate} maximumDate={dateInputValue()} />
          <Field label="Monto pagado" value={amountPaid} onChangeText={setAmountPaid} keyboardType="decimal-pad" placeholder="Ej. 500" />
          <Text style={styles.label}>Metodo de pago</Text>
          <View style={styles.methodRow}>
            {[
              [1, "Efectivo"],
              [2, "Transferencia"],
              [3, "Deposito"],
              [4, "Otro"],
              [5, "Kash"]
            ].map(([value, label]) => (
              <Pressable
                key={String(value)}
                style={[styles.method, paymentMethod === value && styles.methodActive]}
                onPress={() => setPaymentMethod(value as 1 | 2 | 3 | 4 | 5)}
              >
                <Text style={[styles.methodText, paymentMethod === value && styles.methodTextActive]}>{label}</Text>
              </Pressable>
            ))}
          </View>
          <Field label="Referencia" value={referenceNumber} onChangeText={setReferenceNumber} placeholder="Numero de referencia o comprobante" />
          {needsReceipt ? (
            <View style={styles.receiptSection}>
              <Text style={styles.label}>{paymentMethod === 5 ? "Comprobante de Kash" : "Imagen del comprobante"}</Text>
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
          <PrimaryButton title="Registrar pago" onPress={submit} loading={saving} disabled={!selectedLoan || !selectedInstallmentId || !amountPaid} />
          <Text style={styles.hint}>El pago se aplica desde la cuota pendiente más antigua hasta la cuota seleccionada, incluyendo la mora pendiente.</Text>
        </Card>

        {selectedLoan ? (
          <Card title={`Historial de pagos (${payments.length})`}>
            {payments.length === 0 ? <EmptyState text="Este préstamo todavía no tiene pagos registrados." /> : null}
            {payments.map((payment) => (
              <View key={payment.id} style={styles.paymentItem}>
                <View style={styles.paymentHeader}>
                  <View style={styles.paymentMain}>
                    <Text style={styles.paymentAmount}>{money(payment.amountPaid, currency)}</Text>
                    <Text style={styles.muted}>{dateOnly(payment.paymentDate)} · {paymentMethodLabels[payment.paymentMethod]}</Text>
                  </View>
                  {payment.type === 2 ? <Text style={styles.extraordinaryBadge}>Abono extraordinario</Text> : payment.receiptId ? <Text style={styles.receiptBadge}>Comprobante</Text> : null}
                </View>
                {payment.type === 2 ? (
                  <View style={styles.extraordinarySummary}>
                    {payment.previousOutstandingPrincipal != null && payment.newOutstandingPrincipal != null ? (
                      <Text style={styles.muted}>Capital: {money(payment.previousOutstandingPrincipal, currency)} → {money(payment.newOutstandingPrincipal, currency)}</Text>
                    ) : null}
                    {payment.previousInstallmentAmount != null && payment.newInstallmentAmount != null ? (
                      <Text style={styles.muted}>Cuota: {money(payment.previousInstallmentAmount, currency)} → {money(payment.newInstallmentAmount, currency)}</Text>
                    ) : null}
                    {payment.previousInstallmentCount != null && payment.newInstallmentCount != null ? (
                      <Text style={styles.muted}>Pagos restantes: {payment.previousInstallmentCount} → {payment.newInstallmentCount}</Text>
                    ) : null}
                  </View>
                ) : null}
                {payment.referenceNumber ? <Text style={styles.muted}>Referencia: {payment.referenceNumber}</Text> : null}
                {payment.notes ? <Text style={styles.muted}>{payment.notes}</Text> : null}
                {payment.receiptId ? (
                  <Pressable accessibilityRole="button" onPress={() => setSelectedReceiptId(payment.receiptId ?? null)} style={styles.receiptLink}>
                    <Image source={api.paymentReceiptSource(payment.receiptId)} style={styles.paymentReceipt} />
                    <Text style={styles.receiptLinkText}>Ver comprobante</Text>
                  </Pressable>
                ) : null}
              </View>
            ))}
          </Card>
        ) : null}

        {loans.length === 0 ? <EmptyState text="No hay prestamos activos." /> : null}
      </ScrollView>

      <Modal animationType="fade" transparent visible={Boolean(selectedReceiptId)} onRequestClose={() => setSelectedReceiptId(null)}>
        <Pressable onPress={() => setSelectedReceiptId(null)} style={styles.receiptModalOverlay}>
          <View style={styles.receiptModalCard}>
            <Text style={styles.receiptModalTitle}>Comprobante de pago</Text>
            {selectedReceiptId ? <Image resizeMode="contain" source={api.paymentReceiptSource(selectedReceiptId)} style={styles.receiptModalImage} /> : null}
            <GhostButton title="Cerrar" onPress={() => setSelectedReceiptId(null)} />
          </View>
        </Pressable>
      </Modal>
    </Screen>
  );
}

function installmentPendingAmount(installment: Installment) {
  return Math.max(0, installment.paymentAmount - installment.amountPaid);
}

const styles = StyleSheet.create({
  content: {
    paddingBottom: spacing.xl
  },
  loanName: {
    color: colors.text,
    fontSize: 17,
    fontWeight: "900"
  },
  muted: {
    color: colors.muted,
    marginTop: 4
  },
  due: {
    color: colors.warn,
    fontWeight: "900",
  },
  dueRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 4,
    marginTop: spacing.sm
  },
  late: {
    color: colors.danger,
    fontWeight: "900",
    marginTop: 4
  },
  lateFeeSummary: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.sm,
    marginTop: 4
  },
  label: {
    color: colors.muted,
    fontSize: 13,
    fontWeight: "800",
    marginBottom: spacing.xs
  },
  methodRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.sm,
    marginBottom: spacing.sm
  },
  method: {
    backgroundColor: colors.soft,
    borderRadius: 999,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm
  },
  methodActive: {
    backgroundColor: colors.primary
  },
  methodText: {
    color: colors.primaryDark,
    fontWeight: "800"
  },
  methodTextActive: {
    color: "#fff"
  },
  hint: {
    color: colors.muted,
    fontSize: 12,
    lineHeight: 18,
    marginTop: spacing.sm
  },
  receiptSection: {
    marginBottom: spacing.sm
  },
  receiptHint: {
    color: colors.muted,
    fontSize: 12,
    marginBottom: spacing.sm
  },
  receiptActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.sm
  },
  receiptPreview: {
    alignItems: "center",
    backgroundColor: colors.soft,
    borderRadius: 9,
    flexDirection: "row",
    gap: spacing.sm,
    marginTop: spacing.sm,
    padding: spacing.sm
  },
  receiptImage: {
    borderRadius: 6,
    height: 64,
    width: 64
  },
  receiptInfo: {
    flex: 1,
    gap: spacing.sm
  },
  receiptName: {
    color: colors.text,
    fontWeight: "800"
  },
  paymentItem: {
    borderBottomColor: colors.border,
    borderBottomWidth: 1,
    paddingVertical: spacing.md
  },
  paymentHeader: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: spacing.sm,
    justifyContent: "space-between"
  },
  paymentMain: {
    flex: 1
  },
  paymentAmount: {
    color: colors.good,
    fontSize: 17,
    fontWeight: "900"
  },
  receiptBadge: {
    backgroundColor: colors.soft,
    borderRadius: 999,
    color: colors.primary,
    fontSize: 11,
    fontWeight: "900",
    paddingHorizontal: 8,
    paddingVertical: 4
  },
  extraordinaryBadge: {
    backgroundColor: "#dff5ea",
    borderRadius: 999,
    color: colors.good,
    fontSize: 11,
    fontWeight: "900",
    paddingHorizontal: spacing.sm,
    paddingVertical: 4
  },
  extraordinarySummary: {
    backgroundColor: colors.soft,
    borderRadius: 8,
    marginTop: spacing.sm,
    padding: spacing.sm
  },
  receiptLink: {
    alignItems: "center",
    alignSelf: "flex-start",
    flexDirection: "row",
    gap: spacing.sm,
    marginTop: spacing.sm
  },
  paymentReceipt: {
    backgroundColor: colors.soft,
    borderRadius: 6,
    height: 48,
    width: 48
  },
  receiptLinkText: {
    color: colors.primary,
    fontWeight: "900"
  },
  receiptModalOverlay: {
    alignItems: "center",
    backgroundColor: "rgba(16, 34, 50, 0.72)",
    flex: 1,
    justifyContent: "center",
    padding: spacing.lg
  },
  receiptModalCard: {
    backgroundColor: colors.card,
    borderRadius: 10,
    maxHeight: "86%",
    padding: spacing.md,
    width: "100%"
  },
  receiptModalTitle: {
    color: colors.text,
    fontSize: 18,
    fontWeight: "900",
    marginBottom: spacing.md
  },
  receiptModalImage: {
    backgroundColor: colors.soft,
    borderRadius: 8,
    height: 430,
    marginBottom: spacing.md,
    width: "100%"
  }
});
