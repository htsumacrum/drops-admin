<# DROPS release pipeline: publica o loader, atualiza o drops-updates e faz push.
Uso: .\build.ps1 [-Version 1.5.3] [-Message "texto do commit"]
Sem -Version, lê <Version> do loader-ui.csproj.
#>
param([string]$Version = "", [string]$Message = "")

$ErrorActionPreference = "Stop"
$root = "C:\Users\euleo\hackworks\opencode-team"
$ui   = Join-Path $root "loader-ui"
$rel  = Join-Path $root "drops-release"

[xml]$csproj = Get-Content (Join-Path $ui "loader-ui.csproj")
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $csproj.Project.PropertyGroup.Version }
if ([string]::IsNullOrWhiteSpace($Version)) { throw "Versao nao encontrada no csproj." }
if ([string]::IsNullOrWhiteSpace($Message)) { $Message = "release $Version" }

Write-Host "== DROPS build $Version"
dotnet publish (Join-Path $ui "loader-ui.csproj") -c Release -o (Join-Path $ui "dist-slim") --nologo
if ($LASTEXITCODE -ne 0) { throw "publish falhou." }

Copy-Item (Join-Path $ui "dist-slim\loader.exe") (Join-Path $rel "loader\loader.exe") -Force
python3 (Join-Path $ui "make-manifest.py") $rel $Version

# Assina o manifest. O loader VERIFICA esta assinatura antes de instalar
# qualquer coisa: sem ela, o update é abortado e nada é escrito em disco.
# A chave privada vem de DROPS_PRIVATE_KEY (nunca vai para o repo).
if (-not $env:DROPS_PRIVATE_KEY) {
    throw "DROPS_PRIVATE_KEY nao definido. Gere as chaves com:`n  python loader-ui\tools\drops_signing.py gen <arquivo.json>`ne exporte DROPS_PRIVATE_KEY antes de buildar."
}
python3 (Join-Path $ui "tools\drops_signing.py") sign (Join-Path $rel "manifest.json") (Join-Path $rel "manifest.sig")
if ($LASTEXITCODE -ne 0) { throw "assinatura do manifest falhou."; }

Set-Location $rel
git add -A
git -c user.name="drops" -c user.email="drops@local" commit -m $Message
git push origin main
$sha = (Get-FileHash (Join-Path $rel "loader\loader.exe") -Algorithm SHA256).Hash.ToLower()
Write-Host "== OK $Version sha=$sha"
