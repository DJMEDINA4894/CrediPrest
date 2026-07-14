import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useCallback, useState } from "react";
import { Alert, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, DangerButton, EmptyState, ErrorText, GhostButton, PrimaryButton, Screen, Text } from "../components/ui";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { AppUser } from "../types/models";

type Props = NativeStackScreenProps<RootStackParamList, "Users">;

export function UsersScreen({ navigation }: Props) {
  const [users, setUsers] = useState<AppUser[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      setError("");
      setLoading(true);
      setUsers((await api.users()).filter((user) => user.role === 2));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudieron cargar los prestamistas.");
    } finally {
      setLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => {
    void load();
  }, [load]));

  async function setActive(user: AppUser, isActive: boolean) {
    try {
      setError("");
      await api.updateUser(user.id, {
        clientId: null,
        email: user.email,
        fullName: user.fullName,
        phone: user.phone ?? null,
        identificationNumber: user.identificationNumber ?? null,
        password: null,
        confirmPassword: null,
        role: 2,
        isActive
      });
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo actualizar el prestamista.");
    }
  }

  function remove(user: AppUser) {
    Alert.alert(
      "Eliminar prestamista",
      `Eliminar a ${user.fullName} quitará su acceso. Si tiene información asociada, se conservará el historial.`,
      [
        { text: "Cancelar", style: "cancel" },
        {
          text: "Eliminar",
          style: "destructive",
          onPress: () => void (async () => {
            try {
              setError("");
              await api.deleteUser(user.id);
              await load();
            } catch (err) {
              setError(err instanceof Error ? err.message : "No se pudo eliminar el prestamista.");
            }
          })()
        }
      ]
    );
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content} refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        <View style={styles.createAction}>
          <PrimaryButton title="Nuevo prestamista" onPress={() => navigation.navigate("UserForm")} />
        </View>
        {users.length === 0 ? <EmptyState text="No hay prestamistas registrados." /> : null}
        {users.map((user) => (
          <Card key={user.id}>
            <View style={styles.header}>
              <View style={styles.main}>
                <Text style={styles.name}>{user.fullName}</Text>
                <Text style={styles.alias}>@{user.userName}</Text>
              </View>
              <Text style={[styles.badge, user.isActive ? styles.active : styles.inactive]}>{user.isActive ? "Activo" : "Inactivo"}</Text>
            </View>
            <Text style={styles.detail}>{user.email}</Text>
            <Text style={styles.detail}>{user.phone ?? "Sin teléfono"} | {user.identificationNumber ?? "Sin cédula"}</Text>
            <View style={styles.actions}>
              <GhostButton title="Editar" onPress={() => navigation.navigate("UserForm", { user })} />
              <GhostButton title={user.isActive ? "Desactivar" : "Activar"} onPress={() => void setActive(user, !user.isActive)} />
              <DangerButton title="Eliminar" onPress={() => remove(user)} />
            </View>
          </Card>
        ))}
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingBottom: spacing.xl },
  createAction: { marginBottom: spacing.md },
  header: { alignItems: "flex-start", flexDirection: "row", gap: spacing.sm, justifyContent: "space-between" },
  main: { flex: 1 },
  name: { color: colors.text, fontSize: 16, fontWeight: "900" },
  alias: { color: colors.primary, fontWeight: "800", marginTop: 3 },
  detail: { color: colors.muted, marginTop: spacing.xs },
  badge: { borderRadius: 999, fontSize: 11, fontWeight: "900", paddingHorizontal: 9, paddingVertical: 4 },
  active: { backgroundColor: "#dff5ea", color: colors.good },
  inactive: { backgroundColor: "#ffe8ea", color: colors.danger },
  actions: { flexDirection: "row", flexWrap: "wrap", gap: spacing.sm, marginTop: spacing.md }
});
