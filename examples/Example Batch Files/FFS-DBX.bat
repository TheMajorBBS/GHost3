@echo off
set NODE=%1
set NODEDIR=%2
set PORT=%3
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\FFS" ^
		 -c "mount f E:\UTILS" ^
		 -c "f:\bnu170\bnu" ^
         -c "e:" ^
		 -c "fishing 2 d:\ /f " ^
		 -c "exit" ^


REM cd\doors\ffs
REM fishing 2 C:\gameserv\node%1 /f
REM FISHTEXT C:\doors\ffs\FISHTEXT.ASC C:\doors\ffs\FISHTEXT.ANS
REM copy fishtext.asc C:\wgserv\webpages\highscores
REM cd\wgserv\webpages\highscores
REM rename fishtext.asc fishtext.txt
