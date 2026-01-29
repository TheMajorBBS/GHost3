@echo off
set NODEDIR=%1
set PORT=%2
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\BRE" ^
		 -c "mount f E:\UTILS" ^
		 -c "f:\bnu170\bnu" ^
         -c "e:" ^
		 -c "srdoor d:\ " ^
         -c "bre.exe " ^
		 -c "exit"
		 
REM @ECHO OFF
REM C:
REM CD \DOORS\BRE
REM SRDOOR
REM BRE
REM C:
REM CD \doors\bre
REM EXIT
