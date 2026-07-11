@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 936 >nul
title DNF专用工具箱 8.0 - 2026-07 客户端适配版

set "COMMAND_MODE=%~1"
if /i "!COMMAND_MODE!"=="--self-test" goto :self_test

for %%I in ("%~dp0.") do set "GAME_DIR=%%~fI"
if not exist "!GAME_DIR!\DNF.exe" (
    echo ============================================================
    echo [错误] 当前目录中没有 DNF.exe。
    echo 请将 DNF专用工具箱8.0.bat 复制到 DNF 游戏根目录后再运行。
    echo 游戏根目录应同时包含 DNF.exe、start 和 Pandora 等文件或目录。
    echo 当前脚本目录：%~dp0
    echo ============================================================
    pause
    goto :end
)

set "TENCENT_ROAMING=%APPDATA%\Tencent"
if /i "!COMMAND_MODE!"=="--status" goto :status

net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WorkingDirectory '%~dp0'"
    exit /b
)

:menu
cls
echo ============================================================
echo DNF专用工具箱 8.0 - 2026-07 客户端适配版
echo 游戏目录：!GAME_DIR!
echo ============================================================
echo 1. 禁用当前仍有效的无用组件
echo 2. 恢复由本工具禁用的组件
echo 3. 清理 DNF 日志和用户缓存
echo 4. 查看禁用状态
echo 5. 修复黑屏连接服务器
echo 6. 退出
echo.
choice /c 123456 /n /m "请选择："
if errorlevel 6 goto :end
if errorlevel 5 goto :fix_connection
if errorlevel 4 goto :status
if errorlevel 3 goto :clean
if errorlevel 2 goto :restore
if errorlevel 1 goto :disable
goto :menu

:disable
call :ensure_game_closed
if errorlevel 1 goto :menu_pause
echo.
echo 正在禁用当前仍存在的组件……

rem 原脚本仍有效的游戏目录项目
call :block_file_at "!GAME_DIR!\Install.dll" "Install.dll" "existing"
call :block_file_at "!GAME_DIR!\TP3Helper.exe" "TP3Helper.exe" "existing"
call :block_directory_at "!GAME_DIR!\TGuard" "游戏目录 TGuard" "existing"

rem 原 TCLS 广告路径已失效；当前客户端对应文件位于 start 目录
call :block_file_at "!GAME_DIR!\start\AdvertDialog.exe" "启动器广告窗口" "existing"
call :block_file_at "!GAME_DIR!\start\AdvertTips.exe" "启动器广告提示" "existing"

rem 2026-07 新增的井盖网页插件
call :block_file_at "!GAME_DIR!\Pandora\cache\archive\9193\gamelet9193GRobot_bin.zip" "井盖（Pandora 9193 / GRobot）" "always"

rem 原脚本中当前仍存在、且不属于下载链路的外部项目
call :block_directory_at "!TENCENT_ROAMING!\AndroidAssist" "AndroidAssist" "existing"
call :block_directory_at "!TENCENT_ROAMING!\AndroidServer" "AndroidServer" "existing"
call :block_directory_at "!TENCENT_ROAMING!\QQDoctor" "QQDoctor" "existing"
call :block_directory_at "!TENCENT_ROAMING!\MiniQBrowser" "MiniQBrowser" "existing"
call :block_directory_at "!TENCENT_ROAMING!\QQPCMgr" "QQPCMgr" "existing"
call :block_directory_at "!TENCENT_ROAMING!\QQPhoneAssistant" "QQPhoneAssistant" "existing"
call :block_directory_at "!TENCENT_ROAMING!\QQPhoneManager" "QQPhoneManager" "existing"
call :block_directory_at "!TENCENT_ROAMING!\TAS" "TAS" "existing"
call :block_directory_at "!TENCENT_ROAMING!\TCLSCore" "TCLSCore" "existing"
call :block_directory_at "!TENCENT_ROAMING!\WebGamePlugin" "WebGamePlugin" "existing"
call :block_file_at "!TENCENT_ROAMING!\Common\gjdatareport.dll" "gjdatareport.dll" "existing"

echo.
echo 操作完成。
echo 未修改 BackgroundDownloader、Tencentdl、TenioDL、TesService、QQDownload、QQMiniDL、DeskUpdate、TXPTOP 或 TXFTN。
goto :menu_pause

