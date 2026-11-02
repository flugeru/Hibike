# Privacy Policy

Effective date: November 2, 2026

This policy describes how Hibike, the open-source music player maintained by Flugel at https://github.com/flugeru/Hibike, handles information. Features vary by platform and build, including experimental builds. Sections about optional integrations apply only when those integrations are available and used.

## Information stored on your device

Hibike stores settings, favorites, playback queue and session information locally. Builds with listening history, playlists or personalization also store those records locally. Imported audio may be copied into the application's private storage, together with metadata and artwork. Removing a favorite does not necessarily delete the imported file.

Hibike does not operate an account server, advertising service or analytics backend. It does not intentionally send your local library or listening history to the maintainer. Playback diagnostics may contain track titles, URLs and error details; review them before sharing them publicly.

## Online search, playback and artwork

Online features contact third-party services, including YouTube/Google and the servers hosting audio and artwork. Requests can disclose your IP address, search terms, requested media identifiers and ordinary connection information to those services. Radio features, where available, may send the selected track identifier or a metadata-based search to obtain related tracks.

Some desktop builds look up artwork for local audio through Apple's iTunes service using artist and album names. This lookup does not upload the audio file. Third-party services process requests under their own policies.

## Optional Google and YouTube authorization

Builds supporting Google sign-in use your authorization to read your YouTube library, such as playlists and liked videos. The requested YouTube scope is `https://www.googleapis.com/auth/youtube.readonly`. Hibike does not use this permission to modify your YouTube account, upload content or post on your behalf.

Google access tokens are used to make authorized library requests and are held in memory in current sign-in builds. They are not provided to yt-dlp or used as credentials for media extraction. Google authorization data is not sold, used for advertising, or used to train generalized artificial intelligence or machine-learning models. It is not shared with other parties except as needed to make the Google API requests you initiate, as required by law, or with your explicit consent.

Hibike's use and transfer of information received from Google APIs will adhere to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including its Limited Use requirements.

You can disconnect within the application where supported and revoke authorization at https://myaccount.google.com/permissions. Revocation stops future authorized access; it does not automatically erase local copies of information already saved on your device.

Older builds may allow importing a YouTube cookie file. Treat it as sensitive account information. Where this feature exists, the file is stored locally and may be passed to the media extractor for requests to YouTube. Newer builds may not offer or use this feature. Remove imported cookies through the available settings or by deleting the corresponding local file.

## Discord and operating-system integrations

Desktop Discord Rich Presence can share the current title, artist, artwork and playback timing with your Discord client and Discord. Other people may see this activity according to your Discord settings. On supported desktop builds, setting `HIBIKE_DISCORD_RPC=0` disables this integration and its associated local-track artwork lookup.

Some experimental Android builds offer an unofficial Discord integration using a personal token. If enabled, the token is held in memory and used to connect to Discord; activity information is sent to Discord. Personal tokens are sensitive credentials, and using this integration may violate Discord's terms. Do not publish or share tokens.

System media controls and Android playback notifications expose track metadata and artwork to your operating system and may show them on the lock screen.

## Retention, deletion and control

Local records remain until removed through available application controls or by deleting the application's data. On Android, clearing app storage or uninstalling normally removes private app data. On desktop, application data may remain after deleting the executable and must be removed separately. Files exported outside private storage and operating-system backups may remain until you delete them separately.

You can avoid online requests by not using online features, disconnect optional integrations and revoke Google authorization independently. Third-party retention and deletion are governed by those providers, not by Hibike.

## Security and children

No software or storage method guarantees absolute security. Protect your device, backups and credentials. Hibike is not directed specifically to children and does not knowingly collect children's personal information through a maintainer-operated service.

## External policies

- Google Privacy Policy: https://policies.google.com/privacy
- YouTube Terms of Service: https://www.youtube.com/t/terms
- Discord Privacy Policy: https://discord.com/privacy
- Apple Privacy Policy: https://www.apple.com/legal/privacy/

## Changes and contact

Updates to this policy will be published in this repository with a revised effective date. For privacy questions or deletion assistance, contact the maintainer through https://github.com/flugeru/Hibike/issues. GitHub issues are public: never include tokens, cookies, private files or other sensitive information. The maintainer cannot remotely access or delete data stored only on your device.
