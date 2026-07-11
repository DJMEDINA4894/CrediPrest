import { useCallback, useState } from "react";
import { RefreshControl, ScrollView, StyleSheet, Text, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, SecondaryButton, Screen } from "../components/ui";
import { colors, spacing } from "../theme/theme";
import type { Notification } from "../types/models";
import { dateOnly } from "../utils/format";

export function NotificationsScreen() {
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      setError("");
      setLoading(true);
      setNotifications(await api.notifications());
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudieron cargar los avisos.");
    } finally {
      setLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => {
    void load();
  }, [load]));

  async function markAsRead(id: string) {
    try {
      await api.markNotificationRead(id);
      setNotifications((items) => items.map((item) => item.id === id ? { ...item, isRead: true } : item));
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo actualizar el aviso.");
    }
  }

  return (
    <Screen>
      <ScrollView refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        {notifications.length === 0 ? <EmptyState text="No tienes avisos pendientes." /> : null}
        {notifications.map((notification) => (
          <Card key={notification.id}>
            <View style={styles.row}>
              <View style={styles.content}>
                <Text style={styles.title}>{notification.title}</Text>
                <Text style={styles.message}>{notification.message}</Text>
                <Text style={styles.date}>{dateOnly(notification.createdAtUtc)}</Text>
              </View>
              {!notification.isRead ? <Text style={styles.unread}>Nuevo</Text> : null}
            </View>
            {!notification.isRead ? <SecondaryButton title="Marcar como leido" onPress={() => void markAsRead(notification.id)} /> : null}
          </Card>
        ))}
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    gap: spacing.sm,
    marginBottom: spacing.sm
  },
  content: {
    flex: 1
  },
  title: {
    color: colors.text,
    fontSize: 16,
    fontWeight: "900"
  },
  message: {
    color: colors.muted,
    lineHeight: 20,
    marginTop: 4
  },
  date: {
    color: colors.muted,
    fontSize: 12,
    marginTop: spacing.sm
  },
  unread: {
    backgroundColor: "#fff0d7",
    borderRadius: 999,
    color: colors.warn,
    fontSize: 11,
    fontWeight: "900",
    paddingHorizontal: 8,
    paddingVertical: 4
  }
});
