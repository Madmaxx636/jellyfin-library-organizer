# Changelog

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
