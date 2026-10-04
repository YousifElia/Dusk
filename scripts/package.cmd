@echo off
rem Builds Dusk and packages a release zip into dist\.
rem Usage: scripts\package.cmd [version]   (for example: scripts\package.cmd v1.0.0)
setlocal
cd /d "%~dp0.."
set VERSION=%1
if "%VERSION%"=="" set VERSION=v1.0.0

call build.cmd || exit /b 1
bin\DuskTests.exe || exit /b 1

if not exist dist mkdir dist
if exist staging rmdir /s /q staging
mkdir staging
copy /y bin\Dusk.exe staging >nul
copy /y README.md staging >nul
copy /y LICENSE staging >nul
copy /y CHANGELOG.md staging >nul
copy /y fonts\*-OFL.txt staging >nul

powershell -NoProfile -Command "Compress-Archive staging\* 'dist\Dusk-%VERSION%-win.zip' -Force; Get-FileHash 'dist\Dusk-%VERSION%-win.zip' -Algorithm SHA256 | ForEach-Object { \"$($_.Hash)  Dusk-%VERSION%-win.zip\" } | Out-File dist\SHA256SUMS.txt -Encoding ascii"
rmdir /s /q staging
echo Packaged dist\Dusk-%VERSION%-win.zip
