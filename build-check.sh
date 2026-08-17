#!/bin/bash
cd /c/work/tray
echo "=== Debug Build ==="
dotnet build -c Debug -v minimal 2>&1
echo ""
echo "=== Release Build ==="
dotnet build -c Release -v minimal 2>&1
