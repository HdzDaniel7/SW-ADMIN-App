# Genera el paquete distribuible de SWDataExtractor en dist/:
#   - UI y Batch como ejecutables únicos self-contained (no requieren .NET instalado)
#   - README de instalación + script de acceso directo
#   - ZIP listo para copiar a cualquier PC con Windows 10/11 x64
# Uso:  powershell -ExecutionPolicy Bypass -File herramientas/publicar.ps1
$ErrorActionPreference = 'Stop'

$raiz    = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content "$raiz\src\UI\SWDataExtractor.UI.csproj")).Project.PropertyGroup.Version |
           Where-Object { $_ } | Select-Object -First 1
$dist    = Join-Path $raiz "dist\SWDataExtractor"

Write-Host "== SWDataExtractor v$version - publicando en $dist ==" -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$argumentos = @(
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '/p:PublishSingleFile=true',
    '/p:IncludeNativeLibrariesForSelfExtract=true',
    '/p:EnableCompressionInSingleFile=true'
)

Write-Host "-- Publicando UI --" -ForegroundColor Yellow
dotnet publish "$raiz\src\UI\SWDataExtractor.UI.csproj" @argumentos -o $dist
if ($LASTEXITCODE -ne 0) { throw "Fallo el publish de la UI" }

Write-Host "-- Publicando Batch --" -ForegroundColor Yellow
dotnet publish "$raiz\src\Batch\SWDataExtractor.Batch.csproj" @argumentos -o $dist
if ($LASTEXITCODE -ne 0) { throw "Fallo el publish del Batch" }

# Limpieza: los .pdb no hacen falta en el paquete de usuario final
Get-ChildItem $dist -Filter *.pdb | Remove-Item -Force

Copy-Item "$raiz\herramientas\LEEME-INSTALACION.txt" $dist

# Script de acceso directo para quien prefiere crearlo ANTES de abrir la app
@'
$shell = New-Object -ComObject WScript.Shell
$acceso = $shell.CreateShortcut([System.IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'SWDataExtractor.lnk'))
$acceso.TargetPath       = Join-Path $PSScriptRoot 'SWDataExtractor.UI.exe'
$acceso.WorkingDirectory = $PSScriptRoot
$acceso.IconLocation     = (Join-Path $PSScriptRoot 'SWDataExtractor.UI.exe') + ',0'
$acceso.Description      = 'SWDataExtractor'
$acceso.Save()
Write-Host 'Acceso directo creado en el escritorio.'
'@ | Set-Content (Join-Path $dist 'Crear acceso directo.ps1') -Encoding UTF8

$zip = Join-Path $raiz "dist\SWDataExtractor-v$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $dist -DestinationPath $zip

$tamano = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "== Listo ==" -ForegroundColor Green
Write-Host "Carpeta: $dist"
Write-Host ("ZIP:     {0} ({1} MB)" -f $zip, $tamano)
