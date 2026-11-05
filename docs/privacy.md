---
layout: default
title: Privacy Policy
permalink: /privacy.html
---

# Privacy Policy

Effective date: November 2, 2026

This policy covers Hibike, the open-source music player maintained by Flugel at [github.com/flugeru/Hibike](https://github.com/flugeru/Hibike). Features vary by platform and build. Optional integrations described below apply only when available and used.

## Local information

Settings, favorites, playback queues and sessions are stored on your device. Builds with history, playlists or personalization also store those records locally. Imported audio may be copied into private app storage with metadata and artwork. Removing a favorite does not necessarily delete its audio file.

Hibike has no maintainer-operated account server, advertising service or analytics backend. It does not intentionally upload your local library or listening history to the maintainer. Diagnostics can contain titles, URLs and errors; review them before sharing.

## Online requests

Search, playback, radio and artwork contact third-party services, including YouTube/Google and media hosts. They receive your IP address, connection information and the search terms, media identifiers or metadata needed for each request. Some desktop builds look up local-track artwork through Apple's iTunes service using artist and album names, without uploading the audio file. Providers apply their own privacy and retention policies.

## Google and YouTube authorization

Builds with Google sign-in request `https://www.googleapis.com/auth/youtube.readonly` to display your YouTube library, such as playlists and liked videos. Hibike does not use this permission to modify your account, upload content or post on your behalf.

Official OAuth access tokens remain in memory. Current Android/Windows web sign-in builds retain the Google web session encrypted on the device (Android Keystore AES-GCM / Windows DPAPI for the current user), outside Hibike library backups. Disconnect removes the saved session. Temporary login browser data is cleared. Session cookies grant session access, not a read-only OAuth scope. Library data is refreshed while the app is open and on manual refresh; Google can expire or revoke the session and require sign-in again. Credentials are not supplied to yt-dlp or used for media extraction, logged, sold, used for advertising, or used to train generalized AI or machine-learning models. They are not shared except to make authorized Google/YouTube Music requests, as required by law, or with your explicit consent.

Hibike's use and transfer of information received from Google APIs will adhere to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including its Limited Use requirements.

Disconnect in the app where supported or revoke authorization at [Google account permissions](https://myaccount.google.com/permissions). Revoking access stops future authorized requests but does not erase previously saved local data.

Older builds may support importing YouTube cookie files, stored locally and passed to the extractor for YouTube requests. Cookies are sensitive account information. Remove them through available settings or delete the local file. Newer builds may not offer or use this feature.

## Discord and system controls

Desktop Discord Rich Presence shares current title, artist, artwork and playback timing with Discord. Visibility depends on your Discord settings. Supported desktop builds accept `HIBIKE_DISCORD_RPC=0` to disable presence and its related local-artwork lookup.

Some experimental Android builds offer an unofficial personal-token Discord integration. The token connects to Discord and publishes activity there. If you choose to remember it, Hibike encrypts it on your Android device using Android Keystore and excludes the file from backups. You can remove it from the Discord connection screen. Personal tokens are sensitive; this integration may violate Discord's terms and risk account restrictions. Never share tokens.

Operating-system media controls and Android notifications receive metadata and artwork, which may appear on your lock screen.

## Retention and deletion

Local data remains until removed through available controls or deleted from app storage. Android clearing of app storage or uninstallation normally removes private data. Desktop data may remain after deleting the executable. Exported files and backups require separate deletion. The maintainer cannot remotely access or delete device-only data.

Avoid online features to avoid their requests, disable optional integrations, and revoke Google access independently. Third-party data deletion is subject to each provider's policies.

## Security and children

No software guarantees absolute security. Protect your device, backups and credentials. Hibike is not directed specifically to children and does not knowingly collect children's personal information through a maintainer-operated service.

## Third-party policies

- [Google Privacy Policy](https://policies.google.com/privacy)
- [YouTube Terms of Service](https://www.youtube.com/t/terms)
- [Discord Privacy Policy](https://discord.com/privacy)
- [Apple Privacy Policy](https://www.apple.com/legal/privacy/)

## Changes and contact

Updates will be published here with a revised effective date. For privacy questions or deletion assistance, [contact the maintainer](https://github.com/flugeru/Hibike/issues). Issues are public: never include tokens, cookies, private files or sensitive personal information.
