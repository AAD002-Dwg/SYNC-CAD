@echo off
echo ==========================================
echo INICIANDO SERVICIOS CAD SYNC
echo ==========================================

:: Iniciar Servidor
start "CAD SYNC SERVER" cmd /k "cd server && npm start"

:: Esperar un momento para que el servidor inicie
timeout /t 3

:: Iniciar Cliente
start "CAD SYNC WEB" cmd /k "cd client && npm run dev"

echo ==========================================
echo Servicios iniciados correctamente.
echo Servidor: http://localhost:3001
echo Web: Ver consola para URL de Vite
echo ==========================================
pause
