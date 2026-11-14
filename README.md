# Library Organizer for Jellyfin

Builds and orders Jellyfin collections automatically from rules, and can show them as rows on the home screen.

Tested on Jellyfin 10.11.

## What it does

- **Rules to collections.** A rule matches movies or series by genre, tag, production country, studio, official rating, title keywords, year range or minimum rating. Example rules ship for "Filipino Movies" (country: Philippines) and "Christmas Movies" (Holiday genre, christmas tag, or christmas/santa in the title).
- **Ordering.** Each rule picks how items are sorted inside its collection (premiere date or name) and has a Position number that sets where the collection sits among your other collections.
- **Home screen rows.** Each collection can appear as a row on the home screen (needs the plugins listed below).
- **Runs on its own.** A sync runs about a minute after the server starts, about two minutes after movies or shows are added or their metadata changes, and daily at 4 AM. New matches go in and items that no longer match come out. Nothing to click.
- **Automatic genre and country collections** are on by default, so a fresh install organizes the library without any rules. You can also run "Sync organizer collections" by hand from Dashboard, Scheduled Tasks.

## Settings

Open Dashboard and click Library Organizer in the left sidebar.

**Rules.** Each rule builds one collection:

- **Collection name.** Also the name of its home screen row.
- **Media type.** Movie, Series or Both.
- **Combine fields.** "Any" puts an item in if any filled field matches. "All" needs every filled field to match. Several values inside one field (two genres, say) always mean "any of these".
- **Genres, Tags, Countries, Studios, Official ratings.** Comma separated, matched exactly (ignoring case).
- **Title keywords.** Matched anywhere in the title.
- **Audio languages.** Names or codes: Korean, ko and kor are the same. Movies only, read from the audio tracks.
- **Leave out.** Items matching any of these genres, tags, countries or title words are always excluded. A rule with only exclusions means "everything except these".
- **Year from / to, Min rating.** Extra limits that always apply.
- **Order on the collection page.** Premiere date or name.
- **Position.** Lower numbers come first among your collections. 0 means no forced position.
- **Show on home screen, sorted by.** Newest, name, rating, date added or random, in either direction.
- **Preview matches.** Shows what the rule matches right now without changing anything.

**Helpers.** The "What is in my library" panel lists the genres, tags, countries, studios and ratings you actually have, with counts. Click one to add it to the field you last used. "Add from template" gives starting points such as kids and family, decades, Korean movies and Halloween.

**Automatic collections.** On by default: build one collection per genre ("Comedy Movies") and/or per country ("Movies from Japan"), only when there are at least N items. A manual rule with the same name wins, and an automatic collection with exactly the same items as one of your rules is skipped.

**General.**

- Remove items that stop matching.
- Re-sync about two minutes after movies or shows are added or their metadata changes, and about a minute after the server starts.
- Delete a collection when its rule is removed or disabled (off by default; media files are never touched).
- Minimum items for a collection, and items per home row.

Use "Save, then sync collections now" to apply changes immediately. Otherwise the daily 4 AM task and the auto-sync do it.

## Install

### From the plugin repository (easiest)

1. In Jellyfin, open Dashboard, Plugins, Repositories, Add.
2. Name it anything, and use this URL: `https://raw.githubusercontent.com/Madmaxx636/jellyfin-library-organizer/main/manifest.json`
3. Open the Catalog, find Library Organizer under General, install it, and restart Jellyfin.

### Manually

1. Download `LibraryOrganizer_<version>.zip` from the Releases page. Check it against the SHA-256 in the release notes.
2. Unzip into `plugins/LibraryOrganizer_<version>/` inside your Jellyfin data folder.
3. Restart Jellyfin, then open Dashboard, Plugins, Library Organizer to edit your rules.
4. That's it. Collections build themselves about a minute after the restart. To force it, use "Save, then sync collections now" on the settings page.

## Home screen rows

These need three other plugins:

- [Home Screen Sections](https://github.com/IAmParadox27/jellyfin-plugin-home-sections) (version for your Jellyfin release)
- File Transformation, which Home Screen Sections requires to inject its rows
- Plugin Pages, optional, for per-user overrides

After installing them, enable the organizer rows in Home Screen Sections' settings (Dashboard, Plugins, Home Screen Sections), then reload the web client. Rows are named after your rules.

## Build and test

Needs Docker, nothing else:

```bash
./build.sh
```

This writes `LibraryOrganizer_<version>.zip` and prints its SHA-256. Run the tests with:

```bash
docker run --rm -v "$PWD":/w -w /w mcr.microsoft.com/dotnet/sdk:9.0 dotnet test tests/Jellyfin.Plugin.Organizer.Tests
```
