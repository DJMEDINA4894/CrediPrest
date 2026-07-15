import { useCallback, useState } from "react";
import { Alert, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, DangerButton, EmptyState, ErrorText, Field, GhostButton, PrimaryButton, Screen, Text } from "../components/ui";
import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { Client } from "../types/models";
import { money } from "../utils/format";

type Props = NativeStackScreenProps<RootStackParamList, "Clients">;

export function ClientsScreen({ navigation }: Props) {
  const [clients, setClients] = useState<Client[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async (term = search) => {
    try {
      setError("");
      setLoading(true);
      setClients(await api.clients(term));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudieron cargar los clientes.");
    } finally {
      setLoading(false);
    }
  }, [search]);

  useFocusEffect(useCallback(() => {
    void load("");
  }, [load]));

  async function setActive(client: Client, isActive: boolean) {
    try {
      setError("");
      if (isActive) {
        await api.activateClient(client.id);
      } else {
        await api.deactivateClient(client.id);
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo actualizar el cliente.");
    }
  }

  function deleteClient(client: Client) {
    Alert.alert(
      "Eliminar cliente",
      `Eliminar a ${client.fullName} borrara sus prestamos, cuotas y pagos. Esta accion no se puede deshacer.`,
      [
        { text: "Cancelar", style: "cancel" },
        {
          text: "Eliminar",
          style: "destructive",
          onPress: () => void (async () => {
            try {
              setError("");
              await api.deleteClient(client.id);
              await load();
            } catch (err) {
              setError(err instanceof Error ? err.message : "No se pudo eliminar el cliente.");
            }
          })()
        }
      ]
    );
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content} refreshControl={<RefreshControl refreshing={loading} onRefresh={() => load()} />}>
        <ErrorText text={error} />
        <Field
          label="Buscar cliente"
          placeholder="Nombre, cedula o telefono"
          value={search}
          onChangeText={(value) => {
            setSearch(value);
            load(value);
          }}
        />
        <View style={styles.createAction}>
          <PrimaryButton title="Nuevo cliente" onPress={() => navigation.navigate("ClientForm")} />
        </View>
        {clients.length === 0 ? <EmptyState text="No hay clientes para mostrar." /> : null}
        {clients.map((client) => (
          <Card key={client.id}>
            <View style={styles.row}>
              <View style={styles.main}>
                <Text style={styles.name}>{client.fullName}</Text>
                <Text style={styles.muted}>{client.identificationNumber} | {client.phone}</Text>
                <Text style={styles.muted}>{client.address}</Text>
              </View>
              <Text style={[styles.badge, client.isActive ? styles.active : styles.inactive]}>
                {client.isActive ? "Activo" : "Inactivo"}
              </Text>
            </View>
            <Text style={styles.debt}>
              Debe: {client.pendingCordobas > 0 && client.pendingUsd > 0
                ? `${money(client.pendingCordobas)} / ${money(client.pendingUsd, "USD")}`
                : client.pendingUsd > 0
                  ? money(client.pendingUsd, "USD")
                  : money(client.pendingCordobas)}
            </Text>
            <View style={styles.actions}>
              <GhostButton title="Editar" onPress={() => navigation.navigate("ClientForm", { client })} />
              <GhostButton title={client.isActive ? "Desactivar" : "Activar"} onPress={() => void setActive(client, !client.isActive)} />
              <DangerButton title="Eliminar" onPress={() => deleteClient(client)} />
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
  active: {
    backgroundColor: "#dff5ea",
    color: colors.good
  },
  inactive: {
    backgroundColor: "#e8edf1",
    color: colors.muted
  },
  debt: {
    color: colors.warn,
    fontWeight: "900",
    marginTop: spacing.sm
  },
  actions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.sm,
    marginTop: spacing.sm
  },
  createAction: {
    marginBottom: spacing.md
  }
});
