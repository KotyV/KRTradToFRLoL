<#
.SYNOPSIS
Assemble la distribution portable pour les streamers : zip "tout inclus" ou .exe unique.

.DESCRIPTION
Deux formats, zero installation dans les deux cas (aucun .NET requis sur la machine cible) :

- Par defaut : un dossier (a zipper avec -Zip) contenant l'exe auto-suffisant + les
  modeles OCR coreen et M2M-100 s'ils sont installes sur la machine de build
  (tools/export_korean_ocr.py / tools/export_m2m100.py).
- -SingleExe : UN SEUL fichier KRTradToFRLoL.exe (app + runtime + DLL natives + donnees
  embarquees, extraites dans un cache au premier lancement). Les modeles ne sont pas
  embarques : l'app les cherche dans models/ a cote de l'exe, puis dans
  %AppData%/KRTradToFRLoL/models.

.EXAMPLE
./tools/make-portable.ps1 -Zip          # dossier complet + zip
./tools/make-portable.ps1 -SingleExe    # un seul .exe a envoyer
#>
[CmdletBinding()]
param(
    [string]$OutDir = 'publish/KRTradToFRLoL-portable',
    [switch]$Zip,
    [switch]$SingleExe
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo $OutDir

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$publishArgs = @(
    (Join-Path $repo 'src/KRTradToFRLoL'), '-c', 'Release', '-r', 'win-x64', '--self-contained',
    '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None',
    '-o', $out
)
if ($SingleExe) {
    # Tout embarquer dans l'exe : DLL natives + fichiers data/, extraits au premier
    # lancement dans un cache (%TEMP%\.net) puis reutilises.
    $publishArgs += '-p:IncludeNativeLibrariesForSelfExtract=true'
    $publishArgs += '-p:IncludeAllContentForSelfExtract=true'
}

dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a echoue (code $LASTEXITCODE)" }

# Fichiers de link C++ inutiles au runtime
Get-ChildItem $out -Filter '*.lib' -ErrorAction SilentlyContinue | Remove-Item -Force

if ($SingleExe) {
    $exe = Join-Path $out 'KRTradToFRLoL.exe'
    $mb = [math]::Round(((Get-Item $exe).Length / 1MB), 1)
    Write-Host "Exe unique : $exe ($mb Mo) - envoie ce fichier tel quel."
    Write-Host 'Pour l''OCR coreen precis : poser un dossier models/ a cote de l''exe (cf. README).'
}
else {
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
}

if ($Zip) {
    $zipPath = "$out.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zipPath
    $zmb = [math]::Round(((Get-Item $zipPath).Length / 1MB), 1)
    Write-Host "Zip    : $zipPath ($zmb Mo)"
}
