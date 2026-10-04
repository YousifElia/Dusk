@echo off
setlocal
rem Builds Dusk as a 32-bit app with the C# compiler that ships with Windows (.NET Framework 4.8.1). No SDK needed.
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set OPTS=-nologo -optimize+ -platform:x86 -r:System.Drawing.dll
set SRC=src\*.cs src\Ui\*.cs
set FONTS=-resource:fonts\HankenGrotesk-400.ttf,Dusk.Fonts.HankenGrotesk-400.ttf -resource:fonts\HankenGrotesk-500.ttf,Dusk.Fonts.HankenGrotesk-500.ttf -resource:fonts\HankenGrotesk-600.ttf,Dusk.Fonts.HankenGrotesk-600.ttf -resource:fonts\InstrumentSerif-400.ttf,Dusk.Fonts.InstrumentSerif-400.ttf

cd /d "%~dp0"
if not exist bin mkdir bin

rem 1. The icon
"%CSC%" %OPTS% -target:exe -out:bin\IconGen.exe -main:Dusk.IconGen %SRC% tools\IconGen.cs || exit /b 1
bin\IconGen.exe bin\dusk.ico || exit /b 1
del bin\IconGen.exe

rem 2. The app
"%CSC%" %OPTS% -target:winexe -out:bin\Dusk.exe -win32icon:bin\dusk.ico %FONTS% -main:Dusk.App %SRC% || exit /b 1

rem 3. Tests
"%CSC%" %OPTS% -target:exe -out:bin\DuskTests.exe %FONTS% -main:Dusk.Tests %SRC% tools\Tests.cs || exit /b 1

echo Built bin\Dusk.exe
