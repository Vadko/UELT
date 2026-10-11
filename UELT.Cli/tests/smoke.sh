#!/usr/bin/env bash
# Smoke test for uelt-cli. Runs on Linux, macOS and Windows (Git Bash).
#
#   UELT.Cli/tests/smoke.sh <path-to-uelt-cli[.exe]>
#
# Exercises every command on the committed fixtures (locres v0..v4 + a CV2-encrypted v2 locres,
# all generated with `uelt-cli create-locres` from fixtures/sample.txt, and two tiny StringTable
# .uasset files built from scratch by tests/fixture-gen) and checks round trips, batch mode and
# exit codes. No third-party game assets are used.

set -u

if [ $# -lt 1 ]; then
    echo "usage: $0 <path-to-uelt-cli>" >&2
    exit 2
fi

CLI_DIR=$(cd "$(dirname "$1")" && pwd)
CLI="$CLI_DIR/$(basename "$1")"
FIXTURES=$(cd "$(dirname "$0")/fixtures" && pwd)
WORK=$(mktemp -d 2>/dev/null || mktemp -d -t uelt-smoke)
export MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*'
# Git Bash: pass Cyrillic arguments/file names to the native exe as UTF-8.
case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) export LANG=C.UTF-8 LC_ALL=C.UTF-8 ;; esac
export DOTNET_CLI_TELEMETRY_OPTOUT=1

PASS=0
FAIL=0
FAILED_NAMES=()

cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

cd "$WORK" || exit 1
cp -R "$FIXTURES" fx

u() { "$CLI" "$@"; }

ok()   { PASS=$((PASS + 1)); echo "  ok   $1"; }
bad()  { FAIL=$((FAIL + 1)); FAILED_NAMES+=("$1"); echo "  FAIL $1" >&2; }

# check <name> <command...>: passes if the command succeeds.
check() {
    local name=$1; shift
    if "$@" >/dev/null 2>>"$WORK/stderr.log"; then ok "$name"; else bad "$name"; fi
}

# code <name> <expected-exit-code> <cli-args...>
code() {
    local name=$1 expected=$2; shift 2
    u "$@" >/dev/null 2>>"$WORK/stderr.log"
    local got=$?
    if [ "$got" -eq "$expected" ]; then ok "$name (exit $got)"; else bad "$name (expected exit $expected, got $got)"; fi
}

# Normalise line endings (Windows writes CRLF for TXT, CsvHelper writes CRLF everywhere).
norm() { tr -d '\r' < "$1"; }
same_text() { [ "$(norm "$1")" == "$(norm "$2")" ]; }
has() { norm "$1" | grep -qF -- "$2"; }

grep -v '^#' fx/sample.txt | tr -d '\r' > expected.txt

echo "== general"
check "--version prints a version" bash -c "'$CLI' --version | grep -Eq '^uelt-cli [0-9]+\.[0-9]+'"
code "help" 0 help
code "help pack" 0 help pack
code "pack --help" 0 pack --help
code "no arguments" 2
code "unknown command" 2 frobnicate
code "unknown option" 2 unpack fx/locres/sample_v2.locres --frobnicate
code "missing file" 1 unpack does-not-exist.locres
code "bad --engine" 2 info fx/locres/sample_v2.locres --engine 9.9
code "--mappings without --engine" 2 info fx/locres/sample_v2.locres --mappings fx/sample.txt

echo "== create-locres (deterministic, equals committed fixtures)"
for v in 0 1 2 3 4; do
    u create-locres fx/sample.txt -o "created_v$v.locres" --version $v -q >/dev/null 2>>stderr.log
    check "create-locres v$v == fixture" cmp "created_v$v.locres" "fx/locres/sample_v$v.locres"
done
code "create-locres refuses to overwrite" 1 create-locres fx/sample.txt -o created_v0.locres --version 0

echo "== info"
for v in 0 1 2 3 4; do
    check "info --json v$v" bash -c "'$CLI' info fx/locres/sample_v$v.locres --json | tr -d '\r' | grep -q '\"version\": $v'"
done
check "info cv2" bash -c "'$CLI' info fx/locres/sample_v2_cv2.locres --cv2 --json | tr -d '\r' | grep -q '\"cv2Encrypted\": true'"

