# Publication de Malbo pour une release GitHub.
# - Compile un seul Malbo.exe autonome (runtime .NET inclus, rien à installer pour l'utilisateur).
# - Le fichier produit (Malbo.exe à la racine du projet) est l'asset à joindre à la release :
#   la mise à jour automatique le télécharge et remplace l'exe en place.
# - Pense à augmenter <Version> dans DynamicIsland.csproj et à nommer la release vX.Y.Z.
# Utilisation : powershell -ExecutionPolicy Bypass -File .\publish.ps1

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

[xml]$proj = Get-Content -Path 'DynamicIsland.csproj' -Raw
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = '1.0.0' }

if (Test-Path 'publish') { Remove-Item -Recurse -Force 'publish' }

dotnet publish 'DynamicIsland.csproj' -c Release -r win-x64 -o publish `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué ($LASTEXITCODE)" }

Copy-Item 'publish\Malbo.exe' 'Malbo.exe' -Force
$size = [math]::Round((Get-Item 'Malbo.exe').Length / 1MB, 1)
Write-Host "Malbo $version prêt : Malbo.exe ($size Mo) à joindre à la release v$version"
