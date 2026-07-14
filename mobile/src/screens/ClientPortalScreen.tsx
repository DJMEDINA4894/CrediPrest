import { useCallback, useState } from "react";
import { RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, Metric, Screen, SecondaryButton, Text } from "../components/ui";
import { useAuth } from "../context/AuthContext";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { LoanDetail } from "../types/models";
import { currencyLabels, dateOnly, effectiveInstallmentStatus, installmentPendingAmount, installmentStatusLabels, lateFeeAllocation, lateFeePolicyText, money } from "../utils/format";

export function ClientPortalScreen() {
  const { user } = useAuth();
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [plans, setPlans] = useState<LoanDetail[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      setError("");
      setLoading(true);
      setPlans(await api.clientPaymentPlans());
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cargar tu plan.");
    } finally {
      setLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => {
    load();
  }, [load]));

  const pendingCordobas = plans.filter((plan) => plan.loan.currency === 1).reduce((sum, plan) => sum + plan.loan.pendingBalance, 0);
  const pendingUsd = plans.filter((plan) => plan.loan.currency === 2).reduce((sum, plan) => sum + plan.loan.pendingBalance, 0);

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.contentContainer} refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <View style={styles.header}>
          <View style={styles.headerText}>
            <Text style={styles.kicker}>Portal cliente</Text>
            <Text style={styles.title}>{user?.fullName}</Text>
          </View>
        </View>
        <ErrorText text={error} />
        <View style={styles.metrics}>
          <Metric title="Debe C$" value={money(pendingCordobas)} tone="warn" />
          <Metric title="Debe USD" value={money(pendingUsd, "USD")} tone="warn" />
        </View>
        <SecondaryButton title="Ver avisos y recordatorios" onPress={() => navigation.navigate("Notifications")} />

        {plans.length === 0 ? <EmptyState text="No tienes prestamos activos registrados para mostrar." /> : null}
        {plans.map((plan) => {
          const currency = currencyLabels[plan.loan.currency];
          return (
            <Card key={plan.loan.id} title={plan.loan.referenceName ?? "Prestamo"}>
              <Text style={styles.due}>Debe: {money(plan.loan.pendingBalance, currency)}</Text>
              {plan.loan.lateFeeDescription ? <Text style={styles.muted}>Mora configurada: {plan.loan.lateFeeDescription}. {lateFeePolicyText(plan.loan.paymentFrequency, plan.loan.principalAmount, plan.loan.monthlyInterestRate, Number(plan.loan.lateFeeDescription.replace("%", "")) || 50, currencyLabels[plan.loan.currency], plan.loan.termMonths)}</Text> : null}
              {plan.loan.lateFeesPending > 0 ? <Text style={styles.late}>Mora pendiente: {money(plan.loan.lateFeesPending, currency)}</Text> : null}
              {plan.installments.map((installment) => {
                const mora = lateFeeAllocation(plan, installment);
                const pending = installmentPendingAmount(installment) + mora.pendingAmount;

                return (
                  <View key={installment.id} style={styles.item}>
                    <Text style={styles.itemTitle}>Cuota {installment.installmentNumber} - {installmentStatusLabels[effectiveInstallmentStatus(installment)]}</Text>
                    <Text style={styles.muted}>Vence: {dateOnly(installment.dueDate)}</Text>
                    <Text style={styles.muted}>Cuota: {money(installment.paymentAmount, currency)}</Text>
                    {mora.amount > 0 ? <Text style={styles.late}>Mora: {money(mora.amount, currency)}</Text> : null}
                    <Text style={styles.pending}>Pendiente: {money(pending, currency)}</Text>
                  </View>
                );
              })}
              {plan.charges.length > 0 ? (
                <View style={styles.chargeBox}>
                  <Text style={styles.itemTitle}>Moras aplicadas</Text>
                  {plan.charges.map((charge) => (
                    <Text key={charge.id} style={styles.late}>
                      Periodo {charge.periodNumber}: {money(charge.pendingAmount, currency)} pendiente
                    </Text>
                  ))}
                </View>
              ) : null}
            </Card>
          );
        })}
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  contentContainer: {
    paddingBottom: spacing.xl
  },
  header: {
    alignItems: "center",
    flexDirection: "row",
    gap: spacing.sm,
    justifyContent: "space-between",
    marginBottom: spacing.md
  },
  headerText: {
    flex: 1
  },
  kicker: {
    color: colors.primary,
    fontSize: 12,
    fontWeight: "900",
    textTransform: "uppercase"
  },
  title: {
    color: colors.text,
    fontSize: 22,
    fontWeight: "900"
  },
  metrics: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.sm,
    marginBottom: spacing.md
  },
  due: {
    color: colors.warn,
    fontWeight: "900"
  },
  late: {
    color: colors.danger,
    fontWeight: "900",
    marginTop: 4
  },
  item: {
    borderBottomColor: colors.border,
    borderBottomWidth: 1,
    paddingVertical: spacing.sm
  },
  itemTitle: {
    color: colors.text,
    fontWeight: "900"
  },
  muted: {
    color: colors.muted,
    marginTop: 3
  },
  pending: {
    color: colors.warn,
    fontWeight: "900",
    marginTop: 3
  },
  chargeBox: {
    backgroundColor: "#fff4f4",
    borderRadius: 9,
    marginTop: spacing.md,
    padding: spacing.sm
  }
});