echo "== unpack / pack round trips"
for v in 0 1 2 3 4; do
    f=fx/locres/sample_v$v.locres
    u unpack "$f" --format txt -o "v$v.txt" -q >/dev/null 2>>stderr.log
    check "unpack txt v$v == sample" same_text "v$v.txt" expected.txt
    u unpack "$f" -o "v$v.csv" -q >/dev/null 2>>stderr.log
    check "unpack csv v$v header" has "v$v.csv" '"key","source","Translation"'
    u pack "$f" "v$v.csv" -o "same_v$v.locres" -q >/dev/null 2>>stderr.log
    check "pack unchanged csv v$v is byte-identical" cmp "same_v$v.locres" "$f"
    u pack "$f" "v$v.txt" -o "same_txt_v$v.locres" -q >/dev/null 2>>stderr.log
    check "pack unchanged txt v$v is byte-identical" cmp "same_txt_v$v.locres" "$f"
    # modified translation must show up after re-unpack
    norm "v$v.txt" | sed -e 's/^Menu::Start=.*/Menu::Start=Почати гру/' > "tr_v$v.txt"
    u pack "$f" "tr_v$v.txt" -o "tr_v$v.locres" -q >/dev/null 2>>stderr.log
    u unpack "tr_v$v.locres" --format txt -o "tr_v$v.out.txt" -q >/dev/null 2>>stderr.log
    check "pack modified txt v$v round trip" same_text "tr_v$v.out.txt" "tr_v$v.txt"
done

f=fx/locres/sample_v2.locres
check "unpack -o - writes to stdout" bash -c "'$CLI' unpack $f --format txt -o - | tr -d '\r' | diff -q - expected.txt"
u unpack $f --format txt-plain -o plain.txt -q >/dev/null 2>>stderr.log
check "unpack txt-plain" has plain.txt "Привіт, світе — «тест» ✓"
check "pack txt-plain by order" bash -c "'$CLI' pack $f plain.txt -o plain.locres -q --by order >/dev/null && cmp plain.locres $f"

# CSV import modes
norm v2.csv | sed -e 's/^"Menu::Quit","Quit",""$/"Menu::Quit","Quit","Вийти"/' > csv_mod.csv
for by in id order source id+source auto; do
    u pack $f csv_mod.csv -o "csv_$by.locres" --by "$by" -q >/dev/null 2>>stderr.log
    check "pack csv --by $by" bash -c "'$CLI' unpack csv_$by.locres --format txt -o - | grep -q '^Menu::Quit=Вийти'"
done
code "pack txt --by source is a usage error" 2 pack $f tr_v2.txt -o x.locres --by source

# default output name and overwrite protection
cp $f game.locres
u pack game.locres tr_v2.txt -q >/dev/null 2>>stderr.log
check "pack default output <base>_N.locres" test -f game_N.locres
code "pack refuses to overwrite" 1 pack game.locres tr_v2.txt
code "pack --overwrite" 0 pack game.locres tr_v2.txt --overwrite
code "pack --in-place" 0 pack game.locres tr_v2.txt --in-place
check "pack --in-place changed the file" bash -c "'$CLI' unpack game.locres --format txt -o - | grep -q '^Menu::Start=Почати гру'"
code "--in-place with -o is a usage error" 2 pack game.locres tr_v2.txt --in-place -o y.locres

# CV2: decrypt on load, re-encrypt on save
c=fx/locres/sample_v2_cv2.locres
check "cv2 unpack decrypts" bash -c "'$CLI' unpack $c --cv2 --format txt -o - | tr -d '\r' | diff -q - expected.txt"
u unpack $c --cv2 -o cv2.csv -q >/dev/null 2>>stderr.log
u pack $c cv2.csv --cv2 -o cv2_same.locres -q >/dev/null 2>>stderr.log
check "cv2 pack unchanged is byte-identical" cmp cv2_same.locres $c
u pack $c tr_v2.txt --cv2 -o cv2_tr.locres -q >/dev/null 2>>stderr.log
check "cv2 pack modified round trip" bash -c "'$CLI' unpack cv2_tr.locres --cv2 --format txt -o - | grep -q '^Menu::Start=Почати гру'"

