import { useCallback, useState } from "react";
import { Pressable, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, SecondaryButton, Screen, Text } from "../components/ui";
import { useAuth } from "../context/AuthContext";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import type { Notification } from "../types/models";
import { dateOnly } from "../utils/format";

type Props = NativeStackScreenProps<RootStackParamList, "Notifications">;

export function NotificationsScreen({ navigation }: Props) {
  const { user } = useAuth();
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

  async function markAsRead(notification: Notification) {
    if (notification.isRead) return;

    setNotifications((current) => current.map((item) => item.id === notification.id ? { ...item, isRead: true } : item));
    try {
      await api.markNotificationRead(notification.id);
    } catch (err) {
      setNotifications((current) => current.map((item) => item.id === notification.id ? { ...item, isRead: false } : item));
      setError(err instanceof Error ? err.message : "No se pudo actualizar el aviso.");
    }
  }

  async function openRelatedLoan(notification: Notification) {
    await markAsRead(notification);
    if (!notification.relatedLoanId) return;

    if (user?.role === "Client") {
      navigation.navigate("ClientPortal");
    } else {
      navigation.navigate("Payments", { loanId: notification.relatedLoanId });
    }
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.contentContainer} refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}>
        <ErrorText text={error} />
        <View style={styles.header}>
          <Text style={styles.heading}>Avisos</Text>
          <Text style={styles.count}>{notifications.length}</Text>
        </View>
        {notifications.length === 0 ? <EmptyState text="No tienes avisos pendientes." /> : null}
        {notifications.map((notification) => (
          <Card key={notification.id} tone={!notification.isRead ? "new" : undefined}>
            <Pressable accessibilityRole="button" onPress={() => void markAsRead(notification)} style={styles.row}>
              <View style={styles.content}>
                <Text style={styles.title}>{notification.title}</Text>
                <Text style={styles.message}>{notification.message}</Text>
                <Text style={styles.date}>Vence: {dateOnly(notification.dueDate ?? notification.createdAtUtc)}</Text>
              </View>
              {!notification.isRead ? <Text style={styles.unread}>Nuevo</Text> : null}
            </Pressable>
            {notification.relatedLoanId ? (
              <SecondaryButton
                title={user?.role === "Client" ? "Ver mi plan" : "Ver en pagos"}
                onPress={() => void openRelatedLoan(notification)}
              />
            ) : null}
          </Card>
        ))}
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  contentContainer: {
    paddingBottom: spacing.xl
  },
  header: {
    alignItems: "center",
    flexDirection: "row",
    gap: spacing.sm,
    marginBottom: spacing.md
  },
  heading: {
    color: colors.text,
    fontSize: 22,
    fontWeight: "900"
  },
  count: {
    backgroundColor: colors.primary,
    borderRadius: 999,
    color: "#fff",
    fontSize: 12,
    fontWeight: "900",
    minWidth: 26,
    overflow: "hidden",
    paddingHorizontal: 8,
    paddingVertical: 4,
    textAlign: "center"
  },
  row: {
    alignItems: "flex-start",
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
    alignSelf: "flex-start",
    paddingHorizontal: 8,
    paddingVertical: 3
  }
});
