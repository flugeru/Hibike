$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts\Hibiki-win-x64'
dotnet publish (Join-Path $root 'src\Hibiki.Desktop\Hibiki.Desktop.csproj') -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
$version = (Get-Content (Join-Path $PSScriptRoot 'runtime-lock.json') -Raw | ConvertFrom-Json).yt_dlp.version
$release = "https://github.com/yt-dlp/yt-dlp/releases/download/$version"
$checksums = (Invoke-WebRequest "$release/SHA2-256SUMS" -UseBasicParsing).Content
if ($checksums -is [byte[]]) { $checksums = [Text.Encoding]::UTF8.GetString($checksums) }
$expected = [regex]::Match($checksums, '(?m)^([a-fA-F0-9]{64})\s+\*?yt-dlp\.exe\s*$').Groups[1].Value
if (!$expected) { throw 'yt-dlp.exe checksum missing.' }
$extractor = Join-Path $output 'yt-dlp.exe'
Invoke-WebRequest "$release/yt-dlp.exe" -OutFile $extractor -UseBasicParsing
if ((Get-FileHash $extractor -Algorithm SHA256).Hash -ne $expected) {
    Remove-Item $extractor
    throw 'yt-dlp.exe checksum mismatch.'
}
& $extractor --version
if ($LASTEXITCODE -ne 0) { throw 'Bundled yt-dlp failed to start.' }
Set-Content (Join-Path $output 'LEIA-ME.txt') 'Abra Hibiki.Desktop.exe. Mantenha todos os arquivos desta pasta juntos. Inclui .NET, VLC e yt-dlp. Internet necessaria para busca e streaming. Login YouTube pode exigir cookies nas configuracoes.'
Compress-Archive -Path "$output\*" -DestinationPath (Join-Path $root 'artifacts\Hibiki-win-x64.zip') -Force
Write-Output "Portable app: $output\Hibiki.Desktop.exe"
