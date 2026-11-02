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
Set-Content (Join-Path $output 'LEIA-ME.txt') 'Abra Hibike.Desktop.exe. Mantenha todos os arquivos desta pasta juntos. Inclui .NET, VLC e yt-dlp. Internet necessaria para busca e streaming. Login Google opcional para biblioteca; reproducao nao recebe credenciais da conta.'
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
