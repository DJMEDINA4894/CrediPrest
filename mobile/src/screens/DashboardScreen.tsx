import { useCallback, useState } from "react";
import { RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, Metric, Screen } from "../components/ui";
import type { Dashboard } from "../types/models";
import { money } from "../utils/format";
import { spacing } from "../theme/theme";

export function DashboardScreen() {
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      setError("");
      setLoading(true);
      setDashboard(await api.dashboard());
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cargar el dashboard.");
    } finally {
      setLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => {
    load();
  }, [load]));

  return (
    <Screen>
      <ScrollView refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        {dashboard ? (
          <>
            <View style={styles.metrics}>
              <Metric title="Prestado C$" value={money(dashboard.totalLoanedCordobas)} />
              <Metric title="Prestado USD" value={money(dashboard.totalLoanedUsd, "USD")} />
              <Metric title="Recuperado C$" value={money(dashboard.totalRecoveredCordobas)} tone="good" />
              <Metric title="Recuperado USD" value={money(dashboard.totalRecoveredUsd, "USD")} tone="good" />
              <Metric title="Pendiente C$" value={money(dashboard.pendingCordobas)} tone="warn" />
              <Metric title="Pendiente USD" value={money(dashboard.pendingUsd, "USD")} tone="warn" />
            </View>
            <Card title="Actividad">
              <View style={styles.metrics}>
                <Metric title="Clientes activos" value={String(dashboard.activeClients)} />
                <Metric title="Prestamos activos" value={String(dashboard.activeLoans)} />
                <Metric title="Vencidos" value={String(dashboard.overdueLoans)} tone="danger" />
                <Metric title="Vencen hoy" value={String(dashboard.dueTodayInstallments)} tone="warn" />
                <Metric title="Vencen esta semana" value={String(dashboard.dueThisWeekInstallments)} tone="warn" />
                <Metric title="Cuotas atrasadas" value={String(dashboard.overdueInstallments)} tone="danger" />
              </View>
            </Card>
            <Card title="Cobros registrados">
              <View style={styles.metrics}>
                <Metric title="Hoy C$" value={money(dashboard.paidTodayCordobas)} tone="good" />
                <Metric title="Hoy USD" value={money(dashboard.paidTodayUsd, "USD")} tone="good" />
                <Metric title="Semana C$" value={money(dashboard.paidThisWeekCordobas)} tone="good" />
                <Metric title="Semana USD" value={money(dashboard.paidThisWeekUsd, "USD")} tone="good" />
                <Metric title="Mes C$" value={money(dashboard.paidThisMonthCordobas)} tone="good" />
                <Metric title="Mes USD" value={money(dashboard.paidThisMonthUsd, "USD")} tone="good" />
              </View>
            </Card>
          </>
        ) : (
          <EmptyState text="Cargando informacion del dashboard..." />
        )}
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  metrics: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.sm,
    marginBottom: spacing.md
  }
});
