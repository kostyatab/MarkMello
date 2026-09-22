# Quick Look на macOS — результат spike

## Статус

Частично проведён, вердикт — **делаем** (MM-11) при условии, что ручная
проверка «скачал → Open Anyway → пробел» пройдёт (ниже, «Осталось проверить»).
Технически всё, от чего зависело решение, подтвердилось: ad-hoc подписанное
расширение регистрируется и работает в песочнице, NativeAOT-библиотека
загружается, соседний файл читается.

Код spike лежит в `tests/quicklook-macos-spike/` — только чтобы повторить
проверку (раздел «Как повторить»), продакшен-кодом он не является.

## Дата

2026-09-22

## Окружение

- macOS 27.0 (26A428), Apple Silicon (arm64). Intel-машины и macOS 12 не было.
- Swift 6.4 из Command Line Tools. **Xcode не нужен**: расширение собирается
  `swiftc` + Info.plist + `codesign`.
- .NET SDK 10.0.401, Native AOT, `osx-arm64`.

## Что собрано

### Расширение `MarkMelloQuickLook.appex`

Data-based preview (macOS 12+): `QLPreviewProvider` + `QLPreviewingController`,
ответ — `QLPreviewReply(dataOfContentType: .html, …)`. Расширение `dlopen`-ом
загружает `Contents/Frameworks/libmmpreview.dylib` и вызывает C-функцию
`mm_render_html(path) -> char*` (UTF-8, освобождается через `mm_free`).

```bash
swiftc -O -module-name MarkMelloQuickLook -parse-as-library -application-extension \
  -target arm64-apple-macos12.0 -sdk "$(xcrun --show-sdk-path)" \
  -framework QuickLookUI -Xlinker -e -Xlinker _NSExtensionMain \
  -o MarkMelloQuickLook PreviewProvider.swift
```

Бинарь расширения — 95 КБ, `minos 12.0`.

Info.plist расширения, главное:

| Ключ | Значение |
|---|---|
| `CFBundlePackageType` | `XPC!` |
| `CFBundleIdentifier` | `<id приложения>.quicklook` |
| `NSExtensionPointIdentifier` | `com.apple.quicklook.preview` |
| `NSExtensionPrincipalClass` | `MarkMelloQuickLook.PreviewProvider` |
| `QLIsDataBasedPreview` | `true` |
| `QLSupportedContentTypes` | `net.daringfireball.markdown` |

Entitlements расширения:

```xml
<key>com.apple.security.app-sandbox</key><true/>
<key>com.apple.security.temporary-exception.files.absolute-path.read-only</key>
<array><string>/</string></array>
```

### NativeAOT-библиотека

Проект `NativeLib=Shared`, `PublishAot=true`, ссылается на `MarkMello.Domain`,
`MarkMello.Application`, `MarkMello.Infrastructure`. Экспорты —
`[UnmanagedCallersOnly(EntryPoint = "mm_render_html")]` и `mm_free`. Рендер —
тот же путь, что в приложении: `RenderMarkdownDocumentUseCase`
(`MarkdigMarkdownDocumentRenderer` + `DiagramRenderService` с
`MermaidDiagramRenderer`) → `RenderedMarkdownDocument` → черновой обход блоков
в HTML. Относительные картинки библиотека читает сама и вставляет как
`data:` URI.

Сборка проходит; единственное предупреждение — уже известный `IL3053` от
Pidgin (зависимость Naiad), приложение гасит его так же
(`WarningsNotAsErrors` в `MarkMello.Desktop.csproj`).

### Бандл и подпись

Расширение вложено в `Contents/PlugIns/`, библиотека — в
`Contents/PlugIns/MarkMelloQuickLook.appex/Contents/Frameworks/`. Подпись
ad-hoc изнутри наружу: сначала все `.dylib`, затем `.appex` с entitlements,
затем приложение.

