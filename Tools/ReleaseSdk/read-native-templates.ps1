$ErrorActionPreference = 'Stop'
$editor = 'C:\Unity\Editor\Data\PlaybackEngines\AndroidPlayer'
$templates = Join-Path $editor 'Tools\GradleTemplates'
if (Test-Path $templates) {
    Get-ChildItem $templates -File | ForEach-Object { [ordered]@{ template=$_.Name; path=$_.FullName } | ConvertTo-Json -Compress }
}
foreach ($name in @('AndroidManifest.xml','mainTemplate.gradle','launcherTemplate.gradle','gradleTemplate.properties','settingsTemplate.gradle')) {
    $path = Join-Path $templates $name
    if (Test-Path $path) { [ordered]@{ name=$name; content=[string](Get-Content -LiteralPath $path -Raw) } | ConvertTo-Json -Compress }
}
$apk = Join-Path $editor 'Apk'
if (Test-Path $apk) {
    Get-ChildItem $apk -File | Where-Object { $_.Name -like '*Manifest*' } | ForEach-Object {
        [ordered]@{ name=$_.Name; content=[string](Get-Content -LiteralPath $_.FullName -Raw) } | ConvertTo-Json -Compress
    }
}
