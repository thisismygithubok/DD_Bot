/* DD_Bot - A Discord Bot to control Docker containers*/

/*  Copyright (C) 2022 Maxim Kovac

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.

*/

using System;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;
using DD_Bot.Application.Services;
using DD_Bot.Domain;
using System.Linq;
using Docker.DotNet.Models;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DD_Bot.Application.Commands
{
    public class ListCommand
    {
        private DiscordSocketClient _discord;
        private readonly ILogger<ListCommand> _logger;
        public ListCommand(DiscordSocketClient discord, ILogger<ListCommand> logger)
        {
            _discord = discord;
            _logger = logger;
        }

        #region CreateCommand
        public static ApplicationCommandProperties Create()
        {
            var builder = new SlashCommandBuilder()
            {
                Name = "list",
                Description = "List all Docker containers"
            };
            return builder.Build();
        }

        #endregion

        #region GetSectionsForUser
        
        public static List<string> GetSectionsForUser(DiscordSettings settings, IReadOnlyCollection<SocketRole> roles, ulong userId)
        {
            var sections = new HashSet<string>();

            // **Grant access to all sections if the user is an admin**
            if (settings.AdminIDs.Contains(userId))
            {
                return settings.SectionOrder;
            }

            // Existing logic to get sections based on roles
            foreach (var role in roles)
            {
                if (settings.RoleStartPermissions.ContainsKey(role.Id))
                {
                    sections.UnionWith(settings.RoleStartPermissions[role.Id]);
                }
                if (settings.RoleStopPermissions.ContainsKey(role.Id))
                {
                    sections.UnionWith(settings.RoleStopPermissions[role.Id]);
                }
            }

            return sections.ToList();
        }

        #endregion

        #region ExecuteCommand

        public static async void Execute(SocketSlashCommand arg, DockerService dockerService, DiscordSettings settings, DockerSettings dockerSettings, ILogger<ListCommand> logger)
        {
            await arg.RespondAsync("Contacting Docker Service...");
            await dockerService.DockerUpdate();
            List<string> allowedContainers = new List<string>();

            if (!settings.AdminIDs.Contains(arg.User.Id))
            {
                var socketUser = arg.User as SocketGuildUser;
                var guild = socketUser.Guild;
                var socketGuildUser = guild.GetUser(socketUser.Id);
                var userRoles = socketGuildUser.Roles;
                var userId = arg.User.Id;

                var dockerCommand = new DockerCommand(null, null, settings, null);
                var sections = dockerCommand.GetSectionsForUser(settings, userRoles, userId);

                if (socketGuildUser == null)
                {
                    await arg.ModifyOriginalResponseAsync(edit => edit.Content = "Failed to retrieve user data.");
                    return;
                }

                var sectionObjects = sections.Select(sectionName => new ContainerSection
                {
                    SectionName = sectionName,
                    Containers = dockerService.DockerStatus
                        .Where(c => c.Labels != null && c.Labels.ContainsKey("section") && c.Labels["section"] == sectionName)
                        .ToList()
                }).ToList();

                allowedContainers.AddRange(GetPermissionsForUser(settings, arg.User.Id));
                allowedContainers.AddRange(GetPermissionsForRoles(settings, userRoles));

                // Check for SectionOrder labels
                allowedContainers.AddRange(ValidateSectionLabels(dockerService, settings, userRoles));
                allowedContainers = allowedContainers.Distinct().ToList();
            }
            else
            {
                // Admins can see all containers
                allowedContainers = dockerService.DockerStatus.Select(c => c.Names[0]).ToList();
            }

            if (dockerSettings.DebugLogging)
            {
                // Debugging output
                logger.LogDebug("Allowed Containers (Admins):");
                foreach (var container in allowedContainers)
                {
                    logger.LogDebug(container);
                }
            }

            await DisplayContainers(dockerService, settings, dockerSettings, arg, allowedContainers, logger);
        }

        private static IEnumerable<string> GetPermissionsForUser(DiscordSettings settings, ulong userId)
        {
            List<string> permissions = new List<string>();
            if (settings.UserStartPermissions.ContainsKey(userId))
            {
                permissions.AddRange(settings.UserStartPermissions[userId]);
            }
            if (settings.UserStopPermissions.ContainsKey(userId))
            {
                permissions.AddRange(settings.UserStopPermissions[userId]);
            }
            return permissions;
        }

        private static IEnumerable<string> GetPermissionsForRoles(DiscordSettings settings, IReadOnlyCollection<SocketRole> roles)
        {
            List<string> permissions = new List<string>();
            foreach (var role in roles)
            {
                if (settings.RoleStartPermissions.ContainsKey(role.Id))
                {
                    permissions.AddRange(settings.RoleStartPermissions[role.Id]);
                }
                if (settings.RoleStopPermissions.ContainsKey(role.Id))
                {
                    permissions.AddRange(settings.RoleStopPermissions[role.Id]);
                }
            }
            return permissions;
        }

        private static IEnumerable<string> ValidateSectionLabels(DockerService dockerService, DiscordSettings settings, IReadOnlyCollection<SocketRole> roles)
        {
            List<string> containers = new List<string>();
            var dockerContainers = dockerService.DockerStatus;
            foreach (var container in dockerContainers)
            {
                if (container.Labels != null && container.Labels.ContainsKey("section"))
                {
                    var sectionLabel = container.Labels["section"];
                    if (settings.SectionOrder.Contains(sectionLabel))
                    {
                        foreach (var role in roles)
                        {
                            if (settings.RoleStartPermissions.ContainsKey(role.Id) && settings.RoleStartPermissions[role.Id].Contains(sectionLabel))
                            {
                                containers.Add(container.Names[0]);
                            }
                            if (settings.RoleStopPermissions.ContainsKey(role.Id) && settings.RoleStopPermissions[role.Id].Contains(sectionLabel))
                            {
                                containers.Add(container.Names[0]);
                            }
                        }
                    }
                }
            }
            return containers;
        }

        private static async Task DisplayContainers(DockerService dockerService, DiscordSettings settings, DockerSettings dockerSettings, SocketSlashCommand arg, List<string> allowedContainers, ILogger<ListCommand> logger)
        {
        
            int maxLength = dockerService.DockerStatusLongestName() + 1;
            if (maxLength > 28)  // Ensure a maximum column width for "Container Name"
            {
                maxLength = 28;
            }

            int statusColumnLength = 8; // Adjust length for "Status" column
            int totalLength = maxLength + statusColumnLength + 4; // Adjust total length calculation

            // New implementation using EmbedBuilder
            var embed = new EmbedBuilder()
                .WithTitle("Docker Containers")
                .WithColor(Color.Blue);
            
            var socketUser = arg.User as SocketGuildUser;
            var guild = socketUser.Guild;
            var socketGuildUser = guild.GetUser(socketUser.Id);
            var userRoles = socketGuildUser.Roles;
            var userId = arg.User.Id;
            var sections = GetSectionsForUser(settings, userRoles, userId);
            var dockerCommand = new DockerCommand(null, null, settings, null);
            var sectionNames = dockerCommand.GetSectionsForUser(settings, userRoles, userId);
            var sectionObjects = sectionNames.Select(sectionName => new ContainerSection
            
            {
                SectionName = sectionName,
                Containers = dockerService.DockerStatus
                    .Where(c => c.Labels != null && c.Labels.ContainsKey("section") && c.Labels["section"] == sectionName)
                    .ToList()
            }).ToList();

            // Update embed logic to handle character count and split if total characters exceed 1024
            foreach (var section in sectionObjects)
            {
                var chunks = new List<string>();
                // Build header and footer for code block formatting
                string header = "```\n" 
                                + new string('-', totalLength + 2) + "\n"
                                + "| Container Name" + new string(' ', maxLength - 14) + " | Status  |\n"
                                + new string('-', totalLength + 2) + "\n";
                string footer = new string('-', totalLength + 2) + "\n" + "```";
                
                // Use a StringBuilder for better performance when building strings
                StringBuilder currentChunk = new StringBuilder();
                currentChunk.Append(header);
                
                foreach (var container in section.Containers)
                {
                    var containerName = container.Names[0].PadRight(maxLength);
                    var status = container.Status.Contains("Up") ? "Running" : "Stopped";
                    string line = $"| {containerName} | {status.PadRight(6)} |\n";
                    
                    // Check if adding this line, along with the footer, would exceed the 1024-character limit
                    if (currentChunk.Length + line.Length + footer.Length > 1024)
                    {
                        // Append footer and store the chunk
                        currentChunk.Append(footer);
                        chunks.Add(currentChunk.ToString());
                        
                        // Start a new chunk with the same header
                        currentChunk.Clear();
                        currentChunk.Append(header);
                    }
                    
                    currentChunk.Append(line);
                }
                
                // Append the footer for the final chunk and store it
                currentChunk.Append(footer);
                chunks.Add(currentChunk.ToString());
                
                // Add each chunk as a separate field, marking them as continuations if necessary
                for (int i = 0; i < chunks.Count; i++)
                {
                    string fieldName = chunks.Count == 1 
                        ? $"Section: {section.SectionName}" 
                        : $"Section: {section.SectionName} (Part {i+1})";
                    embed.AddField(fieldName, chunks[i], inline: false);
                }
            }

            if (embed.Fields.Count == 0)
            {
                embed.WithDescription("No containers available to display.");
            }

            await arg.ModifyOriginalResponseAsync(edit =>
            {
                edit.Content = null;
                edit.Embed = embed.Build();
            });
        }

        private static string FormatListObjects(List<ContainerListResponse> list, DiscordSettings settings, int maxLength, SocketSlashCommand arg, List<string> allowedContainers)
        {
            string outputList = string.Empty;
            foreach (var item in list)
            {
                if (allowedContainers.Contains(item.Names[0]) || settings.AdminIDs.Contains(arg.User.Id))
                {
                    string containerName = item.Names[0].Trim('/');
                    if (containerName.Length > maxLength)
                    {
                        containerName = containerName.Substring(0, maxLength - 3) + "..."; // Truncate and add ellipsis
                    }
                    string paddedName = containerName.PadRight(maxLength);
                    outputList += $"| {paddedName} | {(item.Status.Contains("Up") ? "Running" : "Stopped")} |\n";
                }
            }
            return outputList;
        }

        private class ContainerSection
        {
            public string SectionName { get; set; }
            public List<ContainerListResponse> Containers { get; set; }
        }

        #endregion
    }
}
