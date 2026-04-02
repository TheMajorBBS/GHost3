# Door Server for The Major BBS

A modern C# implementation of a door server that accepts RLogin connections from The MajorBBS and launches BBS door games.

## What's New in v1.3.0

- **BBS Tag Support** — Multi-network door games can now track players by their originating BBS. When a BBS connects with a tag (e.g. `[PPB]mark`), GHost3 builds a unique username for the drop file so that players from different BBSes are treated as distinct users. Five configurable modes are supported — see the `TagParsingMode` configuration option below.

## Features

- ✅ Full RLogin protocol support
- ✅ Multi-session support (handles multiple users simultaneously)
- ✅ Creates standard drop files (DOOR.SYS, DOOR32.SYS, DORINFO1.DEF)
- ✅ Per-node drop file directories
- ✅ JSON configuration
- ✅ Cross-platform (Windows/Linux)
- ✅ Easy door configuration
- ✅ BBS tag support for multi-network door games

## How It Works

1. The Major BBS connects via RLogin protocol (port 5103)
2. Server parses RLogin handshake to extract:
   - Username
   - Terminal type
   - Door name (from Major BBS command string)
3. Server creates drop files in node-specific directories
4. Server launches the configured door game
5. I/O is relayed between the BBS client and door process
6. Session cleans up when door exits

## Requirements

1. The Major BBS
2. DOSBox-X
3. FOSSIL Driver for 16-bit doors (such as BNU)

## Setup

1. Extract contents to a directory (such as C:\GHOST3)
2. Follow the instructions in QUICKSTART.md

## Credits

- The Major BBS community
- jamierc for testing and feedback
