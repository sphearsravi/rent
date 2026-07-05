@echo off
echo Killing all dotnet processes...
taskkill /F /IM dotnet.exe /T 2>nul
taskkill /F /IM RentCollection.Web.exe /T 2>nul
timeout /t 3 /nobreak >nul

echo Starting application...
cd /d C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
dotnet run
