# CrediPrestApp

MVP privado para administrar clientes, préstamos personales, cuotas, pagos, saldos, intereses y atrasos en córdobas y dólares.

## Stack

- Backend: ASP.NET Core Web API `net8.0`
- Frontend: React + TypeScript + Vite
- Base de datos: SQL Server LocalDB / SQL Server
- ORM: Entity Framework Core
- Autenticación: JWT

## Estructura

```text
src/
  CrediPrest.Domain/          Entidades y enums del dominio
  CrediPrest.Application/     DTOs, contratos y servicios de negocio
  CrediPrest.Infrastructure/  EF Core, SQL Server, JWT, hashing y migraciones
  CrediPrest.Api/             Controladores HTTP, Swagger y configuración
web/                          Frontend React
```

## Credenciales iniciales

La app no incluye usuario ni contraseña por defecto. Si necesitas crear un administrador automaticamente en una base vacia, configura estos valores como user-secrets o variables de entorno privadas:

- `AdminSeed__UserName`
- `AdminSeed__Email`
- `AdminSeed__FullName`
- `AdminSeed__Password`

No publiques esas credenciales en el repositorio. Cambia tambien el `Jwt__SecretKey` antes de usar la app fuera de desarrollo.

## Base de datos

La conexión por defecto apunta a:

```text
Server=(localdb)\MSSQLLocalDB;Database=CrediPrestAppDB;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True
```

Migraciones incluidas:

- `InitialCreate`: tablas principales (`Users`, `Clients`, `Loans`, `Installments`, `Payments`, `LoanStatus`, `PaymentMethods`, `__EFMigrationsHistory`).
- `AddReportingDatabaseObjects`: crea `vw_LoanPortfolioSummary`, `fn_LoanPendingBalance` y `sp_GetOverdueInstallments`.

Aplicar migraciones:

```powershell
sqllocaldb start MSSQLLocalDB
dotnet tool restore
dotnet tool run dotnet-ef database update --project src\CrediPrest.Infrastructure --startup-project src\CrediPrest.Api
```

## Ejecutar backend

```powershell
dotnet run --project src\CrediPrest.Api
```

- API: `http://localhost:5052`
- Swagger: `http://localhost:5052/swagger`
- Health: `http://localhost:5052/api/health`

## Ejecutar frontend

Si tienes Node/npm instalados normalmente:

```powershell
cd web
pnpm install
pnpm run dev
```

Frontend: `http://127.0.0.1:5173`

## Notificaciones push

- Android con Expo/FCM: `mobile/PUSH_NOTIFICATIONS.md`
- Navegadores con Web Push/VAPID: `web/PUSH_NOTIFICATIONS.md`
- Correo para web y movil con Azure Communication Services: `EMAIL_NOTIFICATIONS.md`

## Tipo de cambio USD/C$

La API consulta las tasas de compra y venta publicadas por BAC Credomatic Nicaragua, las guarda por fecha y ejecuta una actualización automática a las `05:00` hora de Nicaragua. Si el servicio no está disponible, utiliza la última tasa guardada; cuando todavía no existe historial usa las tasas de respaldo configuradas.

Configuración opcional del App Service:

```text
ExchangeRates__LocalRunTime=05:00
ExchangeRates__TimeZoneId=America/Managua
ExchangeRates__FallbackBuyCordobasPerUsd=36.30
ExchangeRates__FallbackSellCordobasPerUsd=37.14
```

La API aplica las migraciones al iniciar. Después de publicar, revisa el Log Stream de Azure para confirmar que arrancó sin errores. Cuando se entregan dólares se aplica la compra de BAC; cuando se entregan córdobas para cubrir una obligación en dólares se aplica la venta. Los pagos guardan el monto y moneda recibidos, la tasa C$/USD utilizada y el monto aplicado en la moneda contractual del préstamo.

En planes de Azure que suspenden la aplicación, el trabajo de las `05:00` puede ejecutarse al reactivarse el servicio. Esto no deja al sistema sin tasa: el primer acceso del día intenta obtenerla nuevamente antes de convertir un monto.

## Funcionalidad inicial

- Login privado con JWT.
- CRUD inicial de clientes con búsqueda.
- Crear préstamos por cliente.
- Generación automática de cuotas semanales, quincenales o mensuales.
- Registro de pagos completos, parciales y adelantados.
- Actualización de estado de cuotas y préstamos vencidos/cancelados.
- Dashboard financiero básico.
- Reporte básico de cartera y préstamos.
