import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useCallback, useState } from "react";
import { RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, GhostButton, Screen, Text } from "../components/ui";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { LoanDetail } from "../types/models";
import { currencyLabels, dateOnly, effectiveInstallmentStatus, installmentPendingAmount, installmentStatusLabels, lateFeeAllocation, lateFeePolicyText, money } from "../utils/format";
import { shareLoanAgreement, shareLoanPaymentPlan } from "../utils/loanDocuments";

type Props = NativeStackScreenProps<RootStackParamList, "LoanDetail">;

export function LoanDetailScreen({ route, navigation }: Props) {
  const [detail, setDetail] = useState<LoanDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [documentAction, setDocumentAction] = useState<"pdf" | "agreement" | null>(null);

  const load = useCallback(async () => {
    try {
      setError("");
      setLoading(true);
      setDetail(await api.loanDetail(route.params.loanId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cargar el detalle.");
    } finally {
      setLoading(false);
    }
  }, [route.params.loanId]);

  useFocusEffect(useCallback(() => {
    load();
  }, [load]));

  const currency = detail ? currencyLabels[detail.loan.currency] : "C$";

  async function shareDocument(type: "pdf" | "agreement") {
    if (!detail) return;

    try {
      setError("");
      setDocumentAction(type);
      if (type === "pdf") {
        await shareLoanPaymentPlan(detail);
      } else {
        await shareLoanAgreement(detail);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo generar el documento.");
    } finally {
      setDocumentAction(null);
    }
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content} refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        {detail ? (
          <>
            <Card title={detail.loan.clientName}>
              {detail.loan.referenceName ? <Text style={styles.muted}>Referencia: {detail.loan.referenceName}</Text> : null}
              <Text style={styles.total}>Total: {money(detail.loan.totalToPay, currency)}</Text>
              <Text style={styles.paid}>Pagado: {money(detail.loan.totalPaid, currency)}</Text>
              <Text style={styles.due}>Debe: {money(detail.loan.pendingBalance, currency)}</Text>
              {detail.loan.lateFeeDescription ? <Text style={styles.muted}>Mora configurada: {detail.loan.lateFeeDescription}. {lateFeePolicyText(detail.loan.paymentFrequency, detail.loan.principalAmount, detail.loan.monthlyInterestRate, Number(detail.loan.lateFeeDescription.replace("%", "")) || 50, currency, detail.loan.termMonths)}</Text> : null}
              {detail.loan.lateFeesPending > 0 ? <Text style={styles.late}>Mora pendiente: {money(detail.loan.lateFeesPending, currency)}</Text> : null}
              <View style={styles.documentActions}>
                <GhostButton title={documentAction === "pdf" ? "Generando..." : "Tabla PDF"} onPress={() => void shareDocument("pdf")} />
                <GhostButton title={documentAction === "agreement" ? "Descargando..." : "Acuerdo Word"} onPress={() => void shareDocument("agreement")} />
              </View>
              {detail.loan.status !== 2 ? (
                <View style={styles.action}>
                  <GhostButton title="Registrar pago" onPress={() => navigation.navigate("Payments", { loan: detail.loan })} />
                  {detail.installments.some((installment) => installment.amountPaid >= installment.paymentAmount) && detail.loan.pendingBalance > 0 ? (
                    <GhostButton title="Recalcular cuotas" onPress={() => navigation.navigate("LoanRecalculation", { loanId: detail.loan.id })} />
                  ) : null}
                </View>
              ) : null}
            </Card>

            <Card title="Tabla de pagos">
              {detail.installments.map((installment) => {
                const status = effectiveInstallmentStatus(installment);
                const mora = lateFeeAllocation(detail, installment);
                const pending = installmentPendingAmount(installment) + mora.pendingAmount;

                return (
                <View key={installment.id} style={styles.item}>
                  <View style={styles.row}>
                    <Text style={styles.itemTitle}>Cuota {installment.installmentNumber}</Text>
                    <Text style={[styles.badge, status === 3 ? styles.paidBadge : status === 4 ? styles.overdueBadge : styles.pendingBadge]}>{installmentStatusLabels[status]}</Text>
                  </View>
                  <Text style={styles.muted}>Vence: {dateOnly(installment.dueDate)}</Text>
                  <Text style={styles.muted}>Cuota: {money(installment.paymentAmount, currency)}</Text>
                  {mora.amount > 0 ? <Text style={styles.late}>Mora: {money(mora.amount, currency)}</Text> : null}
                  <Text style={styles.muted}>Pagado: {money(installment.amountPaid + mora.amountPaid, currency)}</Text>
                  <Text style={styles.pending}>Pendiente: {money(pending, currency)}</Text>
                </View>
                );
              })}
            </Card>

            {detail.charges.length > 0 ? (
              <Card title="Moras aplicadas">
                {detail.charges.map((charge) => (
                  <View key={charge.id} style={styles.item}>
                    <Text style={styles.itemTitle}>Periodo {charge.periodNumber}</Text>
                    <Text style={styles.muted}>Desde {dateOnly(charge.periodStartDate)} hasta {dateOnly(charge.periodEndDate)}</Text>
                    <Text style={styles.muted}>Monto: {money(charge.amount, currency)}</Text>
                    <Text style={styles.pending}>Pendiente: {money(charge.pendingAmount, currency)}</Text>
                  </View>
                ))}
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

const styles = StyleSheet.create({
  content: {
    paddingBottom: spacing.xl
  },
  muted: {
    color: colors.muted,
    marginTop: 4
  },
  total: {
    color: colors.text,
    fontWeight: "800",
    marginTop: spacing.sm
  },
  paid: {
    color: colors.good,
    fontWeight: "900",
    marginTop: 4
  },
  due: {
    color: colors.warn,
    fontWeight: "900",
    marginTop: 4
  },
  late: {
    color: colors.danger,
    fontWeight: "900",
    marginTop: 4
  },
  action: {
    gap: spacing.sm,
    marginTop: spacing.md
  },
  documentActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.sm,
    marginTop: spacing.md
  },
  item: {
    borderBottomColor: colors.border,
    borderBottomWidth: 1,
    paddingVertical: spacing.sm
  },
  row: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between"
  },
  itemTitle: {
    color: colors.text,
    fontWeight: "900"
  },
  badge: {
    borderRadius: 999,
    fontSize: 12,
    fontWeight: "900",
    paddingHorizontal: spacing.sm,
    paddingVertical: 4
  },
  paidBadge: {
    backgroundColor: "#dff5ea",
    color: colors.good
  },
  pendingBadge: {
    backgroundColor: "#fff0d7",
    color: colors.warn
  },
  overdueBadge: {
    backgroundColor: "#ffe8ea",
    color: colors.danger
  },
  pending: {
    color: colors.warn,
    fontWeight: "900",
    marginTop: 4
  }
});