```bash
codesign --force --sign - <каждая .dylib>
codesign --force --sign - --entitlements appex.entitlements MarkMelloQuickLook.appex
codesign --force --sign - MarkMello.app
codesign --verify --deep --strict MarkMello.app   # valid on disk
```

**Важно для упаковки:** сейчас релизный `MarkMello.app` бандлом не подписан
вообще — у бинарей только подпись линкера (`flags=adhoc,linker-signed`,
`Info.plist=not bound`, `Sealed Resources=none`). Для расширения бандл
придётся подписывать целиком (шаг в `build-app-bundle.sh` или отдельный скрипт
после него). Проверено на копии настоящего `MarkMello.app`: после вложения
`.appex` и ad-hoc подписи всего бандла приложение запускается,
`--smoke-exit-after-open` отрабатывает (FirstWindow 203 мс,
ReadableDocument 188 мс — в пределах обычного разброса).

## Результаты

| Проверка | Результат |
|---|---|
| Ad-hoc подписанное `.appex` регистрируется в системе | да, но **только после первого запуска приложения**: `pluginkit -m -i …quicklook` пуст, пока приложение не запускали, и находит расширение через секунду после `open` |
| Превью по пробелу (`qlmanage -p`) | работает: Markdig, таблица, блок кода, Mermaid-диаграмма (SVG от Naiad) |
| Песочница | включена: процесс расширения видит `APP_SANDBOX_CONTAINER_ID=com.markmello.qlspike.quicklook` |
| NativeAOT-библиотека в песочнице | загружается: `dlopen` 2 мс, рендер тестового документа 3 мс (тёплый процесс) |
| Соседний файл через `temporary-exception…read-only` `/` | читается, картинка показывается — **но в папках под TCC** (`~/Downloads`, `~/Desktop`, `~/Documents`) macOS показывает запрос «Приложение „…“ запрашивает доступ к файлам в папке „Загрузки“». Исключение песочницы TCC не отменяет |
| DMG с карантином → `/Applications` | карантин переносится на `.app` и `.appex`; `spctl` отклоняет (`rejected`, ожидаемо для ad-hoc); приложение помечено `launch-disabled`, расширение **не регистрируется**, пока пользователь не разрешит первый запуск |
| После снятия карантина и первого запуска | расширение регистрируется и работает (контрольный прогон через `xattr -dr com.apple.quarantine`) |

Карантин имитировался атрибутом `com.apple.quarantine` в формате Safari
(`0083;<время>;Safari;<UUID>`) на DMG; копирование из смонтированного образа
перенесло его на все файлы бандла, как при копировании в Finder.

Попутно: на macOS 27 `hdiutil create/attach/detach` печатают предупреждение
о переходе на `diskutil image …`. `build-dmg.sh` пока работает, но это кандидат
на отдельную задачу.

## Размер

NativeAOT, `osx-arm64`, `dotnet publish -c Release`, без `.dSYM`:

| Состав библиотеки | Размер |
|---|---|
| только рантайм + чтение файла (нижняя граница) | 1 235 776 (1,2 МиБ) |
| + Markdig и слои MarkMello, диаграммы — исходником | 4 183 936 (4,0 МиБ) |
| + Naiad (Mermaid) | 26 257 568 (25,0 МиБ) |
| + TextMateSharp (подсветка кода) | 29 447 376 (28,1 МиБ) + `libonigwrap.dylib` 531 424 |

Naiad с Pidgin — почти всё: **+22 МиБ** на библиотеку. Код приложению не
делится: у расширения свой процесс и своя копия рантайма.

Шрифты: Source Serif 4 (4 начертания), Inter (7), JetBrains Mono (2) — 13 TTF,
4 393 168 байт (4,2 МиБ). В приложении они вшиты в сборку Presentation, файлами
их нет — для превью нужны свои копии (WOFF2 — ориентировочно вдвое меньше; не
замерено, `fonttools` нет под рукой). HTML из data-based превью файлы с диска
не грузит: шрифты — `data:` URI или вложения `QLPreviewReply.attachments`
(`cid:`).

