# Asset Database Contributor
Help crowdsource asset data for the SkylinQ Asset Database.

# IMPORTANT: Please read before subscribing...
Help build the public SkylinQ Asset Database by contributing information about the mods you have installed.

## This mod connects to the SkylinQ server
By subscribing to this mod, you acknowledge that it can communicate with the online SkylinQ server to upload asset information after you have given your consent.

This mod requires Simple Mod Checker Plus installed and active to access SMC's categorisation of subscribed mods. It also requires a logged-in Paradox account and only works on the public game build.

### Why contribute?
The SkylinQ Asset Database (work-in-progress, can be accessed from https://cities2.starq.fyi) is built entirely from community contribution. Every player who chooses to participate helps improve the database for everyone.

On first launch, you will be asked whether you want to contribute.

If you agree, you'll be given two options:
- Anonymous contribution: only asset data is uploaded.
- Public contribution: your user information is also uploaded so your contributions can appear on the public leaderboard. This is completely optional.

### Then what?
After consent is given, the mod will:
- Check whether the server requires data.
- Scan your subscribed public mods for missing asset/map/file information.
- Verify randomly selected packages using Simple Mod Checker Plus to ensure they match the published PDX Mods version.
- Randomly choose up to 100 verified packages (configuarble in Options).
- Extract the required asset data, localization, icons and other metadata.
- Upload only what the server doesn't already have.

This repeats periodically while you stay at the main menu. You can see progress notifications while it runs.

### What data is uploaded?
The uploads include various data about subscribed public mods. Private/unlisted mods, local assets and runtime generated assets are not processed.
The following data is extracted from your game:
- Prefab asset data
- Map data
- Mod's file list (relative paths and sizes only, not file contents)
- Images and localizations used on the prefab and map

If you choose Public contribution, your user information (PDX Username and UserId) is also included so your contributions can be credited publicly. As with any online service, the server can see your IP address.

# TL;DR
- No gameplay data, save data or local file paths are uploaded.
- Only public subscribed contents are scanned. Private/unlisted contents are skipped.
- Contribution is opt-in. User identity is optional.
- Consent can be revoked at any time from the Options UI for future sessions.
- Nothing starts once a map is opened in that session (Save or Editor).