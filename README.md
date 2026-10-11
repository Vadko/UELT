<img width="1845" height="1021" alt="image" src="https://github.com/user-attachments/assets/404c6592-3647-45ec-835e-90a41060a018" />

## Консольна версія (uelt-cli)

`uelt-cli` — консольна версія UELT для Windows, Linux і macOS (Apple Silicon). Вона використовує той самий код читання й запису форматів, що й програма з інтерфейсом. Файли для завантаження є в кожному релізі:

- `UELT-CLI-<версія>-win-x64.exe`
- `UELT-CLI-<версія>-linux-x64.tar.gz`
- `UELT-CLI-<версія>-osx-arm64.tar.gz`

На macOS після завантаження зніміть карантин: `xattr -d com.apple.quarantine uelt-cli`.

Довідка: `uelt-cli help`, `uelt-cli help <команда>` або `uelt-cli <команда> --help`.

| Команда | Що робить |
|---|---|
| `unpack` | розпаковує .locres/.uasset/.umap у CSV (`key,source,Translation`), TXT `ID=текст` або TXT лише з текстом |
| `pack` | запаковує переклад з CSV, TXT, locres або uasset/umap у нові файли гри |
| `info` | показує версію locres, версію рушія, кейс uasset, мови й кількість рядків |
| `stats` | рахує статистику перекладу, як «Загальна статистика» |
| `search` | шукає у файлах, як «Глобальний пошук» |
| `replace` | знаходить і замінює текст у перекладі, як «Заміна» |
| `check` | шукає типові помилки перекладу і перевіряє вживання глосарію |
| `tm` | працює з пам’яттю перекладів: `list`, `create`, `delete`, `activate`, `deactivate`, `target`, `record`, `apply` |
| `glossary` | працює з глосарієм: `list`, `add`, `remove`, `export`, `check` |
| `create-locres` | створює locres з TXT (`--version 0..4`) |
| `merge-locres` | додає до locres рядки з інших locres |
| `add-rows` | додає до locres нові рядки з TXT |
| `remove-rows` | видаляє рядки з locres за ID |
| `import-hashes` | імпортує хеші тексту з locres або TXT |
| `compare-locres` | порівнює два locres і зберігає `<ім’я>_new.txt` та `<ім’я>_changed.csv` |

### Приклади

```sh
# locres -> CSV -> перекладений locres (без -o результат буде Game_N.locres)
uelt-cli unpack Game.locres
uelt-cli pack Game.locres Game.csv

# Ціла тека: дерево тек зберігається у виході
uelt-cli unpack Content/Localization -o csv/ --format txt
uelt-cli pack Content/Localization csv/ -o build/

# Не версійований uasset: версія рушія, mappings і мова
# (версійовані ассети відкриваються автоматично, --engine для них не потрібен)
uelt-cli unpack ST_Items.uasset --engine 5.3 --mappings Game.usmap --lang uk
uelt-cli pack ST_Items.uasset ST_Items.csv --engine 5.3 --mappings Game.usmap --lang uk -o out/ST_Items.uasset

# Пам’ять перекладів і перевірки
uelt-cli tm create Основна
uelt-cli tm record uk/Game.locres --original-from en/Game.locres
uelt-cli tm apply Game.locres -o Game_tm.locres
uelt-cli check Game.locres -t Game.csv --glossary Glossary.json
```

### Головні правила

- **Файли й теки.** Кожна команда приймає файл або теку; теку обходить рекурсивно. Помилка в одному файлі не зупиняє обробку решти.
- **Тека-джерело.** Переклад для файлу шукається за тим самим відносним шляхом (як після `unpack` теки). Пошук за ім’ям по всій теці, як у GUI, працює для одного файлу або плоскої теки (`unpack --flat`).
- **Імпорт.** `--by auto`: CSV за порядком, якщо він збігається з файлом рядок у рядок, інакше за ID; TXT за ID або за порядком (txt-plain). TXT з `unpack` імпортується без змін тексту; `--txt-escapes` перетворює `\n`, `\t` на переноси, як імпорт TXT у GUI. Якщо жоден рядок джерела не збігся, файл не записується (код 3).
- **Перезапис.** Наявні файли не перезаписуються без `--overwrite`. Щоб змінити самі оригінали, додайте `--in-place`.
- **Вивід.** У stdout друкуються лише результати. Повідомлення, попередження й помилки йдуть у stderr.
- **Файли .STATUS.** CLI читає і записує їх лише з параметром `--status`.
- **CV2.** Шифрування CV2 вмикається параметрами `--cv2` або `--cv2-demo` (замість налаштувань GUI).
- **Пам’ять перекладів і глосарій.** Типово вони лежать поруч із програмою (`TranslationMemories/`, `Glossary.json`), як у GUI. Інше місце задають `--tm-dir` і `--glossary`.

### Коди виходу

| Код | Значення |
|---|---|
| `0` | успіх |
| `1` | помилка |
| `2` | неправильні аргументи |
| `3` | виконано частково: помилки в частині файлів, немає відповідного файлу чи жоден рядок джерела не збігся, або в пакеті не оброблено жодного файлу (для `check` — знайдено проблеми) |
| `4` | не версійований uasset/umap: потрібен `--engine` |
