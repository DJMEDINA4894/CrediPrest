# Notificaciones push de Android

La aplicacion ya registra el `ExpoPushToken` del telefono en la API y abre el prestamo relacionado al tocar una notificacion. Para habilitar la entrega real en Android falta asociar el proyecto EAS con Firebase Cloud Messaging.

## 1. Crear la aplicacion Android en Firebase

1. Entra en https://console.firebase.google.com/ y crea o abre el proyecto de CrediPrest.
2. Agrega una aplicacion Android con el identificador `com.crediprest.app`.
3. Descarga `google-services.json` y guardalo como `mobile/google-services.json`.
4. Agrega dentro de `expo.android` en `mobile/app.json`:

```json
"googleServicesFile": "./google-services.json"
```

El archivo `google-services.json` contiene identificadores publicos de Firebase y puede incluirse en Git. No debe confundirse con la clave privada de la cuenta de servicio.

## 2. Configurar FCM V1 en EAS

1. En Firebase abre `Configuracion del proyecto > Cuentas de servicio`.
2. Genera una nueva clave privada y guarda temporalmente el JSON fuera del repositorio.
3. Desde la carpeta `mobile`, ejecuta:

```powershell
eas credentials
```

4. Selecciona `Android > production > Google Service Account`.
5. Selecciona `Manage your Google Service Account Key for Push Notifications (FCM V1)`.
6. Sube la clave privada generada por Firebase.
7. No agregues esa clave privada a Git. Los patrones habituales ya estan excluidos en `.gitignore`.

Tambien se puede subir desde el panel del proyecto en Expo, en `Credentials > Android > Service Credentials > FCM V1 service account key`.

## 3. Publicar API y base de datos

Publica `src/CrediPrest.Api` en Azure y reinicia el App Service. Al iniciar, Entity Framework aplica automaticamente la migracion `AddExpoPushNotifications`, que crea las tablas de dispositivos y entregas.

## 4. Generar e instalar un APK nuevo

El plugin `expo-notifications` contiene cambios nativos, por lo que esta primera instalacion no puede enviarse solamente mediante `eas update`.

```powershell
cd mobile
eas build --platform android --profile preview
```

Instala el APK nuevo, inicia sesion y acepta el permiso de notificaciones. Desde ese momento los avisos nuevos o actualizados se enviaran al telefono. Las actualizaciones posteriores que solo cambien JavaScript podran publicarse con `eas update`.

Documentacion oficial: https://docs.expo.dev/push-notifications/push-notifications-setup/ y https://docs.expo.dev/push-notifications/fcm-credentials/
