@echo off
setlocal
pushd "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /win32icon:app.ico /win32manifest:app.manifest /resource:app.config,AppleControl.Dpi.config /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /out:"Apple Display Control.exe" App.cs Hid.cs SystemControls.cs QuickBrightness.cs Updates.cs Theme.cs Dpi.cs AssemblyInfo.cs
set result=%errorlevel%
popd
exit /b %result%
