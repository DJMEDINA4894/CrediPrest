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

## Funcionalidad inicial

- Login privado con JWT.
- CRUD inicial de clientes con búsqueda.
- Crear préstamos por cliente.
- Generación automática de cuotas semanales, quincenales o mensuales.
- Registro de pagos completos, parciales y adelantados.
- Actualización de estado de cuotas y préstamos vencidos/cancelados.
- Dashboard financiero básico.
- Reporte básico de cartera y préstamos.
