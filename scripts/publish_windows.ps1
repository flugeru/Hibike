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
$mpvRelease = 'https://github.com/freeKC/libmpv-builds/releases/download/20261021-1'
$mpvUrl = "$mpvRelease/mpv-dev-x86_64-20261021-git-0b7ed67.7z"
$mpvArchive = Join-Path $root 'artifacts/mpv-lgpl-20261021-1/mpv.7z'
New-Item -ItemType Directory -Force (Split-Path $mpvArchive) | Out-Null
if (!(Test-Path $mpvArchive)) { Invoke-WebRequest $mpvUrl -OutFile $mpvArchive -UseBasicParsing }
if ((Get-FileHash $mpvArchive -Algorithm SHA256).Hash -ne 'b3d1a4b3a8e202c28369a46f5fb776baa0742e38de8c9d0757b0255bcb8223d1') { throw 'libmpv checksum mismatch.' }
$mpvDir = Join-Path $output 'licenses/libmpv'
New-Item -ItemType Directory -Force $mpvDir | Out-Null
tar -xf $mpvArchive -C $mpvDir
if ($LASTEXITCODE -ne 0) { throw 'libmpv extraction failed.' }
$mpvDll = Get-ChildItem $mpvDir -Recurse -Filter 'libmpv-2.dll' | Select-Object -First 1
if (!$mpvDll) { throw 'libmpv-2.dll missing.' }
Move-Item $mpvDll.FullName (Join-Path $output 'libmpv-2.dll')
$sourceName = 'libmpv-windows-20261021-1-sources.tar.xz'
$sourceDir = Join-Path $root 'artifacts/libmpv-source'
New-Item -ItemType Directory -Force $sourceDir | Out-Null
$sourceArchive = Join-Path $sourceDir $sourceName
if (!(Test-Path $sourceArchive)) { Invoke-WebRequest "$mpvRelease/$sourceName" -OutFile $sourceArchive -UseBasicParsing }
if ((Get-FileHash $sourceArchive -Algorithm SHA256).Hash -ne '77e612c8110b1731c9b180cda840bb965939440c743d480f34cab957861e9d11') { throw 'libmpv source checksum mismatch.' }
Set-Content (Join-Path $mpvDir 'SOURCES.txt') "Corresponding source: $sourceName, supplied alongside Hibike release downloads. Upstream: $mpvRelease/$sourceName. SHA-256: 77e612c8110b1731c9b180cda840bb965939440c743d480f34cab957861e9d11. Replace libmpv-2.dll beside Hibike.Desktop.exe to use a modified compatible library."
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $output
$certificates = Join-Path $output 'cacert.pem'
Invoke-WebRequest 'https://curl.se/ca/cacert-2026-10-20.pem' -OutFile $certificates -UseBasicParsing
if ((Get-FileHash $certificates -Algorithm SHA256).Hash -ne '796f1389bfea13c0ed78f663b765baaff54a508e5483786bd6ece73c5e3e3cbe') { throw 'CA bundle checksum mismatch.' }
Set-Content (Join-Path $output 'LEIA-ME.txt') 'Abra Hibike.Desktop.exe. Mantenha todos os arquivos desta pasta juntos. Inclui .NET, libmpv LGPL (padrao), VLC e yt-dlp. Ambos os motores suportam arquivos locais e streaming. Motor selecionavel nas configuracoes; reinicie apos trocar. Internet necessaria para busca e streaming. Login Google opcional para biblioteca; reproducao nao recebe credenciais da conta.'
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
