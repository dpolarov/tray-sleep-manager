@echo off
chcp 65001 >nul
echo ========================================
echo Обычная сборка (для разработки)
echo EXE + DLL файлы
echo ========================================
echo.

dotnet build -c Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo ✓ Сборка успешна!
    echo ========================================
    echo.
    echo Результат: bin\Release\net8.0-windows\win-x64\
    echo.
    explorer bin\Release\net8.0-windows\win-x64
) else (
    echo.
    echo ========================================
    echo ✗ Ошибка сборки!
    echo ========================================
    pause
    exit /b 1
)

pause