:restore
call :ensure_game_closed
if errorlevel 1 goto :menu_pause
echo.
echo 正在恢复由本工具禁用的组件……

call :restore_file_at "!GAME_DIR!\Install.dll" "Install.dll"
call :restore_file_at "!GAME_DIR!\TP3Helper.exe" "TP3Helper.exe"
call :restore_directory_at "!GAME_DIR!\TGuard" "游戏目录 TGuard"
call :restore_file_at "!GAME_DIR!\start\AdvertDialog.exe" "启动器广告窗口"
call :restore_file_at "!GAME_DIR!\start\AdvertTips.exe" "启动器广告提示"
call :restore_file_at "!GAME_DIR!\Pandora\cache\archive\9193\gamelet9193GRobot_bin.zip" "井盖（Pandora 9193 / GRobot）"

call :restore_directory_at "!TENCENT_ROAMING!\AndroidAssist" "AndroidAssist"
call :restore_directory_at "!TENCENT_ROAMING!\AndroidServer" "AndroidServer"
call :restore_directory_at "!TENCENT_ROAMING!\QQDoctor" "QQDoctor"
call :restore_directory_at "!TENCENT_ROAMING!\MiniQBrowser" "MiniQBrowser"
call :restore_directory_at "!TENCENT_ROAMING!\QQPCMgr" "QQPCMgr"
call :restore_directory_at "!TENCENT_ROAMING!\QQPhoneAssistant" "QQPhoneAssistant"
call :restore_directory_at "!TENCENT_ROAMING!\QQPhoneManager" "QQPhoneManager"
call :restore_directory_at "!TENCENT_ROAMING!\TAS" "TAS"
call :restore_directory_at "!TENCENT_ROAMING!\TCLSCore" "TCLSCore"
call :restore_directory_at "!TENCENT_ROAMING!\WebGamePlugin" "WebGamePlugin"
call :restore_file_at "!TENCENT_ROAMING!\Common\gjdatareport.dll" "gjdatareport.dll"

echo.
echo 恢复完成。
goto :menu_pause

:clean
call :ensure_game_closed
if errorlevel 1 goto :menu_pause
echo.
echo 正在清理当前仍有效的日志和用户缓存……
del /f /q "!GAME_DIR!\*_tmp.dat" >nul 2>&1
del /f /q "!GAME_DIR!\debug.log" >nul 2>&1
del /f /q "!GAME_DIR!\gameloader.log" >nul 2>&1
del /f /q "!GAME_DIR!\LagLog.txt" >nul 2>&1
del /f /q "!GAME_DIR!\BugTrace.log" >nul 2>&1
del /f /q "!GAME_DIR!\awesomium.log" >nul 2>&1
del /f /q "!GAME_DIR!\CrashDNF2.cra" >nul 2>&1
del /f /q "!GAME_DIR!\Thread*.*" >nul 2>&1

set "DNF_USER=%USERPROFILE%\AppData\LocalLow\DNF"
set "CFG_BACKUP=%TEMP%\DNF.cfg.%RANDOM%.bak"
if exist "!DNF_USER!\DNF.cfg" copy /y "!DNF_USER!\DNF.cfg" "!CFG_BACKUP!" >nul
if exist "!DNF_USER!\" del /f /s /q "!DNF_USER!\*" >nul 2>&1
if exist "!CFG_BACKUP!" (
    if not exist "!DNF_USER!\" md "!DNF_USER!" >nul 2>&1
    move /y "!CFG_BACKUP!" "!DNF_USER!\DNF.cfg" >nul
)
del /f /q "%APPDATA%\Tencent\Logs\dnf.tlg" >nul 2>&1
del /f /q "%APPDATA%\Tencent\QQCall*.exe" >nul 2>&1
echo 清理完成。WeGame 更新包和所有下载缓存均未删除。
goto :menu_pause

