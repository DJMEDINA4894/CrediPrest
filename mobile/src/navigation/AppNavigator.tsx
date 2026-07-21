import { NavigationContainer, useNavigationContainerRef } from "@react-navigation/native";
import { createNativeStackNavigator } from "@react-navigation/native-stack";
import { Ionicons } from "@expo/vector-icons";
import { useCallback, useEffect, useRef } from "react";
import { ActivityIndicator, Pressable, StyleSheet, View } from "react-native";
import { useAuth } from "../context/AuthContext";
import { ClientPortalScreen } from "../screens/ClientPortalScreen";
import { ClientFormScreen } from "../screens/ClientFormScreen";
import { ClientsScreen } from "../screens/ClientsScreen";
import { DashboardScreen } from "../screens/DashboardScreen";
import { HomeScreen } from "../screens/HomeScreen";
import { LoanDetailScreen } from "../screens/LoanDetailScreen";
import { LoanFormScreen } from "../screens/LoanFormScreen";
import { LoanRecalculationScreen } from "../screens/LoanRecalculationScreen";
import { LoansScreen } from "../screens/LoansScreen";
import { LoginScreen } from "../screens/LoginScreen";
import { NotificationsScreen } from "../screens/NotificationsScreen";
import { PaymentsScreen } from "../screens/PaymentsScreen";
import { ReportsScreen } from "../screens/ReportsScreen";
import { SettingsScreen } from "../screens/SettingsScreen";
import { UserFormScreen } from "../screens/UserFormScreen";
import { UsersScreen } from "../screens/UsersScreen";
import {
  addPushNotificationResponseListener,
  clearLastPushNotificationResponse,
  getLastPushNotificationResponse,
  registerCurrentDeviceForPushNotifications
} from "../services/pushNotifications";
import { colors } from "../theme/theme";
import type { RootStackParamList } from "./types";

const Stack = createNativeStackNavigator<RootStackParamList>();

export function AppNavigator() {
  const { user, isReady, signOut } = useAuth();
  const navigationRef = useNavigationContainerRef<RootStackParamList>();
  const pendingPushResponse = useRef<Awaited<ReturnType<typeof getLastPushNotificationResponse>>>(null);

  const openPushNotification = useCallback((response: NonNullable<typeof pendingPushResponse.current>) => {
    if (!user) {
      pendingPushResponse.current = response;
      return;
    }

    if (!navigationRef.isReady()) {
      pendingPushResponse.current = response;
      return;
    }

    const relatedLoanId = response.notification.request.content.data.relatedLoanId;
    if (user.role === "Client") {
      navigationRef.navigate("ClientPortal");
    } else if (typeof relatedLoanId === "string" && relatedLoanId) {
      navigationRef.navigate("Payments", { loanId: relatedLoanId });
    } else {
      navigationRef.navigate("Notifications");
    }

    pendingPushResponse.current = null;
    void clearLastPushNotificationResponse();
  }, [navigationRef, user]);

  useEffect(() => {
    if (!user) {
      return;
    }

    void registerCurrentDeviceForPushNotifications().catch((error) => {
      console.warn("No se pudo registrar el dispositivo para notificaciones push.", error);
    });

    const subscription = addPushNotificationResponseListener(openPushNotification);
    void getLastPushNotificationResponse().then((response) => {
      if (response) {
        openPushNotification(response);
      }
    });

    return () => subscription.remove();
  }, [openPushNotification, user]);

  if (!isReady) {
    return (
      <View style={{ alignItems: "center", backgroundColor: colors.background, flex: 1, justifyContent: "center" }}>
        <ActivityIndicator color={colors.primary} size="large" />
      </View>
    );
  }

  return (
    <NavigationContainer
      ref={navigationRef}
      onReady={() => {
        if (pendingPushResponse.current) {
          openPushNotification(pendingPushResponse.current);
        }
      }}
    >
      <Stack.Navigator
        screenOptions={{
          contentStyle: { backgroundColor: colors.background },
          headerStyle: { backgroundColor: colors.primaryDark },
          headerTintColor: "#fff",
          headerTitleStyle: { fontWeight: "900" },
          headerRight: user ? () => (
            <Pressable
              accessibilityHint="Cierra tu cuenta y regresa al inicio de sesión"
              accessibilityLabel="Cerrar sesión"
              accessibilityRole="button"
              hitSlop={10}
              onPress={() => void signOut()}
              style={({ pressed }) => [styles.signOut, pressed && styles.signOutPressed]}
            >
              <Ionicons color="#fff" name="log-out-outline" size={23} />
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
            <Stack.Screen name="LoanRecalculation" component={LoanRecalculationScreen} options={{ title: "Abono o liquidación" }} />
            <Stack.Screen name="Payments" component={PaymentsScreen} options={{ title: "Pagos" }} />
            {user.role === "Admin" ? (
              <>
                <Stack.Screen name="Users" component={UsersScreen} options={{ title: "Prestamistas" }} />
                <Stack.Screen name="UserForm" component={UserFormScreen} options={({ route }) => ({ title: route.params?.user ? "Editar prestamista" : "Nuevo prestamista" })} />
                <Stack.Screen name="Settings" component={SettingsScreen} options={{ title: "Configuración" }} />
              </>
            ) : null}
          </>
        )}
      </Stack.Navigator>
    </NavigationContainer>
  );
}

const styles = StyleSheet.create({
  signOut: {
    alignItems: "center",
    borderColor: "#9ccfc7",
    borderRadius: 8,
    borderWidth: 1,
    height: 38,
    justifyContent: "center",
    width: 40
  },
  signOutPressed: {
    opacity: 0.75
  }
});
