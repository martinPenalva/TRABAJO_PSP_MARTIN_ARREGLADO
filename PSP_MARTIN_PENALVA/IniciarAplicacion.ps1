# Script para gestionar la aplicación de reservas
# Autor: Claude
# Fecha: 2023

# Obtener el directorio actual del script
$scriptDir = $PSScriptRoot
if (-not $scriptDir) {
    $scriptDir = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition
}
if (-not $scriptDir) {
    $scriptDir = Get-Location
}

# Rutas de los componentes de la aplicación
$apiPath = Join-Path -Path $scriptDir -ChildPath "API"
$intermediateServerPath = Join-Path -Path $scriptDir -ChildPath "IntermediateServer"
$clientPath = Join-Path -Path $scriptDir -ChildPath "Clients\Client"

# Puertos utilizados por la aplicación
$apiPort = 5138
$serverPort = 8080

# Crear una variable que almacenará los procesos en segundo plano
$global:appJobs = @()
$global:appProcesses = @()

# Colores para cada componente
$apiColor = "Cyan"
$serverColor = "Green"
$clientColor = "Yellow"

Write-Host "=== SCRIPT DE GESTION DE APLICACION DE RESERVAS ===" -ForegroundColor Magenta
Write-Host "Este script gestionara todos los componentes de la aplicacion." -ForegroundColor Magenta
Write-Host "La API y el servidor intermedio se ejecutaran en esta ventana." -ForegroundColor Magenta
Write-Host "El cliente se abrira en una ventana separada para permitir interaccion." -ForegroundColor Magenta
Write-Host ""
Write-Host "Leyenda de colores:" -ForegroundColor White
Write-Host "- API: " -NoNewline; Write-Host "Este color" -ForegroundColor $apiColor
Write-Host "- Servidor Intermedio: " -NoNewline; Write-Host "Este color" -ForegroundColor $serverColor
Write-Host ""

# Función para mostrar separadores claros
function Show-Separator {
    param (
        [string]$Text,
        [string]$Color = "White"
    )
    
    $width = $Host.UI.RawUI.WindowSize.Width - 1
    if ($width -le 0) { $width = 80 }
    
    $line = "=" * $width
    Write-Host $line -ForegroundColor $Color
    
    if ($Text) {
        $padding = [math]::Max(0, ($width - $Text.Length - 2) / 2)
        $leftPad = "=" * [math]::Floor($padding)
        $rightPad = "=" * [math]::Ceiling($padding)
        
        Write-Host "$leftPad $Text $rightPad" -ForegroundColor $Color
        Write-Host $line -ForegroundColor $Color
    }
}

# Función mejorada para detener procesos que usen un puerto específico
function Stop-ProcessByPort {
    param (
        [int]$Port
    )
    
    Write-Host "Buscando procesos que usen el puerto $Port..." -ForegroundColor DarkYellow
    
    # Método directo usando cmdlet Get-NetTCPConnection si está disponible (Windows 8/Windows Server 2012 o superior)
    $processesByTCP = $null
    $killed = $false
    
    try {
        # Intentar usar Get-NetTCPConnection que es más confiable
        $processesByTCP = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue | 
                           Select-Object -Property OwningProcess -Unique
                           
        # Si tenemos resultados, detener cada proceso
        if ($processesByTCP) {
            foreach ($conn in $processesByTCP) {
                $processId = $conn.OwningProcess
                try {
                    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
                    if ($process) {
                        Write-Host "Deteniendo proceso: $($process.ProcessName) (PID: $processId) en puerto $Port" -ForegroundColor DarkYellow
                        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
                        $killed = $true
                        Write-Host "Proceso detenido." -ForegroundColor DarkGreen
                    }
                } catch {
                    Write-Host "Error al detener el proceso (PID: $processId): $_" -ForegroundColor Red
                }
            }
        }
    } catch {
        Write-Host "Get-NetTCPConnection no está disponible. Usando método alternativo..." -ForegroundColor Yellow
    }
    
    # Si no se pudo usar Get-NetTCPConnection o no detectó procesos, usar método alternativo
    if (-not $killed) {
        Write-Host "Usando método alternativo con netstat y taskkill..." -ForegroundColor Yellow
        
        try {
            # Usar taskkill directamente con netstat y comandos de filtro para mayor compatibilidad
            # Este método funciona en todas las versiones de Windows
            $command = "netstat -ano | findstr ""$Port"" | findstr ""LISTENING"" | ForEach { `$_ -match '(\d+)$' } | ForEach { taskkill /F /PID `$matches[1] }"
            Invoke-Expression -Command $command -ErrorAction SilentlyContinue
            
            # Una solución alternativa es usar for para ejecutar comandos en una línea
            if ($LASTEXITCODE -ne 0) {
                cmd /c "for /f ""tokens=5"" %a in ('netstat -ano ^| findstr :$Port ^| findstr LISTENING') do taskkill /F /PID %a"
            }
            
            $killed = $true
            Write-Host "Comando de limpieza de puerto ejecutado." -ForegroundColor DarkGreen
        } catch {
            Write-Host "Error en el método alternativo: $_" -ForegroundColor Red
        }
    }
    
    # Verificación final
    Start-Sleep -Seconds 1
    $finalCheck = netstat -ano | findstr ":$Port" | findstr "LISTENING"
    if (-not $finalCheck) {
        Write-Host "Puerto $Port liberado correctamente." -ForegroundColor Green
    } else {
        Write-Host "ADVERTENCIA: El puerto $Port podría seguir ocupado. Esto podría causar problemas al iniciar la aplicación." -ForegroundColor Red
        
        # Intento final con cmd para mayor compatibilidad
        Write-Host "Intentando un último método para liberar el puerto..." -ForegroundColor Yellow
        cmd /c "for /f ""tokens=5"" %a in ('netstat -ano ^| findstr :$Port ^| findstr LISTENING') do taskkill /F /PID %a"
    }
}

