# Notificaciones por correo con Azure Communication Services

CrediPrest envia por correo los avisos internos nuevos o actualizados de vencimientos y moras. El canal funciona para usuarios de la web y de la aplicacion movil porque el envio se realiza desde la API.

## 1. Crear los recursos en Azure

1. En Azure Portal crea un recurso `Email Communication Services`.
2. En `Provision domains` crea un dominio administrado por Azure para las primeras pruebas.
3. Crea o abre un recurso `Communication Services`.
4. En `Email > Domains`, conecta el dominio de correo que acabas de provisionar.
5. En el dominio copia el valor `MailFrom`, por ejemplo `DoNotReply@xxxxxxxx.azurecomm.net`.
6. En `Communication Services > Keys`, copia la cadena de conexion primaria.

Para produccion es recomendable verificar un dominio propio. La clave o cadena de conexion nunca debe agregarse a GitHub.

## 2. Configurar el App Service de la API

En `Configuracion > Variables de entorno > Configuracion de la aplicacion` agrega:

```text
Email__Enabled=true
Email__ConnectionString=endpoint=https://...;accesskey=...
Email__SenderAddress=DoNotReply@xxxxxxxx.azurecomm.net
Email__SenderName=CrediPrest
Email__ApplicationUrl=https://orange-mud-02c1b600f.7.azurestaticapps.net
```

`Email__SenderAddress` debe ser exactamente una direccion `MailFrom` del dominio conectado. Guarda los cambios y reinicia el App Service.

## 3. Publicar la API

Publica `src/CrediPrest.Api` en Azure. Al iniciar, Entity Framework aplica la migracion `AddAzureEmailNotifications`, que crea:

- `EmailDispatchState`: registra desde que momento se activo el canal.
- `EmailNotificationDeliveries`: guarda destinatario, version, intentos, resultado e identificador de Azure.

La primera ejecucion configurada activa el canal sin enviar avisos historicos. A partir de ese momento el proceso revisa avisos cada minuto. Un fallo temporal se reintenta hasta cinco veces.

## Comportamiento

- Los prestamistas reciben el aviso en el correo de su usuario activo.
- Los clientes activos lo reciben solamente cuando tienen un correo valido registrado.
- Cada combinacion de notificacion, version y correo se envia una sola vez.
- Si cambia el contenido de una mora o aviso, la nueva version puede generar un correo actualizado.
- El boton `Ver en CrediPrest` abre el prestamo relacionado o la pantalla de notificaciones y exige autenticacion.
- El correo no incluye cedula, cuentas bancarias ni credenciales.

## Desarrollo local

El canal esta desactivado en `appsettings.Development.json`. Para probarlo usa user-secrets:

```powershell
dotnet user-secrets set "Email:Enabled" "true" --project src\CrediPrest.Api
dotnet user-secrets set "Email:ConnectionString" "endpoint=https://...;accesskey=..." --project src\CrediPrest.Api
dotnet user-secrets set "Email:SenderAddress" "DoNotReply@xxxxxxxx.azurecomm.net" --project src\CrediPrest.Api
```

No guardes la cadena de conexion en `appsettings.json`.
