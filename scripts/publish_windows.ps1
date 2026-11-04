param([string]$Name = 'Hibike-win-x64', [string]$GoogleOAuthConfig = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root "artifacts\$Name"
$googleOptions = @()
if ($GoogleOAuthConfig) {
    $configPath = (Resolve-Path $GoogleOAuthConfig).Path
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    if ($config.installed.client_id -ne '608800044236-2tpfrd9da8812kldg6j5jp6bk6qmemon.apps.googleusercontent.com' -or !$config.installed.client_secret) {
        throw 'Google OAuth config must match the Desktop client and contain client_secret.'
    }
    $googleOptions = @("-p:HibikeGoogleOAuthConfig=$configPath")
}
dotnet publish (Join-Path $root 'src\Hibike.Desktop\Hibike.Desktop.csproj') -c Release -r win-x64 --self-contained true -o $output @googleOptions
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
$mpvUrl = 'https://github.com/zhongfly/mpv-winbuild/releases/download/2026-11-03-46787be250/mpv-dev-lgpl-x86_64-20261103-git-46787be250.7z'
$mpvArchive = Join-Path $root 'artifacts/mpv-lgpl-20261103/mpv.7z'
New-Item -ItemType Directory -Force (Split-Path $mpvArchive) | Out-Null
if (!(Test-Path $mpvArchive)) { Invoke-WebRequest $mpvUrl -OutFile $mpvArchive -UseBasicParsing }
if ((Get-FileHash $mpvArchive -Algorithm SHA256).Hash -ne 'ce2563d352aac81c2e6e9b2b63e846ec847f4bdabdec74fb14c5b29049c161d7') { throw 'libmpv checksum mismatch.' }
$mpvDir = Join-Path $output 'licenses/libmpv'
New-Item -ItemType Directory -Force $mpvDir | Out-Null
tar -xf $mpvArchive -C $mpvDir
if ($LASTEXITCODE -ne 0) { throw 'libmpv extraction failed.' }
$mpvDll = Get-ChildItem $mpvDir -Recurse -Filter 'libmpv-2.dll' | Select-Object -First 1
if (!$mpvDll) { throw 'libmpv-2.dll missing.' }
Move-Item $mpvDll.FullName (Join-Path $output 'libmpv-2.dll')
Invoke-WebRequest 'https://raw.githubusercontent.com/mpv-player/mpv/46787be250/LICENSE.LGPL' -OutFile (Join-Path $mpvDir 'LICENSE.LGPL') -UseBasicParsing
Invoke-WebRequest 'https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/COPYING.LGPLv3' -OutFile (Join-Path $mpvDir 'COPYING.LGPLv3') -UseBasicParsing
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $output
Set-Content (Join-Path $output 'LEIA-ME.txt') 'Abra Hibike.Desktop.exe. Mantenha todos os arquivos desta pasta juntos. Inclui .NET, libmpv LGPL (padrao), VLC, NAudio e yt-dlp. Motor selecionavel nas configuracoes; reinicie apos trocar. BASS nao incluido: exige bass.dll x64 oficial e licenca aplicavel. Media Foundation/NAudio dependem dos codecs Windows. Internet necessaria para busca e streaming. Login Google opcional para biblioteca; reproducao nao recebe credenciais da conta.'
Add-Type -AssemblyName System.IO.Compression
$archivePath = Join-Path $root "artifacts\$Name.zip"
$archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::Create)
$archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $output -Recurse -File) {
        $relative = $file.FullName.Substring($output.Length + 1).Replace('\', '/')
        $entry = $archive.CreateEntry("$Name/$relative", [IO.Compression.CompressionLevel]::Optimal)
        $inputStream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $entryStream = $entry.Open()
        try { $inputStream.CopyTo($entryStream) } finally { $entryStream.Dispose(); $inputStream.Dispose() }
    }
} finally { $archive.Dispose(); $archiveStream.Dispose() }
Write-Output "Portable app: $output\Hibike.Desktop.exe"
