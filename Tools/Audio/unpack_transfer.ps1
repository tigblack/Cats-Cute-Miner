# Распаковать перенос аудиофайлов с хаба (бинарники не синхронизируются): Docs/Audio/_transfer_<имя>.b64 → tar → папка.
# Запуск на ПК из корня проекта: powershell -ExecutionPolicy Bypass -File Tools/Audio/unpack_transfer.ps1 -Name game -Dest Assets/Game/Resources/Audio
param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Dest)
$ErrorActionPreference = 'Stop'
$b64 = "Docs/Audio/_transfer_$Name.b64"
$text = [IO.File]::ReadAllText((Resolve-Path $b64).Path)
$bytes = [Convert]::FromBase64String(($text -replace '\s', ''))
New-Item -ItemType Directory -Force -Path build | Out-Null
$tar = Join-Path (Get-Location) "build/$Name.tar"
[IO.File]::WriteAllBytes($tar, $bytes)
"sha256 " + (Get-FileHash $tar -Algorithm SHA256).Hash.ToLower()
New-Item -ItemType Directory -Force -Path $Dest | Out-Null
tar -xf $tar -C $Dest
Remove-Item $tar
"files " + (Get-ChildItem $Dest -File).Count
