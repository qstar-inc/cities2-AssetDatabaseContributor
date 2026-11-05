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

## License Notes
This project is licensed under GPLv3 (see LICENSE). A quick summary of what that means in practice:

- You're free to use, modify, and redistribute this code.
- Any modified version you distribute must also be licensed under GPLv3 (copyleft). This means you can't use this code in a closed-source project.
- Please retain attribution to the original author when redistributing or forking.

### Forks and Redistribution
This mod is distributed exclusively via Paradox Mods, and I actively maintain it there.

If you'd like to contribute, please consider submitting a PR or reaching out instead of publishing a separate copy or fork.

I kindly ask that you do **not**:
- Upload this mod, or any fork of it, to Nexus Mods or any platform other than Paradox Mods, under any circumstances.
- Publish it as a separate listing on Paradox Mods, unless the original mod is abandoned and I'm unresponsive to contact for an extended period.

If you do publish a fork under those circumstances, please:
- Clearly mark it as a fork/unofficial version (not the original),
- Link back to this repository and credit the original work,
- Follow GPLv3's requirements for source availability and licensing.

This is a request from the maintainer, not an added legal restriction beyond what GPLv3 already requires. It does not modify or limit any rights granted under the GPLv3 license, which remains the sole binding license for this Software.

Reposting an actively maintained mod under a new listing, without need, fragments the community and support for users.