import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { ScrollView, StyleSheet, Text, View } from "react-native";
import { Card, PrimaryButton, Screen } from "../components/ui";
import { useAuth } from "../context/AuthContext";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";

type Props = NativeStackScreenProps<RootStackParamList, "Home">;

export function HomeScreen({ navigation }: Props) {
  const { user } = useAuth();
  const isClient = user?.role === "Client";

  if (isClient) {
    navigation.replace("ClientPortal");
    return null;
  }

  return (
    <Screen>
      <ScrollView>
        <View style={styles.header}>
          <View>
            <Text style={styles.kicker}>{user?.role === "Admin" ? "Administrador" : "Prestamista"}</Text>
            <Text style={styles.title}>Hola, {user?.fullName}</Text>
          </View>
        </View>

        <Card title="Operaciones">
          <View style={styles.grid}>
            <PrimaryButton title="Dashboard" onPress={() => navigation.navigate("Dashboard")} />
            <PrimaryButton title="Clientes" onPress={() => navigation.navigate("Clients")} />
            <PrimaryButton title="Prestamos" onPress={() => navigation.navigate("Loans")} />
            <PrimaryButton title="Pagos" onPress={() => navigation.navigate("Payments")} />
            <PrimaryButton title="Reportes" onPress={() => navigation.navigate("Reports")} />
            <PrimaryButton title="Avisos" onPress={() => navigation.navigate("Notifications")} />
          </View>
        </Card>

        <Card title="Control desde el movil">
          <Text style={styles.copy}>
            Consulta tu cartera, revisa atrasos y moras, registra pagos y atiende los avisos del día sin depender de una computadora.
          </Text>
        </Card>
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    marginBottom: spacing.md
  },
  kicker: {
    color: colors.primary,
    fontSize: 12,
    fontWeight: "900",
    textTransform: "uppercase"
  },
  title: {
    color: colors.text,
    fontSize: 24,
    fontWeight: "900"
  },
  grid: {
    gap: spacing.sm
  },
  copy: {
    color: colors.muted,
    lineHeight: 21
  }
});
