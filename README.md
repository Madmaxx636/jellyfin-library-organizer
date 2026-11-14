# Library Organizer for Jellyfin

Builds and orders Jellyfin collections automatically from rules, and can show them as rows on the home screen.

Tested on Jellyfin 10.11.

## What it does

- **Rules to collections.** A rule matches movies or series by genre, tag, production country, studio, official rating, title keywords, year range or minimum rating. Example rules ship for "Filipino Movies" (country: Philippines) and "Christmas Movies" (Holiday genre, christmas tag, or christmas/santa in the title).
- **Ordering.** Each rule picks how items are sorted inside its collection (premiere date or name) and has a Position number that sets where the collection sits among your other collections.
- **Home screen rows.** Each collection can appear as a row on the home screen (needs the plugins listed below).
- **Stays current.** A daily task (4 AM) adds new matches and removes items that no longer match. You can also run "Sync organizer collections" by hand from Dashboard, Scheduled Tasks.

## Install

1. Download `LibraryOrganizer_<version>.zip` from the Releases page. Check it against the SHA-256 in the release notes.
2. Unzip into `plugins/LibraryOrganizer_<version>/` inside your Jellyfin data folder.
3. Restart Jellyfin, then open Dashboard, Plugins, Library Organizer to edit your rules.
4. Run "Sync organizer collections" once, or wait for the nightly run.

## Home screen rows

These need three other plugins:

- [Home Screen Sections](https://github.com/IAmParadox27/jellyfin-plugin-home-sections) (version for your Jellyfin release)
- File Transformation, which Home Screen Sections requires to inject its rows
- Plugin Pages, optional, for per-user overrides

After installing them, enable the organizer rows in Home Screen Sections' settings (Dashboard, Plugins, Home Screen Sections), then reload the web client. Rows are named after your rules.

## Build

Needs Docker, nothing else:

```bash
./build.sh
```

This writes `LibraryOrganizer_<version>.zip` and prints its SHA-256.
