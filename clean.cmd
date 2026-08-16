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

if exist SleepMngr.Tests\bin (
    echo Удаление SleepMngr.Tests\bin...
    rmdir /s /q SleepMngr.Tests\bin
)

if exist SleepMngr.Tests\obj (
    echo Удаление SleepMngr.Tests\obj...
    rmdir /s /q SleepMngr.Tests\obj
)

echo.
echo ========================================
echo ✓ Очистка завершена!
echo ========================================
echo.

pause
