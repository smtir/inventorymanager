# Builds InventoryManager-Setup.exe with IExpress (built into Windows).
# The setup contains the single-file app plus the offline .NET Framework 4.8 installer, so nothing is
# downloaded on the customer's PC. .NET is only installed when the PC has no .NET 4.5 or later (Windows 7).
param(
    [Parameter(Mandatory = $true)][string]$Exe,       # the single-file InventoryManager.exe
    [Parameter(Mandatory = $true)][string]$OutFile    # where to put InventoryManager-Setup.exe
)
$ErrorActionPreference = 'Stop'
$here   = $PSScriptRoot
$redist = Join-Path $here 'redist'
$ndp    = Join-Path $redist 'ndp48-x86-x64-allos-enu.exe'
$cer    = Join-Path $redist 'MicRooCerAut2011.cer'
New-Item -ItemType Directory -Force $redist | Out-Null

# ---- redistributables (fetched once, then reused from installer\redist) ----
if (-not (Test-Path $ndp)) {
    Write-Host 'Downloading the offline .NET Framework 4.8 installer (121 MB, one time only)...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest 'https://go.microsoft.com/fwlink/?linkid=2088631' -OutFile $ndp -UseBasicParsing
}
$sig = Get-AuthenticodeSignature $ndp
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike '*O=Microsoft Corporation*') {
    throw ".NET installer signature check failed ($($sig.Status)). Delete $ndp and try again."
}
if (-not (Test-Path $cer)) {
    # Microsoft Root Certificate Authority 2011: signs the .NET 4.8 installer; old offline Windows 7 PCs lack it.
    $root = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Thumbprint -eq '8F43288AD272F3103B6FB1428485EA3014C0BCFE' }
    if (-not $root) { throw 'Microsoft Root Certificate Authority 2011 not found in this PC''s certificate store.' }
    [IO.File]::WriteAllBytes($cer, $root.Export('Cert'))
}

# ---- staging (IExpress does not cope with spaces in paths, so stage under %TEMP%) ----
$stage = Join-Path $env:TEMP 'InventoryManagerSetup\files'
$out   = Join-Path $env:TEMP 'InventoryManagerSetup\out'
foreach ($d in $stage, $out) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Force $d | Out-Null
}
# 8.3 short paths, in case the Windows user name (and so %TEMP%) contains spaces.
$fso   = New-Object -ComObject Scripting.FileSystemObject
$stage = $fso.GetFolder($stage).ShortPath
$out   = $fso.GetFolder($out).ShortPath
$v = (Get-Item $Exe).VersionInfo
$version = '{0}.{1}.{2}' -f $v.FileMajorPart, $v.FileMinorPart, $v.FileBuildPart

Copy-Item $Exe (Join-Path $stage 'InventoryManager.exe')
Copy-Item $ndp, $cer $stage
$textFiles = 'install.cmd', 'uninstall.cmd', 'message.vbs', 'shortcuts.vbs', 'README.txt'
foreach ($name in $textFiles) {
    # cmd.exe needs CRLF line endings, and plain ASCII for these scripts.
    $text = [IO.File]::ReadAllText((Join-Path $here $name)).Replace("`r`n", "`n").Replace("`n", "`r`n").Replace('__VERSION__', $version)
    [IO.File]::WriteAllText((Join-Path $stage $name), $text, [Text.Encoding]::ASCII)
}

$files = @('InventoryManager.exe', (Split-Path $ndp -Leaf), (Split-Path $cer -Leaf)) + $textFiles
$target = Join-Path $out 'InventoryManager-Setup.exe'
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=Install Inventory Manager $version on this computer?
DisplayLicense=
FinishMessage=
TargetName=$target
FriendlyName=Inventory Manager Setup
AppLaunched=cmd.exe /c install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=

"@
for ($i = 0; $i -lt $files.Count; $i++) { $sed += "FILE$i=`"$($files[$i])`"`r`n" }
$sed += "[SourceFiles]`r`nSourceFiles0=$stage\`r`n[SourceFiles0]`r`n"
for ($i = 0; $i -lt $files.Count; $i++) { $sed += "%FILE$i%=`r`n" }
$sedPath = Join-Path $out 'setup.sed'
[IO.File]::WriteAllText($sedPath, $sed.Replace("`r`n", "`n").Replace("`n", "`r`n"), [Text.Encoding]::ASCII)

# 32-bit IExpress gives a 32-bit setup that also starts on 32-bit Windows 7.
$iexpress = Join-Path $env:WINDIR 'SysWOW64\iexpress.exe'
if (-not (Test-Path $iexpress)) { $iexpress = Join-Path $env:WINDIR 'System32\iexpress.exe' }
Write-Host 'Packing the setup with IExpress (about a minute)...'
# IExpress cannot open a quoted SED path, so pass it relative to its own folder.
$p = Start-Process $iexpress -ArgumentList '/N /Q setup.sed' -WorkingDirectory $out -Wait -PassThru
if (-not (Test-Path $target)) { throw "IExpress failed (exit $($p.ExitCode)); see $sedPath" }

Copy-Item $target $OutFile -Force
Write-Host ("Setup built -> {0} ({1:N0} MB)" -f $OutFile, ((Get-Item $OutFile).Length / 1MB))