# .STATUS files
u pack $f tr_v2.txt -o st.locres --status -q >/dev/null 2>>stderr.log
check "pack --status writes .STATUS" has st.STATUS '"Menu::Start": "NeedsReview"'
u pack $f tr_v2.txt -o nost.locres -q >/dev/null 2>>stderr.log
check "pack without --status writes no .STATUS" test ! -e nost.STATUS

echo "== batch"
mkdir -p in/a in/b/c
cp fx/locres/sample_v2.locres in/a/Game.locres
cp fx/locres/sample_v3.locres in/b/c/Game.locres
cp fx/locres/sample_v4.locres in/b/Other.locres
u unpack in -o csv -q >/dev/null 2>>stderr.log
check "batch unpack mirrors tree" test -f csv/a/Game.csv -a -f csv/b/c/Game.csv -a -f csv/b/Other.csv
u unpack in -o flat --flat -q >/dev/null 2>>stderr.log
check "batch unpack --flat uses _2 on clashes" test -f flat/Game.csv -a -f flat/Game_2.csv -a -f flat/Other.csv
norm csv/b/c/Game.csv | sed -e 's/^"Menu::Options","Options",""$/"Menu::Options","Options","Налаштування"/' > tmp.csv && mv tmp.csv csv/b/c/Game.csv
u pack in csv -o build > batch.out 2>>stderr.log
check "batch pack exit 0" test $? -eq 0
check "batch pack prints summary" has batch.out "Загалом змінено 1"
check "batch pack unchanged file is byte-identical" cmp build/a/Game.locres in/a/Game.locres
check "batch pack modified file" bash -c "'$CLI' unpack build/b/c/Game.locres --format txt -o - | grep -q '^Menu::Options=Налаштування'"
code "batch pack without -o is a usage error" 2 pack in csv
rm csv/b/Other.csv
code "batch pack with an unmatched file exits 3" 3 pack in csv -o build2
check "unmatched file is not written" test ! -e build2/b/Other.locres
mkdir -p bad && cp in/a/Game.locres bad/Good.locres && printf 'not a locres' > bad/Broken.locres
code "batch continues after a broken file" 3 unpack bad -o bad_out
check "good file still exported" test -f bad_out/Good.csv
code "single broken file exits 1" 1 info bad/Broken.locres

echo "== original-from"
u pack $f tr_v2.txt -o uk.locres -q >/dev/null 2>>stderr.log
u unpack uk.locres --original-from $f -o uk.csv -q >/dev/null 2>>stderr.log
check "unpack --original-from fills source + translation" has uk.csv '"Menu::Start","Start Game","Почати гру"'

echo "== stats"
check "stats json counts translated rows" bash -c "'$CLI' stats $f -t tr_v2.txt --json | tr -d '\r' | grep -q '\"translatedRows\": 1'"
check "stats text summary" bash -c "'$CLI' stats in | grep -q 'Файлів: 3'"
cp st.locres appr.locres
printf '{"key_mode":"id","rows":{"Menu::Start":"Approved"}}' > appr.STATUS
check "stats --status counts approved rows" bash -c "'$CLI' stats appr.locres --original-from $f --status --json | tr -d '\r' | grep -q '\"approvedRows\": 1'"

echo "== search"
check "search text" bash -c "'$CLI' search sword $f | grep -q 'Items::Sword'"
check "search --in id --regex finds exactly 3 rows" bash -c "test \"\$('$CLI' search '^Menu::' $f --regex --in id | grep -c .)\" -eq 3"
check "search in translation source" bash -c "'$CLI' search 'Почати' $f -t tr_v2.txt --in translation | grep -q 'Menu::Start'"
check "search --whole-word does not match a part of a word" bash -c "test -z \"\$('$CLI' search Swo $f --whole-word)\""
check "search is case-insensitive by default" bash -c "'$CLI' search SWORD $f --in translation | grep -q 'Items::Sword'"
check "search --case finds nothing for a different case" bash -c "test -z \"\$('$CLI' search SWORD $f --case --in translation)\""
check "search --json" bash -c "'$CLI' search Quit in --json | tr -d '\r' | grep -q '\"id\": \"Menu::Quit\"'"