Итого прибавка к DMG (до сжатия): около +8 МиБ без Mermaid или около +30 МиБ
с Mermaid, включая шрифты. Для сравнения: приложение — около 75 МиБ
(ADR-0010, «Размер»).

## Выводы для MM-11

1. **Ad-hoc подпись работает.** Developer ID для расширения не обязателен.
   Лишнего ручного шага для пользователя расширение не добавляет: тот же
   «Open Anyway» при первом запуске, что и сейчас нужен неподписанному
   приложению. Но превью появится только **после первого запуска** MarkMello —
   это надо написать в README и в тексте релиза.
2. **Подписывать весь бандл.** Упаковка должна ad-hoc подписывать `.app`
   изнутри наружу (сейчас этого нет). Когда появится Developer ID (ADR-0004,
   шаг 5), тот же шаг меняет `-` на identity и добавляет `--options runtime`.
3. **Соседние файлы и TCC.** Чтение картинок рядом с документом в
   `~/Downloads`/`~/Desktop`/`~/Documents` показывает системный запрос от имени
   MarkMello — неожиданный на нажатие пробела. Варианты для MM-11: не читать
   соседние файлы вовсе (картинка — подписью alt); читать, только если
   документ вне защищённых папок; или принять запрос как цену. Рекомендую
   первый вариант в первой версии.
4. **Mermaid — отдельное решение по размеру.** +22 МиБ ради диаграмм в
   превью; без Naiad диаграмма показывается исходником.
5. **Xcode в CI не обязателен.** Хватает `swiftc` из Command Line Tools
   (раннеры `macos-26` его содержат). Xcode-проект можно не заводить.

## Осталось проверить (вручную)

- [ ] Настоящее скачивание браузером (Safari/Chrome) DMG из GitHub Release,
      копирование в `/Applications` через Finder, первый запуск через
      «Системные настройки → Конфиденциальность и безопасность → Всё равно
      открыть», затем пробел на `.md` в Finder. Ожидание: расширение
      регистрируется после первого запуска.
- [ ] macOS 12 (виртуальная машина). Бинарь собран с `minos 12.0`, но
      запускался только на 27. Риск: Swift-оверлей `libswiftQuickLookUI` на 12;
      если не загрузится — переписать класс на Objective-C (API тот же).
- [ ] Intel (`osx-x64`) — та же сборка с `-target x86_64-apple-macos12.0` и
      `-r osx-x64`.
- [ ] Выбор между двумя расширениями для `.md`, если у пользователя уже стоит
      чужое (например, QLMarkdown): какое берёт Finder и можно ли переключить в
      «Системные настройки → Расширения → Quick Look».

## Как повторить

Код spike — в `tests/quicklook-macos-spike/` (в `MarkMello.sln` не входит,
продакшен-кодом не является):

- `native/` — NativeAOT-библиотека (`MarkMelloPreviewSpike.csproj`,
  `Exports.cs`); вариант без Mermaid — заменить в `Exports.cs`
  `new DiagramRenderService([new MermaidDiagramRenderer()])` на
  `new DiagramRenderService([])`;
- `appex/` — Swift-расширение, Info.plist, entitlements, пустое
  host-приложение;
- `make-bundle.sh` — собирает Swift, раскладывает бандл и подписывает ad-hoc.

```bash
dotnet publish tests/quicklook-macos-spike/native -c Release -r osx-arm64 -o /tmp/mmql
tests/quicklook-macos-spike/make-bundle.sh /tmp/mmql/libmmpreview.dylib /tmp/mmql
open /tmp/mmql/MarkMelloQLSpike.app          # регистрирует расширение
pluginkit -mAvvv -i com.markmello.qlspike.quicklook
qlmanage -p some.md
```

После проверки удалить приложение, иначе оно продолжит перехватывать
превью `.md` в Finder.
