<h1 align="center">DD_Bot</h1>
<h1 align="center">This custom fork has added categories/sections based on container labels</h1>

<p align="center">A Discord-Bot to start and stop Docker Containers, using the Docker Socket</p>
<p align="center">
<a href="https://hub.docker.com/r/thisismynameok/docker-discord-bot"><img alt="Docker Image Size (tag)" src="https://img.shields.io/docker/image-size/thisismynameok/docker-discord-bot/latest?style=for-the-badge">
<img alt="Docker Pulls" src="https://img.shields.io/docker/pulls/thisismynameok/docker-discord-bot?style=for-the-badge"></a>
<img alt="GitHub commit activity" src="https://img.shields.io/github/commit-activity/m/thisismygithubok/DD_Bot?color=brightgreen&style=for-the-badge">
<img alt="GitHub" src="https://img.shields.io/github/license/thisismygithubok/DD_Bot?style=for-the-badge"></p>

`"Conveniently, the program itself can be used as a Docker Container"` - ***Gadget Gabe*** \
**NEW: Now with commands to adjust permissions** 

## NEW: Container Labels for Section Outputs

- Add a label to your docker container via the labels instruction

    ```yml
    {
    container:
        image: ...
        ...
        labels:
            section: "Game Servers"

    container2:
        image: ...
        ...
        labels:
            section: "Frontend"
    }
- Add a new section in settings.json's "DiscordSettings" to set this order based on labels

    ```json
    {
    "DiscordSettings": {
        "Token": "",
        "AdminIDs": [],
        "UserWhitelist": true,
        "UserIDs": [],
        "UsersCanStopContainers": true,
        "AllowedContainers": [],
        "RoleStartPermissions": {},
        "RoleStopPermissions": {},
        "UserStartPermissions": {},
        "UserStopPermissions": {},
        "SectionOrder": [
            "Game Servers",
            "Frontend"
        ]
    }
    }
- Output of /list will now separate into separate tables based on these labels and ordering
![List Command Sections](pics/ListCommandSections.png)

## Screenshots

![Show Status of Containers](pics/Listcommand.png)
![Structured Settings File](pics/Settings.png)
![Send Command to Server](pics/Dockercommand.png)
![Bot's reply to command](pics/Dockerstart.png)

## Features

- Remotely start and stop Docker Containers using Discord Slash Commands
- Easily grant Users and Groups on your Discord access to selected containers
- Enable Friends to start specified Containers, e.g. Gameservers
    - Save Energy when noone is playing
- DD_Bot is designed to work on the same machine in its own Container
- Easy configuration through a single json file
- Built using [Discord.NET](https://github.com/discord-net/Discord.Net) and [Docker.DotNet](https://github.com/dotnet/Docker.DotNet)

## Requirements

- Docker
- a correctly configured bot from [Discord Developer Portal](https://discord.com/developers/), instructions can be found [here](/sites/discordbot.md)
- Internet connection

## [Installation](/sites/installation.md)

## [Settings](/sites/settings.md)

## [Commands](/sites/commands.md)

## [FAQ/Troubleshooting](/sites/faq.md)

## To-Do List

- [x] Initial release
- [x] Rewrite for docker sockets
- [x] Auto-updates for the settings.json Files
- [x] Commands to grant and revoke privileges to users and groups
- [ ] Fully customizable messages for Discord
- [ ] More statistics
- [ ] \(Maybe\) Auto-Shutdown for certain containers
- [ ] \(Maybe\) more command options
- [ ] \(Maybe\) implement RCON to control gameservers


### If you like my work, feel free to buy me a coffee
<p>
<br><a href="https://www.buymeacoffee.com/assaro"> <img align="left" src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" height="50" width="210" alt="assaro" /></a></p><br>

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for more details.

## Attribution

This project is a fork of the original repository by https://github.com/Assaro/DD_Bot. Significant modifications have been made by https://github.com/thisismygithubok/DD_Bot on 1 Feb 2025. Changes include: Added categories/sections based on container labels.