:status
echo.
call :show_file_status "!GAME_DIR!\Install.dll" "Install.dll"
call :show_file_status "!GAME_DIR!\TP3Helper.exe" "TP3Helper.exe"
call :show_directory_status "!GAME_DIR!\TGuard" "游戏目录 TGuard"
call :show_file_status "!GAME_DIR!\start\AdvertDialog.exe" "启动器广告窗口"
call :show_file_status "!GAME_DIR!\start\AdvertTips.exe" "启动器广告提示"
call :show_file_status "!GAME_DIR!\Pandora\cache\archive\9193\gamelet9193GRobot_bin.zip" "井盖（Pandora 9193 / GRobot）"
call :show_directory_status "!TENCENT_ROAMING!\AndroidAssist" "AndroidAssist"
call :show_directory_status "!TENCENT_ROAMING!\AndroidServer" "AndroidServer"
call :show_directory_status "!TENCENT_ROAMING!\QQDoctor" "QQDoctor"
call :show_directory_status "!TENCENT_ROAMING!\MiniQBrowser" "MiniQBrowser"
call :show_directory_status "!TENCENT_ROAMING!\QQPCMgr" "QQPCMgr"
call :show_directory_status "!TENCENT_ROAMING!\QQPhoneAssistant" "QQPhoneAssistant"
call :show_directory_status "!TENCENT_ROAMING!\QQPhoneManager" "QQPhoneManager"
call :show_directory_status "!TENCENT_ROAMING!\TAS" "TAS"
call :show_directory_status "!TENCENT_ROAMING!\TCLSCore" "TCLSCore"
call :show_directory_status "!TENCENT_ROAMING!\WebGamePlugin" "WebGamePlugin"
call :show_file_status "!TENCENT_ROAMING!\Common\gjdatareport.dll" "gjdatareport.dll"
if /i "!COMMAND_MODE!"=="--status" goto :end
goto :menu_pause

:fix_connection
call :ensure_game_closed
if errorlevel 1 goto :menu_pause
set "DNF_USER=%USERPROFILE%\AppData\LocalLow\DNF"
if exist "!DNF_USER!\" del /f /s /q "!DNF_USER!\*" >nul 2>&1
echo 已清空 DNF 用户缓存。
goto :menu_pause

:ensure_game_closed
tasklist.exe /fi "ImageName eq DNF.exe" /nh 2>nul | find.exe /i "DNF.exe" >nul
if not errorlevel 1 (
    echo.
    echo [提示] 已记录当前游戏目录，请先关闭 DNF 后再执行文件操作。
    exit /b 1
)
exit /b 0

:block_file_at
set "TARGET=%~1"
set "DISPLAY_NAME=%~2"
set "CREATE_MODE=%~3"
set "BACKUP=!TARGET!.dnf-toolbox-disabled"
if exist "!TARGET!\" (
    echo [已禁用] !DISPLAY_NAME!
    exit /b 0
)
if not exist "!TARGET!" (
    if /i not "!CREATE_MODE!"=="always" (
        echo [未安装] !DISPLAY_NAME!
        exit /b 0
    )
) else (
    if exist "!BACKUP!" (
        echo [冲突] !DISPLAY_NAME! 已有备份，未覆盖。
        exit /b 1
    )
    move /y "!TARGET!" "!BACKUP!" >nul || exit /b 1
)
for %%I in ("!TARGET!") do if not exist "%%~dpI" md "%%~dpI" >nul 2>&1
md "!TARGET!" >nul 2>&1
if not exist "!TARGET!\" exit /b 1
>"!TARGET!\.blocked-by-dnf-toolbox" echo Blocked at %date% %time%
echo [已禁用] !DISPLAY_NAME!
exit /b 0

:restore_file_at
set "TARGET=%~1"
set "DISPLAY_NAME=%~2"
set "BACKUP=!TARGET!.dnf-toolbox-disabled"
if exist "!TARGET!\.blocked-by-dnf-toolbox" rd /s /q "!TARGET!"
if exist "!BACKUP!" (
    if exist "!TARGET!" (
        echo [冲突] !DISPLAY_NAME! 的原路径已被占用。
        exit /b 1
    )
    move /y "!BACKUP!" "!TARGET!" >nul || exit /b 1
    echo [已恢复] !DISPLAY_NAME!
) else (
    echo [无 8.0 备份] !DISPLAY_NAME!
)
exit /b 0

