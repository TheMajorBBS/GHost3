@echo off
set NODE=%1
set NODEDIR=%2
set PORT=%3
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\AGG" ^
		 -c "mount f E:\UTILS" ^
		 -c "f:\bnu170\bnu" ^
		 -c "copy d:\door.sys E:\ " ^
         -c "e:" ^
		 -c "FIXSYS 57600 " ^
		 -c "Griffin NODE1.CFG " ^
		 -c "exit" ^