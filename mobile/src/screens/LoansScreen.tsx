import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useCallback, useState } from "react";
import { Alert, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, DangerButton, EmptyState, ErrorText, GhostButton, PrimaryButton, Screen, Text } from "../components/ui";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { Loan } from "../types/models";
import { currencyLabels, money, statusLabels } from "../utils/format";

type Props = NativeStackScreenProps<RootStackParamList, "Loans">;

export function LoansScreen({ navigation }: Props) {
  const [loans, setLoans] = useState<Loan[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      setError("");
      setLoading(true);
      setLoans(await api.loans());
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudieron cargar los prestamos.");
    } finally {
      setLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => {
    void load();
  }, [load]));

  async function cancelLoan(loan: Loan) {
    try {
      setError("");
      await api.cancelLoan(loan.id);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cancelar el prestamo.");
    }
  }

  function confirmCancel(loan: Loan) {
    Alert.alert("Cancelar prestamo", `Cancelar el prestamo de ${loan.clientName}? Ya no se podran registrar mas pagos.`, [
      { text: "Volver", style: "cancel" },
      { text: "Cancelar prestamo", style: "destructive", onPress: () => void cancelLoan(loan) }
    ]);
  }

  function deleteLoan(loan: Loan) {
    Alert.alert("Eliminar prestamo", `Eliminar el prestamo de ${loan.clientName} borrara sus cuotas y pagos. Esta accion no se puede deshacer.`, [
      { text: "Cancelar", style: "cancel" },
      {
        text: "Eliminar",
        style: "destructive",
        onPress: () => void (async () => {
          try {
            setError("");
            await api.deleteLoan(loan.id);
            await load();
          } catch (err) {
            setError(err instanceof Error ? err.message : "No se pudo eliminar el prestamo.");
          }
        })()
      }
    ]);
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content} refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        <View style={styles.createAction}>
          <PrimaryButton title="Nuevo prestamo" onPress={() => navigation.navigate("LoanForm")} />
        </View>
        {loans.length === 0 ? <EmptyState text="No hay prestamos registrados." /> : null}
        {loans.map((loan) => (
          <Card key={loan.id}>
            <View style={styles.row}>
              <View style={styles.main}>
                <Text style={styles.name}>{loan.clientName}</Text>
                {loan.referenceName ? <Text style={styles.muted}>{loan.referenceName}</Text> : null}
              </View>
              <Text style={[styles.badge, loan.status === 1 ? styles.activeBadge : styles.inactiveBadge]}>{statusLabels[loan.status]}</Text>
            </View>
            <View style={styles.amounts}>
              <Text style={styles.amount}>Monto: {money(loan.principalAmount, currencyLabels[loan.currency])}</Text>
              <Text style={styles.due}>Debe: {money(loan.pendingBalance, currencyLabels[loan.currency])}</Text>
              {loan.lateFeesPending > 0 ? <Text style={styles.late}>Mora: {money(loan.lateFeesPending, currencyLabels[loan.currency])}</Text> : null}
            </View>
            <View style={styles.actions}>
              <GhostButton title="Detalle" onPress={() => navigation.navigate("LoanDetail", { loanId: loan.id })} />
              <GhostButton title="Nuevo" onPress={() => navigation.navigate("LoanForm", { clientId: loan.clientId })} />
              {loan.status !== 2 ? <GhostButton title="Editar" onPress={() => navigation.navigate("LoanForm", { loan })} /> : null}
              {loan.status !== 2 ? <GhostButton title="Pagar" onPress={() => navigation.navigate("Payments", { loan })} /> : null}
              {loan.status !== 2 ? <GhostButton title="Cancelar" onPress={() => confirmCancel(loan)} /> : null}
              <DangerButton title="Eliminar" onPress={() => deleteLoan(loan)} />
            </View>
          </Card>
        ))}
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingBottom: spacing.xl
  },
  row: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: spacing.sm,
    justifyContent: "space-between"
  },
  main: {
    flex: 1
  },
  name: {
    color: colors.text,
    fontSize: 16,
    fontWeight: "900"
  },
  muted: {
    color: colors.muted,
    marginTop: 3
  },
  badge: {
    borderRadius: 999,
    fontSize: 12,
    fontWeight: "900",
    paddingHorizontal: spacing.sm,
    paddingVertical: 4
  },
  activeBadge: {
    backgroundColor: "#dff5ea",
    color: colors.good
  },
  inactiveBadge: {
    backgroundColor: "#ffe8ea",
    color: colors.danger
  },
  amounts: {
    gap: 4,
    marginVertical: spacing.sm
  },
  amount: {
    color: colors.text,
    fontWeight: "700"
  },
  due: {
    color: colors.warn,
    fontWeight: "900"
  },
  late: {
    color: colors.danger,
    fontWeight: "900"
  },
  actions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.sm
  },
  createAction: {
    marginBottom: spacing.md
  }
});
