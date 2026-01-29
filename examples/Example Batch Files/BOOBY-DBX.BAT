@echo off
set NODE=%1
set NODEDIR=%2
set PORT=%3
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\BOOBY " ^
		 -c "mount f E:\UTILS" ^
		 -c "f:\bnu170\bnu /L:0=57600 /F" ^
		 -c "REM Copy d:\DOOR.SYS E:\ " ^
         -c "e:" ^
		 -c "BOOBYTRP.EXE BOOBYTRP.CFG FOSSIL " ^
		 -c "EXIT"