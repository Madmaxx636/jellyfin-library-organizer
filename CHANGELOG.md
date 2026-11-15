# Changelog

## 0.6.0 - Artwork for every collection

- **Posters, thumbs and backdrops for all your collections**, made automatically. The default is a mosaic of the posters of the titles inside, with the collection name, a title count and an accent colour picked from the name. The thumb and backdrop (used by home screen rows and the collection page) use the titles' backdrops. Collections with no artwork to draw from get a plain colour title card.
- Drawn on your server with the image library Jellyfin already ships; nothing is downloaded or sent anywhere.
- Redrawn only when a collection's contents or look change, never on every sync.
- Settings: "Make and keep up to date" (default), "Only where there is none", or "Off"; style "Mosaic of posters" or "Colour title card".
- Per collection: "Keep my own artwork for this collection" (never touched), and an optional accent colour (#RRGGBB).
- Each rule on the settings page shows the collection's current poster.
- Note: in the default mode this replaces the collage Jellyfin makes for a collection by itself. Use "Only where there is none", or "Keep my own artwork" on a collection, if you want to keep an image.
- The settings page moved to a new address again (page version 3) so a cached copy of the old page can't be reused; the server keeps the newer artwork settings if an old page saves.

## 0.5.1 - Settings that stick

Fixed
- **The Christmas fix could be undone by an old cached settings page.** Browsers can keep showing an old copy of the settings page after an update. A save from that old page doesn't know the newer fields, so it silently reset "Fields that must match" and the public list on every rule. Now the settings page has a new address, so the old copy is never reused, and the server ignores the missing fields when a save comes from an old page.
- The upgrade now tightens the Christmas Movies rule even if you had changed its tags or settings (rules set to "All" matching are left alone).

New
- A warning on any rule where a single match is enough ("Loose rule: one stray tag is enough") with a one-click "Require 2 fields" button.
- The settings page title shows its version ("settings page 0.5.1, version 2"), so you can tell you are not looking at an old cached copy.

## 0.5.0 - Fixing false positives

Fixed
- **Items never actually left a collection.** The sync removed them and then saved an out-of-date copy of the collection, which put them straight back. A title that stopped matching, or that you excluded, now really comes out on the next sync. Every earlier version had this bug.
- The stock Christmas Movies rule put hundreds of unrelated titles in (James Bond, Mary Poppins, anything with a stray "christmas" tag or "santa" in the title). See below.

New
- **Public database lists (Wikidata).** A rule can use curated genre lists from Wikidata, such as every film with the genre "Christmas film" (about 1,700 titles, matched by IMDb or TMDB id). A title on the list counts as a match by itself. Search for any genre by name on the rule. Needs internet; only a genre id is sent, nothing about your library. The list is cached for a week and the last copy is used if Wikidata can't be reached. Turn it off with "Use the public Wikidata database" in settings.
- **Corrections.** Each rule has a "Corrections" section: titles you mark "Never include" are always left out, and "Always include" adds titles the rule misses. Search for a title there, or click "Not a match" in the preview.
- **Preview shows why** each title matched (genre, tag, title word, public database).
- **Fields that must match.** Require 2 or 3 of your filled-in fields instead of just 1, so one stray tag is no longer enough.
- A "Christmas (curated)" template.

Changed
- The stock Christmas Movies rule now needs the Wikidata "Christmas film" list, or two of genre, tag and title word. It is updated once when you upgrade, only if you haven't changed it. If you had edited it, adjust it by hand: set "Fields that must match" to 2 and add the "Christmas film" public list.
- The sync logs what it adds and removes for every rule.

## 0.4.0 - Runs on its own

- Collections are now built without you pressing anything: about a minute after the server starts, about two minutes after movies or shows are added, and again when their metadata changes (for example after a metadata refresh).
- The automatic genre and country collections are now on by default (only for genres and countries with at least 5 items; change that in settings). Settings saved by 0.3.x get them switched on once when you update; turn them off again if you don't want them and the choice sticks.
- New setting "Sync about a minute after the server starts".
- An automatic collection with exactly the same items as one of your own rules is skipped, so "Movies from Philippines" no longer appears next to "Filipino Movies".

## 0.3.0 - Much more control

New
- **Leave-out fields** on every rule: exclude genres, tags, countries or title words. A rule can also be only exclusions ("everything except Horror").
- **Audio language rule** (Korean, kor and ko all mean the same), read from the audio tracks of movies.
- **Automatic collections**: one per genre ("Comedy Movies") and/or one per country ("Movies from Japan"), with a minimum item count and an option to show them on the home screen.
- **Home row sorting**: newest, name, rating, date added or random, in either direction, set per rule.
- **Preview matches** button on each rule, and a "What is in my library" panel listing your genres, tags, countries, studios and ratings with counts. Click a value to add it to a field.
- **Templates** for common rules: kids and family, top rated, decades, Korean movies, Japanese anime, Filipino audio, Halloween, documentaries, recently added.
- **Auto-sync**: collections update about two minutes after new movies or shows are added (can be turned off).
- **Cleanup option**: delete a collection when its rule is removed or disabled. Off by default; media files are never touched.
- Unit tests for the matching, language and sorting logic.

Fixed
- Default rules came back and duplicated every time the settings were loaded. Existing duplicates are cleaned up on the first start with this version. If you had deleted the Christmas Movies rule before, it may come back once; delete it again.
- Home rows sorted by release date now fall back to the production year when an item has no full premiere date.

## 0.2.1 - Settings are easy to find

- Library Organizer now has its own entry in the dashboard sidebar. Before, the settings page only opened from a direct link, so it looked like the plugin had no settings.
- New "Save, then sync collections now" button on the settings page, so changes show up straight away instead of at the next 4 AM run.

## 0.2.0 - Home screen rows

- Each rule can now show up as a row on the home screen through the Home Screen Sections plugin. Turn it off per rule with "Show on home screen".
- New setting for how many items each home row shows (default 16).
- Home rows are registered every time the server starts, after a collection sync, and when you save settings.
- Without Home Screen Sections installed the plugin works as before and just skips the rows.

## 0.1.0 - First version

- Rule-based collections built from genres, tags, production countries, studios, official ratings, title keywords, year range and minimum rating.
- Rules can match on any filled field or require all of them.
- Per-rule item order inside a collection (premiere date or name) and a position number to order the collections themselves.
- Settings page in the dashboard and a daily "Sync organizer collections" task.
- Ships with example rules for Filipino Movies and Christmas Movies.
