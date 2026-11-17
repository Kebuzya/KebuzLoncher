# Repository and release layout

The public repository is `Kebuzya/KebuzLoncher`. The launcher and modpacks share
its releases and tags. KebuzPublisher and its private settings stay outside
the public source repository.

| Location | Purpose | Updated by |
| --- | --- | --- |
| `main` | Launcher source, documentation and licenses | Developer |
| `catalog/manifest.json` | Launcher version and HTTPS download page | Launcher release process |
| `catalog/config.json` | Current games, modpack versions and patch URLs | KebuzPublisher |
| Releases | Launcher EXEs, modpack ZIPs and patches | Developer and KebuzPublisher |

## Initial transition

1. Preserve the existing GitHub history, tags, releases and asset URLs. Integrate
   the existing remote history before publishing local source; do not replace it
   with an unrelated history or force-push over it.
2. Create `catalog` and put both live JSON files at its root. Copy the current
   remote `main/config.json` into `catalog/config.json`, keeping game IDs, versions,
   complete patch chains and download URLs. Remove launcher update fields from
   the game catalog. Prepare `catalog/manifest.json` using `manifest.example.json`,
   but advertise only an already published, downloadable launcher release.
   Source files are not required in the catalog branch.
3. Add the launcher source to `main`. Keep live JSON files in `catalog`; the
   source branch contains only `config.example.json` and `manifest.example.json`
   as templates. Never replace live data with example game entries. Exclude
   Publisher, secrets, certificates, build output and the unverified local icon.
4. Use the rebuilt Publisher. In its existing private settings, change only
   `config_branch` to `catalog`; keep the repository and `config_path` unchanged.
   New settings default to `catalog`; existing settings retain their old value.
5. Publish the launcher EXE as an asset of a release such as `launcher-v1.2.0`.
   Check that the asset is downloadable before publishing the corresponding
   version and URL in `catalog/manifest.json`. Mark the launcher release Latest.
6. Distribute the new launcher directly to users. Legacy clients do not need to
   remain functional. Remove obsolete `main/config.json`, `main/manifest.json`
   and the old root-level EXE after the new files and release are verified.

The local `manifest.example.json` is prepared for version 1.2.0. Its release URL is a
deployment target, not confirmation that the release already exists.

## Automated builds and launcher releases

`.github/workflows/build.yml` builds and verifies the launcher on Windows 2022
for main branch pushes, pull requests and manual dispatch. Its artifact contains
only the EXE. Desktop and Aero regression checks remain local.

For a new launcher release, update `LauncherVersion.Number`, commit and push
the change, then push a matching tag such as `launcher-v1.2.1`. The release
workflow verifies the tag against the EXE, checks packaging, uploads a draft
asset and verifies its digest before publication. Only after a verified public
download does it update `catalog/manifest.json`. It never accesses `config.json`.
The workflow uses `GITHUB_TOKEN` with contents write permission; do not add
Publisher credentials to Actions secrets. Modpack tags do not trigger it.
Reruns preserve matching published assets and reject different contents or
attempts to replace a newer launcher manifest.

## Runtime behavior

The launcher reads the game catalog and the launcher manifest independently.
Both URLs point to the `catalog` branch. Each request has its own timeout.
A failed or invalid response falls back to that feed's separate local cache.
The launcher never queries obsolete files in `main`.
The catalog is displayed from cache while fresh data loads. Closing the window
cancels pending feed requests.

Launcher update text uses embedded English and Russian `.resx` resources.
Both resource sets are embedded in the main EXE, without satellite DLLs.
Other screens retain their existing localization behavior.

## Modpack publishing and compatibility

Keep the complete patch chain from the base archive. At the configured patch
limit, Publisher requires a new full modpack rather than dropping old entries.
If an older Publisher already truncated a chain, publish a new full modpack
before extending it. The version of a legacy full ZIP cannot be inferred
reliably from the remaining patch list alone.

When configured for `catalog`, Publisher updates only `catalog/config.json`.
It never reads or writes a compatibility copy in `main`, and it does not modify
`catalog/manifest.json`. The catalog editor rejects stale file revisions rather
than overwriting changes made after it was opened.

New modpack releases use `make_latest: "false"`. The existing `{gameId}-latest`
tags and replacement behavior are retained. Keep those releases mutable.
For archived versions, use additional tags such as
`modpack-letcomp-2026.11.14.1`. Launcher releases use `launcher-v1.2.0` and may
be marked Latest. Releases remain shared across branches.

## Local verification and deployment status

Local preparation does not create a GitHub branch, add a remote, commit, push,
or publish a release. Perform those operations separately when authorized.
Verify both catalog branch URLs, independent loading, offline caches, notification
dismissal and isolated Publisher writes before rollout. Before distributing the EXE,
complete the single-file packaging and migration checks. License notices are
embedded and accessible from the application. The developer-supplied
`KebuzLoncher.ico` must be included in the export and embedded in the EXE.
