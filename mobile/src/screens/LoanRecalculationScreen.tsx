import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useCallback, useState } from "react";
import { Alert, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, Field, PrimaryButton, Screen, SelectField, Text } from "../components/ui";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { LoanDetail, LoanRecalculationPreview } from "../types/models";
import { currencyLabels, dateInputValue, dateOnly, money } from "../utils/format";

type Props = NativeStackScreenProps<RootStackParamList, "LoanRecalculation">;

const modes = [
  { value: "1", label: "Reducir el monto de las cuotas" },
  { value: "2", label: "Mantener la cuota y reducir el plazo" }
];

export function LoanRecalculationScreen({ route, navigation }: Props) {
  const [detail, setDetail] = useState<LoanDetail | null>(null);
  const [mode, setMode] = useState("1");
  const [effectiveDate, setEffectiveDate] = useState(dateInputValue());
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

  function requestPayload() {
    return { mode: Number(mode), effectiveDate };
  }

  async function calculatePreview() {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(effectiveDate)) {
      setError("La fecha efectiva debe usar el formato AAAA-MM-DD.");
      return;
    }

    try {
      setLoading(true);
      setError("");
      setPreview(await api.previewLoanRecalculation(route.params.loanId, requestPayload()));
    } catch (err) {
      setPreview(null);
      setError(err instanceof Error ? err.message : "No se pudo calcular la vista previa.");
    } finally {
      setLoading(false);
    }
  }

  async function applyRecalculation() {
    try {
      setSaving(true);
      setError("");
      const updated = await api.recalculateLoan(route.params.loanId, requestPayload());
      Alert.alert(
        "Plan recalculado",
        "Las cuotas pagadas se conservaron y el nuevo plan quedo guardado.",
        [{ text: "Ver tabla", onPress: () => navigation.replace("LoanDetail", { loanId: updated.loan.id }) }]
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo recalcular el prestamo.");
    } finally {
      setSaving(false);
    }
  }

  const currency = detail ? currencyLabels[detail.loan.currency] : "C$";

  return (
    <Screen>
      <ScrollView keyboardShouldPersistTaps="handled" refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        {detail ? (
          <>
            <Card title={`Recalcular - ${detail.loan.clientName}`}>
              <Text style={styles.help}>Las cuotas pagadas no se modificaran. El nuevo plan se calcula sobre el capital pendiente.</Text>
              <SelectField
                label="Modalidad"
                value={mode}
                options={modes}
                onChange={(value) => {
                  setMode(value);
                  setPreview(null);
                }}
              />
              <Field
                label="Fecha efectiva"
                value={effectiveDate}
                onChangeText={(value) => {
                  setEffectiveDate(value);
                  setPreview(null);
                }}
                placeholder="AAAA-MM-DD"
              />
              <Text style={styles.rule}>El prestamo debe estar al dia, sin cuotas parciales, vencidas ni mora pendiente.</Text>
              <PrimaryButton title="Calcular vista previa" onPress={() => void calculatePreview()} loading={loading} />
            </Card>

            {preview ? (
              <Card title="Vista previa del nuevo plan">
                <PreviewRow label="Capital pendiente" value={money(preview.outstandingPrincipal, currency)} />
                <PreviewRow label="Cuota actual" value={money(preview.currentInstallmentAmount, currency)} />
                <PreviewRow label="Nueva cuota" value={money(preview.newInstallmentAmount, currency)} highlight />
                <PreviewRow label="Pagos restantes" value={`${preview.currentRemainingInstallments} -> ${preview.newRemainingInstallments}`} />
                <PreviewRow label="Interes nuevo" value={money(preview.newInterest, currency)} />
                <PreviewRow label="Nuevo pendiente" value={money(preview.newPendingTotal, currency)} />
                <PreviewRow label="Primera cuota" value={dateOnly(preview.firstDueDate)} />
                <View style={styles.confirm}>
                  <PrimaryButton title="Confirmar nuevo plan" onPress={() => void applyRecalculation()} loading={saving} />
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
