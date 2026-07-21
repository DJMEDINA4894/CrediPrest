import Constants from "expo-constants";
import * as Device from "expo-device";
import * as Notifications from "expo-notifications";
import * as SecureStore from "expo-secure-store";
import { Platform } from "react-native";
import { api } from "../api/client";

const PUSH_TOKEN_KEY = "crediprest.mobile.expoPushToken";
const NOTIFICATION_CHANNEL_ID = "crediprest-alerts";

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldPlaySound: true,
    shouldSetBadge: true,
    shouldShowBanner: true,
    shouldShowList: true
  })
});

export async function registerCurrentDeviceForPushNotifications() {
  if (!Device.isDevice || Platform.OS === "web") {
    return null;
  }

  if (Platform.OS === "android") {
    await Notifications.setNotificationChannelAsync(NOTIFICATION_CHANNEL_ID, {
      name: "Avisos de pagos",
      description: "Vencimientos, atrasos y moras de CrediPrest",
      importance: Notifications.AndroidImportance.MAX,
      lightColor: "#157064",
      vibrationPattern: [0, 250, 250, 250]
    });
  }

  const currentPermission = await Notifications.getPermissionsAsync();
  const permission = currentPermission.status === "granted"
    ? currentPermission
    : await Notifications.requestPermissionsAsync();
  if (permission.status !== "granted") {
    return null;
  }

  const projectId = Constants.expoConfig?.extra?.eas?.projectId ?? Constants.easConfig?.projectId;
  if (!projectId) {
    throw new Error("No se encontró el projectId de Expo para registrar notificaciones.");
  }

  const expoPushToken = (await Notifications.getExpoPushTokenAsync({ projectId })).data;
  await api.registerPushDevice({
    expoPushToken,
    platform: Platform.OS,
    deviceName: Device.deviceName ?? Device.modelName ?? undefined
  });
  await SecureStore.setItemAsync(PUSH_TOKEN_KEY, expoPushToken);
  return expoPushToken;
}

export async function unregisterCurrentDeviceFromPushNotifications() {
  const expoPushToken = await SecureStore.getItemAsync(PUSH_TOKEN_KEY);
  if (!expoPushToken) {
    return;
  }

  await api.unregisterPushDevice(expoPushToken);
  await SecureStore.deleteItemAsync(PUSH_TOKEN_KEY);
}

export function addPushNotificationResponseListener(
  listener: (response: Notifications.NotificationResponse) => void
) {
  return Notifications.addNotificationResponseReceivedListener(listener);
}

export async function getLastPushNotificationResponse() {
  return Notifications.getLastNotificationResponseAsync();
}

export async function clearLastPushNotificationResponse() {
  await Notifications.clearLastNotificationResponseAsync();
}
