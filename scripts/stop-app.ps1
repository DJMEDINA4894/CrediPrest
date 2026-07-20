$processes = Get-CimInstance Win32_Process |
    Where-Object {
        $_.CommandLine -like "*CrediPrest.Api*" -or
        $_.CommandLine -like "*crediprest-web*" -or
        $_.CommandLine -like "*vite*--host 127.0.0.1*"
    }

foreach ($process in $processes) {
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
}
