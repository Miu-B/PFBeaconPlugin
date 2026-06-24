# PFBeacon

<p align="center">
  <img src="src/PFBeacon/PFBeacon.png" alt="PFBeacon icon" width="256" />
</p>

Finding MINE Party Finder groups is mostly a timing problem. The listing you care about might appear while you're crafting, in a duty, alt-tabbed, or simply looking at the wrong Discord channel.

PFBeacon helps with that. The plugin watches Party Finder, filters for qualifying MINE raid listings, and contributes **sanitized** observations to the official PFBeacon service. Discord servers that have opted in can then receive compact alerts when relevant PFs appear.

The goal is simple: help people find old-content groups faster, while keeping descriptions, names, worlds, and Discord routing out of the plugin payload entirely.

## What it does

* Watches Party Finder listings when you open or refresh the Party Finder window
* Filters for 8-player Raid, Extreme, Savage, Ultimate, and Unreal listings
* Requires Minimum Item Level and No Echo
* Sends sanitized observations to the PFBeacon service
* Optionally polls the global PFBeacon feed for colored local in-game chat alerts
* Helps the Discord bot keep alerts active, stale, or deleted as PF listings change

## Privacy

PFBeacon sends only the data needed for alerts, such as duty name, data center, MINE flags, category, role/job slot summaries, timestamps, and listing IDs.

PFBeacon does **not** send:

* PF descriptions
* recruiter names
* player names
* player worlds
* host/player content IDs
* chat messages
* Discord guild IDs or channel IDs
* Discord bot tokens or webhooks

Submitted observations may update alert channels in all Discord guilds that have opted into the same PFBeacon bot service.

For in-game global feed alerts, PFBeacon receives only sanitized display fields. Known MSQ-spoiler duties are redacted by the server before the plugin receives them.

## Important behavior

PFBeacon does **not** continuously scan Party Finder in the background and does **not** query Square Enix servers on its own. It only sees listings when you open the Party Finder window, change filters, switch tabs, or refresh the list in-game.

The optional in-game global feed alert feature is different: it makes one batched, authenticated request to the PFBeacon service every few minutes for your selected data centers, then prints local system-chat alerts for new/updated sanitized feed items. Alerts use compact tags such as `[PFBeacon][New][Light]`, color the change tag, and make the duty title a Party Finder link when the listing can still be resolved by the game client.

So if nobody with the plugin opens or refreshes the Party Finder window, PFBeacon has nothing new to send to the global feed.

## Requirements

* Final Fantasy XIV with Dalamud
* A PFBeacon API token from the Discord bot command:

```text
/pf register
```

A Discord server admin also needs to invite and configure the PFBeacon bot before alerts can appear in that server.

## Installation

PFBeacon is **not** in the official Dalamud plugin repository. Install it as a third-party/custom plugin.

1. Open Dalamud settings in-game with `/xlsettings`
2. Go to `Experimental`
3. Add this custom plugin repository URL:

```text
https://raw.githubusercontent.com/Miu-B/PFBeaconPlugin/main/repo.json
```

4. Open the Plugin Installer with `/xlplugins`
5. Search for **PFBeacon** and install it

Dalamud may show the usual warning for third-party repositories. That's expected; only add repositories you trust.

## How to use

1. In Discord, run:

```text
/pf register
```

2. Copy the token from the bot's ephemeral response
3. In-game, open PFBeacon with:

```text
/pfbeacon
```

4. Paste the token into the **Token** field
5. Click **Test connection**
6. Enable **Contribute sanitized PF observations**
7. Optional: enable **Show local chat alerts from the global PFBeacon feed** and choose interested data centers, e.g. Light and Chaos

When you open or refresh Party Finder and qualifying MINE listings are visible, the plugin contributes sanitized observations to PFBeacon. If global feed alerts are enabled, the plugin also polls PFBeacon with jitter/backoff and prints local spoiler-safe system-chat alerts such as:

```text
[PFBeacon][New][Light] Lv70 Sigmascape V1.0 (Savage) - 1/8 filled - Need: 2 Tanks, 2 Healers, 3 DPS
```

The duty title is clickable where the game supports linking to the live Party Finder listing; expired listings or listings outside your current reachable data center may not open.

## Commands

* `/pfbeacon` - Toggle the configuration window
* `/pfbeacon status` - Write a local status line to the Dalamud log

## Development

Build a release package with:

```bash
dotnet build src/PFBeacon/PFBeacon.csproj -c Release
```

The Dalamud package is generated at:

```text
src/PFBeacon/bin/Release/PFBeacon/latest.zip
```

## License

MIT

## Credits

Based on the Dalamud plugin ecosystem and the `Dalamud.NET.Sdk` packaging flow by goatcorp.
