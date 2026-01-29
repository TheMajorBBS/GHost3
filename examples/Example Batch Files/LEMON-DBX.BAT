@echo off
set NODE=%1
set NODEDIR=%2
set PORT=%3
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\LEMON" ^
		 -c "mount f E:\UTILS" ^
		 -c "f:\bnu170\bnu" ^
         -c "e:" ^
		 -c "lemon lemon%NODE%.cfg /fd " ^
		 -c "EXIT" ^