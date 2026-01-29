@echo off
set NODE=%1
set NODEDIR=%2
set PORT=%3
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\CROM" ^
		 -c "mount f E:\UTILS" ^
		 -c "f:\bnu170\bnu /L:0=38400 /F" ^
		 -c "REM copy d:\door.sys E:\ " ^
         -c "e:" ^
		 -c "CROM /N%NODE% /PD: " ^
		 -c "EXIT"