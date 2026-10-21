param([ValidateSet('Create','Latest','Verify')][string]$Mode, [int]$Sequence = 0)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
try {
    $points = @(Get-ComputerRestorePoint -ErrorAction Stop)
    if ($Mode -eq 'Create') {
        $label = 'OPTF - ' + (Get-Date -Format 'yyyy-MM-dd HH:mm') + ' - ' + [guid]::NewGuid().ToString('N').Substring(0,8)
        Checkpoint-Computer -Description $label -RestorePointType MODIFY_SETTINGS -ErrorAction Stop -WarningAction SilentlyContinue
        $point = Get-ComputerRestorePoint -ErrorAction Stop | Where-Object { $_.Description -eq $label } | Sort-Object SequenceNumber -Descending | Select-Object -First 1
        if (-not $point) { throw 'Windows no confirmó un punto nuevo. Comprueba Protección del sistema; si ya hay uno creado en las últimas 24 horas, usa Verificar punto reciente. No se aplicaron optimizaciones.' }
    } elseif ($Mode -eq 'Latest') {
        $point = $points | Sort-Object SequenceNumber -Descending | Select-Object -First 1
    } else {
        $point = $points | Where-Object { $_.SequenceNumber -eq $Sequence } | Select-Object -First 1
    }
    if (-not $point) { throw 'No hay un punto de restauración disponible. Activa Protección del sistema para la unidad de Windows y crea uno desde Inicio.' }
    $created = [System.Management.ManagementDateTimeConverter]::ToDateTime($point.CreationTime).ToUniversalTime()
    @{ Sequence = [int]$point.SequenceNumber; Description = [string]$point.Description; CreatedUtc = $created.ToString('o') } | ConvertTo-Json -Compress
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
