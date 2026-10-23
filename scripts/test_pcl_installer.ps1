[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$build = Join-Path $repo 'build\pcl2'
$source = Join-Path $repo 'installer\pcl2'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $build 'PclInstallerTests.exe'
if (-not (Test-Path -LiteralPath (Join-Path $repo 'dist\PCL2_Setup.exe'))) { throw 'Build the installer first.' }
# Compile the production engine and form with workspace-only integration doubles.
& $csc /nologo /target:exe /main:PclInstallerTests /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll ('/out:' + $exe) ('/resource:' + (Join-Path $build 'payload.zip') + ',PCL2.Payload.zip') ('/resource:' + (Join-Path $build 'Uninstall.exe') + ',PCL2.Uninstall.exe') (Join-Path $source 'Common.cs') (Join-Path $source 'Installer.cs') (Join-Path $build 'PayloadHashes.cs') (Join-Path $repo 'test\PclInstallerTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
$run = Join-Path $build ('tests-' + [Guid]::NewGuid().ToString('N'))
& $exe $run
if ($LASTEXITCODE -ne 0) { throw 'Installer tests failed.' }
Copy-Item -LiteralPath (Join-Path $run 'installer-preview.png') -Destination (Join-Path $build 'installer-preview.png') -Force

# Prove that a contaminated allowlisted input cannot replace the verified output.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fixture = Join-Path $run 'privacy-rejection'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $build 'payload.zip'))
try {
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith('/') -or $entry.FullName -eq 'PCL/Setup.ini') { continue }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $fixture ([IO.Path]::GetFileName($entry.FullName))))
    }
} finally { $zip.Dispose() }
[IO.File]::AppendAllText((Join-Path $fixture 'JavaWrapper.jar'), 'LeDestin11')
$setup = Join-Path $repo 'dist\PCL2_Setup.exe'
$audit = Join-Path $repo 'dist\PCL2_Setup.audit.json'
$beforeExe = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
$beforeAudit = (Get-FileHash -LiteralPath $audit -Algorithm SHA256).Hash
$buildCommand = "& '$((Join-Path $PSScriptRoot 'build_pcl_installer.ps1'))' -SourceExe '$((Join-Path $fixture 'Plain Craft Launcher 2.exe'))' -RuntimeDirectory '$fixture'"
$arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $buildCommand)
$stdout = Join-Path $fixture 'stdout.txt'; $stderr = Join-Path $fixture 'stderr.txt'
$process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
if ($process.ExitCode -eq 0) { throw 'Privacy rejection test accepted a contaminated payload.' }
$output = [IO.File]::ReadAllText($stdout) + [IO.File]::ReadAllText($stderr)
if ($output -notmatch 'Privacy verification failed' -or $output.Contains('LeDestin11')) { throw 'Privacy rejection did not fail safely.' }
if ($beforeExe -ne (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -or $beforeAudit -ne (Get-FileHash -LiteralPath $audit -Algorithm SHA256).Hash) { throw 'Rejected build modified the verified output.' }
Write-Output 'PASS Contaminated payload rejected without leaking the value or replacing verified output'
Write-Output 'All 14 checks passed (13 compiled runtime tests + 1 privacy rejection test).'
Write-Output ('Test artifacts: ' + $run)
