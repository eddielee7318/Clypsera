$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = Join-Path (Split-Path (Split-Path $projectRoot -Parent) -Parent) 'outputs'
$portableRoot = Join-Path $outputRoot 'Clypsera-v26-portable'
$webViewRoot = Join-Path (Split-Path $projectRoot -Parent) 'deps\webview2'
$webViewLib = Join-Path $webViewRoot 'lib\net462'
$guidePath = Join-Path (Split-Path $outputRoot -Parent) 'output\pdf\Clypsera-v26-零基础使用手册.pdf'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '找不到 .NET Framework C# 编译器。' }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $portableRoot | Out-Null
$sources = Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
$iconPath = Join-Path $projectRoot 'Assets\Clypsera.ico'
& $compiler /nologo /target:winexe /optimize+ /platform:x64 /win32icon:"$iconPath" /out:"$portableRoot\Clypsera.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll /reference:"$webViewLib\Microsoft.Web.WebView2.Core.dll" /reference:"$webViewLib\Microsoft.Web.WebView2.WinForms.dll" $sources
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Copy-Item -LiteralPath "$webViewLib\Microsoft.Web.WebView2.Core.dll" -Destination $portableRoot -Force
Copy-Item -LiteralPath "$webViewLib\Microsoft.Web.WebView2.WinForms.dll" -Destination $portableRoot -Force
Copy-Item -LiteralPath "$webViewRoot\runtimes\win-x64\native\WebView2Loader.dll" -Destination $portableRoot -Force
Copy-Item -LiteralPath "$webViewRoot\LICENSE.txt" -Destination (Join-Path $portableRoot 'WebView2-LICENSE.txt') -Force
Copy-Item -LiteralPath "$webViewRoot\NOTICE.txt" -Destination (Join-Path $portableRoot 'WebView2-NOTICE.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $portableRoot '使用说明.md') -Force
if (-not (Test-Path -LiteralPath $guidePath)) { throw "缺少零基础使用手册：$guidePath" }
Copy-Item -LiteralPath $guidePath -Destination (Join-Path $portableRoot 'Clypsera-v26-零基础使用手册.pdf') -Force
Write-Host "Built: $portableRoot\Clypsera.exe"
