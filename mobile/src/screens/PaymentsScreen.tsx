import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useCallback, useState } from "react";
import { Alert, Image, Pressable, RefreshControl, ScrollView, StyleSheet, Text, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import * as ImagePicker from "expo-image-picker";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, Field, GhostButton, PrimaryButton, Screen, SelectField } from "../components/ui";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { Loan } from "../types/models";
import { currencyLabels, dateInputValue, money } from "../utils/format";

type Props = NativeStackScreenProps<RootStackParamList, "Payments">;

export function PaymentsScreen({ route, navigation }: Props) {
  const [loans, setLoans] = useState<Loan[]>([]);
  const [selectedLoan, setSelectedLoan] = useState<Loan | null>(route.params?.loan ?? null);
  const [paymentDate, setPaymentDate] = useState(dateInputValue());
  const [amountPaid, setAmountPaid] = useState("");
  const [referenceNumber, setReferenceNumber] = useState("");
  const [notes, setNotes] = useState("");
  const [paymentMethod, setPaymentMethod] = useState<1 | 2 | 3 | 4>(1);
  const [receipt, setReceipt] = useState<{ base64: string; fileName: string; contentType: string; uri: string } | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      setError("");
      setLoading(true);
      setLoans((await api.loans()).filter((loan) => loan.status !== 2));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudieron cargar los prestamos.");
    } finally {
      setLoading(false);
    }
  }, []);

  const selectLoan = useCallback(async (loan: Loan) => {
    setSelectedLoan(loan);

    try {
      const detail = await api.loanDetail(loan.id);
      const orderedInstallments = [...detail.installments]
        .filter((installment) => installment.status !== 3)
        .sort((left, right) => dateInputValue(left.dueDate).localeCompare(dateInputValue(right.dueDate)));
      const today = dateInputValue();
      const suggestedInstallment = orderedInstallments.find((installment) => dateInputValue(installment.dueDate) <= today)
        ?? orderedInstallments[0];

      if (suggestedInstallment) {
        setPaymentDate(dateInputValue(suggestedInstallment.dueDate));
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo preparar la fecha de pago.");
    }
  }, []);

  useFocusEffect(useCallback(() => {
    void load();
    if (route.params?.loan) {
      void selectLoan(route.params.loan);
    }
  }, [load, route.params?.loan, selectLoan]));

  async function submit() {
    if (!selectedLoan) {
      setError("Selecciona un prestamo.");
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
        installmentId: null,
        paymentDate,
        amountPaid: amount,
        paymentMethod,
        referenceNumber: referenceNumber || undefined,
        notes: notes || undefined,
        ...(paymentMethod === 2 || paymentMethod === 3 ? {
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
  const needsReceipt = paymentMethod === 2 || paymentMethod === 3;
  const loanOptions = loans.map((loan) => ({
    value: loan.id,
    label: `${loan.clientName}${loan.referenceName ? ` - ${loan.referenceName}` : ""} - ${money(loan.pendingBalance, currencyLabels[loan.currency])}`
  }));

  return (
    <Screen>
      <ScrollView refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
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
              <Text style={styles.due}>Debe: {money(selectedLoan.pendingBalance, currency)}</Text>
              {selectedLoan.lateFeesPending > 0 ? <Text style={styles.late}>Mora: {money(selectedLoan.lateFeesPending, currency)}</Text> : null}
            </>
          ) : (
            <EmptyState text="Selecciona un prestamo arriba." />
          )}
        </Card>

        <Card title="Registrar pago">
          <Field label="Fecha de pago" value={paymentDate} onChangeText={setPaymentDate} placeholder="YYYY-MM-DD" />
          <Field label="Monto pagado" value={amountPaid} onChangeText={setAmountPaid} keyboardType="decimal-pad" placeholder="Ej. 500" />
          <Text style={styles.label}>Metodo de pago</Text>
          <View style={styles.methodRow}>
            {[
              [1, "Efectivo"],
              [2, "Transferencia"],
              [3, "Deposito"],
              [4, "Otro"]
            ].map(([value, label]) => (
              <Pressable
                key={String(value)}
                style={[styles.method, paymentMethod === value && styles.methodActive]}
                onPress={() => setPaymentMethod(value as 1 | 2 | 3 | 4)}
              >
                <Text style={[styles.methodText, paymentMethod === value && styles.methodTextActive]}>{label}</Text>
              </Pressable>
            ))}
          </View>
          <Field label="Referencia" value={referenceNumber} onChangeText={setReferenceNumber} placeholder="Numero de referencia o comprobante" />
          {needsReceipt ? (
            <View style={styles.receiptSection}>
              <Text style={styles.label}>Imagen del comprobante</Text>
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
          <PrimaryButton title="Registrar pago" onPress={submit} loading={saving} disabled={!selectedLoan || !amountPaid} />
          <Text style={styles.hint}>El pago se aplica primero a cuotas vencidas, luego a mora pendiente y despues a cuotas actuales.</Text>
        </Card>

        {loans.length === 0 ? <EmptyState text="No hay prestamos activos." /> : null}
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
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
    marginTop: spacing.sm
  },
  late: {
    color: colors.danger,
    fontWeight: "900",
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
  }
});
