@echo off
chcp 65001 >nul
title Escritorio
cd /d "%~dp0"

if not exist ".venv\Scripts\pythonw.exe" (
  echo Preparando el entorno por primera vez. Esto tarda un minuto...
  py -3 -m venv .venv
  if errorlevel 1 goto error
  ".venv\Scripts\python.exe" -m pip install --disable-pip-version-check -r requirements.txt
  if errorlevel 1 goto error
)

start "" ".venv\Scripts\pythonw.exe" escritorio.py
exit /b 0

:error
echo.
echo No se pudo preparar el entorno. Verifique que Python 3.11 o posterior este instalado y que el comando py funcione.
pause
exit /b 1
