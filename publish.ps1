# Publication de Malbo pour une release GitHub.
# - Compile un seul Malbo.exe autonome (runtime .NET inclus, rien à installer pour l'utilisateur).
# - Le fichier produit (Malbo.exe à la racine du projet) est l'asset à joindre à la release :
#   la mise à jour automatique le télécharge et remplace l'exe en place.
# - Génère update.json (manifeste lu par la mise à jour automatique : version, url, sha256, notes).
#   Commit + push update.json APRÈS avoir publié la release vX.Y.Z avec Malbo.exe en asset :
#   tous les Malbo installés se mettront à jour tout seuls.
# - Pense à augmenter <Version> dans DynamicIsland.csproj et à nommer la release vX.Y.Z.
# Utilisation : powershell -ExecutionPolicy Bypass -File .\publish.ps1 [-Repo owner/nom] [-Notes "..."] [-Mandatory]
param(
    [string]$Repo = 'MattRvfl/Malbo',
    [string]$Notes = '',
    [switch]$Mandatory
)

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
$sha = (Get-FileHash -Algorithm SHA256 -Path 'Malbo.exe').Hash
$manifest = [ordered]@{
    version   = $version
    url       = "https://github.com/$Repo/releases/download/v$version/Malbo.exe"
    sha256    = $sha
    notes     = $Notes
    mandatory = [bool]$Mandatory
    published = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
}
$json = $manifest | ConvertTo-Json
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'update.json'), $json, (New-Object System.Text.UTF8Encoding $false))

Write-Host "Malbo $version prêt : Malbo.exe ($size Mo) à joindre à la release v$version"
Write-Host "SHA-256 : $sha"
Write-Host "1) Crée la release v$version sur https://github.com/$Repo/releases et joins Malbo.exe"
Write-Host "2) git add update.json ; git commit -m 'Malbo $version' ; git push  -> mise à jour automatique pour tout le monde"
