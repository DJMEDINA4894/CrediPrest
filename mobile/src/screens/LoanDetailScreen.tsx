import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useCallback, useState } from "react";
import { RefreshControl, ScrollView, StyleSheet, Text, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, GhostButton, Screen } from "../components/ui";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { LoanDetail } from "../types/models";
import { currencyLabels, dateOnly, installmentPendingAmount, installmentStatusLabels, money } from "../utils/format";

type Props = NativeStackScreenProps<RootStackParamList, "LoanDetail">;

export function LoanDetailScreen({ route, navigation }: Props) {
  const [detail, setDetail] = useState<LoanDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

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

  return (
    <Screen>
      <ScrollView refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        {detail ? (
          <>
            <Card title={detail.loan.clientName}>
              {detail.loan.referenceName ? <Text style={styles.muted}>Referencia: {detail.loan.referenceName}</Text> : null}
              <Text style={styles.total}>Total: {money(detail.loan.totalToPay, currency)}</Text>
              <Text style={styles.paid}>Pagado: {money(detail.loan.totalPaid, currency)}</Text>
              <Text style={styles.due}>Debe: {money(detail.loan.pendingBalance, currency)}</Text>
              {detail.loan.lateFeesPending > 0 ? <Text style={styles.late}>Mora pendiente: {money(detail.loan.lateFeesPending, currency)}</Text> : null}
              {detail.loan.status !== 2 ? (
                <View style={styles.action}>
                  <GhostButton title="Registrar pago" onPress={() => navigation.navigate("Payments", { loan: detail.loan })} />
                </View>
              ) : null}
            </Card>

            <Card title="Tabla de pagos">
              {detail.installments.map((installment) => (
                <View key={installment.id} style={styles.item}>
                  <View style={styles.row}>
                    <Text style={styles.itemTitle}>Cuota {installment.installmentNumber}</Text>
                    <Text style={[styles.badge, installment.status === 3 ? styles.paidBadge : installment.status === 4 ? styles.overdueBadge : styles.pendingBadge]}>{installmentStatusLabels[installment.status]}</Text>
                  </View>
                  <Text style={styles.muted}>Vence: {dateOnly(installment.dueDate)}</Text>
                  <Text style={styles.muted}>Cuota: {money(installment.paymentAmount, currency)}</Text>
                  <Text style={styles.muted}>Pagado: {money(installment.amountPaid, currency)}</Text>
                  <Text style={styles.pending}>Pendiente: {money(installmentPendingAmount(installment), currency)}</Text>
                </View>
              ))}
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
