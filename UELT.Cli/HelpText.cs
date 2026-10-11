using UELT.Hikaro;

namespace UELT.Cli;

/// <summary>Help texts (Ukrainian, like the GUI).</summary>
public static class HelpText
{
    private const string Exe = "uelt-cli";

    private const string AssetOpts = """
    Відкриття файлів:
      --cv2                 розшифрувати/зашифрувати locres CV2 (ключі Release)
      --cv2-demo            те саме з ключами Demo
      --engine <версія>     версія рушія для не версійованих uasset/umap (як у GUI, спершу
                            завжди автовизначення; версійовані ассети --engine не потребують):
                            auto, 4.0..4.27, 5.0EA, 5.0..5.8, VER_UE4_27, VER_UE5_3
                            або теги GUI 0..27, 500EA, 500..508
      --mappings <файл>     .usmap/.jmap/.jmap.gz для не версійованих ассетів (потребує --engine)
      --lang <мова>         мова для ассетів з кількома мовами (без неї — усі мови).
                            Для unpack і pack вказуйте ту саму мову: вона змінює ID і набір рядків.
    """;

    private const string StatusOpt = """
      --status              читати <файл>.STATUS при відкритті і записувати поруч із результатом
                            (у GUI увімкнено за замовчуванням, у CLI — лише з цим параметром)
    """;

    private const string SourceOpts = """
    Джерела:
      -t, --translation <файл|тека>  спершу застосувати переклад (як pack, без збереження)
      --by <режим>          auto (типово), id, order, source, id+source (див. pack)
      --src-format <ф>      auto, csv, txt, locres, uasset, umap — тип файлів у теці-джерелі
      --txt-escapes         TXT: перетворювати \n, \r, \t на переноси/табуляцію (як імпорт TXT у GUI)
      --original-from <файл|тека>    імпорт у стовпчик «Оригінал» з locres/uasset/umap (за ID)
    """;

    private const string TargetSourcesNote = """
    Файли-джерела застосовуються до кожної цілі. Тека-джерело для однієї цілі — усі файли з неї;
    для теки цілей — файл з тим самим відносним шляхом/ім’ям (як у pack), інакше ціль пропускається (код 3).
    """;

    private const string OutputOpts = """
    Збереження:
      -o, --output <шлях>   файл (для одного файлу) або тека (для теки — дерево тек зберігається)
      --in-place            перезаписати оригінальні файли
      --overwrite           дозволити перезапис наявних файлів
    Без -o один файл зберігається як <ім’я>_N<розширення> поруч з оригіналом (як «Зберегти як...» у GUI).
    """;

    public static string General() => $"""
    {AppVersion.Name} CLI {AppVersion.Version} — інструмент для редагування файлів локалізацій Unreal Engine (.locres, .uasset, .umap).

    Використання: {Exe} <команда> [аргументи] [параметри]
                  {Exe} help <команда>  |  {Exe} <команда> --help  |  {Exe} --version

    Команди:
      unpack          розпакувати тексти у CSV / TXT (ID=текст) / TXT (лише текст)
      pack            запакувати переклад (CSV, TXT, locres, uasset/umap) у файли гри
      info            відомості про файл (версія locres, версія рушія, кейс uasset, мови...)
      stats           статистика перекладу («Загальна статистика»)
      search          пошук у файлах («Глобальний пошук»)
      replace         знайти і замінити у перекладі («Замінити»)
      check           перевірка типових помилок і глосарію
      tm              пам’ять перекладів: list, create, delete, activate, deactivate, target, record, apply
      glossary        глосарій: list, add, remove, export, check
      create-locres   створити locres з TXT
      merge-locres    об’єднати декілька locres
      add-rows        імпортувати нові рядки з TXT у locres
      remove-rows     видалити рядки з locres за ID
      import-hashes   імпортувати хеші тексту з locres або TXT
      compare-locres  порівняти два locres (експорт <ім’я>_new.txt і <ім’я>_changed.csv)

    Кожна команда приймає файл або теку (рекурсивно). Загальні параметри: -q/--quiet, -h/--help.
    stdout — лише результати; повідомлення, попередження та помилки — у stderr.

    Коди виходу: 0 — успіх; 1 — помилка; 2 — неправильні аргументи; 3 — виконано частково
    (помилки у пакеті, не знайдено відповідний файл, жоден рядок джерела не збігся, у пакеті
    не оброблено жодного файлу; для check — знайдено проблеми); 4 — не версійований uasset/umap: потрібен --engine.
    """;

