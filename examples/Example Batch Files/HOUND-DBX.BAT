@echo off
set NODE=%1
set NODEDIR=%2
set PORT=%3
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\HOUND" ^
		 -c "mount f E:\UTILS" ^
		 -c "f:\bnu170\bnu /L:0=57600 /F" ^
		 -c "copy d:\DORINFO%NODE%.DEF E:\ " ^
         -c "e:" ^
		 -c "Mutant %NODE% " ^
		 -c "EXIT"