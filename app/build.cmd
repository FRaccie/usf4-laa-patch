@echo off
cd /d "%~dp0"
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:winexe /out:LaaPatch.exe /win32manifest:app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll LaaPatch.cs
