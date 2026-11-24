$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
$fx  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'

if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

# Generate clean SysTelemetry.ico into dist/build staging so exe has an embedded icon
Add-Type -AssemblyName System.Drawing
$icoPath = Join-Path $root 'app.ico'
$bmp = New-Object System.Drawing.Bitmap(32, 32)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(240, 18, 22, 30))
$g.FillRectangle($bg, 2, 2, 28, 28)
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 56, 189, 248), 2.5)
$pts = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF(5, 18)),
    (New-Object System.Drawing.PointF(10, 18)),
    (New-Object System.Drawing.PointF(13, 9)),
    (New-Object System.Drawing.PointF(17, 23)),
    (New-Object System.Drawing.PointF(21, 13)),
    (New-Object System.Drawing.PointF(24, 18)),
    (New-Object System.Drawing.PointF(27, 18))
)
$g.DrawLines($pen, $pts)
$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.File]::Create($icoPath)
$icon.Save($fs)
$fs.Close()
$g.Dispose(); $bmp.Dispose(); $bg.Dispose(); $pen.Dispose()

$sources = @(
    (Join-Path $root 'App.cs'),
    (Join-Path $root 'Models\AppConfig.cs'),
    (Join-Path $root 'Models\TrafficStats.cs'),
    (Join-Path $root 'Models\TelemetrySnapshot.cs'),
    (Join-Path $root 'Interop\NativeMethods.cs'),
    (Join-Path $root 'Services\ThemeHelper.cs'),
    (Join-Path $root 'Services\AutoStartService.cs'),
    (Join-Path $root 'Services\TrafficMonitorService.cs'),
    (Join-Path $root 'Services\TelemetryService.cs'),
    (Join-Path $root 'UI\IconFactory.cs'),
    (Join-Path $root 'UI\WidgetVisualBuilder.cs'),
    (Join-Path $root 'UI\TaskbarBarWindow.cs'),
    (Join-Path $root 'UI\DesktopOverlayWindow.cs'),
    (Join-Path $root 'UI\TelemetryPopoverWindow.cs')
)

$outExe = Join-Path $dist 'SysTelemetry.exe'

$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    "/out:$outExe",
    "/win32manifest:$(Join-Path $root 'app.manifest')",
    "/win32icon:$icoPath",
    "/r:$wpf\PresentationFramework.dll",
    "/r:$wpf\PresentationCore.dll",
    "/r:$wpf\WindowsBase.dll",
    "/r:$fx\System.Xaml.dll",
    "/r:$fx\System.Runtime.Serialization.dll",
    "/r:$fx\System.Management.dll",
    "/r:$fx\System.Windows.Forms.dll",
    "/r:$fx\System.Drawing.dll",
    "/r:$(Join-Path $root 'lib\LibreHardwareMonitorLib.dll')",
    "/r:$(Join-Path $root 'lib\HidSharp.dll')"
) + $sources

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

Copy-Item (Join-Path $root 'lib\LibreHardwareMonitorLib.dll') $dist -Force
Copy-Item (Join-Path $root 'lib\HidSharp.dll') $dist -Force

Get-ChildItem $dist | Select-Object Name, Length
