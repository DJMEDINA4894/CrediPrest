import { api } from "../api/client";

const SERVICE_WORKER_PATH = "/push-service-worker.js";

export type WebPushStatus = "unsupported" | "blocked" | "disabled" | "enabled";

export function supportsWebPush() {
  return window.isSecureContext
    && "serviceWorker" in navigator
    && "PushManager" in window
    && "Notification" in window;
}

export async function getWebPushStatus(): Promise<WebPushStatus> {
  if (!supportsWebPush()) {
    return "unsupported";
  }

  if (Notification.permission === "denied") {
    return "blocked";
  }

  const registration = await navigator.serviceWorker.getRegistration();
  const subscription = await registration?.pushManager.getSubscription();
  return subscription ? "enabled" : "disabled";
}

export async function enableWebPush() {
  if (!supportsWebPush()) {
    throw new Error("Este navegador no admite notificaciones Web Push o la página no usa HTTPS.");
  }

  const { publicKey } = await api.webPushPublicKey();
  const permission = Notification.permission === "granted"
    ? Notification.permission
    : await Notification.requestPermission();
  if (permission !== "granted") {
    throw new Error(permission === "denied"
      ? "Las notificaciones están bloqueadas en este navegador. Habilítalas desde la configuración del sitio."
      : "No se concedió permiso para mostrar notificaciones.");
  }

  await navigator.serviceWorker.register(SERVICE_WORKER_PATH, { scope: "/" });
  const registration = await navigator.serviceWorker.ready;
  const existingSubscription = await registration.pushManager.getSubscription();
  const subscription = existingSubscription ?? await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToUint8Array(publicKey)
  });
  const serialized = subscription.toJSON();
  if (!serialized.endpoint || !serialized.keys?.p256dh || !serialized.keys.auth) {
    throw new Error("El navegador no devolvió una suscripción Web Push completa.");
  }

  await api.registerWebPushDevice({
    endpoint: serialized.endpoint,
    keys: {
      p256dh: serialized.keys.p256dh,
      auth: serialized.keys.auth
    },
    userAgent: navigator.userAgent
  });
}

export async function disableWebPush() {
  if (!supportsWebPush()) {
    return;
  }

  const registration = await navigator.serviceWorker.getRegistration();
  const subscription = await registration?.pushManager.getSubscription();
  if (!subscription) {
    return;
  }

  await api.unregisterWebPushDevice(subscription.endpoint);
  await subscription.unsubscribe();
}

function urlBase64ToUint8Array(value: string) {
  const padding = "=".repeat((4 - value.length % 4) % 4);
  const base64 = (value + padding).replace(/-/g, "+").replace(/_/g, "/");
  const raw = window.atob(base64);
  return Uint8Array.from(raw, (character) => character.charCodeAt(0));
}
