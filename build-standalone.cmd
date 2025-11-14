@echo off
chcp 65001 >nul
echo ========================================
echo Сборка автономного EXE (~65 МБ)
echo Не требует .NET Runtime
echo ========================================
echo.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo ✓ Сборка успешна!
    echo ========================================
    echo.
    echo Результат: bin\Release\net8.0-windows\win-x64\publish\LidSleepManager.exe
    echo.
    explorer bin\Release\net8.0-windows\win-x64\publish
) else (
    echo.
    echo ========================================
    echo ✗ Ошибка сборки!
    echo ========================================
    pause
    exit /b 1
)

pause
