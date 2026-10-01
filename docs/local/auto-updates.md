# Local host updates

Plan Cope Local checks Central for an update after the host UI starts and every four hours. A failed automatic check is silent; **Buscar actualizaciones** shows an error if a manual check fails, including when no update feed is configured. An available release is offered in Spanish, and the operator can start the download or defer it. If an exam session is active, the host shows a non-blocking status and presents the prompt after the session ends. The host verifies the package SHA-256 before applying it. After the operator accepts, the host waits for active exam sessions to close before restarting into the update.

The default update feed is `Central:BaseUrl` from the local API configuration plus `/api/updates`. `PLANCOPE_UPDATE_FEED_URL` can override that feed base URL, and `PLANCOPE_UPDATE_CHANNEL` selects `stable` or `beta` (default `stable`). The host sends its existing node access JWT to both the feed and package endpoint.

## Release assets and Central configuration

The release workflow publishes each Velopack `.nupkg` and `releases.<channel>.json` beside the installer in the existing private GitHub release. Central reads `FileName`, `SHA256`, `Size`, and `Version` from that channel feed and streams package bytes from the matching published GitHub release. Central caches release/feed lookups for five minutes. Draft releases are ignored; stable only uses published stable releases, while beta only uses published prereleases. Package downloads require a valid node access token and must match the exact version currently authorized by the rollout gate.

Configure the Central API with the existing private release settings:

- `PLANCOPE_PRIVATE_INSTALLER_REPO`: the private GitHub repository in `owner/repo` form.
- `INSTALLER_REPO_TOKEN`: a server-side token with read access to that repository's releases and assets. The same token is used for installers and update packages; it is never shipped to Local.

The release workflow also requires its existing `INSTALLER_REPO_TOKEN` secret and `PLANCOPE_PRIVATE_INSTALLER_REPO` Actions variable with write access for publishing. No new secret value is required.

By default, the newest published release on each channel is offered to all enrolled nodes when that node's installed version is older. No release ring or manual SHA-256/URL entry is needed for ordinary releases. An optional release ring overrides the channel's target version and rollout policy for staged rollout or a hold; it only needs the version, channel, rollout mode, and optional percentage. The version must exist in the published channel feed. Central uses the feed metadata and gate decision to build the Velopack feed and validate the later package request.

Installers already at version 1.0.8 cannot self-update into this implementation. Each such installation needs one manual install of the first release containing the updater changes; later releases can update in-app.
