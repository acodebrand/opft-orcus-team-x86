$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceZip = Join-Path $projectRoot 'artifacts\release\OPTF-ORCUS-TEAM-source.zip'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceFiles = @()
foreach ($folder in @('src','tests','scripts','docs')) {
    $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|Payload)[\\/]' -and $_.Extension -notin @('.exe','.dll','.pdb') }
}
foreach ($name in @('README.md','Orcus.sln','build.ps1','Iniciar.cmd','.gitignore')) {
    $sourceFiles += Get-Item -LiteralPath (Join-Path $projectRoot $name)
}
New-Item -ItemType Directory -Path (Split-Path -Parent $sourceZip) -Force | Out-Null
$zipStream = [IO.File]::Open($sourceZip, [IO.FileMode]::Create)
$archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($source in $sourceFiles) {
        $entryName = $source.FullName.Substring($projectRoot.Length + 1).Replace('\','/')
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $source.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose(); $zipStream.Dispose() }
Write-Output "Source archive: $sourceZip ($($sourceFiles.Count) files)"
