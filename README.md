# PFBeaconPlugin

Public-safe Dalamud plugin for PFBeacon.

The plugin observes Party Finder listings, filters for qualifying MINE raid listings, and sends sanitized observations to the separately hosted PFBeacon bot service as a global-feed transmitter.

## Privacy/security rules

- The plugin never talks directly to Discord.
- The plugin never contains a Discord bot token, webhook URL, or server-side shared secret.
- Do not hardcode private API hostnames.
- Do not serialize PF descriptions, recruiter/player names, player worlds, host/player content IDs, Discord guild IDs, channel IDs, or routing instructions.
- API `listing.contentId` means duty/content-finder row ID, **not** Dalamud `IPartyFinderListing.ContentId`.

See `docs/spec.md` for the plugin implementation spec and `docs/api-contract.md` for the shared plugin/bot API contract.

## Development status

Current scaffold is focused on Phase 0/1:

1. Config UI and `/pfbeacon` command.
2. Party Finder listing subscription.
3. Sanitized logging-only feasibility spike.
4. Mapping/filtering helpers with explicit privacy guardrails.

Network submission should stay disabled until feasibility findings are documented. The config UI/docs must disclose that sanitized observations may update all Discord guilds subscribed to the bot service.

## Build

```bash
dotnet restore

dotnet build src/PFBeacon/PFBeacon.csproj -c Release
```

## Commit policy

Do not commit or push until the user has approved the proposed commit message.
