@echo off
set NODEDIR=%1
set PORT=%2
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\SS" ^
	     -c "mount f E:\UTILS" ^
	     -c "f:\bnu170\bnu" ^
         -c "e:" ^
	     -c "suprslot suprslot.cfg d:\door.sys  " ^
	     -c "exit"
		 
rem cd\doors\ss
rem suprslot suprslot.cfg C:\sbbs\node%1\door.sys
