using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>
    /// Server configuration
    /// </summary>
    public class ServerConfig
    {
        public int RLoginPort { get; set; } = 5103;
        public string DropFileDirectory { get; set; } = "";
        public int MaxSessions { get; set; } = 10;
        public List<DoorDefinition> Doors { get; set; } = new List<DoorDefinition>();
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
