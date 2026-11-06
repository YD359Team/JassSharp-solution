@echo off
setlocal
cd /d "%~dp0"

echo === JASS# compiler ===
dotnet build src\JassSharp.Compiler\JassSharp.Compiler.csproj -c Release
if errorlevel 1 goto :fail

echo.
echo === VSIX ===
cd src\jasssharp-vscode
if not exist node_modules (
    call npm install
    if errorlevel 1 goto :fail
)
call npm run package
if errorlevel 1 goto :fail

echo.
echo === done ===
echo compiler: src\JassSharp.Compiler\bin\Release\
for %%f in (*.vsix) do echo vsix: %%~ff
cd /d "%~dp0"
goto :eof

:fail
echo.
echo FAILED
cd /d "%~dp0"
exit /b 1
