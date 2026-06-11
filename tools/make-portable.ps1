<#
.SYNOPSIS
Assemble le bundle portable "tout inclus" pour la distribution aux streamers.

.DESCRIPTION
Publie un exe auto-suffisant (aucun .NET requis sur la machine cible), puis y ajoute
les modeles OCR coreen et M2M-100 s'ils sont installes sur la machine de build
(tools/export_korean_ocr.py / tools/export_m2m100.py). L'app cherche les modeles
d'abord dans models/ a cote de l'exe, puis dans %AppData%/KRTradToFRLoL/models :
le zip se distribue donc en "dezipper -> double-clic", sans installation.

.EXAMPLE
./tools/make-portable.ps1 -Zip
#>
[CmdletBinding()]
param(
    [string]$OutDir = 'publish/KRTradToFRLoL-portable',
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo $OutDir

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $repo 'src/KRTradToFRLoL') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a echoue (code $LASTEXITCODE)" }

# Fichiers de link C++ inutiles au runtime
Get-ChildItem $out -Filter '*.lib' | Remove-Item -Force

# Modeles optionnels : repris de %AppData% s'ils y sont installes
$appDataModels = Join-Path $env:APPDATA 'KRTradToFRLoL\models'
foreach ($name in 'ocr-ko', 'm2m100') {
    $src = Join-Path $appDataModels $name
    if (Test-Path $src) {
        Copy-Item $src -Destination (Join-Path $out "models\$name") -Recurse
        Write-Host "[OK] Modele $name inclus ($src)"
    }
    else {
        Write-Host "[--] Modele $name absent ($src) : bundle sans lui." -ForegroundColor Yellow
        if ($name -eq 'ocr-ko') {
            Write-Host '     -> l''app utilisera l''OCR Windows (pack de langue coreen requis cote streamer)'
        }
        else {
            Write-Host '     -> pas de traduction hors ligne (glossaire + LLM uniquement)'
        }
    }
}

$mb = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Bundle : $out ($mb Mo)"

if ($Zip) {
    $zipPath = "$out.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zipPath
    $zmb = [math]::Round(((Get-Item $zipPath).Length / 1MB), 1)
    Write-Host "Zip    : $zipPath ($zmb Mo)"
}
