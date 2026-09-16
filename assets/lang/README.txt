FORGEHAVEN - translations (by humans, for humans)
================================================
The game ships in English. Any language can be added as a single text file,
translated by a person - the game never machine-translates.

HOW TO ADD A LANGUAGE
1. In-game: enable the DEV menu (Settings), open it (the "=" button),
   click "EXPORT LANGUAGE TEMPLATE".
   That writes assets/lang/template.txt listing every translatable string as:
       English text=
2. Copy template.txt next to itself and name it with the language code:
       ru.txt (Russian), tr.txt (Turkish), de.txt (German), ...
3. Translate the RIGHT side of each '='. Keep the left side untouched.
   Lines starting with '#' are comments. Example:
       New Game=
       Iron Plate=
       SAVE GAME=
4. Start the game -> Settings -> LANGUAGE cycles to your file.
   Missing lines simply stay English, so partial translations are fine.

NOTES
- UTF-8 encoding (Notepad default). Russian/Turkish characters just work.
- Upper/lower case matters: the left side must match the game text exactly.
- The file loads at startup and when switched in Settings - no rebuild needed.
- Tell the dev to wire more strings: in-game log messages and help text are
  not all in the template yet.
