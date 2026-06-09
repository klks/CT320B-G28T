UI languages
============

Each *.json file in this folder is one UI language and appears in the language
picker at the bottom-left of the app. The file name (without .json) is the
language code, e.g. en.json, fr.json, zh-Hans.json.

Add your own language
---------------------
1. Copy en.json to <code>.json  (e.g. it.json for Italian, ja.json for Japanese).
   Use a .NET culture code where one exists so dates/numbers localise too.
2. Set "name" to the language's display name (shown in the picker).
3. Translate the values under "strings". Leave the keys unchanged.
   Any key you omit falls back to English, so partial files are fine.
4. Restart the app.

Notes
-----
- {0}, {1} etc. are placeholders filled in at runtime — keep them in the text.
- Don't translate the keys, font names, or barcode/symbology names.
- Files whose name starts with "_" are ignored (not treated as a language).
- Editing a shipped file (e.g. fr.json) changes that built-in language.
