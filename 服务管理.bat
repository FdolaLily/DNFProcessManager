@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 936 >nul
title AutoManagerProcess 服务管理

set "SERVICE_NAME=AutoManagerProcess"
set "DISPLAY_NAME=Auto Manager Process"
set "EXE_PATH=%~dp0AutoManagerProcess.exe"

if /i "%~1"=="--self-test" goto :self_test

net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WorkingDirectory '%~dp0'"
    exit /b
)

:menu
cls
echo ============================================================
echo AutoManagerProcess Windows 服务管理
echo 程序路径：!EXE_PATH!
echo ============================================================
echo 1. 安装或更新并启动服务
echo 2. 停止服务
echo 3. 重启服务
echo 4. 查看完整状态
echo 5. 卸载服务
echo 6. 退出
echo.
choice /c 123456 /n /m "请选择："
if errorlevel 6 goto :end
if errorlevel 5 goto :uninstall
if errorlevel 4 goto :status
if errorlevel 3 goto :restart
if errorlevel 2 goto :stop
if errorlevel 1 goto :install
goto :menu

:install
if not exist "!EXE_PATH!" (
    echo.
    echo [错误] 找不到 !EXE_PATH!
    echo 请将本脚本与发布输出中的 AutoManagerProcess.exe 放在同一目录。
    goto :pause_menu
)
if not exist "%~dp0DNFAutoFire.exe" echo [警告] 未找到 DNFAutoFire.exe，游戏启动时不会自动运行连发程序。
if not exist "%~dp0config.ini" echo [警告] 未找到 config.ini，连发程序配置不完整。

set "SERVICE_EXISTS=0"
sc.exe query "!SERVICE_NAME!" >nul 2>&1
if not errorlevel 1 (
    set "SERVICE_EXISTS=1"
    sc.exe stop "!SERVICE_NAME!" >nul 2>&1
    for /l %%I in (1,1,20) do (
        sc.exe query "!SERVICE_NAME!" | find.exe /i "STOPPED" >nul && goto :install_config
        timeout.exe /t 1 /nobreak >nul
    )
    echo [失败] 等待旧服务停止超时，未更新服务。
    goto :pause_menu
)

:install_config
if "!SERVICE_EXISTS!"=="1" (
    sc.exe config "!SERVICE_NAME!" binPath= "\"!EXE_PATH!\"" start= auto obj= LocalSystem DisplayName= "!DISPLAY_NAME!"
) else (
    sc.exe create "!SERVICE_NAME!" binPath= "\"!EXE_PATH!\"" start= auto obj= LocalSystem DisplayName= "!DISPLAY_NAME!"
)
if errorlevel 1 (
    echo [失败] 服务安装或更新失败。
) else (
    sc.exe description "!SERVICE_NAME!" "DNF process automation and resource manager"
    sc.exe start "!SERVICE_NAME!"
    if errorlevel 1 (
        echo [失败] 服务已安装或更新，但启动失败，请查看完整状态。
    ) else (
        echo [完成] 服务已安装或更新，并已启动。
    )
)
goto :pause_menu

:stop
sc.exe query "!SERVICE_NAME!" >nul 2>&1
if errorlevel 1 (
    echo [错误] 服务尚未安装。
) else (
    sc.exe stop "!SERVICE_NAME!"
)
goto :pause_menu

:restart
sc.exe query "!SERVICE_NAME!" >nul 2>&1
if errorlevel 1 (
    echo [错误] 服务尚未安装。
    goto :pause_menu
)
sc.exe stop "!SERVICE_NAME!" >nul 2>&1
for /l %%I in (1,1,20) do (
    sc.exe query "!SERVICE_NAME!" | find.exe /i "STOPPED" >nul && goto :restart_start
    timeout.exe /t 1 /nobreak >nul
)
echo [警告] 等待服务停止超时，将尝试启动。
:restart_start
sc.exe start "!SERVICE_NAME!"
goto :pause_menu

:status
echo.
sc.exe queryex "!SERVICE_NAME!"
if errorlevel 1 (
    echo [状态] 服务未安装。
) else (
    echo.
    sc.exe qc "!SERVICE_NAME!"
)
goto :pause_menu

:uninstall
sc.exe query "!SERVICE_NAME!" >nul 2>&1
if errorlevel 1 (
    echo [状态] 服务未安装。
    goto :pause_menu
)
sc.exe stop "!SERVICE_NAME!" >nul 2>&1
for /l %%I in (1,1,20) do (
    sc.exe query "!SERVICE_NAME!" | find.exe /i "STOPPED" >nul && goto :uninstall_delete
    timeout.exe /t 1 /nobreak >nul
)
:uninstall_delete
sc.exe delete "!SERVICE_NAME!"
if errorlevel 1 (
    echo [失败] 服务卸载失败。
) else (
    echo [完成] 服务已卸载，程序文件未删除。
)
goto :pause_menu

:pause_menu
echo.
pause
goto :menu

:self_test
set "FAILED=0"
for %%F in ("AutoManagerProcess.exe" "appsettings.json" "DNFAutoFire.exe" "config.ini" "DNF专用工具箱8.0.bat" "服务管理.bat") do (
    if not exist "%~dp0%%~F" (
        echo MISSING: %%~F
        set "FAILED=1"
    )
)
sc.exe query EventLog >nul 2>&1 || set "FAILED=1"
if "!FAILED!"=="1" (
    echo SELF-TEST FAILED.
    endlocal
    exit /b 1
)
echo SELF-TEST PASSED: self-contained single-file output and service prerequisites are present.
echo SERVICE COMMAND: sc.exe create "!SERVICE_NAME!" binPath= "\"!EXE_PATH!\"" start= auto obj= LocalSystem
endlocal
exit /b 0

:end
endlocal
exit /b
