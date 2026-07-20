import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useCallback, useState } from "react";
import { Pressable, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, Screen, Text } from "../components/ui";
import { useAuth } from "../context/AuthContext";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";

type Props = NativeStackScreenProps<RootStackParamList, "Home">;

export function HomeScreen({ navigation }: Props) {
  const { user } = useAuth();
  const [unreadNotifications, setUnreadNotifications] = useState(0);
  const isClient = user?.role === "Client";

  useFocusEffect(useCallback(() => {
    api.notifications()
      .then((notifications) => setUnreadNotifications(notifications.filter((notification) => !notification.isRead).length))
      .catch(() => setUnreadNotifications(0));
  }, []));

  if (isClient) {
    navigation.replace("ClientPortal");
    return null;
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.header}>
          <View>
            <Text style={styles.kicker}>{user?.role === "Admin" ? "Administrador" : "Prestamista"}</Text>
            <Text style={styles.title}>Hola, {user?.fullName}</Text>
          </View>
        </View>

        <Card title="Operaciones principales">
          <View style={styles.grid}>
            <MenuItem title="Dashboard" description="Resumen de cartera" onPress={() => navigation.navigate("Dashboard")} />
            <MenuItem title="Clientes" description="Datos y saldos" onPress={() => navigation.navigate("Clients")} />
            <MenuItem title="Préstamos" description="Cuotas y moras" onPress={() => navigation.navigate("Loans")} />
            <MenuItem title="Pagos" description="Registrar cobros" onPress={() => navigation.navigate("Payments")} />
            <MenuItem title="Reportes" description="Cartera pendiente" onPress={() => navigation.navigate("Reports")} />
            <MenuItem title="Avisos" description="Pagos por atender" count={unreadNotifications} onPress={() => navigation.navigate("Notifications")} />
            {user?.role === "Admin" ? (
              <>
                <MenuItem title="Prestamistas" description="Usuarios y acceso" onPress={() => navigation.navigate("Users")} />
                <MenuItem title="Configuración" description="Tamaño de letra" onPress={() => navigation.navigate("Settings")} />
              </>
            ) : null}
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

function MenuItem({
  title,
  description,
  count,
  onPress
}: {
  title: string;
  description: string;
  count?: number;
  onPress: () => void;
}) {
  return (
    <Pressable accessibilityRole="button" onPress={onPress} style={({ pressed }) => [styles.menuItem, pressed && styles.menuItemPressed]}>
      <View style={styles.menuHeader}>
        <Text style={styles.menuTitle}>{title}</Text>
        {count ? <Text style={styles.menuCount}>{Math.min(99, count)}</Text> : null}
      </View>
      <Text style={styles.menuDescription}>{description}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingBottom: spacing.xl
  },
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
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.sm
  },
  menuItem: {
    backgroundColor: colors.soft,
    borderColor: colors.border,
    borderRadius: 8,
    borderWidth: 1,
    flexGrow: 1,
    minHeight: 86,
    minWidth: "47%",
    padding: spacing.md,
    width: "47%"
  },
  menuItemPressed: {
    backgroundColor: "#dcefed"
  },
  menuHeader: {
    alignItems: "center",
    flexDirection: "row",
    gap: spacing.xs,
    justifyContent: "space-between"
  },
  menuTitle: {
    color: colors.primaryDark,
    flex: 1,
    fontWeight: "900"
  },
  menuDescription: {
    color: colors.muted,
    fontSize: 12,
    lineHeight: 17,
    marginTop: spacing.xs
  },
  menuCount: {
    backgroundColor: "#f7b928",
    borderRadius: 999,
    color: colors.text,
    fontSize: 11,
    fontWeight: "900",
    minWidth: 23,
    overflow: "hidden",
    paddingHorizontal: 6,
    paddingVertical: 3,
    textAlign: "center"
  },
  copy: {
    color: colors.muted,
    lineHeight: 21
  }
});
