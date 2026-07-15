import { useCallback, useState } from "react";
import { Pressable, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { Ionicons } from "@expo/vector-icons";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, InfoTooltip, Metric, Screen, SecondaryButton, Text } from "../components/ui";
import { useAuth } from "../context/AuthContext";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { LoanDetail } from "../types/models";
import { currencyLabels, dateOnly, effectiveInstallmentStatus, installmentPendingAmount, installmentStatusLabels, lateFeeAllocation, lateFeePolicyText, money, monthYear } from "../utils/format";
import { shareLoanPaymentPlan } from "../utils/loanDocuments";

export function ClientPortalScreen() {
  const { user } = useAuth();
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [plans, setPlans] = useState<LoanDetail[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [downloadingLoanId, setDownloadingLoanId] = useState<string | null>(null);
  const [unreadNotifications, setUnreadNotifications] = useState(0);

  const load = useCallback(async () => {
    try {
      setError("");
      setLoading(true);
      const [paymentPlans, notifications] = await Promise.all([
        api.clientPaymentPlans(),
        api.notifications()
      ]);
      setPlans(paymentPlans);
      setUnreadNotifications(notifications.filter((notification) => !notification.isRead).length);
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

  async function downloadPlan(plan: LoanDetail) {
    try {
      setError("");
      setDownloadingLoanId(plan.loan.id);
      await shareLoanPaymentPlan(plan, true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo descargar la tabla de pagos.");
    } finally {
      setDownloadingLoanId(null);
    }
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.contentContainer} refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <View style={styles.header}>
          <View style={styles.headerText}>
            <Text style={styles.kicker}>Portal cliente</Text>
            <Text style={styles.title}>{user?.fullName}</Text>
          </View>
          <Pressable
            accessibilityLabel={`Notificaciones${unreadNotifications > 0 ? `, ${unreadNotifications} nuevas` : ""}`}
            accessibilityRole="button"
            hitSlop={8}
            onPress={() => navigation.navigate("Notifications")}
            style={({ pressed }) => [styles.notificationButton, pressed && styles.notificationButtonPressed]}
          >
            <Ionicons color="#fff" name="notifications" size={24} />
            {unreadNotifications > 0 ? (
              <Text style={styles.notificationBadge}>{Math.min(unreadNotifications, 99)}</Text>
            ) : null}
          </Pressable>
        </View>
        <ErrorText text={error} />
        <View style={styles.metrics}>
          <Metric title="Debe C$" value={money(pendingCordobas)} tone="warn" />
          <Metric title="Debe USD" value={money(pendingUsd, "USD")} tone="warn" />
        </View>
        {plans.length === 0 ? <EmptyState text="No tienes prestamos activos registrados para mostrar." /> : null}
        {plans.map((plan) => {
          const currency = currencyLabels[plan.loan.currency];
          return (
            <Card key={plan.loan.id} title={plan.loan.referenceName ?? "Prestamo"}>
              <Text style={styles.due}>Debe: {money(plan.loan.pendingBalance, currency)}</Text>
              {plan.loan.lateFeeDescription ? (
                <View style={styles.lateFeeSummary}>
                  <Text style={styles.muted}>Mora configurada: {plan.loan.lateFeeDescription}</Text>
                  <InfoTooltip
                    title="Cómo se calcula la mora"
                    message={lateFeePolicyText(plan.loan.paymentFrequency, plan.loan.principalAmount, plan.loan.monthlyInterestRate, Number(plan.loan.lateFeeDescription.replace("%", "")) || 50, currencyLabels[plan.loan.currency], plan.loan.termMonths)}
                  />
                </View>
              ) : null}
              {plan.loan.lateFeesPending > 0 ? <Text style={styles.late}>Mora pendiente: {money(plan.loan.lateFeesPending, currency)}</Text> : null}
              <View style={styles.documentAction}>
                <SecondaryButton
                  title={downloadingLoanId === plan.loan.id ? "Descargando..." : "Descargar tabla PDF"}
                  onPress={() => void downloadPlan(plan)}
                />
              </View>
              {plan.installments.map((installment) => {
                const mora = lateFeeAllocation(plan, installment);
                const pending = installmentPendingAmount(installment) + mora.pendingAmount;
                const status = effectiveInstallmentStatus(installment);

                return (
                  <View
                    key={installment.id}
                    style={[
                      styles.item,
                      status === 3 ? styles.itemPaid : status === 4 ? styles.itemOverdue : styles.itemPending
                    ]}
                  >
                    <View style={styles.installmentHeader}>
                      <Text style={styles.itemTitle}>Cuota {installment.installmentNumber}</Text>
                      <Text style={[
                        styles.statusBadge,
                        status === 3 ? styles.statusPaid : status === 4 ? styles.statusOverdue : styles.statusPending
                      ]}>
                        {installmentStatusLabels[status]}
                      </Text>
                    </View>
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
                      Mora de {monthYear(charge.periodStartDate)}: {money(charge.pendingAmount, currency)} pendiente
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
  notificationButton: {
    alignItems: "center",
    backgroundColor: colors.primary,
    borderRadius: 999,
    height: 46,
    justifyContent: "center",
    position: "relative",
    width: 46
  },
  notificationButtonPressed: {
    opacity: 0.82
  },
  notificationBadge: {
    backgroundColor: "#d63d45",
    borderColor: "#fff",
    borderRadius: 999,
    borderWidth: 2,
    color: "#fff",
    fontSize: 10,
    fontWeight: "900",
    minWidth: 20,
    overflow: "hidden",
    paddingHorizontal: 4,
    paddingVertical: 1,
    position: "absolute",
    right: -4,
    textAlign: "center",
    top: -5
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
    borderLeftWidth: 4,
    borderRadius: 8,
    marginTop: spacing.sm,
    padding: spacing.sm
  },
  itemPaid: {
    backgroundColor: "#e6f7ef",
    borderLeftColor: colors.good
  },
  itemOverdue: {
    backgroundColor: "#fdebec",
    borderLeftColor: colors.danger
  },
  itemPending: {
    backgroundColor: "#fff5dc",
    borderLeftColor: colors.warn
  },
  installmentHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between"
  },
  itemTitle: {
    color: colors.text,
    fontWeight: "900"
  },
  statusBadge: {
    borderRadius: 999,
    fontSize: 11,
    fontWeight: "900",
    overflow: "hidden",
    paddingHorizontal: 9,
    paddingVertical: 4
  },
  statusPaid: {
    backgroundColor: "#cdeedf",
    color: colors.good
  },
  statusOverdue: {
    backgroundColor: "#f8ced1",
    color: colors.danger
  },
  statusPending: {
    backgroundColor: "#ffe7ad",
    color: colors.warn
  },
  muted: {
    color: colors.muted,
    marginTop: 3
  },
  lateFeeSummary: {
    alignItems: "center",
    flexDirection: "row",
    gap: spacing.xs,
    marginTop: 4
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
  },
  documentAction: {
    marginTop: spacing.md
  }
});