echo "== replace"
check "replace --dry-run" bash -c "'$CLI' replace $f --find sword --replace blade --dry-run | grep -q 'Iron blade'"
u replace $f --find sword --replace blade --whole-word -o rep.locres -q >/dev/null 2>>stderr.log
check "replace writes output" bash -c "'$CLI' unpack rep.locres --format txt -o - | grep -q '^Items::Sword=Iron blade'"
u replace $f --find zzz --replace y -o rep_none.locres -q >/dev/null 2>>stderr.log
check "replace without matches writes nothing" test ! -e rep_none.locres
code "replace without --find" 2 replace $f --replace x

echo "== check"
norm tr_v2.txt | sed -e 's/^Menu::Quit=.*/Menu::Quit=Вийти  з гри/' > err.txt
code "check finds errors " 3 check $f -t err.txt
check "check reports double space" bash -c "'$CLI' check $f -t err.txt | grep -q 'Подвійний пробіл'"
code "check clean " 0 check $f -t tr_v2.txt

echo "== glossary"
G=gl/Glossary.json
mkdir -p gl
code "glossary add" 0 glossary add shield щит --glossary $G
code "glossary add variant" 0 glossary add Shield щитом --glossary $G
code "glossary add second" 0 glossary add sword меч --glossary $G
check "glossary list" bash -c "'$CLI' glossary list --glossary $G | grep -q 'щит / щитом'"
check "glossary file is GUI format" has $G '"Original": "shield"'
code "glossary export csv" 0 glossary export gl.csv --glossary $G
check "glossary export content" has gl.csv '"sword","меч"'
norm tr_v2.txt | sed -e 's/^Items::Shield=.*/Items::Shield=Дерев’яна броня/' -e 's/^Items::Sword=.*/Items::Sword=Залізний меч/' > gl_tr.txt
code "glossary check finds a missing term " 3 glossary check $f -t gl_tr.txt --glossary $G
check "glossary check message" bash -c "'$CLI' glossary check $f -t gl_tr.txt --glossary $G | grep -q 'Items::Shield'"
code "check --glossary-only" 3 check $f -t gl_tr.txt --glossary $G --glossary-only
code "glossary remove variant" 0 glossary remove shield щитом --glossary $G
code "glossary remove entry" 0 glossary remove shield --glossary $G
check "glossary after remove" bash -c "! '$CLI' glossary list --glossary $G | grep -q shield"

echo "== translation memory"
TM=tm
code "tm record without memory fails" 1 tm record uk.locres --original-from $f --tm-dir $TM
code "tm create" 0 tm create Основна --tm-dir $TM
code "tm create duplicate" 1 tm create Основна --tm-dir $TM
code "tm create invalid name" 1 tm create 'a:b' --tm-dir $TM
check "tm files in GUI format" test -f "$TM/Основна.tm.json" -a -f "$TM/_active.json"
code "tm record" 0 tm record uk.locres --original-from $f --tm-dir $TM
check "tm record content" has "$TM/Основна.tm.json" '"Start Game":["Почати гру"]'
check "tm list" bash -c "'$CLI' tm list --tm-dir $TM | grep -q 'Основна'"
u tm apply $f --tm-dir $TM -o tm_applied.locres -q >/dev/null 2>>stderr.log
check "tm apply fills untranslated rows" bash -c "'$CLI' unpack tm_applied.locres --format txt -o - | grep -q '^Menu::Start=Почати гру'"
u tm apply $f --tm-dir $TM --export csv -o tm_applied.csv -q >/dev/null 2>>stderr.log
check "tm apply --export csv" has tm_applied.csv '"Menu::Start","Start Game","Почати гру"'
code "tm deactivate" 0 tm deactivate Основна --tm-dir $TM
code "tm apply without active memories" 1 tm apply $f --tm-dir $TM -o none.locres
code "tm apply --memory" 0 tm apply $f --tm-dir $TM --memory Основна -o tm_mem.locres
code "tm create second" 0 tm create Друга --tm-dir $TM
code "tm target" 0 tm target Друга --tm-dir $TM
code "tm delete" 0 tm delete Друга --tm-dir $TM

