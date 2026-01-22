# GHost3 Quick Start Guide

## Overview
GHost3 is an RLogin door server for The MajorBBS that launches door games via socket handle inheritance. It supports both 32-bit native doors and 16-bit DOS doors (DOSBox-X).

## Requirements

1. The Major BBS
2. DOSBox-X
3. FOSSIL Driver for 16-bit doors (such as BNU)

## Installation

1. Extract GHost3 files to a directory (e.g., `C:\GHost3`)
2. Edit `doorserver.json` to configure your doors
3. Run `GHost3.exe` (requires Administrator privileges for port 5103)

## Basic Usage

```cmd
REM Normal mode
GHost3.exe

REM Debug mode (verbose output)
GHost3.exe -debug
```

## Configuration

### Sample doorserver.json

```json
{
  "RLoginPort": 5103,
  "DropFileDirectory": "C:\\GHost3",
  "MaxSessions": 50,
  "Doors": [
    {
      "Code": "BBW",
      "Name": "BBS Wordle",
      "ExecutablePath": "C:\\DOORS\\BBW\\BBSWORDLE.EXE",
      "CommandLine": "-D{NODEDIR}\\DOOR32.SYS",
      "WorkingDirectory": "C:\\DOORS\\BBW",
      "DropFileFormat": "DOOR32.SYS",
      "Enabled": true,
      "Description": ""
    },
	{
      "Code": "USP",
      "Name": "Usurper",
      "ExecutablePath": "C:\\DOORS\\USP\\USURPER.EXE",
      "CommandLine": "/N{NODE} /P{NODEDIR}",
      "WorkingDirectory": "C:\\DOORS\\USP",
      "DropFileFormat": "DOOR32.SYS",
      "Enabled": true,
      "Description": ""
    },
    {
      "Code": "THT",
      "Name": "Tree House Truants",
      "ExecutablePath": "C:\\DOORS\\THT.BAT",
      "CommandLine": "{NODEDIR} {PORT}",
      "WorkingDirectory": "C:\\DOORS\\THT",
      "DropFileFormat": "DOOR.SYS",
      "Enabled": true,
      "Description": ""
    },
	  "Code": "LOD",
      "Name": "Land Of Devastation",
	  "ExecutablePath": "C:\\DOORS\\LOD\\LOD-DBX.bat",
      "CommandLine": "{NODE} {NODEDIR} {PORT}",
      "WorkingDirectory": "C:\\DOORS\\LOD",
      "DropFileFormat": "DOOR.SYS",
      "Enabled": true,
      "Description": ""  
	}
  ]
}
```

### DOSBox-X setup
Ensure the [serial] section of dosbox-x.conf contains:

serial1       = nullmodem

## Command Line Placeholders

| Placeholder | Description | Example |
|------------|-------------|---------|
| `{NODE}` | Session/node number | `1`, `2`, `3` |
| `{NODEDIR}` | Full path to node directory | `C:\GHost3\nodes\NODE1` |
| `{DROPFILE}` | Full path to DOOR.SYS | `C:\GHost3\nodes\NODE1\DOOR.SYS` |
| `{HANDLE}` | Socket handle (rarely needed) | `1064` |
| `{PORT}` | TCP relay port (DOSBox-X only) | `54321` |

## Common Door Configurations

### DOOR32.SYS Doors (most 32-bit doors)
```json
"CommandLine": "-D{NODEDIR}\\DOOR32.SYS"
```

### Doors using path parameter
```json
"CommandLine": "/N{NODE} /P{NODEDIR}"
```

### DOSBox-X Doors (16-bit DOS doors)
```json
"CommandLine": "{NODEDIR} {PORT}"
```

#### Example DOSBOX-X batch file
##### Tree House Truants
```bat
@echo off
set NODEDIR=%1
set PORT=%2
dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount c C:\DOORS\THT" ^
         -c "mount d %NODEDIR%" ^
         -c "c:" ^
         -c "THT.exe /S" ^
		 -c "exit"
```

##### Land Of Devestation
```bat
@echo off
set NODE=%1
set NODEDIR=%2
set PORT=%3
C:\dosbox-x\dosbox-x -c "config -set serial1 nullmodem server:127.0.0.1 port:%PORT%" ^
         -c "mount d %NODEDIR%" ^
         -c "mount e E:\DOORS\LOD" ^
		 -c "mount f E:\UTILS" ^
		 -c "f:\bnu170\bnu" ^
         -c "e:" ^
         -c "GAME.exe /PD:\ /N%NODE% /CHECK" ^
		 -c "exit"
```

## Major BBS Configuration

Add to your Major BBS menu:

```
Module: Rlogin Client
Command String: 192.x.x.x -p 513 -d USURPER
```

Where `USP` matches the `Code` in your doorserver.json.

## Drop Files Created

GHost3 automatically creates these files in each `NODE#` directory:
- **DOOR32.SYS** - 32-bit door format with socket handle
- **DOOR.SYS** - Standard 52-line format
- **DORINFOx.DEF** - Node-specific format

## Troubleshooting

**Door launches but no input/output:**
- Is it a 16bit door? If so, ensure you are using a FOSSIL driver inside DOSBOX-X

**Connection times out:**
- Run GHost3 as Administrator
- Check firewall allows port 5103
- Verify Major BBS RLogin client command string

**Username appears blank:**
- Use `-D{NODEDIR}\\DOOR32.SYS` (without `-N` or `-S` flags)
