@echo off
set NODE=%1
set NODEDIR=%2
set PORT=%3
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\BOWLSOL" ^
		 -c "mount f E:\UTILS" ^
		 -c "f:\bnu170\bnu /L:0=57600 /F" ^
		 -c "copy d:\dorinfo%node%.def E:\dorinfo1.def " ^
         -c "SET NO87=ON " ^
         -c "e:" ^
		 -c "BOWLSOL BOWLSOL.CFG /FD " ^
		 -c "EXIT"