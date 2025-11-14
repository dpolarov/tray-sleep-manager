@echo off
chcp 65001 >nul
echo ========================================
echo Очистка файлов сборки
echo ========================================
echo.

if exist bin (
    echo Удаление папки bin...
    rmdir /s /q bin
)

if exist obj (
    echo Удаление папки obj...
    rmdir /s /q obj
)

echo.
echo ========================================
echo ✓ Очистка завершена!
echo ========================================
echo.

pause
