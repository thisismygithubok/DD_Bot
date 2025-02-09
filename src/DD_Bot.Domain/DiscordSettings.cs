﻿/* DD_Bot - A Discord Bot to control Docker containers*/

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

using System.Collections.Generic;

namespace DD_Bot.Domain
{
    public class DiscordSettings
    {
        public string Token { get; set; } = "<- Please Insert Token here! ->";
        public List<ulong> AdminIDs { get; set; } = new List<ulong>();
        public bool UserWhitelist { get; set; } = true;
        //old settings
        public ulong[] UserIDs { get; set; } = System.Array.Empty<ulong>();
        public bool UsersCanStopContainers { get; set; } = false;
        public string[] AllowedContainers { get; set; } = System.Array.Empty<string>();
        
        //new settings
        public Dictionary<ulong, List<string>> RoleStartPermissions { get; set; } = new Dictionary<ulong, List<string>>();
        public Dictionary<ulong, List<string>> RoleStopPermissions { get; set; } = new Dictionary<ulong, List<string>>();
        public Dictionary<ulong, List<string>> UserStartPermissions { get; set; } = new Dictionary<ulong, List<string>>();
        public Dictionary<ulong, List<string>> UserStopPermissions { get; set; } = new Dictionary<ulong, List<string>>();
        public List<string> SectionOrder { get; set; } // New setting for section order
        public bool EnableMetrics { get; set; } = false; // New setting for passing host usage metrics to discord voice channel name
    }
}
