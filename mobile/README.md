# CrediPrest Mobile

App Android/iOS con Expo que reutiliza la API de CrediPrest.

## Ejecutar

```powershell
cd C:\Users\denis\source\repos\DJMEDINA4894\CrediPrest\mobile
npm install
npm start
```

Luego abre Expo Go en Android y escanea el QR.

## API

Por defecto usa la API publicada:

```text
https://creadiprest-c6a3e6dya2cbhtf9.centralus-01.azurewebsites.net/api
```

Para usar otra API:

```powershell
$env:EXPO_PUBLIC_API_URL="https://tu-api.azurewebsites.net/api"
npm start
```

En emulador Android con API local:

```powershell
$env:EXPO_PUBLIC_API_URL="http://10.0.2.2:5052/api"
npm start
```

En telefono fisico con API local, usa la IP de tu PC:

```powershell
$env:EXPO_PUBLIC_API_URL="http://192.168.1.10:5052/api"
npm start
```

## Validar

```powershell
npm run typecheck
```
