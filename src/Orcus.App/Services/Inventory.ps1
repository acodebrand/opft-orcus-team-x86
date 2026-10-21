$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$warnings = [System.Collections.Generic.List[string]]::new()
try {
    $os = Get-CimInstance Win32_OperatingSystem
    $pc = Get-CimInstance Win32_ComputerSystem
    $cpu = Get-CimInstance Win32_Processor
    $cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $managed = [bool]$pc.PartOfDomain
    if (Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo') { $managed = $true }
    # A conservative block also covers enrolled MDM devices and machine update policy.
    $enrollments = @(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Enrollments' -ErrorAction SilentlyContinue | Get-ItemProperty -ErrorAction SilentlyContinue | Where-Object { $_.ProviderID -and $_.UPN })
    if ($enrollments.Count -gt 0) { $managed = $true }
    $wu = Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' -ErrorAction SilentlyContinue
    if ($wu.WUServer) { $managed = $true }
    $gpu = @(); $disks = @(); $apps = @(); $startup = @()
    try { $gpu = @(Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name }) } catch { $warnings.Add('GPU: no disponible.') }
    try {
        $disks = @(Get-PhysicalDisk | ForEach-Object { '{0} · {1} · bus {2} · {3:N0} GB' -f $_.FriendlyName, $_.MediaType, $_.BusType, ($_.Size / 1GB) })
    } catch { $warnings.Add('Tipo de almacenamiento no disponible; no se adivina HDD/SSD.') }
    try {
        $apps = @(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object DisplayName | ForEach-Object { '{0} {1}' -f $_.DisplayName, $_.DisplayVersion })
        $apps += @(Get-AppxPackage -ErrorAction Stop | ForEach-Object { '{0} {1} [AppX]' -f $_.Name, $_.Version })
    } catch { $warnings.Add('Inventario de aplicaciones incompleto por permisos o compatibilidad.') }
    try { $startup = @(Get-CimInstance Win32_StartupCommand | ForEach-Object { '{0} · {1}' -f $_.Name, $_.Location }) } catch { $warnings.Add('Programas de inicio: no disponibles.') }
    $processes = @(Get-Process | Sort-Object WorkingSet64 -Descending | ForEach-Object { '{0} · PID {1} · {2:N0} MB' -f $_.ProcessName, $_.Id, ($_.WorkingSet64 / 1MB) })
    $services = @(Get-CimInstance Win32_Service | Sort-Object Name | ForEach-Object { '{0} · {1} · {2}' -f $_.Name, $_.State, $_.StartMode })
    $volumes = @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object { '{0} {1:N1} GB libres / {2:N1} GB' -f $_.DeviceID, ($_.FreeSpace / 1GB), ($_.Size / 1GB) })
    $servicing = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') -or [bool](Get-Process TiWorker,TrustedInstaller,MoUsoCoreWorker -ErrorAction SilentlyContinue)
    [ordered]@{
        Os = ('{0} · {1} · build {2}' -f $os.Caption, $cv.DisplayVersion, $os.BuildNumber)
        Edition = [string]$cv.EditionID
        Cpu = ($cpu.Name -join ' / ')
        CpuPercent = [double](($cpu | Measure-Object LoadPercentage -Average).Average)
        RamGB = [math]::Round($pc.TotalPhysicalMemory / 1GB, 1)
        UsedGB = [math]::Round(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / 1MB, 1)
        Gpu = ($gpu -join ' / ')
        Managed = $managed
        Servicing = [bool]$servicing
        Disks = @($disks); Volumes = @($volumes); Apps = @($apps | Sort-Object -Unique)
        Startup = @($startup); Processes = @($processes); Services = @($services)
        Warnings = @($warnings.ToArray())
    } | ConvertTo-Json -Depth 5 -Compress
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