    public static string For(string command) => command switch
    {
        "unpack" => $"""
        {Exe} unpack <файл|тека>... [-o <файл|тека>] [--format csv|txt|txt-plain] [--flat] [--overwrite]

        Розпаковує тексти з .locres/.uasset/.umap у текстові файли (як «Експортувати» у GUI):
          csv        (типово) стовпці key, source, Translation; Translation заповнено лише для змінених рядків
          txt        рядки ID=Переклад («Текст з ID»)
          txt-plain  лише текст, по рядку на запис («Лише текст»)
        Переноси рядків записуються тегами <cf>, <lf>, <cr>, <tb>, як у GUI.

        Вихід: <ім’я>.csv / <ім’я>.txt поруч із файлом або в -o (-o - — у stdout, лише для одного файлу).
        Для теки дерево тек відтворюється в -o;
        --flat — усе в одну теку з суфіксами _2, _3 при збігу імен (як «Пакетно» у GUI).
        Наявні файли не перезаписуються без --overwrite.

        {SourceOpts}

        {AssetOpts}

        Приклади:
          {Exe} unpack Game.locres
          {Exe} unpack Content/Localization -o csv/ --format txt
          {Exe} unpack ST_Items.uasset --engine 5.3 --mappings Game.usmap --lang uk
          {Exe} unpack uk/Game.locres --original-from en/Game.locres -o Game_uk.csv
        """,

        "pack" => $"""
        {Exe} pack <оригінал|тека> <переклад|тека> [-o <файл|тека>] [--in-place] [--overwrite] [--by <режим>]

        Відкриває оригінальні файли гри, імпортує переклад і зберігає нові файли (як імпорт + «Зберегти як...»).
        Записуються всі рядки; неперекладені — оригінальним текстом.

        Джерела перекладу:
          .csv    --by id | order | source | id+source | auto
          .txt    --by id | order | auto (формати unpack --format txt і txt-plain)
          .locres у locres, за ID (лише непорожні значення)
          .uasset/.umap у uasset/umap, за ID (з версією, з якою відкрито оригінал)
        --by auto (типово): CSV — за порядком, якщо він збігається з файлом рядок у рядок (ті самі ID
        на тих самих місцях), інакше за ID; TXT — за ID, якщо більшість рядків починається з наявного
        «ID=», за порядком, якщо рядків стільки ж, скільки у файлі; інакше — попередження.
        За ID: n-й рядок з певним ID отримує n-й запис джерела з цим ID (повтори ID не зсувають інші рядки).
        TXT імпортується без втрат: «ID=» — порожній текст, \n і \t лишаються як є (див. --txt-escapes).
        Якщо жоден рядок джерела не збігся з файлом, файл не записується (код 3).
        Тека-джерело: спершу файл з тим самим відносним шляхом (або схожим ім’ям у тій самій теці);
        пошук по всій теці як у GUI (рекурсивно, ім’я без префікса N_ і суфікса _N) — лише для одного
        файлу або плоскої теки-джерела (наприклад, після unpack --flat). Пріоритет типів: csv, txt,
        файл гри (або лише --src-format). Для неспівставлених файлів нічого не записується (код 3).

        Без -o один файл зберігається як <ім’я>_N<розширення> поруч з оригіналом. Для теки потрібен -o <тека>
        або --in-place. Для uasset/umap поруч записується і .uexp.
        Друкує «OK вхід -> вихід (N рядків, змінено M)» і підсумок для пакета.

          --src-format <ф>        тип файлів у теці-джерелі: auto, csv, txt, locres, uasset, umap
          --txt-escapes           TXT: \n, \r, \t -> переноси/табуляція (як імпорт TXT у GUI)
          --original-from <файл|тека>  спершу імпортувати стовпчик «Оригінал»

        {OutputOpts}

        {AssetOpts}
        {StatusOpt}

        Приклади:
          {Exe} pack Game.locres Game.csv                     # -> Game_N.locres
          {Exe} pack Game.locres Game.txt -o out/Game.locres
          {Exe} pack Content/Localization csv/ -o build/
          {Exe} pack Paks/ uk/ --in-place --engine 4.27
        """,

        "info" => $"""
        {Exe} info <файл|тека>... [--json]

        locres: формат (версія), кількість областей імен (namespace), записів, шифрування CV2.
        uasset/umap: версія рушія, чи використано mappings, кейс (тип ассета), мови, кількість рядків, попередження.

        {AssetOpts}
        """,

        "stats" => $"""
        {Exe} stats <файл|тека>... [-t <переклад>] [--json] [--status]

        Статистика як у «Загальна статистика»: рядки, слова, перекладено (рядків/слів, %), затверджено.
        Перекладеним вважається рядок, у якому переклад відрізняється від оригіналу.
        Затверджені рядки рахуються лише зі статусами з .STATUS (--status).

        {SourceOpts}

        {AssetOpts}
        {StatusOpt}
        """,

        "search" => $"""
        {Exe} search <запит> <файл|тека>... [--regex] [--case] [--whole-word|--exact] [--in <де>] [--json]

        Пошук як у «Глобальний пошук». Друкує: файл:рядок:ID<TAB>стовпчик<TAB>текст.
          --in text          (типово) оригінал і переклад
          --in id|source|translation|all
          --regex            регулярний вираз (.NET)
          --case             враховувати регістр
          --whole-word       ціле слово;  --exact  повний збіг тексту комірки

        {SourceOpts}

        {AssetOpts}
        """,

        "replace" => $"""
        {Exe} replace <файл|тека> --find <текст> --replace <текст> [--whole-word|--exact] [--case] [--dry-run]

        Заміна у стовпчику «Переклад» як у вікні «Заміна» (неперекладені рядки містять оригінал і теж змінюються).
        Файли без замін не записуються. --dry-run лише показує зміни: файл:рядок:ID<TAB>було<TAB>стало.

        {SourceOpts}

        {OutputOpts}

        {AssetOpts}
        {StatusOpt}

        Приклад:
          {Exe} replace Game.locres --find "Меч" --replace "Клинок" --whole-word --in-place
        """,

        "check" => $"""
        {Exe} check <файл|тека>... [-t <переклад>] [--glossary <Glossary.json>] [--glossary-only] [--verbose] [--json]

        Перевіряє перекладені рядки на типові помилки (як фільтр «Типові помилки» у GUI): плейсхолдери й теги,
        числа, повтори слів, подвійні пробіли, пробіли на початку/в кінці, кінцеву пунктуацію, дужки й лапки,
        пробіл після розділових знаків, пари тегів, подвоєні розділові знаки.
        --glossary — також перевірити глосарій: якщо в оригіналі є термін, у перекладі має бути один з його перекладів.
        Друкує: файл:рядок:ID<TAB>помилки. Код виходу 3, якщо знайдено проблеми.

        {SourceOpts}

        {AssetOpts}
        """,

        "tm" => $"""
        {Exe} tm <підкоманда> ... [--tm-dir <тека>]

        Пам’ять перекладів у форматі GUI: <тека>/<назва>.tm.json і _active.json.
        Типова тека — TranslationMemories поруч із програмою (як у GUI); --tm-dir змінює її.

          tm list [--json]                           список пам’ятей (активність, ціль запису, кількість записів;
                                                     пошкоджені файли позначено, у них не можна записувати)
          tm create <назва>                          створити (активна; ціль запису, якщо іншої немає)
          tm delete <назва>                          видалити файл пам’яті
          tm activate|deactivate <назва>             увімкнути/вимкнути пам’ять
          tm target <назва>                          зробити ціллю запису
          tm record <файл|тека>... [-t <переклад>] [--original-from <оригінал>] [--memory <назва>] [--max-variants N]
                                                     записати змінені рядки (оригінал -> переклад) у пам’ять
          tm apply <файл|тека>... [--memory <назва>]... [-o|--in-place|--export csv|txt|txt-plain] [--flat]
                                                     заповнити неперекладені рядки точними збігами (перший варіант)

        Приклади:
          {Exe} tm create Основна
          {Exe} tm record uk/Game.locres --original-from en/Game.locres
          {Exe} tm record Game.locres -t Game.csv
          {Exe} tm apply Game.locres -o Game_tm.locres

        {SourceOpts}

        {OutputOpts}

        {AssetOpts}
        """,

        "glossary" => $"""
        {Exe} glossary <підкоманда> ... [--glossary <Glossary.json>]

        Глосарій у форматі GUI (Glossary.json поруч із програмою; --glossary змінює шлях).
          glossary list [--json]
          glossary add <оригінал> <переклад>          додати запис або ще один варіант перекладу
          glossary remove <оригінал> [<переклад>]     видалити запис або один варіант
          glossary export <файл.json|.csv|.txt> [--overwrite]
          glossary check <файл|тека>... [-t <переклад>] [--verbose] [--json]
                                                      перевірити вживання термінів (код 3, якщо є проблеми)
        """,

        "create-locres" => $"""
        {Exe} create-locres <файл.txt|тека>... [-o <файл.locres>] [--version 0..4] [--each] [--overwrite]

        Створює locres з TXT у форматі ID=текст (як «Створити locres»). ID «простір::ключ» ділиться за першим «::».
        Порожні рядки й рядки з # пропускаються; при повторі ID лишається перший (список — у stderr).
          --version  0 legacy, 1 compact, 2 optimized (типово, CRC32), 3 cityhash (CityHash64 UTF-16),
                     4 externid (CityHash64 + ExternID, Stellar Blade)
          --each     окремий locres для кожного TXT (у -o відтворюється дерево тек)
        Без -o: <перший TXT>.locres.
        """,

        "merge-locres" => $"""
        {Exe} merge-locres <ціль.locres|тека> <джерело.locres|тека>... [-o|--in-place] [--overwrite]

        Додає до цілі рядки з інших locres, яких у ній ще немає (як «Об’єднати декілька locres»).
        Наявні ID пропускаються; хеші ключів перераховуються, хеш тексту береться з джерела; ExternID для v4.
        {TargetSourcesNote}

        {OutputOpts}
          --cv2, --cv2-demo, --status
        """,

        "add-rows" => $"""
        {Exe} add-rows <ціль.locres|тека> <рядки.txt|тека>... [-o|--in-place] [--overwrite]

        Імпортує нові рядки з TXT (ID=текст) у locres (як «Імпортувати нові рядки з TXT»); наявні ID пропускаються.
        {TargetSourcesNote}

        {OutputOpts}
          --cv2, --cv2-demo, --status
        """,

        "remove-rows" => $"""
        {Exe} remove-rows <ціль.locres|тека> [<ID>...] [--ids-file <файл>]... [-o|--in-place] [--overwrite]

        Видаляє рядки за ID (як «Видалити виділені рядки»), порожні області імен теж видаляються.
        --ids-file: по ID на рядок; у рядках виду ID=текст береться частина до «=»; # — коментар.

        {OutputOpts}
          --cv2, --cv2-demo, --status
        """,

        "import-hashes" => $"""
        {Exe} import-hashes <ціль.locres|тека> <джерело.locres|.txt|тека>... [-o|--in-place] [--overwrite]

        Імпортує хеші тексту (ValueHash) за ID (як «Імпортувати хеші тексту»):
        з locres — збережені хеші; з TXT (ID=текст) — CRC32 тексту.
        {TargetSourcesNote}

        {OutputOpts}
          --cv2, --cv2-demo, --status
        """,

        "compare-locres" => $"""
        {Exe} compare-locres <старий.locres|тека> <новий.locres|тека> [-o <шлях>] [--list] [--json] [--overwrite]

        Порівнює два locres (як «Порівняти два locres»): нові рядки, змінений текст, приховані зміни хешів.
        Експорт: <основа>_new.txt (ID=новий текст) і <основа>_changed.csv (key, old_text, new_text).
        Основа — ім’я нового файлу поруч із ним або шлях з -o. --list друкує всі відмінності.
        З --json або --list файли експортуються лише тоді, коли вказано -o.
          --cv2, --cv2-demo
        """,

        _ => General()
    };
}
