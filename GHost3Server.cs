using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace GHost3
{
    // Windows socket structures for WSADuplicateSocket
    [StructLayout(LayoutKind.Sequential)]
    internal struct WSAPROTOCOL_INFO
    {
        public uint dwServiceFlags1;
        public uint dwServiceFlags2;
        public uint dwServiceFlags3;
        public uint dwServiceFlags4;
        public uint dwProviderFlags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ProviderId;
        public uint dwCatalogEntryId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
        public uint[] ProtocolChain;
        public int iVersion;
        public int iAddressFamily;
        public int iMaxSockAddr;
        public int iMinSockAddr;
        public int iSocketType;
        public int iProtocol;
        public int iProtocolMaxOffset;
        public int iNetworkByteOrder;
        public int iSecurityScheme;
        public uint dwMessageSize;
        public uint dwProviderReserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)]
        public byte[] szProtocol;
    }

    // Windows API imports for socket sharing
    internal static class NativeMethods
    {
        [DllImport("ws2_32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern int WSADuplicateSocket(
            IntPtr s,
            uint dwProcessId,
            ref WSAPROTOCOL_INFO lpProtocolInfo);

        [DllImport("ws2_32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr WSASocket(
            int af,
            int type,
            int protocol,
            ref WSAPROTOCOL_INFO lpProtocolInfo,
            uint g,
            uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            bool bInheritHandle,
            uint dwOptions);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        internal const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    }

    /// <summary>
    /// RLogin Door Server for Major BBS
    /// Accepts RLogin connections and launches door games
    /// </summary>
    public class GHost3Server
    {
        private TcpListener? _listener;
        private bool _running;
        private readonly int _port;
        private readonly Dictionary<int, DoorSession> _activeSessions;
        private int _nextSessionId;
        private readonly object _sessionLock = new object();
        private readonly DoorConfig _config;
        private readonly bool _debugMode;

        public GHost3Server(int port = 513, DoorConfig? config = null, bool debugMode = false)
        {
            _port = port;
            _activeSessions = new Dictionary<int, DoorSession>();
            _nextSessionId = 1;
            _config = config ?? new DoorConfig();
            _debugMode = debugMode;
        }

        public async Task StartAsync()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _running = true;

            CleanupNodeDirectories();
            Console.WriteLine($"\r\nGHost3 Door Server started on port {_port}");
            Console.WriteLine($"Waiting for connections from The Major BBS");

            while (_running)
            {
                try
                {
                    // Periodic time-out check
                    if (_listener.Pending())
                    {
                        TcpClient client = await _listener.AcceptTcpClientAsync();
                        _ = Task.Run(() => HandleClientAsync(client));
                    }
                    else
                    {
                        await Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        Console.WriteLine($"Error accepting client: {ex.Message}");
                    }
                }
            }

            Console.WriteLine("Server loop exited, performing cleanup...");
        }

        public void Stop()
        {
            _running = false;
            _listener?.Stop();

            lock (_sessionLock)
            {
                foreach (var session in _activeSessions.Values)
                {
                    session.Dispose();
                }
                _activeSessions.Clear();
            }

            CleanupNodeDirectories();
        }

        public void CleanupNodeDirectories()
        {
            try
            {
                if (Directory.Exists(_config.DropFileDirectory))
                {
                    Console.WriteLine("Cleaning up node directories...");

                    var nodeDirs = Directory.GetDirectories(_config.DropFileDirectory, "NODE*");
                    foreach (var nodeDir in nodeDirs)
                    {
                        try
                        {
                            Directory.Delete(nodeDir, true);
                            Console.WriteLine($"Deleted {Path.GetFileName(nodeDir)}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not delete {Path.GetFileName(nodeDir)}: {ex.Message}");
                        }
                    }

                    Console.WriteLine($"Cleanup complete. Removed {nodeDirs.Length} node directories.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            int sessionId;
            lock (_sessionLock)
            {
                sessionId = _nextSessionId++;
            }

            Console.WriteLine($"[Session {sessionId}] New connection from {client.Client.RemoteEndPoint}");

            DoorSession? session = null;
            try
            {
                session = new DoorSession(sessionId, client, _config, _debugMode);

                lock (_sessionLock)
                {
                    _activeSessions[sessionId] = session;
                }

                await session.ProcessAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {sessionId}] Error: {ex.Message}");
            }
            finally
            {
                lock (_sessionLock)
                {
                    _activeSessions.Remove(sessionId);
                }
                session?.Dispose();
                Console.WriteLine($"[Session {sessionId}] Connection closed");
            }
        }

        public void ListActiveSessions()
        {
            lock (_sessionLock)
            {
                Console.WriteLine($"\nActive Sessions: {_activeSessions.Count}");
                foreach (var session in _activeSessions.Values)
                {
                    Console.WriteLine($"  Session {session.SessionId}: {session.Username} - {session.DoorName}");
                }
            }
        }
    }

    /// <summary>
    /// Represents a single door session
    /// </summary>
    public class DoorSession : IDisposable
    {
        public int SessionId { get; }
        public string Username { get; private set; } = "";
        public string DoorName { get; private set; } = "";

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly DoorConfig _config;
        private readonly bool _debugMode;
        private RLoginInfo? _rloginInfo = null;

        public DoorSession(int sessionId, TcpClient client, DoorConfig config, bool debugMode = false)
        {
            ArgumentNullException.ThrowIfNull(config);

            SessionId = sessionId;
            _client = client;
            _stream = client.GetStream();
            _config = config;
            _debugMode = debugMode;
        }

        private void Debug(string message)
        {
            if (_debugMode)
            {
                Console.WriteLine(message);
            }
        }

        public async Task ProcessAsync()
        {
            // Parse RLogin protocol
            _rloginInfo = await ParseRLoginHandshakeAsync();

            if (_rloginInfo == null)
            {
                Console.WriteLine($"[Session {SessionId}] Failed to parse RLogin handshake");
                return;
            }

            Username = _rloginInfo.ClientUsername;
            DoorName = _rloginInfo.DoorName ?? "Unknown";

            Console.WriteLine($"[Session {SessionId}] RLogin from {Username}, Door: {DoorName}");
            Console.WriteLine($"[Session {SessionId}] Terminal: {_rloginInfo.TerminalType}");

            // Look up door configuration
            if (!_config.Doors.TryGetValue(DoorName, out var doorInfo))
            {
                // Door not found
                await SendMessageAsync("Door not configured.\r\n");
                return;
            }

            // Create drop file
            string dropFilePath = CreateDropFile(doorInfo);
            if (string.IsNullOrEmpty(dropFilePath))
            {
                Console.WriteLine($"[Session {SessionId}] Unable to create dropfile");
                await SendMessageAsync("Door configuration error.\r\n");
                return;
            }
            else
            {
                Console.WriteLine($"[Session {SessionId}] Created drop file: {dropFilePath}");

                // Launch the door game
                await LaunchDoorAsync(doorInfo, dropFilePath);
            }
        }

        private async Task<RLoginInfo?> ParseRLoginHandshakeAsync()
        {
            try
            {
                var info = new RLoginInfo();

                // RLogin protocol: null-terminated strings
                // Format: <null><client-user-name><null><server-user-name><null><terminal-type/speed><null>
                // MBBS extension: terminal-type may be "xtrn=DOORCODE" when using -d flag

                byte[] buffer = new byte[1024];

                Debug($"[Session {SessionId}] DEBUG: Waiting for RLogin handshake data...");

                // Add timeout to handshake read (10 seconds)
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    try
                    {
                        int bytesRead = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);

                        Debug($"[Session {SessionId}] DEBUG: RLogin handshake received {bytesRead} bytes");

                        if (bytesRead == 0)
                        {
                            Debug($"[Session {SessionId}] DEBUG: No data received in handshake (client closed connection)");
                            return null;
                        }

                        var hexBytes = BitConverter.ToString(buffer, 0, Math.Min(bytesRead, 200)).Replace("-", " ");
                        Debug($"[Session {SessionId}] DEBUG: Raw bytes (hex): {hexBytes}");

                        var printable = new StringBuilder();
                        for (int i = 0; i < bytesRead; i++)
                        {
                            if (buffer[i] == 0)
                                printable.Append("<NULL>");
                            else if (buffer[i] >= 32 && buffer[i] < 127)
                                printable.Append((char)buffer[i]);
                            else
                                printable.Append($"<{buffer[i]:X2}>");
                        }
                        Debug($"[Session {SessionId}] DEBUG: Printable: {printable}");

                        var strings = new List<string>();
                        int start = 0;

                        for (int i = 0; i < bytesRead; i++)
                        {
                            if (buffer[i] == 0)
                            {
                                if (i > start)
                                {
                                    string str = Encoding.ASCII.GetString(buffer, start, i - start);
                                    strings.Add(str);
                                }
                                start = i + 1;
                            }
                        }

                        Debug($"[Session {SessionId}] DEBUG: Parsed {strings.Count} strings from handshake:");
                        for (int i = 0; i < strings.Count; i++)
                        {
                            Debug($"[Session {SessionId}] DEBUG:   String[{i}]: '{strings[i]}'");
                        }

                        // Standard RLogin format
                        if (strings.Count >= 3)
                        {
                            info.ClientUsername = strings[0];
                            info.ServerUsername = strings[1];
                            info.TerminalType = strings[2];
                        }
                        else
                        {
                            Console.WriteLine($"[Session {SessionId}] WARNING: Expected at least 3 strings, got {strings.Count}");
                        }

                        // Check if MBBS sent door code in terminal field as "xtrn=DOORCODE"
                        if (info.TerminalType != null && info.TerminalType.StartsWith("xtrn="))
                        {
                            info.DoorName = info.TerminalType.Substring(5); // Extract door code after "xtrn="
                            info.TerminalType = "ansi"; // Default to ANSI terminal
                            Console.WriteLine($"[Session {SessionId}] Detected MBBS xtrn format: door={info.DoorName}");
                        }
                        else
                        {
                            // Check for door name in fourth field (alternate format)
                            if (strings.Count >= 4)
                            {
                                info.DoorName = strings[3];
                            }

                            // Parse terminal speed if included (e.g., "ansi/9600")
                            if (info.TerminalType != null && info.TerminalType.Contains("/"))
                            {
                                var parts = info.TerminalType.Split('/');
                                info.TerminalType = parts[0];
                                if (parts.Length > 1 && int.TryParse(parts[1], out int speed))
                                {
                                    info.BaudRate = speed;
                                }
                            }
                        }

                        return info;
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine($"[Session {SessionId}] ERROR: RLogin handshake timed out after 10 seconds (no data received from client)");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {SessionId}] RLogin parse error: {ex.Message}");
                return null;
            }
        }

        private string CreateDropFile(DoorInfo doorInfo)
        {
            // Create a unique directory for this session
            string nodeDir = Path.Combine(_config.DropFileDirectory, $"NODE{SessionId}");
            Directory.CreateDirectory(nodeDir);

            // Get the native Windows socket handle
            int nativeSocketHandle = GetNativeSocketHandle();

            // For socket-based doors create an inheritable handle
            int handleForDropFile = nativeSocketHandle;

            try
            {
                IntPtr currentProcess = NativeMethods.GetCurrentProcess();
                IntPtr socketHandle = (IntPtr)nativeSocketHandle;
                IntPtr dupHandle;

                bool success = NativeMethods.DuplicateHandle(
                    currentProcess,
                    socketHandle,
                    currentProcess,
                    out dupHandle,
                    0,
                    true, // bInheritHandle = true
                    NativeMethods.DUPLICATE_SAME_ACCESS);

                if (success)
                {
                    handleForDropFile = dupHandle.ToInt32();
                    Console.WriteLine($"[Session {SessionId}] Created inheritable handle for drop file: {handleForDropFile}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {SessionId}] Warning: Could not create inheritable handle: {ex.Message}");
            }

            // Create only the specified drop file format
            string dropFilePath = "";
            string format = doorInfo.DropFileFormat?.ToUpperInvariant() ?? "DOOR.SYS";

            try
            {
                switch (format)
                {
                    case "DOOR.SYS":
                        dropFilePath = Path.Combine(nodeDir, "DOOR.SYS");
                        CreateDoorSysFile(dropFilePath);
                        Console.WriteLine($"[Session {SessionId}] Created DOOR.SYS format drop file");
                        break;

                    case "DOOR32.SYS":
                        dropFilePath = Path.Combine(nodeDir, "DOOR32.SYS");
                        CreateDoor32SysFile(dropFilePath, handleForDropFile);
                        Console.WriteLine($"[Session {SessionId}] Created DOOR32.SYS format drop file");
                        break;

                    case "DORINFOX.DEF":
                        dropFilePath = Path.Combine(nodeDir, GetDorinfoFilename());
                        CreateDorinfoDefFile(dropFilePath);
                        Console.WriteLine($"[Session {SessionId}] Created DORINFOx.DEF format drop file");
                        break;

                    default:
                        // Invalid format specified, default to DOOR.SYS
                        Console.WriteLine($"[Session {SessionId}] Warning: Unknown drop file format '{doorInfo.DropFileFormat}', defaulting to DOOR.SYS");
                        dropFilePath = Path.Combine(nodeDir, "DOOR.SYS");
                        CreateDoorSysFile(dropFilePath);
                        break;
                }

                return dropFilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {SessionId}] ERROR: Could not create dropfile: {ex.Message}");
                return string.Empty;
            }
        }

        private int GetNativeSocketHandle()
        {
            try
            {
                // Get the Socket's SafeHandle
                var socket = _client.Client;

                Debug($"[Session {SessionId}] DEBUG: Getting native socket handle");
                Debug($"[Session {SessionId}] DEBUG: Socket.Handle type: {socket.Handle.GetType()}");
                Debug($"[Session {SessionId}] DEBUG: Socket.Handle value: {socket.Handle}");

                // Use reflection to access the internal SafeSocketHandle
                var handleProperty = typeof(Socket).GetProperty("SafeHandle",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (handleProperty != null)
                {
                    var safeHandle = handleProperty.GetValue(socket) as System.Runtime.InteropServices.SafeHandle;
                    if (safeHandle != null)
                    {
                        int handle = (int)safeHandle.DangerousGetHandle();
                        Debug($"[Session {SessionId}] DEBUG: Got handle via SafeHandle: {handle}");
                        return handle;
                    }
                }

                int fallbackHandle = (int)socket.Handle;
                Debug($"[Session {SessionId}] DEBUG: Got handle via direct cast: {fallbackHandle}");
                return fallbackHandle;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {SessionId}] ERROR: Could not get native socket handle: {ex.Message}");
                Console.WriteLine($"[Session {SessionId}] ERROR: Stack trace: {ex.StackTrace}");
                return -1;
            }
        }

        private void CreateDoorSysFile(string path)
        {
            ArgumentNullException.ThrowIfNull(_rloginInfo);

            string doorPath;
            try
            {
                doorPath = Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"Invalid door path: '{path}' has no directory component");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[Session {SessionId}] ERROR: {ex.Message}");
                return;
            }

            var lines = new List<string>
            {
                "COM1:",                                   // Comm port
                "9600",                                    // Baud rate
                "8",                                       // Parity
                SessionId.ToString(),                      // Node number
                "57600",                                   // DTE rate (locked)
                "Y",                                       // Screen display
                "Y",                                       // Printer toggle
                "Y",                                       // Page bell
                "Y",                                       // Caller alarm
                _rloginInfo.ClientUsername,                // User name
                "Unknown, XX",                             // Location
                "555-555-5555",                            // Phone number (data)
                "555-555-5555",                            // Phone number (voice)
                "PASSWORD",                                // Password
                "100",                                     // Security level
                "999",                                     // Times on
                DateTime.Now.ToString("MM/dd/yy"),         // Last call date
                "60",                                      // Seconds remaining
                "60",                                      // Minutes remaining
                "GR",                                      // Graphics mode
                "24",                                      // Page length
                "N",                                       // Expert mode
                "1,2,3,4,5,6,7",                           // Conferences
                "1",                                       // Conference number
                "01/01/99",                                // Expiration date
                "1",                                       // User number
                "X",                                       // Default protocol
                "0",                                       // Total uploads
                "0",                                       // Total downloads
                "0",                                       // Daily download limit (KB)
                "999999",                                  // Daily download limit remaining
                DateTime.Now.ToString("MM/dd/yy"),         // Birthdate
                doorPath,                                  // Path to control files
                doorPath,                                  // Path to gen files
                "SysOp",                                   // Sysop name
                "My BBS",                                  // BBS name
                "00:00",                                   // Event time
                "Y",                                       // Error correcting connection
                "N",                                       // ANSI in NG mode
                "Y",                                       // Record locking
                "7",                                       // Default color
                "60",                                      // Time credits
                "60",                                      // Last new files scan
                DateTime.Now.ToString("HH:mm"),            // Time of this call
                DateTime.Now.ToString("HH:mm"),            // Last time online
                "9999",                                    // Max files per day
                "999",                                     // Files downloaded today
                "0",                                       // Total KB uploaded
                "0",                                       // Total KB downloaded
                "Comment line",                            // User comment
                "0",                                       // Doors opened
                "999"                                      // Messages left
            };

            File.WriteAllLines(path, lines);

            // Verify file was written
            if (File.Exists(path))
            {
                Console.WriteLine($"[Session {SessionId}] Created DOOR.SYS with username: {_rloginInfo.ClientUsername}");
                Debug($"[Session {SessionId}] DEBUG: DOOR.SYS size: {new FileInfo(path).Length} bytes");
            }
            else
            {
                Console.WriteLine($"[Session {SessionId}] ERROR: Failed to create DOOR.SYS!");
            }
        }

        private void CreateDoor32SysFile(string path, int nativeSocketHandle)
        {
            // DOOR32.SYS Format (official specification):
            // Line 1: Comm type (0=local, 1=serial, 2=telnet)
            // Line 2: Comm or socket handle  
            // Line 3: Baud rate
            // Line 4: BBSID (software name and version)
            // Line 5: User record position (1-based)
            // Line 6: User's real name
            // Line 7: User's handle/alias
            // Line 8: User's security level
            // Line 9: User's time left (in minutes)
            // Line 10: Emulation (0=ASCII, 1=ANSI, 2=AVATAR, 3=RIP, 4=MAX)
            // Line 11: Current node number

            ArgumentNullException.ThrowIfNull(_rloginInfo);

            var lines = new List<string>
            {
                "2",                                       // Comm type (2 = TCP/IP)
                nativeSocketHandle.ToString(),             // Socket handle
                "57600",                                   // Baud rate
                "GHost3 BBS",                              // BBSID
                SessionId.ToString(),                      // User record position
                _rloginInfo.ClientUsername ?? "Unknown",   // User's real name (from RLogin)
                _rloginInfo.ClientUsername ?? "Unknown",   // User's handle/alias (same as real name)
                "100",                                     // Security level
                "60",                                      // Time remaining (minutes)
                "1",                                       // Emulation (1 = ANSI)
                SessionId.ToString()                       // Node number
            };

            using (var writer = new StreamWriter(path, false))
            {
                foreach (var line in lines)
                {
                    writer.WriteLine(line);
                }
                writer.Flush();
            }

            // Verify file was written
            if (File.Exists(path))
            {
                Console.WriteLine($"[Session {SessionId}] Created DOOR32.SYS with username: {_rloginInfo.ClientUsername}");
                Debug($"[Session {SessionId}] DEBUG: DOOR32.SYS size: {new FileInfo(path).Length} bytes");
            }
            else
            {
                Console.WriteLine($"[Session {SessionId}] ERROR: Failed to create DOOR32.SYS!");
            }
        }

        private string GetDorinfoFilename()
        {
            // DORINFO format: DORINFOx.DEF where x is node number
            // 1-9 = "1" through "9", 10 = "0", 11-36 = "A" through "Z"
            if (SessionId < 10)
                return $"DORINFO{SessionId}.DEF";
            else if (SessionId == 10)
                return "DORINFO0.DEF";
            else if (SessionId <= 36)
                return $"DORINFO{(char)('A' + SessionId - 11)}.DEF";
            else
                return "DORINFO1.DEF"; // Fallback
        }

        private void CreateDorinfoDefFile(string path)
        {
            ArgumentNullException.ThrowIfNull(_rloginInfo);

            var lines = new List<string>
            {
                "My BBS",                                  // BBS name
                "SysOp",                                   // Sysop first name
                "Name",                                    // Sysop last name
                "COM1",                                    // Comm port
                "9600 BAUD,N,8,1",                        // Baud rate
                "0",                                       // Network type
                _rloginInfo.ClientUsername,                // User first name
                "",                                        // User last name
                "Unknown, XX",                             // Location
                "1",                                       // Graphics mode (1 = ANSI)
                "100",                                     // Security level
                "60",                                      // Time remaining (minutes)
                "-1"                                       // Fossil (-1 = not used)
            };

            File.WriteAllLines(path, lines);

            // Verify file was written
            if (File.Exists(path))
            {
                Console.WriteLine($"[Session {SessionId}] Created DORINFOx.DEF with username: {_rloginInfo.ClientUsername}");
                Debug($"[Session {SessionId}] DEBUG: DORINFOx.DEF size: {new FileInfo(path).Length} bytes");
            }
            else
            {
                Console.WriteLine($"[Session {SessionId}] ERROR: Failed to create DORINFOx.DEF!");
            }
        }

        private async Task LaunchDoorAsync(DoorInfo doorInfo, string dropFilePath)
        {

            string nodeDir;
            try
            {
                nodeDir = Path.GetDirectoryName(dropFilePath) ?? throw new InvalidOperationException($"Invalid door path: '{dropFilePath}' has no directory component");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[Session {SessionId}] ERROR: {ex.Message}");
                return;
            }

            Console.WriteLine($"[Session {SessionId}] Launching: {doorInfo.ExecutablePath}");

            // Check if door needs DOSBox-X relay (if {PORT} is in command line)
            bool needsRelay = doorInfo.CommandLine.Contains("{PORT}");

            if (needsRelay)
            {
                await LaunchDoorWithRelayAsync(doorInfo, dropFilePath, nodeDir);
            }
            else
            {
                await LaunchDoorDirectAsync(doorInfo, dropFilePath, nodeDir);
            }
        }

        private async Task LaunchDoorDirectAsync(DoorInfo doorInfo, string dropFilePath, string nodeDir)
        {
            // Get native socket handle for doors that need it
            int nativeSocketHandle = GetNativeSocketHandle();

            string commandLine = doorInfo.CommandLine
                .Replace("{NODE}", SessionId.ToString())
                .Replace("{DROPFILE}", dropFilePath)
                .Replace("{NODEDIR}", nodeDir)
                .Replace("{HANDLE}", nativeSocketHandle.ToString());

            // Detect if door uses socket handle directly (no stdio redirection needed)
            bool usesSocketDirectly = doorInfo.CommandLine.Contains("{HANDLE}") ||
                                      doorInfo.CommandLine.Contains("-S") ||
                                      doorInfo.CommandLine.Contains("/S");

            // Check if DOOR32.SYS exists with comm type 2 (telnet)
            if (!usesSocketDirectly)
            {
                try
                {
                    string door32Path = Path.Combine(nodeDir, "DOOR32.SYS");
                    if (File.Exists(door32Path))
                    {
                        string[] lines = File.ReadAllLines(door32Path);
                        if (lines.Length >= 2 && lines[0].Trim() == "2") // Comm type 2 = telnet
                        {
                            usesSocketDirectly = true;
                            Debug($"[Session {SessionId}] DEBUG: DOOR32.SYS exists with comm type 2 (telnet), door will use socket directly");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug($"[Session {SessionId}] DEBUG: Could not check DOOR32.SYS: {ex.Message}");
                }
            }

            try
            {
                Debug($"[Session {SessionId}] DEBUG: Command line after substitution: {commandLine}");
                Debug($"[Session {SessionId}] DEBUG: Uses socket directly: {usesSocketDirectly}");

                if (usesSocketDirectly)
                {
                    Console.WriteLine($"[Session {SessionId}] Door uses socket handle directly, no stdio redirection");
                    Debug($"[Session {SessionId}] DEBUG: Launching {doorInfo.ExecutablePath} {commandLine}");

                    // Read handle (duplicated in CreateDropFile) from the drop file
                    int inheritableHandle = nativeSocketHandle;

                    try
                    {
                        string door32Path = Path.Combine(nodeDir, "DOOR32.SYS");
                        if (File.Exists(door32Path))
                        {
                            string[] lines = File.ReadAllLines(door32Path);
                            if (lines.Length >= 2 && int.TryParse(lines[1], out int handleFromFile))
                            {
                                inheritableHandle = handleFromFile;
                                Debug($"[Session {SessionId}] DEBUG: Using inheritable handle from DOOR32.SYS: {inheritableHandle}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Session {SessionId}] WARNING: Could not read handle from DOOR32.SYS: {ex.Message}");
                    }

                    // Update command line with the inheritable handle
                    commandLine = doorInfo.CommandLine
                        .Replace("{NODE}", SessionId.ToString())
                        .Replace("{DROPFILE}", dropFilePath)
                        .Replace("{NODEDIR}", nodeDir)
                        .Replace("{HANDLE}", inheritableHandle.ToString());

                    Debug($"[Session {SessionId}] DEBUG: Updated command line: {commandLine}");

                    // Use door's configured working directory (not node directory)
                    // Door needs access to its own config files
                    string workingDir = doorInfo.WorkingDirectory
                            ?? Path.GetDirectoryName(doorInfo.ExecutablePath)
                            ?? throw new InvalidOperationException($"Door '{DoorName}' has invalid executable path: '{doorInfo.ExecutablePath}'");

                    Debug($"[Session {SessionId}] DEBUG: Using door working directory: {workingDir}");

                    // If command line contains drop file reference, verify it exists and show content
                    if (commandLine.Contains("DOOR32.SYS"))
                    {
                        string door32Path = Path.Combine(nodeDir, "DOOR32.SYS");
                        if (File.Exists(door32Path))
                        {
                            Debug($"[Session {SessionId}] DEBUG: DOOR32.SYS exists at: {door32Path}");
                            Debug($"[Session {SessionId}] DEBUG: DOOR32.SYS content:");
                            foreach (var line in File.ReadAllLines(door32Path))
                            {
                                Debug($"[Session {SessionId}] DEBUG:   {line}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[Session {SessionId}] WARNING: DOOR32.SYS not found at: {door32Path}");
                        }
                    }

                    Debug($"[Session {SessionId}] DEBUG: Socket handle: {inheritableHandle}");

                    // Set socket options to keep connection alive while door runs
                    _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    _client.Client.NoDelay = true;

                    // Launch without stdio redirection - door will use socket handle directly
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = doorInfo.ExecutablePath,
                        Arguments = commandLine,
                        WorkingDirectory = workingDir,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var process = System.Diagnostics.Process.Start(startInfo))
                    {
                        if (process == null)
                        {
                            await SendMessageAsync("Failed to start door process.\r\n");
                            return;
                        }

                        Debug($"[Session {SessionId}] DEBUG: Process started with PID {process.Id}");
                        Debug($"[Session {SessionId}] DEBUG: Keeping socket and stream alive while door runs...");

                        var startTime = DateTime.Now;

                        // Keep the socket alive - don't close _stream or _client while door is using the handle
                        // Wait for the process to exit
                        await Task.Run(() => process.WaitForExit());

                        var runTime = DateTime.Now - startTime;
                        Console.WriteLine($"[Session {SessionId}] Door process exited with code {process.ExitCode} after {runTime.TotalSeconds:F1} seconds");
                    }
                }
                else
                {
                    Console.WriteLine($"[Session {SessionId}] Door uses stdio redirection");
                    Debug($"[Session {SessionId}] DEBUG: Launching {doorInfo.ExecutablePath} {commandLine}");

                    // Use stdio redirection for doors that read from stdin/stdout
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = doorInfo.ExecutablePath,
                        Arguments = commandLine,
                        WorkingDirectory = doorInfo.WorkingDirectory ?? Path.GetDirectoryName(doorInfo.ExecutablePath),
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    Debug($"[Session {SessionId}] DEBUG: Starting process...");

                    using (var process = System.Diagnostics.Process.Start(startInfo))
                    {
                        if (process == null)
                        {
                            Console.WriteLine($"[Session {SessionId}] ERROR: Process.Start returned null");
                            await SendMessageAsync("Failed to start door process.\r\n");
                            return;
                        }

                        Debug($"[Session {SessionId}] DEBUG: Process started with PID {process.Id}, setting up I/O relay...");

                        var stderrTask = Task.Run(async () =>
                        {
                            try
                            {
                                string? line;
                                while ((line = await process.StandardError.ReadLineAsync()) != null)
                                {
                                    Console.WriteLine($"[Session {SessionId}] STDERR: {line}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Session {SessionId}] STDERR reader error: {ex.Message}");
                            }
                        });

                        await Task.Delay(100);

                        Debug($"[Session {SessionId}] DEBUG: Starting relay tasks...");

                        // Relay I/O between client and door process
                        var relayTask1 = Task.Run(() => RelayStreamAsync(_stream, process.StandardInput.BaseStream, "Client->Door"));
                        var relayTask2 = Task.Run(() => RelayStreamAsync(process.StandardOutput.BaseStream, _stream, "Door->Client"));

                        await Task.Delay(100);

                        // Some doors expect initial input before outputting - send a null byte to trigger
                        try
                        {
                            Debug($"[Session {SessionId}] DEBUG: Sending initial null byte to trigger door output...");
                            await process.StandardInput.BaseStream.WriteAsync(new byte[] { 0 }, 0, 1);
                            await process.StandardInput.BaseStream.FlushAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug($"[Session {SessionId}] DEBUG: Could not send initial byte: {ex.Message}");
                        }

                        Debug($"[Session {SessionId}] DEBUG: Relay tasks created, waiting for completion...");
                        await Task.WhenAny(relayTask1, relayTask2);

                        Debug($"[Session {SessionId}] DEBUG: Relay completed, waiting for process exit...");

                        // Give process time to exit gracefully
                        if (!process.WaitForExit(5000))
                        {
                            Console.WriteLine($"[Session {SessionId}] Door process did not exit, killing it");
                            process.Kill();
                        }
                        else
                        {
                            Console.WriteLine($"[Session {SessionId}] Door process exited with code {process.ExitCode}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {SessionId}] Error launching door: {ex.Message}");
                await SendMessageAsync($"Error launching door: {ex.Message}\r\n");
            }
        }

        private async Task LaunchDoorWithRelayAsync(DoorInfo doorInfo, string dropFilePath, string nodeDir)
        {
            // DOSBox-X relay method - creates local TCP port for DOSBox-X to connect to
            TcpListener? relayListener = null;
            TcpClient? dosboxClient = null;
            System.Diagnostics.Process? process = null;

            try
            {
                // Create a local TCP listener on a random port
                relayListener = new TcpListener(IPAddress.Loopback, 0);
                relayListener.Start();
                int relayPort = ((IPEndPoint)relayListener.LocalEndpoint).Port;

                Console.WriteLine($"[Session {SessionId}] Created relay listener on port {relayPort}");

                // Build command line with relay port
                string commandLine = doorInfo.CommandLine
                    .Replace("{NODE}", SessionId.ToString())
                    .Replace("{DROPFILE}", dropFilePath)
                    .Replace("{NODEDIR}", nodeDir)
                    .Replace("{PORT}", relayPort.ToString())
                    .Replace("{HANDLE}", _client.Client.Handle.ToString());

                Console.WriteLine($"[Session {SessionId}] Command: {doorInfo.ExecutablePath} {commandLine}");

                // Start the door process (DOSBox-X will connect back to relay port)
                // Use UseShellExecute = true to avoid handle inheritance issues with NTVDMX64
                // (relay mode doesn't need stdio redirection or handle inheritance - it uses TCP)
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = doorInfo.ExecutablePath,
                    Arguments = commandLine,
                    WorkingDirectory = doorInfo.WorkingDirectory ?? Path.GetDirectoryName(doorInfo.ExecutablePath),
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    await SendMessageAsync("Failed to start door process.\r\n");
                    return;
                }

                Console.WriteLine($"[Session {SessionId}] Door process started, waiting for DOSBox-X connection...");

                // Wait for DOSBox-X to connect back
                var acceptTask = relayListener.AcceptTcpClientAsync();
                var timeoutTask = Task.Delay(10000); // 10 second timeout

                var completedTask = await Task.WhenAny(acceptTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Console.WriteLine($"[Session {SessionId}] Timeout waiting for DOSBox-X connection");
                    await SendMessageAsync("Door failed to connect.\r\n");
                    return;
                }

                dosboxClient = await acceptTask;
                Console.WriteLine($"[Session {SessionId}] DOSBox-X connected to relay port");

                var dosboxStream = dosboxClient.GetStream();

                // Relay data in both directions:
                var relayTask1 = RelayStreamAsync(_stream, dosboxStream, "RLogin->DOSBox-X");
                var relayTask2 = RelayStreamAsync(dosboxStream, _stream, "DOSBox-X->RLogin");

                // Wait for either relay to complete (one side disconnects)
                await Task.WhenAny(relayTask1, relayTask2);

                Console.WriteLine($"[Session {SessionId}] Relay ended, cleaning up");

                // Give process time to exit gracefully
                if (!process.WaitForExit(5000))
                {
                    Console.WriteLine($"[Session {SessionId}] Door process did not exit, killing it");
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {SessionId}] Error in door relay: {ex.Message}");
                await SendMessageAsync($"Error launching door: {ex.Message}\r\n");
            }
            finally
            {
                // Clean up resources
                dosboxClient?.Close();
                relayListener?.Stop();

                if (process != null && !process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch { /* Already exited */ }
                }
            }
        }

        private async Task RelayStreamAsync(Stream input, Stream output, string direction)
        {
            try
            {
                Debug($"[Session {SessionId}] DEBUG: Relay {direction} started");
                byte[] buffer = new byte[8192];
                int bytesRead;
                int totalBytes = 0;

                while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                {
                    totalBytes += bytesRead;
                    Debug($"[Session {SessionId}] DEBUG: Relay {direction} read {bytesRead} bytes (total: {totalBytes})");
                    await output.WriteAsync(buffer.AsMemory(0, bytesRead));
                    await output.FlushAsync();
                    Debug($"[Session {SessionId}] DEBUG: Relay {direction} wrote and flushed {bytesRead} bytes");
                }

                Debug($"[Session {SessionId}] DEBUG: Relay {direction} ended (total bytes: {totalBytes})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {SessionId}] Relay error ({direction}): {ex.Message}");
                Console.WriteLine($"[Session {SessionId}] Stack trace: {ex.StackTrace}");
            }
        }

        private async Task SendMessageAsync(string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(message);
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _client?.Dispose();
        }
    }

    /// <summary>
    /// RLogin connection information
    /// </summary>
    public class RLoginInfo
    {
        public string ClientUsername { get; set; } = "";
        public string ServerUsername { get; set; } = "";
        public string TerminalType { get; set; } = "";
        public string DoorName { get; set; } = "";
        public int BaudRate { get; set; } = 9600;
    }

    /// <summary>
    /// Door configuration
    /// </summary>
    public class DoorConfig
    {
        public string DropFileDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "DoorServer");
        public Dictionary<string, DoorInfo> Doors { get; set; } = new Dictionary<string, DoorInfo>();
    }

    /// <summary>
    /// Information about a configured door
    /// </summary>
    public class DoorInfo
    {
        public string Name { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string CommandLine { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public string DropFileFormat { get; set; } = "DOOR.SYS";
    }
}
