# Local host updates

Plan Cope Local checks Central for an update after the host UI starts and every four hours. A failed automatic check is silent; **Buscar actualizaciones** shows an error if a manual check fails. An available release is offered in Spanish, and the operator can start the download or defer it. The host verifies the package SHA-256 before applying it. After the operator accepts, the host waits for active exam sessions to close before restarting into the update.

The default update feed is `Central:BaseUrl` from the local API configuration plus `/api/updates`. `PLANCOPE_UPDATE_FEED_URL` can override that feed base URL, and `PLANCOPE_UPDATE_CHANNEL` selects `stable` or `beta` (default `stable`). The host sends its existing node access JWT to both the feed and package endpoint.

## Release assets and Central configuration

The release workflow publishes each Velopack `.nupkg` and `releases.<channel>.json` beside the installer in the existing private GitHub release. Central streams package bytes from that private repository after checking the node access token. Package assets are only served from `GET /api/updates/{fileName}` and the endpoint accepts `.nupkg` file names only.

Configure the Central API with the existing private release settings:

- `PLANCOPE_PRIVATE_INSTALLER_REPO`: the private GitHub repository in `owner/repo` form.
- `INSTALLER_REPO_TOKEN`: a server-side token with read access to that repository's releases and assets. The same token is used for installers and update packages; it is never shipped to Local.

The release workflow also requires its existing `INSTALLER_REPO_TOKEN` secret and `PLANCOPE_PRIVATE_INSTALLER_REPO` Actions variable with write access for publishing. No new secret value is required.

Before enrolling nodes in a release ring, publish that release and configure its ring with the channel, version, package SHA-256, and a `DownloadUrl` whose final path segment is the full package asset name, for example `https://<central-host>/api/updates/PlanCope.Local.Host-<version>-full.nupkg`. Central uses the final segment when it builds the Velopack feed; the client then downloads through the authenticated Central proxy. Keep ring rollout policy as the authority for which nodes see the release.

Installers already at version 1.0.8 cannot self-update into this implementation. Each such installation needs one manual install of the first release containing the updater changes; later releases can update in-app.
