@echo off
setlocal enabledelayedexpansion

set "FRAMEWORK_BASE=C:\Windows\Microsoft.NET\Framework64"
set "VBC="

rem Duyet tu 4.8 xuong 4.0, lay phien ban cao nhat duoc cai
for %%V in (4.8 4.7.2 4.7.1 4.7 4.6.2 4.6.1 4.6 4.5.2 4.5.1 4.5 4.0) do (
    if "!VBC!"=="" (
        for /d %%D in ("%FRAMEWORK_BASE%\v%%V*") do (
            if exist "%%D\vbc.exe" (
                set "VBC=%%D\vbc.exe"
            )
        )
    )
)

rem Neu khong tim duoc thi thu Framework 32-bit
if "!VBC!"=="" (
    set "FRAMEWORK_BASE=C:\Windows\Microsoft.NET\Framework"
    for %%V in (4.8 4.7.2 4.7.1 4.7 4.6.2 4.6.1 4.6 4.5.2 4.5.1 4.5 4.0) do (
        if "!VBC!"=="" (
            for /d %%D in ("%FRAMEWORK_BASE%\v%%V*") do (
                if exist "%%D\vbc.exe" (
                    set "VBC=%%D\vbc.exe"
                )
            )
        )
    )
)

if "!VBC!"=="" (
    echo [ERROR] Khong tim thay vbc.exe cua .NET Framework 4.x
    exit /b 1
)

echo [INFO] Dung compiler: !VBC!

"!VBC!" ^
/target:winexe ^
/optionstrict+ ^
/utf8output ^
/r:System.dll ^
/r:System.Windows.Forms.dll ^
/r:System.Drawing.dll ^
/optimize+ ^
/platform:x86 ^
/out:%cd%\PakChrome.exe ^
"%cd%\PakChrome.vb"

endlocal
