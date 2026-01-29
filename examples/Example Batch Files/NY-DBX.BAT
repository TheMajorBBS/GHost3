@echo off
set NODE=%1
set NODEDIR=%2
set PORT=%3
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\NY" ^
		 -c "mount f E:\UTILS" ^
		 -c "f:\bnu170\bnu" ^
         -c "e:" ^
		 -c "NY2008 -N%NODE% -PD:\ " ^
		 -c "EXIT"

rem CD\doors\NY
rem NY2008 -N%1 -PC:\gameserv\NODE%1
rem copy nyscores.asc C:\wgserv\webpages\highscores
rem cd\wgserv\webpages\highscores
rem rename nyscores.asc nyscores.txt



