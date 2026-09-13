@echo off
setlocal
pushd "%~dp0"

set "FRAMEWORK_DIR=C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FRAMEWORK_DIR%\csc.exe" (
    set "FRAMEWORK_DIR=C:\Windows\Microsoft.NET\Framework\v4.0.30319"
)

if not exist "%FRAMEWORK_DIR%\csc.exe" (
    echo Error: C# compiler csc.exe was not found.
    popd
    exit /b 1
)

echo Compiling CopyToast.exe...
"%FRAMEWORK_DIR%\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /out:"%~dp0CopyToast.exe" /lib:"%FRAMEWORK_DIR%\WPF","%FRAMEWORK_DIR%" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll /r:System.dll /r:System.Core.dll "%~dp0src\CopyToast.cs"

if %ERRORLEVEL% equ 0 (
    echo.
    echo ========================================================
    echo  Successfully built: CopyToast.exe
    echo ========================================================
) else (
    echo.
    echo Build failed with error code %ERRORLEVEL%.
)
popd
