[CmdletBinding()]
param(
    [string]$SourceExe = 'D:\Plain Craft Launcher 2.exe',
    [string]$RuntimeDirectory = 'D:\PCL'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$build = Join-Path $repo 'build\pcl2'
$dist = Join-Path $repo 'dist'
$source = Join-Path $repo 'installer\pcl2'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Only these explicitly named source files are ever copied. No directory copy is used.
$inputs = [ordered]@{
    'Plain Craft Launcher 2.exe' = $SourceExe
    'PCL/JavaWrapper.jar' = Join-Path $RuntimeDirectory 'JavaWrapper.jar'
    'PCL/libjlw-x86_64-1.4.4.dll' = Join-Path $RuntimeDirectory 'libjlw-x86_64-1.4.4.dll'
    'PCL/libwebp.dll' = Join-Path $RuntimeDirectory 'libwebp.dll'
}
foreach ($file in @($inputs.Values) + @($csc)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'A required source file or compiler is missing.' }
}
$version = (Get-Item -LiteralPath $SourceExe).VersionInfo.FileVersion
if ($version -ne '2.13.1.1') { throw 'Expected PCL 2.13.1.1. Update installer version metadata before building a different version.' }
New-Item -ItemType Directory -Path $build,$dist -Force | Out-Null

# Keep sensitive values in memory only. Never emit source log lines or token values.
$privateValues = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
[void]$privateValues.Add('LeDestin11')
$sensitiveInputs = @(Get-ChildItem -LiteralPath $RuntimeDirectory -File | Where-Object { $_.Name -eq 'LatestLaunch.bat' -or $_.Name -like 'Log*.txt' })
$tokenPattern = '(?i)(?:--)?(?:access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?token|uuid|username|email|password)["'']?\s*(?::|=|\s)\s*["'']?([A-Za-z0-9._@+:/=-]{8,})'
foreach ($file in $sensitiveInputs) {
    $content = [IO.File]::ReadAllText($file.FullName)
    $content = $content.Replace('\"', '"')
    foreach ($match in [regex]::Matches($content, $tokenPattern)) { [void]$privateValues.Add($match.Groups[1].Value) }
    foreach ($match in [regex]::Matches($content, '(?i)--(?:accessToken|uuid|username)\s+(?:"([^"]+)"|''([^'']+)''|(\S+))')) {
        foreach ($group in 1..3) { if ($match.Groups[$group].Success) { [void]$privateValues.Add($match.Groups[$group].Value) } }
    }
    foreach ($match in [regex]::Matches($content, '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+')) { [void]$privateValues.Add($match.Value) }
    $content = $null
}
function Assert-CleanBytes([byte[]]$Bytes) {
    $texts = @([Text.Encoding]::UTF8.GetString($Bytes), [Text.Encoding]::Unicode.GetString($Bytes), [Text.Encoding]::BigEndianUnicode.GetString($Bytes))
    if ($Bytes.Length -gt 1) {
        $texts += [Text.Encoding]::Unicode.GetString($Bytes, 1, $Bytes.Length - 1)
        $texts += [Text.Encoding]::BigEndianUnicode.GetString($Bytes, 1, $Bytes.Length - 1)
    }
    foreach ($value in $privateValues) {
        foreach ($decoded in $texts) {
            if ($decoded.IndexOf($value, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw 'Privacy verification failed: a personal value was detected. No installer was published.' }
        }
    }
}

$payloadPath = Join-Path $build 'payload.zip'
$payloadBytes = [ordered]@{}
foreach ($name in $inputs.Keys) { $payloadBytes[$name] = [IO.File]::ReadAllBytes($inputs[$name]) }
# Automatically add SystemSystemUpdate:3 to prevent automatic updates
$payloadBytes['PCL/Setup.ini'] = [Text.Encoding]::UTF8.GetBytes("SystemSystemUpdate:3`r`n")
$hashes = [ordered]@{}
foreach ($name in $payloadBytes.Keys) {
    Assert-CleanBytes $payloadBytes[$name]
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hashes[$name] = [BitConverter]::ToString($sha.ComputeHash($payloadBytes[$name])).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}
# Also scan decompressed Java classes/resources inside the approved wrapper JAR.
$jarStream = New-Object IO.MemoryStream
try {
    $jarBytes = $payloadBytes['PCL/JavaWrapper.jar']
    $jarStream.Write($jarBytes, 0, $jarBytes.Length); $jarStream.Position = 0
    $jar = New-Object IO.Compression.ZipArchive($jarStream, [IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        foreach ($entry in $jar.Entries) {
            $memory = New-Object IO.MemoryStream; $input = $entry.Open()
            try { $input.CopyTo($memory); Assert-CleanBytes ($memory.ToArray()) } finally { $input.Dispose(); $memory.Dispose() }
        }
    } finally { $jar.Dispose() }
} finally { $jarStream.Dispose() }
$stream = [IO.File]::Open($payloadPath, [IO.FileMode]::Create)
try {
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($dir in @('PCL/', 'PCL/Musics/', 'PCL/Pictures/')) { [void]$zip.CreateEntry($dir) }
        foreach ($name in $payloadBytes.Keys) {
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $out = $entry.Open()
            try { $out.Write($payloadBytes[$name], 0, $payloadBytes[$name].Length) } finally { $out.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
$hashSource = 'namespace PclSetup { public static class PayloadHashes { public static readonly System.Collections.Generic.Dictionary<string,string> Values = new System.Collections.Generic.Dictionary<string,string> { ' + (($hashes.Keys | ForEach-Object { '{ "' + $_ + '", "' + $hashes[$_] + '" }' }) -join ', ') + ' }; } }'
[IO.File]::WriteAllText((Join-Path $build 'PayloadHashes.cs'), $hashSource, (New-Object Text.UTF8Encoding($false)))

$commonArgs = @('/nologo', '/optimize+', '/target:winexe', '/platform:anycpu', '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.IO.Compression.dll', ('/win32manifest:' + (Join-Path $source 'app.manifest')))
$commonFiles = @((Join-Path $source 'Common.cs'), (Join-Path $build 'PayloadHashes.cs'))
$uninstaller = Join-Path $build 'Uninstall.exe'
& $csc @commonArgs ('/out:' + $uninstaller) @commonFiles (Join-Path $source 'Uninstaller.cs')
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller compilation failed.' }
$candidate = Join-Path $build 'PCL2_Setup.exe'
& $csc @commonArgs ('/out:' + $candidate) ('/resource:' + $payloadPath + ',PCL2.Payload.zip') ('/resource:' + $uninstaller + ',PCL2.Uninstall.exe') @commonFiles (Join-Path $source 'Installer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

# Verify the actual embedded archive rather than only the input files.
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($candidate))
$resource = $assembly.GetManifestResourceStream('PCL2.Payload.zip')
$zip = New-Object IO.Compression.ZipArchive($resource, [IO.Compression.ZipArchiveMode]::Read)
try {
    $expected = @($payloadBytes.Keys) + @('PCL/', 'PCL/Musics/', 'PCL/Pictures/')
    if ($zip.Entries.Count -ne $expected.Count) { throw 'Unexpected payload entry count.' }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($entry in $zip.Entries) {
        if ($expected -cnotcontains $entry.FullName -or -not $seen.Add($entry.FullName)) { throw 'Unexpected or duplicate payload entry.' }
        $memory = New-Object IO.MemoryStream
        $input = $entry.Open()
        try { $input.CopyTo($memory); $bytes = $memory.ToArray(); Assert-CleanBytes $bytes } finally { $input.Dispose(); $memory.Dispose() }
        if ($hashes.Contains($entry.FullName)) {
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
            if ($actual -ne $hashes[$entry.FullName]) { throw 'Embedded payload hash mismatch.' }
        } elseif ($entry.Length -ne 0) { throw 'An asset directory is not empty.' }
    }
} finally { $zip.Dispose(); $resource.Dispose() }
Assert-CleanBytes ([IO.File]::ReadAllBytes($candidate))
Assert-CleanBytes ([IO.File]::ReadAllBytes($uninstaller))
$output = Join-Path $dist 'PCL2_Setup.exe'
Copy-Item -LiteralPath $candidate -Destination $output -Force
$audit = [ordered]@{
    version = $version
    payloadEntries = $expected
    payloadSha256 = $hashes
    installerSha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
    personalValuesChecked = $privateValues.Count
    excludedSensitiveFilesInspected = $sensitiveInputs.Count
    personalValueOccurrences = 0
    setupIni = 'Configured with SystemSystemUpdate:3 to prevent automatic updates'
    excluded = @('LatestLaunch.bat', 'Log*.txt', 'registry dumps', '.minecraft', 'source Setup.ini', 'source Musics contents', 'source Pictures contents')
}
$audit | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $dist 'PCL2_Setup.audit.json') -Encoding UTF8
Write-Output ('Built: ' + $output)
Write-Output ('Verified 8 allowlisted entries; personal-value matches: 0; sensitive values checked: ' + $privateValues.Count)