echo "== locres operations"
printf 'Menu::Start=Duplicate\nNew::Row1=First new row\nNew::Row2=Second<lf>line\n' > new.txt
u add-rows $f new.txt -o added.locres -q >/dev/null 2>>stderr.log
check "add-rows adds 2 rows" bash -c "'$CLI' info added.locres --json | tr -d '\r' | grep -q '\"entries\": 13'"
check "add-rows keeps existing values" bash -c "'$CLI' unpack added.locres --format txt -o - | grep -q '^Menu::Start=Start Game'"
u create-locres new.txt -o other.locres --version 2 -q >/dev/null 2>>stderr.log
u merge-locres $f other.locres -o merged.locres -q >/dev/null 2>>stderr.log
check "merge-locres adds only new IDs" bash -c "test \"\$('$CLI' unpack merged.locres --format txt -o - | grep -c .)\" -eq 13"
printf 'New::Row1=whatever\n# comment\n' > ids.txt
u remove-rows added.locres New::Row2 --ids-file ids.txt -o removed.locres -q >/dev/null 2>>stderr.log
check "remove-rows restores the original" cmp removed.locres $f
u import-hashes $f tr_v2.txt -o hashed.locres -q >/dev/null 2>>stderr.log
check "import-hashes from txt changes hashes" bash -c "! cmp -s hashed.locres $f"
u import-hashes hashed.locres $f -o unhashed.locres -q >/dev/null 2>>stderr.log
check "import-hashes from locres restores hashes" cmp unhashed.locres $f
u compare-locres $f added.locres -o cmp/added > cmp.out 2>>stderr.log
check "compare-locres summary" has cmp.out "Нових рядків: 2"
check "compare-locres _new.txt" has cmp/added_new.txt "New::Row1=First new row"
u compare-locres $f uk.locres -o cmp/uk -q >/dev/null 2>>stderr.log
check "compare-locres _changed.csv" has cmp/uk_changed.csv '"Menu::Start","Start Game","Почати гру"'
check "compare-locres hash-only change" bash -c "'$CLI' compare-locres $f hashed.locres -o cmp/h | grep -q 'Прихованих змін хешів тексту: 1'"

