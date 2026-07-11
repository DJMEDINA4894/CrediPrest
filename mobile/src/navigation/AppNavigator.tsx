import { NavigationContainer } from "@react-navigation/native";
import { createNativeStackNavigator } from "@react-navigation/native-stack";
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from "react-native";
import { useAuth } from "../context/AuthContext";
import { ClientPortalScreen } from "../screens/ClientPortalScreen";
import { ClientFormScreen } from "../screens/ClientFormScreen";
import { ClientsScreen } from "../screens/ClientsScreen";
import { DashboardScreen } from "../screens/DashboardScreen";
import { HomeScreen } from "../screens/HomeScreen";
import { LoanDetailScreen } from "../screens/LoanDetailScreen";
import { LoanFormScreen } from "../screens/LoanFormScreen";
import { LoansScreen } from "../screens/LoansScreen";
import { LoginScreen } from "../screens/LoginScreen";
import { NotificationsScreen } from "../screens/NotificationsScreen";
import { PaymentsScreen } from "../screens/PaymentsScreen";
import { ReportsScreen } from "../screens/ReportsScreen";
import { colors } from "../theme/theme";
import type { RootStackParamList } from "./types";

const Stack = createNativeStackNavigator<RootStackParamList>();

export function AppNavigator() {
  const { user, isReady, signOut } = useAuth();

  if (!isReady) {
    return (
      <View style={{ alignItems: "center", backgroundColor: colors.background, flex: 1, justifyContent: "center" }}>
        <ActivityIndicator color={colors.primary} size="large" />
      </View>
    );
  }

  return (
    <NavigationContainer>
      <Stack.Navigator
        screenOptions={{
          contentStyle: { backgroundColor: colors.background },
          headerStyle: { backgroundColor: colors.primaryDark },
          headerTintColor: "#fff",
          headerTitleStyle: { fontWeight: "900" },
          headerRight: user ? () => (
            <Pressable accessibilityLabel="Cerrar sesion" hitSlop={10} onPress={() => void signOut()} style={styles.signOut}>
              <Text style={styles.signOutText}>Salir</Text>
            </Pressable>
          ) : undefined
        }}
      >
        {!user ? (
          <Stack.Screen name="Login" component={LoginScreen} options={{ headerShown: false }} />
        ) : user.role === "Client" ? (
          <>
            <Stack.Screen name="ClientPortal" component={ClientPortalScreen} options={{ title: "Mi plan de pago", headerBackVisible: false }} />
            <Stack.Screen name="Notifications" component={NotificationsScreen} options={{ title: "Avisos" }} />
          </>
        ) : (
          <>
            <Stack.Screen name="Home" component={HomeScreen} options={{ title: "CrediPrest", headerBackVisible: false }} />
            <Stack.Screen name="Dashboard" component={DashboardScreen} options={{ title: "Dashboard" }} />
            <Stack.Screen name="Reports" component={ReportsScreen} options={{ title: "Reportes" }} />
            <Stack.Screen name="Notifications" component={NotificationsScreen} options={{ title: "Avisos" }} />
            <Stack.Screen name="Clients" component={ClientsScreen} options={{ title: "Clientes" }} />
            <Stack.Screen name="ClientForm" component={ClientFormScreen} options={({ route }) => ({ title: route.params?.client ? "Editar cliente" : "Nuevo cliente" })} />
            <Stack.Screen name="Loans" component={LoansScreen} options={{ title: "Prestamos" }} />
            <Stack.Screen name="LoanForm" component={LoanFormScreen} options={({ route }) => ({ title: route.params?.loan ? "Editar prestamo" : "Nuevo prestamo" })} />
            <Stack.Screen name="LoanDetail" component={LoanDetailScreen} options={{ title: "Detalle prestamo" }} />
            <Stack.Screen name="Payments" component={PaymentsScreen} options={{ title: "Pagos" }} />
          </>
        )}
      </Stack.Navigator>
    </NavigationContainer>
  );
}

const styles = StyleSheet.create({
  signOut: {
    borderColor: "#9ccfc7",
    borderRadius: 8,
    borderWidth: 1,
    paddingHorizontal: 10,
    paddingVertical: 6
  },
  signOutText: {
    color: "#fff",
    fontSize: 12,
    fontWeight: "900"
  }
});
