[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path 'src/Assets/items.json')) { throw 'Extract the item catalog first using tools/Inspect.csproj and the installed Terraria.exe' }
    dotnet build injector/FishingInjector.csproj -c Release -o artifacts/bridge
    if ($LASTEXITCODE -ne 0) { throw 'Bridge build failed' }
    New-Item -ItemType Directory -Force src/Assets | Out-Null
    $bundleFiles = @('0Harmony.dll', 'EasyHook.dll', 'EasyHook32.dll', 'EasyLoad32.dll', 'FishingBridge.Angler12.dll', 'FishingInjector.exe', 'FishingInjector.exe.config')
    $bundlePaths = foreach ($name in $bundleFiles) { Join-Path 'artifacts/bridge' $name }
    Compress-Archive -LiteralPath $bundlePaths -DestinationPath src/Assets/bridge.zip -Force
    dotnet publish src/TerrariaFishing.csproj -c Release -o release
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed' }
} finally { Pop-Location }