# Función para detener todos los procesos de la aplicación
function Stop-ApplicationProcesses {
    Write-Host "Buscando procesos de la aplicacion..." -ForegroundColor DarkYellow
    
    # Detener jobs en segundo plano si existen
    if ($global:appJobs.Count -gt 0) {
        foreach ($job in $global:appJobs) {
            if ($job.State -eq "Running") {
                Write-Host "Deteniendo job: $($job.Name)" -ForegroundColor DarkYellow
                Stop-Job -Job $job -Force
                Remove-Job -Job $job -Force
            }
        }
        $global:appJobs = @()
    }
    
    # Detener procesos si existen
    if ($global:appProcesses.Count -gt 0) {
        foreach ($procInfo in $global:appProcesses) {
            $process = $procInfo.Process
            if ($process -and !$process.HasExited) {
                Write-Host "Deteniendo proceso: $($procInfo.Name) (PID: $($process.Id))" -ForegroundColor DarkYellow
                try {
                    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                } catch {
                    Write-Host "No se pudo detener el proceso: $($procInfo.Name)" -ForegroundColor Red
                }
            }
        }
        $global:appProcesses = @()
    }
    
    # Buscar procesos de dotnet que podrían estar ejecutando nuestra aplicación
    $dotnetProcesses = Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object {
        $_.MainModule.FileName -like "*\API\*" -or 
        $_.MainModule.FileName -like "*\IntermediateServer\*" -or 
        $_.MainModule.FileName -like "*\Client\*" -or
        $_.MainModule.FileName -like "*PSP_MARTIN_PENALVA*"
    }
    
    if ($dotnetProcesses) {
        foreach ($process in $dotnetProcesses) {
            Write-Host "Deteniendo proceso: $($process.ProcessName) (PID: $($process.Id))" -ForegroundColor DarkYellow
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
        Write-Host "Todos los procesos de la aplicacion han sido detenidos." -ForegroundColor DarkGreen
    } else {
        Write-Host "No se encontraron procesos de la aplicacion en ejecucion." -ForegroundColor DarkGreen
    }
    
    # Buscar cualquier proceso de dotnet que esté escuchando en los puertos que usamos
    Write-Host "Buscando procesos dotnet que puedan estar usando nuestros puertos..." -ForegroundColor DarkYellow
    $allDotnetProcesses = Get-Process dotnet -ErrorAction SilentlyContinue
    if ($allDotnetProcesses) {
        foreach ($process in $allDotnetProcesses) {
            # Verificar si este proceso está usando alguno de nuestros puertos
            $processConnections = netstat -ano | findstr /C:":$apiPort" /C:":$serverPort" | findstr /C:"$($process.Id)"
            if ($processConnections) {
                Write-Host "Encontrado proceso dotnet (PID: $($process.Id)) usando uno de nuestros puertos!" -ForegroundColor DarkYellow
                Write-Host "Deteniendo proceso..." -ForegroundColor DarkYellow
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                Write-Host "Proceso detenido." -ForegroundColor DarkGreen
            }
        }
    }
}

# Función para crear y configurar un nuevo proceso
function New-AppProcess {
    param (
        [string]$ComponentName,
        [string]$WorkingDirectory,
        [string]$Arguments,
        [string]$OutputColor
    )
    
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "dotnet"
    $psi.Arguments = $Arguments
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    
    # Configurar manejadores de eventos para capturar salida
    $outputSb = New-Object System.Text.StringBuilder
    $errorSb = New-Object System.Text.StringBuilder
    
    # Crear un prefijo distintivo con el nombre del componente
    $prefix = "[$ComponentName]"
    
    # Registrar evento para la salida estándar
    $stdOutEvent = Register-ObjectEvent -InputObject $process -EventName "OutputDataReceived" -Action {
        $line = $Event.SourceEventArgs.Data
        if ($line) {
            $timestamp = Get-Date -Format "HH:mm:ss"
            $prefixedLine = "[$timestamp $($ComponentName)] $line"
            
            # Usar el color específico para este componente
            Write-Host $prefixedLine -ForegroundColor ([System.ConsoleColor]$OutputColor)
            
            [void]$outputSb.AppendLine($line)
        }
    }
    
    # Registrar evento para la salida de error
    $stdErrEvent = Register-ObjectEvent -InputObject $process -EventName "ErrorDataReceived" -Action {
        $line = $Event.SourceEventArgs.Data
        if ($line) {
            $timestamp = Get-Date -Format "HH:mm:ss"
            $prefixedLine = "[$timestamp $($ComponentName) ERROR] $line"
            
            # Los errores siempre en rojo
            Write-Host $prefixedLine -ForegroundColor Red
            
            [void]$errorSb.AppendLine($line)
        }
    }
    
    # Iniciar el proceso
    [void]$process.Start()
    $process.BeginOutputReadLine()
    $process.BeginErrorReadLine()
    
    # Guardar referencia al proceso y los IDs de eventos
    $processInfo = @{
        Process = $process
        Name = $ComponentName
        OutputEvent = $stdOutEvent
        ErrorEvent = $stdErrEvent
        Color = $OutputColor
    }
    
    $global:appProcesses += $processInfo
    
    Show-Separator "COMPONENTE $ComponentName INICIADO (PID: $($process.Id))" $OutputColor
    
    return $processInfo
}

# Función para iniciar el cliente en una ventana separada
function Start-ClientWindow {
    param (
        [string]$ClientPath
    )
    
    Show-Separator "INICIANDO CLIENTE EN VENTANA SEPARADA" $clientColor
    
    # Usar Start-Process con -NoNewWindow:$false para abrir una nueva ventana
    $clientProcess = Start-Process -FilePath "dotnet" -ArgumentList "run" -WorkingDirectory $ClientPath -PassThru -NoNewWindow
    
    # Agregar el proceso a la lista para seguimiento
    $processInfo = @{
        Process = $clientProcess
        Name = "Client (ventana separada)"
        OutputEvent = $null
        ErrorEvent = $null
        Color = $clientColor
    }
    
    $global:appProcesses += $processInfo
    
    Write-Host "Cliente iniciado en ventana separada (PID: $($clientProcess.Id))" -ForegroundColor $clientColor
    Write-Host "Por favor, interactue con el cliente en su ventana dedicada." -ForegroundColor $clientColor
    
    return $clientProcess
}

# Detener procesos existentes de forma más agresiva
Show-Separator "PREPARACION DEL ENTORNO" "White"
Write-Host "PASO 1: Deteniendo procesos en los puertos utilizados..." -ForegroundColor White

# Asegurarse de que los puertos estén completamente libres
Write-Host "Liberando puerto $apiPort (API)..." -ForegroundColor White
Stop-ProcessByPort -Port $apiPort

Write-Host "Liberando puerto $serverPort (Servidor Intermedio)..." -ForegroundColor White
Stop-ProcessByPort -Port $serverPort

Write-Host "`nPASO 2: Deteniendo todos los procesos de la aplicacion..." -ForegroundColor White
Stop-ApplicationProcesses

# Verificar nuevamente que los puertos estén libres
Write-Host "`nPASO 3: Verificación final de puertos..." -ForegroundColor White
$apiPortInUse = netstat -ano | findstr ":$apiPort"
$serverPortInUse = netstat -ano | findstr ":$serverPort"

if ($apiPortInUse) {
    Write-Host "ADVERTENCIA: El puerto $apiPort (API) todavía está en uso. Esto podría causar problemas." -ForegroundColor Red
    Write-Host "Intentando liberar una última vez..." -ForegroundColor Yellow
    Stop-ProcessByPort -Port $apiPort
}

if ($serverPortInUse) {
    Write-Host "ADVERTENCIA: El puerto $serverPort (Servidor Intermedio) todavía está en uso. Esto podría causar problemas." -ForegroundColor Red
    Write-Host "Intentando liberar una última vez..." -ForegroundColor Yellow
    Stop-ProcessByPort -Port $serverPort
}

# Esperar un momento para asegurar que todos los procesos se hayan detenido
Write-Host "`nEsperando 5 segundos para asegurar que todos los recursos se liberen completamente..." -ForegroundColor White
Start-Sleep -Seconds 5

# 3. Iniciar la API
Show-Separator "INICIANDO API" $apiColor
$apiProcess = New-AppProcess -ComponentName "API" -WorkingDirectory $apiPath -Arguments "run" -OutputColor $apiColor

# Esperar a que la API se inicie completamente
Write-Host "Esperando 10 segundos para que la API se inicie completamente..." -ForegroundColor DarkGray
Start-Sleep -Seconds 10

# 4. Iniciar el servidor intermedio
Show-Separator "INICIANDO SERVIDOR INTERMEDIO" $serverColor
$serverProcess = New-AppProcess -ComponentName "IntermediateServer" -WorkingDirectory $intermediateServerPath -Arguments "run" -OutputColor $serverColor

# Esperar a que el servidor intermedio se inicie completamente
Write-Host "Esperando 10 segundos para que el servidor intermedio se inicie completamente..." -ForegroundColor DarkGray
Start-Sleep -Seconds 10

# 5. Iniciar el cliente en una ventana separada
$clientProcess = Start-ClientWindow -ClientPath $clientPath

Show-Separator "TODOS LOS COMPONENTES INICIADOS" "Magenta"
Write-Host "Todos los componentes han sido iniciados correctamente!" -ForegroundColor Magenta
Write-Host "La API y el servidor intermedio se ejecutan en esta ventana." -ForegroundColor Magenta
Write-Host "El cliente se ha abierto en una ventana separada para permitir interaccion." -ForegroundColor Magenta
Write-Host "Para detener todos los procesos, presiona Ctrl+C o cierra esta ventana." -ForegroundColor Magenta
Write-Host ""

# Registrar evento para limpiar procesos al salir
$exitEvent = Register-EngineEvent -SourceIdentifier ([System.Management.Automation.PsEngineEvent]::Exiting) -Action {
    Write-Host "Deteniendo todos los procesos de la aplicacion..." -ForegroundColor DarkYellow
    Stop-ApplicationProcesses
}

try {
    # Mantener el script en ejecución mientras muestra la salida
    while ($true) {
        # Verificar si algún proceso ha terminado
        $allRunning = $true
        foreach ($procInfo in $global:appProcesses) {
            $proc = $procInfo.Process
            if ($proc.HasExited) {
                Show-Separator "ALERTA: COMPONENTE TERMINADO" "Red"
                Write-Host "El proceso $($procInfo.Name) (PID: $($proc.Id)) ha terminado con codigo de salida $($proc.ExitCode)" -ForegroundColor Red
                $allRunning = $false
            }
        }
        
        if (-not $allRunning) {
            Show-Separator "DETENIENDO TODOS LOS PROCESOS" "Red"
            Write-Host "Al menos uno de los componentes ha terminado. Deteniendo todos los procesos..." -ForegroundColor Red
            Stop-ApplicationProcesses
            break
        }
        
        Start-Sleep -Seconds 1
    }
}
catch {
    Show-Separator "ERROR EN EL SCRIPT" "Red"
    Write-Host "Se ha producido un error: $_" -ForegroundColor Red
}
finally {
    # Limpiar recursos cuando el script termina
    Show-Separator "LIMPIEZA DE RECURSOS" "White"
    Write-Host "Limpiando recursos..." -ForegroundColor White
    
    # Limpiar eventos registrados
    foreach ($procInfo in $global:appProcesses) {
        if ($procInfo.OutputEvent) {
            Unregister-Event -SourceIdentifier $procInfo.OutputEvent.Name -ErrorAction SilentlyContinue
        }
        if ($procInfo.ErrorEvent) {
            Unregister-Event -SourceIdentifier $procInfo.ErrorEvent.Name -ErrorAction SilentlyContinue
        }
    }
    
    # Detener todos los procesos
    Stop-ApplicationProcesses
    
    # Quitar el evento de salida
    Unregister-Event -SourceIdentifier ([System.Management.Automation.PsEngineEvent]::Exiting) -ErrorAction SilentlyContinue
    
    Write-Host "Aplicacion detenida." -ForegroundColor Magenta
} 