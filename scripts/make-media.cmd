@echo off
rem Renders the screenshots and animated demos used by the README.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make-media.ps1"
