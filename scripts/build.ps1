param([string]$Version = '1.0.0', [switch]$SkipNativeTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    New-Item -ItemType Directory -Force artifacts/test-results | Out-Null
    & dotnet run --project tests/JinRiJian.Tests -c Release -- artifacts/test-results/core-tests-windows.json
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    & dotnet publish src/JinRiJian.App -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o artifacts/portable --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
    & dotnet publish src/JinRiJian.App -c Release -r win-x64 --self-contained false "-p:Version=$Version" -o artifacts/framework --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Framework-dependent publish failed.' }
    if (-not $SkipNativeTests) {
        $data = Join-Path $root 'artifacts/windows-smoke-data'
        if (Test-Path $data) { Remove-Item -Recurse -Force $data }
        $report = Join-Path $root 'artifacts/test-results/windows-smoke.json'
        $exe = Join-Path $root 'artifacts/portable/JinRiJian.exe'
        $process = Start-Process -FilePath $exe -ArgumentList @('--smoke-test', '--data-dir', "`"$data`"", '--test-report', "`"$report`"") -PassThru
        if (-not $process.WaitForExit(180000)) { $process.Kill($true); throw 'Native UI tests timed out (180 s).' }
        if (Test-Path "$data/errors.log") { Copy-Item "$data/errors.log" artifacts/test-results/windows-errors.log }
        if ($process.ExitCode -ne 0) { throw "Native UI tests failed: exit $($process.ExitCode)." }
        if (-not (Test-Path $report)) { throw 'Native UI report was not generated.' }
    }
    Copy-Item docs/使用说明.txt artifacts/portable/使用说明.txt
    Copy-Item docs/使用说明.txt artifacts/framework/使用说明.txt
    Set-Content artifacts/framework/运行要求.txt '.NET 10 Desktop Runtime x64 is required: https://dotnet.microsoft.com/download/dotnet/10.0' -Encoding utf8
    New-Item -ItemType Directory -Force artifacts/packages | Out-Null
    Compress-Archive -Path artifacts/portable/* -DestinationPath "artifacts/packages/Schedule-Note-$Version-win-x64-portable.zip" -Force
    Compress-Archive -Path artifacts/framework/* -DestinationPath "artifacts/packages/Schedule-Note-$Version-win-x64-framework.zip" -Force
    Get-ChildItem artifacts/packages/*.zip | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content artifacts/packages/SHA256SUMS.txt -Encoding utf8
} finally { Pop-Location }
