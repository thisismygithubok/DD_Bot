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

namespace DD_Bot.Application.Commands
{
    internal class ListCommand
    {
        private DiscordSocketClient _discord;
        public ListCommand(DiscordSocketClient discord)
        {
            _discord = discord;
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

        #region ExecuteCommand

        public static async void Execute(SocketSlashCommand arg, DockerService dockerService, DiscordSettings settings)
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

            if (settings.DockerSettings.DebugLogging)
            {
                // Debugging output
                Console.WriteLine("Allowed Containers (Admins):");
                foreach (var container in allowedContainers)
                {
                    Console.WriteLine(container);
                }
            }

            await DisplayContainers(dockerService, settings, arg, allowedContainers);
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

        private static async Task DisplayContainers(DockerService dockerService, DiscordSettings settings, SocketSlashCommand arg, List<string> allowedContainers)
        {
            int maxLength = dockerService.DockerStatusLongestName() + 1;
            if (maxLength > 28)
            {
                maxLength = 28;
            }

            int statusColumnLength = 8;
            int totalLength = maxLength + statusColumnLength + 4;

            string outputHeader = new string('-', totalLength + 1)
                            + "\n| Container Name"
                            + new string(' ', maxLength - 14)
                            + " | Status  |\n"
                            + new string('-', totalLength + 1)
                            + "\n";

            string outputFooter = new string('-', totalLength + 1) + "\n```";

            List<ContainerSection> sections;

            if (settings.AdminIDs.Contains(arg.User.Id))
            {
                // Admins can access all sections
                sections = dockerService.DockerStatus
                            .GroupBy(c => c.Labels.ContainsKey("section") ? c.Labels["section"] : "Uncategorized")
                            .Select(g => new ContainerSection
                            {
                                SectionName = g.Key,
                                Containers = g.ToList()
                            })
                            .ToList();
            }
            else
            {
                // Get user's allowed sections
                var allowedSections = new List<string>();
                foreach (var roleId in settings.RoleStartPermissions.Keys)
                {
                    if (settings.RoleStartPermissions[roleId].Any(section => allowedContainers.Contains(section)))
                    {
                        allowedSections.AddRange(settings.RoleStartPermissions[roleId]);
                    }
                }
                foreach (var roleId in settings.RoleStopPermissions.Keys)
                {
                    if (settings.RoleStopPermissions[roleId].Any(section => allowedContainers.Contains(section)))
                    {
                        allowedSections.AddRange(settings.RoleStopPermissions[roleId]);
                    }
                }
                allowedSections = allowedSections.Distinct().ToList();

                if (settings.DockerSettings.DebugLogging)
                {
                    // Debugging output
                    Console.WriteLine("Allowed Sections:");
                    foreach (var section in allowedSections)
                    {
                        Console.WriteLine(section);
                    }
                }

                sections = dockerService.DockerStatus
                            .Where(c => c.Labels.ContainsKey("section") && allowedSections.Contains(c.Labels["section"]))
                            .GroupBy(c => c.Labels["section"])
                            .Select(g => new ContainerSection
                            {
                                SectionName = g.Key,
                                Containers = g.ToList()
                            })
                            .ToList();
            }

            if (settings.SectionOrder != null && settings.SectionOrder.Any())
            {
                sections = sections
                            .OrderBy(s => settings.SectionOrder.IndexOf(s.SectionName))
                            .ToList();
            }
            else
            {
                sections = sections
                            .OrderBy(s => s.SectionName)
                            .ToList();
            }

            string combinedOutput = "";

            foreach (var section in sections)
            {
                combinedOutput += $"**{section.SectionName}**\n```\n" + outputHeader;
                combinedOutput += FormatListObjects(section.Containers, settings, maxLength, arg, allowedContainers);
                combinedOutput += outputFooter;
            }

            if (settings.DockerSettings.DebugLogging)
            {
                // Debugging output
                Console.WriteLine("Combined Output:");
                Console.WriteLine(combinedOutput);
            }

            if (combinedOutput.Length > 0)
            {
                await arg.ModifyOriginalResponseAsync(edit => edit.Content = combinedOutput);
            }
            else
            {
                await arg.ModifyOriginalResponseAsync(edit => edit.Content = "No containers found or you do not have permission to view them.");
            }
        }

        private class ContainerSection
        {
            public string SectionName { get; set; }
            public List<ContainerListResponse> Containers { get; set; }
        }
        
        private static string FormatListObjects(List<ContainerListResponse> list, DiscordSettings settings, int maxLength, SocketSlashCommand arg, List<string> allowedContainers)
        {
            string outputList = String.Empty;
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

        #endregion
    }
}
