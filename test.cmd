@echo off
setlocal
pushd "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:AppleControl.Tests /platform:x64 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll /r:System.Web.Extensions.dll /out:Tests.exe App.cs Hid.cs SystemControls.cs QuickBrightness.cs Updates.cs AssemblyInfo.cs Tests.cs
if errorlevel 1 exit /b 1
Tests.exe %*
set result=%errorlevel%
popd
exit /b %result%
