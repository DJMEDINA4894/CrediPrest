$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

sqllocaldb start MSSQLLocalDB | Out-Host
dotnet run --project src\CrediPrest.Api\CrediPrest.Api.csproj --no-build