echo "== txt import edge cases"
# txt-plain whose lines contain '=' must still be imported by line order
printf 'A::One=First line\nA::Two=Formula: x=1\nA::Three=Third\n' > eq.txt
u create-locres eq.txt -o eq.locres -q >/dev/null 2>>stderr.log
u unpack eq.locres --format txt-plain -o eq_plain.txt -q >/dev/null 2>>stderr.log
norm eq_plain.txt | sed -e '1s/.*/ПЕРШИЙ/' > eq_plain_mod.txt
u pack eq.locres eq_plain_mod.txt -o eq_auto.locres -q >/dev/null 2>>stderr.log
check "txt-plain with '=' (auto) keeps the edit" bash -c "'$CLI' unpack eq_auto.locres --format txt -o - | grep -q '^A::One=ПЕРШИЙ'"
check "txt-plain with '=' (auto) keeps '=' text" bash -c "'$CLI' unpack eq_auto.locres --format txt -o - | grep -q '^A::Two=Formula: x=1'"
u pack eq.locres eq_plain_mod.txt -o eq_order.locres --by order -q >/dev/null 2>>stderr.log
check "txt-plain with '=' (--by order) keeps '=' text" bash -c "'$CLI' unpack eq_order.locres --format txt -o - | grep -q '^A::Two=Formula: x=1'"
# literal backslash sequences survive txt round trips (CSV sets them literally)
printf '"key","source","Translation"\n"Menu::Quit","Quit","Path C:\\new\\table and \\t tab"\n' > bs.csv
u pack $f bs.csv -o bs.locres -q >/dev/null 2>>stderr.log
u unpack bs.locres --format txt -o bs.txt -q >/dev/null 2>>stderr.log
check "literal backslashes exported as is" has bs.txt 'Menu::Quit=Path C:\new\table and \t tab'
u pack bs.locres bs.txt -o bs_id.locres -q >/dev/null 2>>stderr.log
check "txt (id) round trip keeps literal backslashes" cmp bs_id.locres bs.locres
u pack bs.locres bs.txt -o bs_order.locres --by order -q >/dev/null 2>>stderr.log
check "txt (--by order) round trip keeps literal backslashes" cmp bs_order.locres bs.locres
u unpack bs.locres --format txt-plain -o bs_plain.txt -q >/dev/null 2>>stderr.log
u pack bs.locres bs_plain.txt -o bs_plain.locres -q >/dev/null 2>>stderr.log
check "txt-plain round trip keeps literal backslashes" cmp bs_plain.locres bs.locres
u pack bs.locres bs.txt -o bs_esc.locres --txt-escapes -q >/dev/null 2>>stderr.log
check "--txt-escapes converts \\n like the GUI" bash -c "'$CLI' unpack bs_esc.locres --format txt -o - | grep -q '^Menu::Quit=Path C:<lf>ew<tb>able'"
# an empty translation (ID=) stays empty with --by order
u pack $f v2.txt -o empty_order.locres --by order -q >/dev/null 2>>stderr.log
check "txt --by order keeps empty 'ID=' lines empty" cmp empty_order.locres $f
# partial sources
printf 'Menu::Options=Опції\n' > one.txt
u pack $f one.txt -o one.locres -q >/dev/null 2>>stderr.log
check "one-line txt is imported by ID" bash -c "'$CLI' unpack one.locres --format txt -o - | grep -q '^Menu::Options=Опції'"
printf '"key","source","Translation"\n"Menu::Quit","Quit","Вихід"\n"Dialog::Hello","Hello","Привіт"\n"Dialog::Hello","Hello","Вітаю"\n' > partial.csv
u pack $f partial.csv -o partial.locres -q >/dev/null 2>>stderr.log
check "partial csv with a repeated key goes to the right rows" bash -c "'$CLI' unpack partial.locres --format txt -o - | tr -d '\r' | grep -qx 'Menu::Quit=Вихід'"
check "partial csv leaves other rows alone" bash -c "'$CLI' unpack partial.locres --format txt -o - | tr -d '\r' | grep -qx 'Menu::Start=Start Game'"
printf 'Foo::Bar=x\nFoo::Baz=y\n' > foreign.txt
code "txt with only unknown IDs is unmatched" 3 pack $f foreign.txt -o foreign.locres
check "unmatched source writes nothing" test ! -e foreign.locres

echo "== uasset (synthetic StringTable)"
ua=fx/uasset/ST_UeltSmoke.uasset
uu=fx/uasset/ST_UeltSmoke_Unversioned.uasset
check "info uasset" bash -c "'$CLI' info $ua --json | tr -d '\r' | grep -q '\"assetType\": \"StringTable\"'"
u unpack $ua --format txt -o ua.txt -q >/dev/null 2>>stderr.log
check "unpack uasset" has ua.txt 'UELT.Smoke::Menu.Quit=Quit'
u unpack $ua -o ua.csv -q >/dev/null 2>>stderr.log
u pack $ua ua.csv -o ua_same.uasset -q >/dev/null 2>>stderr.log
check "pack unchanged uasset is byte-identical" cmp ua_same.uasset $ua
norm ua.txt | sed -e 's/^UELT.Smoke::Menu.Quit=.*/UELT.Smoke::Menu.Quit=Вийти/' > ua_tr.txt
u pack $ua ua_tr.txt -o ua_tr.uasset -q >/dev/null 2>>stderr.log
check "pack modified uasset round trip" bash -c "'$CLI' unpack ua_tr.uasset --format txt -o - | grep -q '^UELT.Smoke::Menu.Quit=Вийти'"
code "versioned uasset ignores --engine" 0 unpack $ua --engine 5.3 -o ua_eng.csv
code "unversioned uasset without --engine" 4 info $uu
u unpack $uu --engine 4.13 -o uu.csv -q >/dev/null 2>>stderr.log
u pack $uu uu.csv --engine 4.13 -o uu_same.uasset -q >/dev/null 2>>stderr.log
check "pack unchanged unversioned uasset is byte-identical" cmp uu_same.uasset $uu
u pack $uu ua_tr.uasset --engine 4.13 -o uu_tr.uasset -q >/dev/null 2>>stderr.log
check "import translations from another uasset" bash -c "'$CLI' unpack uu_tr.uasset --engine 4.13 --format txt -o - | grep -q '^UELT.Smoke::Menu.Quit=Вийти'"
mkdir -p mixed && cp $ua $uu mixed/
code "mixed batch without --engine" 4 unpack mixed -o mixed_out
code "mixed batch with --engine opens both" 0 unpack mixed --engine 4.13 -o mixed_out2

