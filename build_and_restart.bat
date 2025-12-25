@echo off
echo Building CompanionAI v2.2...

:: Build
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "c:\Users\veria\Downloads\CompanionAI_v2.2\CompanionAI_v2.2.csproj" /p:Configuration=Release /v:minimal /nologo

if %ERRORLEVEL% NEQ 0 (
    echo Build FAILED!
    pause
    exit /b 1
)

echo Build succeeded!

:: Deploy
copy /Y "c:\Users\veria\Downloads\CompanionAI_v2.2\bin\Release\net481\CompanionAI_v2.2.dll" "C:\Users\veria\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\CompanionAI_v2.2\CompanionAI_v2.2.dll"
copy /Y "c:\Users\veria\Downloads\CompanionAI_v2.2\Info.json" "C:\Users\veria\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\CompanionAI_v2.2\Info.json"

echo Deployed!

:: Kill game if running
taskkill /F /IM "WH40KRT.exe" 2>nul
if %ERRORLEVEL% EQU 0 (
    echo Game closed. Waiting 2 seconds...
    timeout /t 2 /nobreak >nul
)

:: Start game
echo Starting game...
start "" "C:\Program Files (x86)\Steam\steamapps\common\Warhammer 40,000 Rogue Trader\WH40KRT.exe"

echo Done!
