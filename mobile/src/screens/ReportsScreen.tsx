import { useCallback, useState } from "react";
import { RefreshControl, ScrollView, StyleSheet, Text, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, Metric, Screen } from "../components/ui";
import { colors, spacing } from "../theme/theme";
import type { Client, Loan } from "../types/models";
import { currencyLabels, money, statusLabels } from "../utils/format";

export function ReportsScreen() {
  const [loans, setLoans] = useState<Loan[]>([]);
  const [clients, setClients] = useState<Client[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      setError("");
      setLoading(true);
      const [loadedLoans, loadedClients] = await Promise.all([api.loans(), api.clients()]);
      setLoans(loadedLoans);
      setClients(loadedClients);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cargar el reporte.");
    } finally {
      setLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => {
    void load();
  }, [load]));

  const activeLoans = loans.filter((loan) => loan.status === 1);
  const overdueLoans = loans.filter((loan) => loan.status === 3);
  const pendingCordobas = loans.filter((loan) => loan.currency === 1).reduce((total, loan) => total + loan.pendingBalance, 0);
  const pendingUsd = loans.filter((loan) => loan.currency === 2).reduce((total, loan) => total + loan.pendingBalance, 0);

  return (
    <Screen>
      <ScrollView refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        <View style={styles.metrics}>
          <Metric title="Cartera C$" value={money(pendingCordobas)} tone="warn" />
          <Metric title="Cartera USD" value={money(pendingUsd, "USD")} tone="warn" />
          <Metric title="Prestamos activos" value={String(activeLoans.length)} />
          <Metric title="Prestamos vencidos" value={String(overdueLoans.length)} tone="danger" />
          <Metric title="Clientes activos" value={String(clients.filter((client) => client.isActive).length)} />
        </View>

        <Card title="Cartera pendiente">
          {loans.length === 0 ? <EmptyState text="No hay datos de prestamos para mostrar." /> : null}
          {loans.map((loan) => (
            <View key={loan.id} style={styles.loan}>
              <View style={styles.loanHeader}>
                <Text style={styles.name}>{loan.clientName}</Text>
                <Text style={[styles.status, loan.status === 3 && styles.overdue]}>{statusLabels[loan.status]}</Text>
              </View>
              <Text style={styles.detail}>{loan.referenceName ?? "Prestamo"}</Text>
              <Text style={styles.pending}>Debe {money(loan.pendingBalance, currencyLabels[loan.currency])}</Text>
              {loan.lateFeesPending > 0 ? <Text style={styles.late}>Mora pendiente {money(loan.lateFeesPending, currencyLabels[loan.currency])}</Text> : null}
            </View>
          ))}
        </Card>
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
  },
  loan: {
    borderBottomColor: colors.border,
    borderBottomWidth: 1,
    paddingVertical: spacing.sm
  },
  loanHeader: {
    alignItems: "center",
    flexDirection: "row",
    gap: spacing.sm,
    justifyContent: "space-between"
  },
  name: {
    color: colors.text,
    flex: 1,
    fontWeight: "900"
  },
  status: {
    backgroundColor: colors.soft,
    borderRadius: 999,
    color: colors.primaryDark,
    fontSize: 11,
    fontWeight: "900",
    paddingHorizontal: 8,
    paddingVertical: 4
  },
  overdue: {
    backgroundColor: "#ffe8ea",
    color: colors.danger
  },
  detail: {
    color: colors.muted,
    marginTop: 4
  },
  pending: {
    color: colors.warn,
    fontWeight: "900",
    marginTop: 4
  },
  late: {
    color: colors.danger,
    fontWeight: "900",
    marginTop: 4
  }
});