echo "== batch edge cases"
mkdir -p tree/Loc/en tree/Loc/de tree/csv/en
cp $f tree/Loc/en/Game.locres && cp $f tree/Loc/de/Game.locres && cp partial.csv tree/csv/en/Game.csv
code "culture tree: a CSV of another folder is not used" 3 pack tree/Loc tree/csv -o tree/build
check "culture tree: unmatched culture not written" test ! -e tree/build/de/Game.locres
mkdir -p flatsrc && cp partial.csv flatsrc/Game.csv
code "flat source folder applies by name" 0 pack tree/Loc flatsrc -o tree/build_flat
mkdir -p nest && cp $f nest/a.locres
u pack nest one.txt -o nest/out -q >/dev/null 2>>stderr.log
code "output folder inside the input folder is not re-read" 0 pack nest one.txt -o nest/out --overwrite
check "no nested output" test ! -e nest/out/out
code "pack -o - is a usage error" 2 pack $f one.txt -o -
check "no file named -" test ! -e ./-
echo precious > keep.STATUS
code "--status refuses to overwrite an existing .STATUS" 1 pack $f one.txt -o keep.locres --status
check ".STATUS left untouched" has keep.STATUS precious
u remove-rows $f Menu::Start Menu::Options Menu::Quit Items::Sword Items::Sword_Desc Items::Shield Dialog::Hello Dialog::Unicode Dialog::Tabs Dialog::Empty NoNamespaceKey -o allgone.locres -q >/dev/null 2>>stderr.log
mkdir -p empties && cp allgone.locres empties/a.locres && cp allgone.locres empties/b.locres
code "batch where every file is skipped exits 3" 3 unpack empties -o empties_out
u info bad/Broken.locres >/dev/null 2>broken.err
check "broken file error is Ukrainian" has broken.err "пошкоджено"

echo "== more locres operations"
mkdir -p mT/x mT/y mS/x mS/y
printf 'A::1=a1\n' > m1.txt; printf 'B::1=b1\n' > m2.txt; printf 'A::2=a2\n' > m3.txt; printf 'B::2=b2\n' > m4.txt
u create-locres m1.txt -o mT/x/G.locres -q >/dev/null 2>>stderr.log
u create-locres m2.txt -o mT/y/G.locres -q >/dev/null 2>>stderr.log
u create-locres m3.txt -o mS/x/G.locres -q >/dev/null 2>>stderr.log
u create-locres m4.txt -o mS/y/G.locres -q >/dev/null 2>>stderr.log
u merge-locres mT mS -o mM -q >/dev/null 2>>stderr.log
check "merge-locres folders pair files by path" bash -c "test \"\$('$CLI' unpack mM/x/G.locres --format txt -o - | tr -d '\r' | tr '\n' ' ')\" = 'A::1=a1 A::2=a2 '"
u compare-locres $f added.locres -o cmpj/first -q >/dev/null 2>>stderr.log
check "compare-locres --json does not export" bash -c "cd cmpj && '$CLI' compare-locres ../$f ../added.locres --json | tr -d '\r' | grep -q '\"key\": \"New::Row1\"' && test ! -e ../added_new.txt"
mkdir -p tmbad && echo '{bad' > tmbad/Broken.tm.json
code "tm list with a broken memory" 0 tm list --tm-dir tmbad
code "tm create next to a broken memory" 0 tm create Good --tm-dir tmbad
code "tm record into a broken memory fails" 1 tm record uk.locres --original-from $f --tm-dir tmbad --memory Broken
check "broken memory file untouched" has tmbad/Broken.tm.json '{bad'

echo
echo "passed: $PASS, failed: $FAIL"
if [ "$FAIL" -gt 0 ]; then
    printf '  - %s\n' "${FAILED_NAMES[@]}" >&2
    echo "--- stderr log ---" >&2
    tail -n 40 "$WORK/stderr.log" >&2
    exit 1
fi
exit 0
