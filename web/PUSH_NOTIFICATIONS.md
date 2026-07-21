# Notificaciones push web

La aplicacion web ya puede registrar el navegador, enviar avisos en segundo plano y abrir el prestamo o la pantalla de notificaciones relacionada cuando el usuario toca el aviso.

## 1. Generar las claves VAPID

Genera este par de claves una sola vez desde una terminal:

```powershell
npx web-push generate-vapid-keys --json
```

El resultado contiene `publicKey` y `privateKey`. Guarda la clave privada como secreto. No la agregues a GitHub, al frontend ni a `staticwebapp.config.json`.

No cambies estas claves despues de tener navegadores registrados. Al rotarlas, las suscripciones existentes dejan de funcionar y cada usuario debe activar las notificaciones nuevamente.

## 2. Configurar Azure App Service

En el App Service de la API abre `Configuracion > Variables de entorno > Configuracion de la aplicacion` y agrega:

```text
WebPush__PublicKey=<publicKey generada>
WebPush__PrivateKey=<privateKey generada>
WebPush__Subject=mailto:denisjmedinac4894@gmail.com
```

Guarda los cambios y reinicia el App Service. Las dos claves deben pertenecer al mismo par VAPID.

## 3. Publicar API y frontend

1. Publica `src/CrediPrest.Api` en Azure App Service.
2. Al iniciar, Entity Framework aplica la migracion `AddWebPushNotifications` y crea las tablas de suscripciones y entregas.
3. Publica `web` en Azure Static Web Apps.
4. Comprueba que `https://<sitio>/push-service-worker.js` responda con JavaScript y no con `index.html`.

## 4. Activar un navegador

1. Abre la aplicacion mediante HTTPS e inicia sesion.
2. Entra en `Notificaciones`.
3. Pulsa `Activar notificaciones` y acepta el permiso del navegador.

La suscripcion queda asociada al usuario o cliente autenticado. Solo los avisos creados o actualizados despues del registro se envian como push, para evitar una avalancha de avisos antiguos.

Si el permiso fue bloqueado, debe habilitarse desde la configuracion del sitio en el navegador. En iPhone y iPad, Web Push requiere instalar el sitio en la pantalla de inicio y usar una version compatible de iOS.

## Seguridad

- La pantalla bloqueada muestra un texto financiero generico; el detalle se consulta despues de abrir CrediPrest e iniciar sesion.
- Los endpoints y claves de suscripcion se almacenan en SQL Server, vinculados al destinatario autenticado.
- Una suscripcion eliminada o expirada se desactiva automaticamente cuando el proveedor responde `404` o `410`.
- La clave VAPID publica puede exponerse; la privada debe permanecer solamente en la API.
