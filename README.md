# Library Organizer for Jellyfin

Builds and orders Jellyfin collections automatically from rules, and can show them as rows on the home screen.

Tested on Jellyfin 10.11.

## What it does

- **Rules to collections.** A rule matches movies or series by genre, tag, production country, studio, official rating, title keywords, year range or minimum rating. Example rules ship for "Filipino Movies" (country: Philippines) and "Christmas Movies" (Holiday genre, christmas tag, or christmas/santa in the title).
- **Ordering.** Sort the titles inside a collection by release date, year, name, rating, runtime, date added or story order, ascending or descending. Story order is built in for Star Wars and Indiana Jones, and you can set your own for any collection with the arrows in its preview. A Position pin and a collection order (A to Z, newest, oldest, highest rated, biggest) decide where collections sit on the Collections page.
- **Artwork.** Each collection gets a poster, thumb and backdrop drawn from the artwork of the titles inside it, with its name on top (see Artwork below).
- **Home screen rows.** Each collection can appear as a row on the home screen (needs the plugins listed below).
- **Runs on its own.** A sync runs about a minute after the server starts, about two minutes after movies or shows are added or their metadata changes, and daily at 4 AM. New matches go in and items that no longer match come out. Nothing to click.
- **Automatic collections** (franchises, genres, and the countries you list) are on by default, so a fresh install organizes the library without any rules. They sort together on the Collections page: franchises, then countries, then genres. You can also run "Sync organizer collections" by hand from Dashboard, Scheduled Tasks.

## Settings

Open Dashboard and click Library Organizer in the left sidebar. The page title shows its version; if it doesn't match the plugin version you installed, hard-refresh the browser (Ctrl+Shift+R).

**Rules.** Each rule builds one collection:

- **Collection name.** Also the name of its home screen row.
- **Media type.** Movie, Series or Both.
- **Combine fields.** "Any" puts an item in if enough filled fields match. "All" needs every filled field to match. Several values inside one field (two genres, say) always mean "any of these".
- **Fields that must match.** With "Any", how many filled fields an item needs (1, 2 or 3). Setting 2 stops a single stray tag from putting an item in.
- **Public database lists.** Curated genre lists from Wikidata, for example "Christmas film". A title on the list counts as a match by itself (or as one field when Combine is "All"). Search for a genre by name and click Add.
- **Corrections.** "Never include" and "Always include" for individual titles. Search for a title, or click "Not a match" next to it in the preview. These win over every other setting.
- **Genres, Tags, Countries, Studios, Official ratings.** Comma separated, matched exactly (ignoring case).
- **Title keywords.** Matched anywhere in the title.
- **Audio languages.** Names or codes: Korean, ko and kor are the same. Movies only, read from the audio tracks.
- **Leave out.** Items matching any of these genres, tags, countries or title words are always excluded. A rule with only exclusions means "everything except these".
- **Year from / to, Min rating.** Extra limits that always apply.
- **Sort titles by, Direction.** Release date, year, name, rating, runtime, date added or story order; ascending or descending. Titles with no value always go last.
- **Pin to position.** Lower numbers come first among your collections. 0 means no pin.
- **Home row.** Show as a row, ordered like the collection or by its own order (or random), optionally hiding titles you have watched.
- **Preview matches.** Shows what the rule matches right now, and why each title matched, without changing anything. Click "Not a match" to exclude a title.

**Helpers.** The "What is in my library" panel lists the genres, tags, countries, studios and ratings you actually have, with counts. Click one to add it to the field you last used. "Add from template" gives starting points such as kids and family, decades, Korean movies and Halloween.

**Automatic collections.** On by default: franchise collections (see below), one collection per genre, and one per country you list in settings. Sort automatic collections as franchises, then countries, then genres, or as one A-Z list. Genres: build one collection per genre ("Comedy Movies"), only when there are at least N items. A manual rule with the same name wins, and an automatic collection with exactly the same items as one of your rules is skipped.

**General.**

- Remove items that stop matching.
- Re-sync about two minutes after movies or shows are added or their metadata changes, and about a minute after the server starts.
- Delete a collection when its rule is removed or disabled (off by default; media files are never touched).
- Minimum items for a collection, and items per home row.

Use "Save, then sync collections now" to apply changes immediately. Otherwise the daily 4 AM task and the auto-sync do it.

## Overview tab

Shows what the last sync did, "Preview what the next sync would change" (nothing is touched), and what is in your library. The Settings tab has a backup: download your collections and settings, or load them on another server.

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

## Sorting

Jellyfin sorts a collection page itself only by a few keys, ascending. For the rest (story order, rating, newest first) the plugin stores the collection in the order you chose and re-applies it on every sync, so the order shows up on the collection page and in home rows. Titles with no rating, runtime or date always go last.

Story order has a built-in list for Star Wars and Indiana Jones. For any other franchise, make a collection of your own with Franchises set, choose Story order, press Preview and move titles with the arrows. Titles you haven't placed follow release order.

## Franchises and countries

Collections are listed under their first letter on Jellyfin's Collections page. To find them quickly use the sort button (Date Added puts new ones first), the A-Z bar, or turn on "Show on home".

- **Franchises.** Movies that belong to the same franchise are grouped automatically (Star Wars, Indiana Jones, ...). This uses the collection name TMDb attaches to each movie, so it needs the TMDb metadata provider on your Movies library. The "What is in my library" panel lists the franchises it found. A franchise needs at least 2 movies by default, and its movies are in release order. If you use Jellyfin's own "automatically add to collection" option, those collections are left alone and not duplicated.
- **Countries.** Only the countries you list in settings get a collection: Philippines becomes "Filipino Movies", United Kingdom "British Movies", Japan "Japanese Movies", and countries without a stored adjective become "Movies from Peru". Nothing is made for countries you don't list.

## Artwork

For every collection the plugin makes a poster (600x900), a thumb and a backdrop (1280x720). The default style is a mosaic of the posters of the best-rated titles in the collection, with the collection's name, how many titles it holds, and an accent colour picked from the name (or set your own as #RRGGBB on the rule). The "Colour title card" style draws just the name on a colour gradient, and is also used when a collection has no artwork to draw from.

- **Modes.** "Make and keep up to date" redraws a collection's images whenever its contents or look change. "Only where there is none" fills gaps and never replaces an image that is already there. "Off" does nothing.
- **Keep my own artwork.** A per-collection checkbox. The plugin never touches that collection's images, so you can upload your own.
- Drawn on the server with the image library Jellyfin ships, using fonts already installed on the server. Nothing is downloaded or sent anywhere. Images are only redrawn when something changed.
- In the default mode this replaces the collage Jellyfin makes for a collection by itself.

## Privacy

The only thing that leaves your server is a request to Wikidata (query.wikidata.org and www.wikidata.org) for a genre list or a genre search, and it contains just the genre id or the text you typed. Nothing about your library is sent. Turn it off with "Use the public Wikidata database" in settings; rules that use a public list then fall back to their other fields.

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
