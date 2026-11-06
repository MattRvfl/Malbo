# Publication de Malbo pour une release GitHub.
# - Compile en Release, dépendant du framework (.NET 10 Desktop Runtime requis), sans fichier unique.
# - Zippe le contenu du dossier publish\ dans Malbo-<version>.zip (version lue dans le .csproj).
# - Ce zip est l'asset attendu par la mise à jour automatique (Updater.CheckAsync / ApplyAsync).
# Utilisation : powershell -ExecutionPolicy Bypass -File .\publish.ps1

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

# Version lue dans DynamicIsland.csproj (<Version>x.y.z</Version>)
[xml]$proj = Get-Content -Path 'DynamicIsland.csproj' -Raw
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = '1.0.0' }

# Nettoyage de l'ancienne publication
if (Test-Path 'publish') { Remove-Item -Recurse -Force 'publish' }

dotnet publish 'DynamicIsland.csproj' -c Release -o publish --self-contained false -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué ($LASTEXITCODE)" }

# Archive pour la release
$zip = "Malbo-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path 'publish\*' -DestinationPath $zip

Write-Host "Archive créée : $zip"