:block_directory_at
set "TARGET=%~1"
set "DISPLAY_NAME=%~2"
set "CREATE_MODE=%~3"
set "BACKUP=!TARGET!.dnf-toolbox-disabled"
set "MARKER=!TARGET!.blocked-by-dnf-toolbox"
if exist "!MARKER!" (
    echo [已禁用] !DISPLAY_NAME!
    exit /b 0
)
if exist "!TARGET!" if not exist "!TARGET!\" (
    echo [已禁用] !DISPLAY_NAME!
    exit /b 0
)
if not exist "!TARGET!\" (
    if /i not "!CREATE_MODE!"=="always" (
        echo [未安装] !DISPLAY_NAME!
        exit /b 0
    )
) else (
    if exist "!BACKUP!\" (
        echo [冲突] !DISPLAY_NAME! 已有备份，未覆盖。
        exit /b 1
    )
    move /y "!TARGET!" "!BACKUP!" >nul || exit /b 1
)
for %%I in ("!TARGET!") do if not exist "%%~dpI" md "%%~dpI" >nul 2>&1
type nul > "!TARGET!"
>"!MARKER!" echo Blocked at %date% %time%
echo [已禁用] !DISPLAY_NAME!
exit /b 0

:restore_directory_at
set "TARGET=%~1"
set "DISPLAY_NAME=%~2"
set "BACKUP=!TARGET!.dnf-toolbox-disabled"
set "MARKER=!TARGET!.blocked-by-dnf-toolbox"
if exist "!MARKER!" (
    del /f /q "!TARGET!" >nul 2>&1
    del /f /q "!MARKER!" >nul 2>&1
)
if exist "!TARGET!" if not exist "!TARGET!\" if not exist "!BACKUP!\" (
    echo [无 8.0 备份] !DISPLAY_NAME!
    exit /b 0
)
if exist "!BACKUP!\" (
    if exist "!TARGET!" (
        echo [冲突] !DISPLAY_NAME! 的原路径已被占用。
        exit /b 1
    )
    move /y "!BACKUP!" "!TARGET!" >nul || exit /b 1
    echo [已恢复] !DISPLAY_NAME!
) else (
    echo [无备份] !DISPLAY_NAME!
)
exit /b 0

:show_file_status
set "TARGET=%~1"
if exist "!TARGET!\.blocked-by-dnf-toolbox" (
    echo [已禁用] %~2
) else if exist "!TARGET!\" (
    echo [已禁用] %~2
) else if exist "!TARGET!" (
    echo [正常] %~2
) else (
    echo [未安装] %~2
)
exit /b 0

:show_directory_status
set "TARGET=%~1"
if exist "!TARGET!.blocked-by-dnf-toolbox" (
    echo [已禁用] %~2
) else if exist "!TARGET!\" (
    echo [正常] %~2
) else if exist "!TARGET!" (
    echo [已禁用] %~2
) else (
    echo [未安装] %~2
)
exit /b 0

:self_test
set "TEST_ROOT=%TEMP%\DNFToolboxSelfTest_%RANDOM%_%RANDOM%"
set "TEST_FILE=!TEST_ROOT!\files\sample.bin"
set "TEST_DIR=!TEST_ROOT!\folders\sample"
md "!TEST_ROOT!\files" >nul 2>&1
md "!TEST_DIR!" >nul 2>&1
>"!TEST_FILE!" echo sample-payload
>"!TEST_DIR!\payload.txt" echo directory-payload
call :block_file_at "!TEST_FILE!" "self-test file" "existing" >nul
if not exist "!TEST_FILE!\.blocked-by-dnf-toolbox" goto :self_test_fail
if not exist "!TEST_FILE!.dnf-toolbox-disabled" goto :self_test_fail
call :block_directory_at "!TEST_DIR!" "self-test directory" "existing" >nul
if not exist "!TEST_DIR!.blocked-by-dnf-toolbox" goto :self_test_fail
if not exist "!TEST_DIR!.dnf-toolbox-disabled\payload.txt" goto :self_test_fail
call :restore_file_at "!TEST_FILE!" "self-test file" >nul
call :restore_directory_at "!TEST_DIR!" "self-test directory" >nul
findstr.exe /x /c:"sample-payload" "!TEST_FILE!" >nul || goto :self_test_fail
findstr.exe /x /c:"directory-payload" "!TEST_DIR!\payload.txt" >nul || goto :self_test_fail
rd /s /q "!TEST_ROOT!"
echo SELF-TEST PASSED: 8.0 file and directory disable/restore are symmetric.
endlocal
exit /b 0

:self_test_fail
rd /s /q "!TEST_ROOT!" >nul 2>&1
echo SELF-TEST FAILED.
endlocal
exit /b 1

:menu_pause
echo.
pause
goto :menu

:end
endlocal
exit /b
