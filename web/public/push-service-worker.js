self.addEventListener("push", (event) => {
  let payload = {};
  try {
    payload = event.data ? event.data.json() : {};
  } catch {
    payload = {};
  }

  const title = payload.title || "CrediPrest";
  event.waitUntil(self.registration.showNotification(title, {
    body: payload.body || "Tienes una notificación pendiente.",
    icon: payload.icon || "/crediprest-icon.png",
    badge: payload.badge || "/crediprest-icon.png",
    tag: payload.tag || "crediprest-notification",
    renotify: true,
    data: payload.data || { url: "/?pushNotifications=1" }
  }));
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const targetUrl = new URL(event.notification.data?.url || "/?pushNotifications=1", self.location.origin).href;

  event.waitUntil((async () => {
    const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
    const existingWindow = windows.find((client) => new URL(client.url).origin === self.location.origin);
    if (existingWindow) {
      await existingWindow.navigate(targetUrl);
      return existingWindow.focus();
    }

    return self.clients.openWindow(targetUrl);
  })());
});
