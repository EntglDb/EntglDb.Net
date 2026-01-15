@echo off
setlocal

echo ==========================================
echo      EntglDb Cross-Platform Cluster
echo ==========================================

echo.
echo [1/3] Building projects...
dotnet build EntglDb.Sample.Console/EntglDb.Sample.Console.csproj --nologo -v q
dotnet build EntglDb.Test.Avalonia/EntglDb.Test.Avalonia.csproj --nologo -v q

if %ERRORLEVEL% neq 0 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo [2/3] Starting Console Node (node-console-1)...
start "EntglDb Console Node 1" dotnet run --project EntglDb.Sample.Console/EntglDb.Sample.Console.csproj --no-build -- node-console-1 5001

echo.
echo [3/3] Starting Avalonia Client (test-node-avalonia)...
start "EntglDb Avalonia Client" dotnet run --project EntglDb.Test.Avalonia/EntglDb.Test.Avalonia.csproj --no-build

echo.
echo ==========================================
echo  Cluster launched! 
echo  - Console Node 1 (Port 5001)
echo  - Avalonia Client (Configured in appsettings)
echo.
echo  Use the Console window to 'l'ist peers.
echo ==========================================
pause
