using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GHost3
{
    /// <summary>
    /// Configuration manager for the door server
    /// </summary>
    public class ConfigManager
    {
        private const string CONFIG_FILE = "doorserver.json";

        public ServerConfig LoadConfig()
        {
            if (!File.Exists(CONFIG_FILE))
            {
                Console.WriteLine($"ERROR: Configuration file '{CONFIG_FILE}' not found!");
                Console.WriteLine($"Please create {CONFIG_FILE} in the current directory.");
                Console.WriteLine();
                Console.WriteLine("See QUICKSTART.md for sample configuration.");
                Environment.Exit(1);
            }

            try
            {
                string json = File.ReadAllText(CONFIG_FILE);
                return JsonSerializer.Deserialize<ServerConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                }) ?? throw new InvalidOperationException("Failed to deserialize server configuration"); ;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to parse {CONFIG_FILE}: {ex.Message}");
                Console.WriteLine("Please check the JSON syntax.");
                Environment.Exit(1);
                return null;
            }
        }

        public DoorConfig ToDoorConfig(ServerConfig serverConfig)
        {
            var doorConfig = new DoorConfig
            {
                DropFileDirectory = serverConfig.DropFileDirectory
            };

            // TAG SUPPORT: Parse and validate the TagParsingMode string from doorserver.json.
            // Using Enum.TryParse with ignoreCase=true so "disabled", "BRACKET", etc. all work.
            // If the value is unrecognized, print a clear error listing valid options and exit.
            if (!string.IsNullOrWhiteSpace(serverConfig.TagParsingMode))
            {
                if (Enum.TryParse<TagParsingMode>(serverConfig.TagParsingMode, ignoreCase: true, out var parsedMode))
                {
                    doorConfig.TagParsingMode = parsedMode;
                }
                else
                {
                    Console.WriteLine($"ERROR: Invalid TagParsingMode '{serverConfig.TagParsingMode}' in doorserver.json.");
                    Console.WriteLine("Valid values are: Disabled, Prepend, PrependUnderscore, Append, AppendUnderscore");
                    Console.WriteLine();
                    Console.WriteLine("  Disabled          - Tag disabled. Plain username used as-is (default)");
                    Console.WriteLine("  Prepend           - Prepend tag directly:          [PPB]mark -> PPBmark");
                    Console.WriteLine("  PrependUnderscore - Prepend tag with underscore:   [PPB]mark -> PPB_mark");
                    Console.WriteLine("  Append            - Append tag directly:            [PPB]mark -> markPPB");
                    Console.WriteLine("  AppendUnderscore  - Append tag with underscore:    [PPB]mark -> mark_PPB");
                    Environment.Exit(1);
                }
            }

            foreach (var door in serverConfig.Doors)
            {
                if (door.Enabled)
                {
                    doorConfig.Doors[door.Code] = new DoorInfo
                    {
                        Name = door.Name,
                        ExecutablePath = door.ExecutablePath,
                        CommandLine = door.CommandLine,
                        WorkingDirectory = door.WorkingDirectory,
                        DropFileFormat = door.DropFileFormat
                    };
                }
            }

            return doorConfig;
        }
    }

    // ---------------------------------------------------------------------------
    // TAG SUPPORT: Enum controlling how the BBS tag is combined with the username
    // in the drop file.  The rlogin ServerUsername field (strings[1]) contains the
    // tagged username in [TAG]username format (e.g. "[PPB]mark").  Ghost3 uses this
    // to build a unique drop-file username so that players from different BBSes are
    // treated as distinct users in door games.
    //
    //   Prepend          : [PPB]mark  ->  PPBmark
    //   PrependUnderscore: [PPB]mark  ->  PPB_mark
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Controls how the BBS tag from rlogin strings[1] is combined with the username.
    /// </summary>
    public enum TagParsingMode
    {
        /// <summary>Tag disabled. strings[0] plain username used as-is (default).</summary>
        Disabled,
        /// <summary>Prepend tag directly: [PPB]mark -> PPBmark.</summary>
        Prepend,
        /// <summary>Prepend tag with underscore separator: [PPB]mark -> PPB_mark.</summary>
        PrependUnderscore,
        /// <summary>Append tag directly: [PPB]mark -> markPPB.</summary>
        Append,
        /// <summary>Append tag with underscore separator: [PPB]mark -> mark_PPB.</summary>
        AppendUnderscore
    }
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Server configuration
    /// </summary>
    public class ServerConfig
    {
        public string ListenAddress { get; set; } = "0.0.0.0";
        public int RLoginPort { get; set; } = 5103;
        public string DropFileDirectory { get; set; } = "";
        public int MaxSessions { get; set; } = 10;
        public List<DoorDefinition> Doors { get; set; } = new List<DoorDefinition>();

        // TAG SUPPORT: Controls how the BBS tag from rlogin strings[1] is combined
        // with the username in the drop file.
        // Stored as a string so JSON deserialization never crashes on a bad value.
        // Validated and converted to the TagParsingMode enum in ToDoorConfig().
        // Valid values: Disabled, Prepend, PrependUnderscore
        /// <summary>
        /// BBS tag mode. Valid values: Disabled (default), Prepend, PrependUnderscore.
        /// Prepend: [PPB]mark -> PPBmark. PrependUnderscore: [PPB]mark -> PPB_mark.
        /// </summary>
        public string TagParsingMode { get; set; } = "Disabled";

        /// <summary>
        /// Parses and validates ListenAddress, returning the corresponding IPAddress.
        /// Exits the process with an error message if the value is invalid.
        /// </summary>
        public IPAddress GetListenIPAddress()
        {
            if (string.IsNullOrWhiteSpace(ListenAddress) || ListenAddress == "0.0.0.0")
                return IPAddress.Any;

            if (ListenAddress == "::" || ListenAddress == "[::]")
                return IPAddress.IPv6Any;

            if (!IPAddress.TryParse(ListenAddress, out var address))
            {
                Console.WriteLine($"ERROR: Invalid ListenAddress '{ListenAddress}' in doorserver.json.");
                Console.WriteLine("Use a valid IPv4 address (e.g. 192.168.1.10), IPv6 address, or 0.0.0.0 to listen on all interfaces.");
                Environment.Exit(1);
                return IPAddress.Any; // unreachable
            }

            // Reject partial IPv4 addresses that IPAddress.TryParse accepts but are not dotted-quad.
            // e.g. "192" parses as 0.0.0.192 — the round-trip check catches this.
            if (address.AddressFamily == AddressFamily.InterNetwork &&
                address.ToString() != ListenAddress.Trim())
            {
                Console.WriteLine($"ERROR: Invalid ListenAddress '{ListenAddress}' in doorserver.json.");
                Console.WriteLine("Use a valid IPv4 address in dotted-quad form (e.g. 192.168.1.10) or 0.0.0.0 to listen on all interfaces.");
                Environment.Exit(1);
                return IPAddress.Any; // unreachable
            }

            // Verify the address is actually assigned to a network interface on this machine.
            try
            {
                bool found = false;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.Equals(address))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                }

                if (!found)
                {
                    Console.WriteLine($"ERROR: ListenAddress '{ListenAddress}' is not assigned to any network interface on this machine.");
                    Console.WriteLine("Check your network configuration or use 0.0.0.0 to listen on all interfaces.");
                    Environment.Exit(1);
                    return IPAddress.Any; // unreachable
                }
            }
            catch
            {
                // If interface enumeration fails, let the bind attempt surface the error naturally.
            }

            return address;
        }
    }

    /// <summary>
    /// Door definition in configuration
    /// </summary>
    public class DoorDefinition
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string CommandLine { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public string DropFileFormat { get; set; } = "DOOR.SYS";
        public bool Enabled { get; set; } = true;
        public string Description { get; set; } = "";
    }
}
