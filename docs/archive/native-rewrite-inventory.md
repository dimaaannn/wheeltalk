# Миграционная опись: WheelTalk.App (MAUI) → нативное приложение на .NET для Android

> **Тип документа:** опись, а не план. Код не менялся, проектов не создавалось — это пофайловая
> карта переноса `WheelTalk.App` и `WheelTalk.Dashboard` с MAUI на чистые Android Views/Canvas.
> **Почему переписываем:** слой отрисовки MAUI не тянет приборную панель — [dashboard-fps.md](../dashboard-fps.md):
> 13 кадров/с и худший кадр 91 мс на MAUI против 62 кадров/с и 17,4 мс нативно, на одном и том же
> телефоне и одних данных. `WheelTalk.Native/` — уже готовый бенчмарк-пример нативной отрисовки,
> используется здесь как образец приёмов (см. §5).
> **Что не трогаем:** `WheelTalk.Core` (38 файлов), `WheelTalk.Storage` (10 файлов) и
> BLE/консольный проект `WheelTalk` (13 файлов) переезжают в новый пакет без изменений — они уже
> не зависят от MAUI. Их файлы в этой описи не перечисляются.

> **Состояние на 30.07.2026: перенос выполнен, опись свою роль отработала.** Созданы три проекта:
> `WheelTalk.Droid` (приложение), `WheelTalk.Dashboard.Droid` (панель) и `WheelTalk.Lab.Droid`
> (стенд, коммит `037d473`). Таблицы ниже оставлены как история и построчно **не
> актуализируются**; заметные фактические отличия готового переноса от описи:
> - `SettingsCatalogue`, `UserSettingsStore` и `WheelOptions` числились «переезжает как есть»
>   (§1.1), но разошлись с MAUI-копией по содержимому;
> - `AndroidBleClient` («единственная строка MAUI», §1.2) разошёлся примерно на 84 строки — запрос
>   `POST_NOTIFICATIONS` в `BleReadiness` (план 11 §2.3) и проверка результата записи
>   (план 11 §3.1);
> - «не нужно портировать вовсе» из §4.2/§4.3 — портировано целиком: виджеты и раскладки Lab
>   понадобились стенду `WheelTalk.Lab.Droid`, который таки пересобрали поверх `Dashboard.Droid`;
> - иконка сделана векторной (`appicon.xml` + `appicon_foreground.xml`), а не растровыми
>   мипмапами из SVG (§1.2);
> - сплэш **не** сделан, `themes.xml` и `res/color` не заведены — открытая недоделка,
>   план 12 §7.

---

## 0. Сводка объёмов

### WheelTalk.App — 63 файла

| Категория | Файлов | Что это значит |
|---|---|---|
| Переезжает как есть | 25 | Копируется без изменений (кроме `using`, если понадобится) |
| Переезжает с правками | 13 | Логика та же, но 1–3 MAUI-вызова заменяются Android-аналогом |
| Переписывается | 25 | XAML-страницы, композиция, ресурсы темы — нативного эквивалента 1:1 нет, пишется заново |

### WheelTalk.Dashboard — 31 файл (30 кода + csproj)

| Категория | Файлов | Что это значит |
|---|---|---|
| Переезжает как есть | 2 | `DashboardOptions`, `DashboardReading` — чистые модели данных |
| Переезжает с правками | 4 | `DashboardCatalog`, `DashboardPalette`, `Layouts/Tapes.cs`, `Widgets/Tape/TapeGeometry.cs` — MAUI-зависимость точечная (`Color.FromArgb`, `RectF`) |
| Переписывается | 25 | Все `*Drawable`/`Layouts/*Dashboard`/`DashboardView` — рисуют через `IDrawable`/`ICanvas`, нужен перевод на `Android.Graphics.Canvas`/`Paint` |

Из переписываемых 25 виджетов **лента (`TapeDrawable` + `Widgets/Tape/*`) уже почти полностью
портирована** в `WheelTalk.Native/Drawing/TapeRenderer.cs` — см. §5.3. Реально нужный для главного
экрана набор — `TwinTapesDashboard` + `SpeedBlockDrawable` + `AlertBarsDrawable` — тоже close to
готов в `WheelTalk.Native/Drawing/DashboardView.cs`, не хватает только слоя настроек и части
поведения тревог (там же).

### Итого по проекту

94 файла с кодом/ресурсами (63 + 31) разобраны; 27 переезжают без изменений, 17 — с точечными
правками, 50 — переписываются. Из переписываемых половина (25 в Dashboard) уже имеет черновой
нативный аналог в бенчмарке.

---

## 1. WheelTalk.App — пофайловая опись

### 1.1 Переезжает как есть (25)

| Файл | Что внутри |
|---|---|
| `Alerts/AlarmTone.cs` | Генератор тона тревоги на `Android.Media.AudioTrack`, свой поток, ритм по счётчику отсчётов |
| `Alerts/AlertSignals.cs` | Звук/вибро/фонарик по `AlertState`: `Android.Media.ToneGenerator`, `Android.OS.Vibrator`, `Android.Hardware.Camera2.CameraManager` |
| `Configuration/AlertSignalOptions.cs` | POCO: какие каналы тревоги включены (Sound/Vibration/Torch) |
| `Configuration/AppWheelConfig.cs` | POCO, реализация `IWheelConfig` из ядра |
| `Configuration/LoggingOptions.cs` | POCO: `RawDump`, `AutoStartRide` |
| `Configuration/ReplayOptions.cs` | POCO: `DumpFile`, `Speed` для режима реплея |
| `Configuration/SettingsCatalogue.cs` | Статический каталог `SettingDescriptor[]` — описания настроек |
| `Configuration/UserSettingsStore.cs` | Запись пользовательского слоя настроек в JSON-файл (`System.Text.Json`) |
| `Configuration/WheelOptions.cs` | POCO: адрес и протокол сохранённого колеса |
| `Dashboard/DashboardFrame.cs` | Сборка кадра панели из `TelemetrySnapshot` + `RideTrace` — шов между ядром и `WheelTalk.Dashboard` |
| `Diagnostics/LogcatLoggerProvider.cs` | `ILoggerProvider` поверх `Android.Util.Log` |
| `Diagnostics/LoggingEventSink.cs` | `IEventSink` ядра, публикует события в логгер |
| `Logging/BufferedLogFile.cs` | Буферизованная построчная запись в файл (`StreamWriter`) |
| `Logging/LogFiles.cs` | Пути к файлам поездок через `Application.Context.GetExternalFilesDir` |
| `Logging/RawFrameRecorder.cs` | Запись сырых BLE-кадров в файл, переживает переподключение |
| `Logging/RideCsvExport.cs` | Экспорт поездки из базы в CSV |
| `Logging/RideRecorder.cs` | Управление записью поездки поверх `WheelSession`/`RideStore` |
| `Pages/RideFormat.cs` | Статический форматтер чисел/дат для экрана поездки |
| `Platforms/Android/Resources/values/colors.xml` | Обычный Android resource-файл цветов |
| `Platforms/Android/WheelForegroundService.cs` | `Android.App.Service`, держит процесс живым — уже 100% нативный код |
| `Resources/Fonts/OpenSans-Regular.ttf`, `OpenSans-Semibold.ttf` | Шрифты — переносятся в `Resources/font/` или `Assets/`, файл не меняется |
| `Resources/Raw/appsettings.json` | Данные (JSON) — меняется только способ упаковки, не содержимое |
| `Resources/Strings/AppStrings.resx` | Обычный .NET resx, читается `ResourceManager` вне зависимости от MAUI |
| `Wheel/TelemetryRate.cs` | Счётчик частоты телеметрии/сырых кадров поверх `ITransport` |

### 1.2 Переезжает с правками (13)

| Файл | MAUI-зависимость | Android-аналог |
|---|---|---|
| `Ble/AndroidBleClient.cs` | `MainThread.BeginInvokeOnMainThread` (строка 256, в `DiscoverServices` после паузы) — единственная строка во всём файле | `new Handler(Looper.MainLooper!).Post(() => gatt?.DiscoverServices())` |
| `Ble/BleReadiness.cs` | `Permissions.RequestAsync<Permissions.Bluetooth>()`, `Permissions.RequestAsync<Permissions.LocationWhenInUse>()` | `ActivityCompat.RequestPermissions`/`RegisterForActivityResult(new RequestPermission())` — нужен доступ к текущей `Activity`, которого у MAUI Essentials не требовалось |
| `Configuration/AppConfiguration.cs` | `FileSystem.AppDataDirectory`, `FileSystem.OpenAppPackageFileAsync("appsettings.json")` | `Application.Context.FilesDir.AbsolutePath`; `Assets.Open("appsettings.json")` (как в `WheelTalk.Native.csproj` — `AndroidAsset`) |
| `Diagnostics/CrashReport.cs` | `AppInfo.Current.VersionString`/`BuildString` (в `Describe()`) | `PackageManager.GetPackageInfo(PackageName, 0).VersionName` / `.LongVersionCode` |
| `Diagnostics/DiagnosticsShare.cs` | `Share.Default.RequestAsync(new ShareFileRequest{...})` | `Intent.ActionSend` + `FileProvider.GetUriForFile(...)` + `Intent.CreateChooser`; нужен `<provider>` `FileProvider` в манифесте (сейчас его нет — MAUI заводит свой неявно) |
| `Platforms/Android/AndroidManifest.xml` | Не MAUI-зависимость, но сейчас это **фрагмент**, который MAUI-тулинг сливает с сгенерированным манифестом (иконка, тема, `SingleProject`-метаданные) | Становится единственным полным манифестом проекта — переносится содержимое как есть, дописывается то, что раньше подставлял MAUI (см. §6) |
| `Platforms/Android/MainActivity.cs` | `MauiAppCompatActivity` | `AppCompatActivity` (или простой `Activity`, как в `WheelTalk.Native/MainActivity.cs`); атрибут `[Activity(...)]` и `ConfigurationChanges` переносятся без изменений; добавляется `SetContentView(...)` вместо MAUI, которое сейчас поднимает `MauiApp` |
| `Platforms/Android/MainApplication.cs` | `MauiApplication`, `CreateMauiApp() => MauiProgram.CreateMauiApp()` | `Android.App.Application`, `OnCreate()` вызывает нативный composition root (см. §2) |
| `Properties/launchSettings.json` | Профиль `"Windows Machine"`/`commandName=Project`, завязанный на MAUI multi-target отладку | Профиль под `AndroidEmulator`/прямой запуск, как в `WheelTalk.Native` |
| `Resources/AppIcon/appicon.svg`, `appiconfg.svg` | Сейчас превращаются в adaptive-icon мипмапы механизмом `MauiIcon` | SVG переиспользуются как исходник, но `mipmap-anydpi-v26/ic_launcher.xml` + растровые уровни собираются вручную (стандартный Android Studio Image Asset либо `dotnet` CLI-инструмент) |
| `Resources/Splash/splash.svg` | Генерируется `MauiSplashScreen` | `Theme.SplashScreen` (API 31+) + drawable layer-list для более старых версий |
| `Resources/Strings/TranslateExtension.cs` | `IMarkupExtension<string>` + `[ContentProperty]` — механизм `{loc:Translate Key}` в XAML | Статический метод `Get(key)` через `ResourceManager` остаётся как обычный C#-хелпер; XAML-обвязка (интерфейс, атрибут) удаляется — XAML-страниц не будет |

### 1.3 Переписывается (25)

| Файл | Нативный эквивалент | Что делает страница |
|---|---|---|
| `App.xaml` + `App.xaml.cs` | `Android.App.Application` (не путать с `MainApplication` — здесь про DI-подписки и жизненный цикл) | Composition root подписок: тревоги → `AlertSignals.Apply` (живёт вне страниц — переживает погашенный экран), `RawFrameRecorder.Apply()`, автозапуск записи по `LoggingOptions.AutoStartRide`, `CrashReport.ActivityAlive` в `OnResume/OnSleep`. Логика (сами подписки) переносится почти как есть в `MainApplication.OnCreate`/`Activity.OnResume/OnPause` — меняется только точка, где это исполняется |
| `AppShell.xaml` + `AppShell.xaml.cs` | Отсутствует как класс — в приложении одна `MainActivity`, страницы становятся отдельными `Activity`/`Fragment`, вызываемыми через `Intent`/`FragmentManager` | Сейчас единственный `ShellContent` → `MainPage`; Shell-навигация с одним экраном не даёт ничего сверх обычного `Intent`-перехода, удаляется целиком |
| `MauiProgram.cs` | Нативный composition root — см. §2 целиком | DI-регистрации, порядок биндинга опций/настроек |
| `Pages/MainPage.xaml` + `.xaml.cs` | Главная `Activity` (или единственная `Activity` приложения) — см. §3 целиком | Главный экран поездки |
| `Pages/RecordingPage.xaml` + `.xaml.cs` | `Activity`/`Fragment` с `TextView`×2 (состояние записи), `Button` старт/стоп, `Button` «Поездки», `Switch`×2 (автостарт, сырой дамп), `TextView` пути к папке | `Dispatcher.CreateTimer()` (обновление раз в секунду) → `Handler`/`CountDownTimer`; `Navigation.PushAsync(RidesPage)` → `StartActivity` |
| `Pages/RidePage.xaml` + `.xaml.cs` | `Activity` с заголовком, `LinearLayout` строк итогов (сейчас строится кодом через MAUI `Grid`), кнопкой экспорта | Строки итогов формируются в коде (`Figures`) — форматирование переносимо, конструирование `Grid/Label` переписывается на `LinearLayout`/`TextView` |
| `Pages/RidesPage.xaml` + `.xaml.cs` | `Activity` с `RecyclerView` вместо `CollectionView` | `ObservableCollection<RideRow>` → адаптер `RecyclerView`; `DisplayActionSheetAsync`/`DisplayAlertAsync` (подтверждение удаления) → `AlertDialog`; два жеста на строке (открыть/меню) → `OnClickListener`/`OnLongClickListener` или `ItemTouchHelper` |
| `Pages/ScanPage.xaml` + `.xaml.cs` | `Activity` с `RadioGroup` (протокол), кнопкой скана, статусом, `RecyclerView` найденных устройств | Сама логика скана уже нативная (`AndroidBleClient`/`BleReadiness`), переписывается только экран и `CollectionView.SelectionChanged` → click-слушатель адаптера |
| `Pages/SettingsListPage.xaml` + `.xaml.cs` | `Activity`/`Fragment` с `LinearLayout`/`ScrollView`-строками настроек | **Самый насыщенный MAUI-код в проекте**: динамически строит `Grid/Label/Switch/Picker/Border/BoxView`, `DisplayAlertAsync/DisplayPromptAsync/DisplayActionSheetAsync`. Бизнес-логика чтения/записи через `SettingsBinder` переносима как есть; UI-построитель переписывается целиком: `Switch`→`android.widget.Switch`, `Picker`→`Spinner`/меню, `DisplayPromptAsync`→`AlertDialog` с `EditText`, `DisplayActionSheetAsync`→`AlertDialog`-список |
| `Pages/SettingsRootPage.xaml` + `.xaml.cs` | `Activity` с 4 `Button` | `Navigation.PushAsync(SettingsListPage...)` → `StartActivity` с параметром раздела |
| `Pages/TelemetryPage.xaml` + `.xaml.cs` | `Activity` со `ScrollView`/`GridLayout` значений и банок BMS | `Application.Current?.RequestedTheme`, `MainThread.BeginInvokeOnMainThread`, динамический `Grid`, `SwipeGestureRecognizer` → `GestureDetector.OnGestureListener` для свайпа назад |
| `Resources/Raw/AboutAssets.txt` | Удаляется | Шаблонный MAUI-текст про `MauiAsset`/`FileSystem.OpenAppPackageFileAsync` — механизма `MauiAsset` вне MAUI не существует, файл не нужен |
| `Resources/Styles/Colors.xaml` | `values/colors.xml` | Палитра цветов, другая XML-схема (`<Color x:Key>` → `<color name>`) |
| `Resources/Styles/Styles.xaml` | `values/styles.xml` + `values/themes.xml` + `res/color` state-list | `Style TargetType`, `AppThemeBinding`, `VisualStateManager` — MAUI-специфичный механизм тем/состояний без прямого аналога, переписывается на Android `Theme`/`style` + `selector` |
| `WheelTalk.App.csproj` | Новый csproj по образцу `WheelTalk.Native.csproj` | Без `UseMaui`, без `MauiIcon/MauiSplashScreen/MauiImage/MauiFont/MauiAsset`, без пакета `Microsoft.Maui.Controls`; обычные Android-ресурсные папки (`Resources/drawable`, `values`, `layout`, `mipmap`) вместо Maui single-project механизма |

---

## 2. Composition root: `MauiProgram.cs` → нативный `Application`

### 2.1 Что регистрирует `MauiProgram.CreateMauiApp()` сейчас

| Блок | Регистрация | Замечание |
|---|---|---|
| Конфигурация | `builder.Configuration.AddConfiguration(AppConfiguration.Load())` | Зависит от MAUI `FileSystem` внутри `AppConfiguration` (§1.2) |
| Логирование | `ClearProviders()` + `AddProvider(new LogcatLoggerProvider())`, `SetMinimumLevel(Debug)` | Провайдер уже нативный |
| Диагностика | `CrashReport.CollectIfPreviousRunCrashed()` — вызывается **до** всего остального | Порядок важен: должен выполняться до того, как что-либо начнёт писать в буфер, который вытеснит хвост прошлого запуска |
| Опции | `Configure<AppWheelConfig>`, `Configure<WheelOptions>`, `AddSingleton<UserSettingsStore>`, `TryAddSingleton<IWheelConfig>(...)`, `TryAddSingleton(TimeProvider.System)`, `TryAddSingleton<IEventSink, LoggingEventSink>()` | Всё платформонезависимо, переносится как есть |
| Транспорт | `Configure<ReplayOptions>`, `AddSingleton<AndroidBleClient>()`, `AddSingleton<ITransport>(sp => ...)` — выбор BLE/реплей по `ReplayOptions.DumpFile` | Фабрика переносится как есть, `AndroidBleClient`/`ReplayTransport` уже нативны/платформонезависимы |
| Сессия | `Configure<ConnectionOptions>`, `Configure<AlertOptions>`, `AddSingleton(sp => new WheelSession(...))` | Как есть |
| Тревоги | `AddSingleton<IObservable<AlertState>>(sp => AlertEvaluator.Create(...).Publish().RefCount())` | Как есть — один общий поток тревог на всё приложение |
| Панель | `Configure<AlertSignalOptions>`, `AddSingleton<AlertSignals>()`, `Configure<LoggingOptions>`, `AddSingleton<DashboardOptions>()`, `AddSingleton(sp => new RideTrace(...))` | Как есть |
| Настройки | `AddSingleton<ISettingsStore>(sp => new SqliteSettingsStore(...))`, `AddSingleton(sp => SettingsCatalogue.Build(...))`, `AddSingleton(sp => new LayeredSettings(...) { Scope = ... })`, `AddSingleton(sp => new SettingsBinder(...))` | Как есть — источник данных `WheelTalk.Storage`, платформонезависим |
| Хранилище поездок | `Configure<StorageOptions>`, `AddSingleton(sp => RideDatabase.Open(Path.Combine(LogFiles.Root, "rides.db"), ...))`, `AddSingleton(sp => new RideStore(...))`, `AddSingleton<RideExporter>()` | Как есть — `LogFiles.Root` уже нативный |
| Запись | `AddSingleton<RideRecorder>()`, `AddSingleton<RawFrameRecorder>()` | Как есть |
| Страницы | `AddTransient<MainPage>()`, `ScanPage`, `TelemetryPage`, `RecordingPage`, `RidesPage`, `RidePage`, `SettingsRootPage`, `SettingsListPage` | **Единственный блок, который не переносится буквально** — страниц-`ContentPage` не будет, вместо `AddTransient<TPage>` регистрируются presenter/view-model-подобные классы, которые новая `Activity` разрешает через DI при создании (см. ниже) |
| Финал | `var app = builder.Build()`; `app.Services.GetRequiredService<SettingsBinder>().Apply()` — **до того, как что-либо прочитает настройку**; `LogInformation("App.Started")` | Порядок (`Apply()` сразу после `Build()`, до первого обращения к настройке) обязателен к сохранению |

### 2.2 Как это воспроизвести в нативном `Application`

Нативный аналог `WheelTalk.Native` не строит DI-контейнер вовсе (бенчмарку он не нужен), поэтому
готового образца в бенчмарке нет — этот кусок пишется с нуля, но переносится почти без изменений
по составу:

1. `MainApplication : Android.App.Application` — `OnCreate()` строит `IServiceCollection`/`IServiceProvider`
   (тот же `Microsoft.Extensions.DependencyInjection`, он не MAUI-специфичен) в том же порядке, что
   выше, и кладёт `IServiceProvider` в статическое свойство (`MainApplication.Services`) или
   отдаёт через собственный `IServiceProviderHolder` — MAUI прятал это внутри `MauiApp`.
2. Каждая `Activity` (`MainActivity`, `RecordingActivity`, …) при создании берёт нужные ей сервисы
   из `MainApplication.Services` вместо конструкторной инъекции — Android создаёт `Activity` сам,
   через `Intent`, конструктор с параметрами ему передать нельзя. Это меняет форму (не DI в
   конструктор, а `GetRequiredService` в `OnCreate`), а не состав регистраций.
3. Блок «Страницы» (`AddTransient<TPage>`) исчезает как список типов `ContentPage`; вместо него
   каждая бывшая страница становится `Activity`, которая при `OnCreate` резолвит свои зависимости
   из общего `IServiceProvider` — то есть транзитная регистрация страницы в DI попросту не нужна,
   её создаёт `ActivityManager` Android.
4. Порядок пунктов 1–9 таблицы выше сохраняется буквально, включая место `CrashReport.CollectIfPreviousRunCrashed()`
   (до первой записи в лог) и `SettingsBinder.Apply()` (сразу после сборки контейнера, до первого чтения настройки).

---

## 3. Главный экран: `Pages/MainPage.xaml` + `.xaml.cs`

### 3.1 Визуальные элементы (сверху вниз, `Grid` с 4 строками `Auto,Auto,*,Auto`)

| Элемент | x:Name | Что показывает | Привязка к данным |
|---|---|---|---|
| Полоса состояния | `StateStrip` (Border) | Цвет = состояние связи (`Linked`/`Trying`/`Unlinked` — три статических `Color`) | `ShowState()`, вызывается на каждый `Render`/`OnDisappearing`-таймер |
| ├─ подпись состояния | `StateLabel` | «Подключено»/«Подключение…»/«Переподключение»/«Отключено» | `AppStrings.State*` |
| ├─ подпись колеса | `WheelLabel` | `{адрес} · {модель} {версия}` | `_session.Address`, `snapshot?.Model/.Version` |
| ├─ точка записи | `RecordDot` | Серая (не пишем) / красная (пишем) | `_recorder.IsRecording` |
| └─ шестерёнка настроек | `SettingsGear` | Статичный символ ⚙ | — |
| Полоса тревоги | `AlertStrip` (Label) | Текст тревоги колеса / «нет связи N сек» / «нажмите ещё раз для выхода» / текст ошибки действия; скрыта, когда нечего сказать | `ShowWheelAlert`, `ShowState` (ветка `Reconnecting`), `OnBackButtonPressed`, catch-блоки обработчиков |
| Приборная панель | `DashboardHost` (ContentView) + `AlertBars` (GraphicsView) | `TwinTapesDashboard` (две ленты + центр скорости) поверх — своя канва с полосами тревоги по ШИМ/скорости | `_dashboard.Show(DashboardFrame.From(snapshot, trace, alertIntensity))` на каждом кадре таймера, НЕ на каждом снэпшоте телеметрии |
| Ряд кнопок | `LightButton`, `BeepButton`, `RecordButton`, `DetailsButton`, `ReplayButton` (скрыта вне реплея) | Команды колесу и навигация | Обработчики `Clicked` |

### 3.2 Жесты и обработчики

| Жест/событие | Обработчик | Действие |
|---|---|---|
| Свайп влево по всему экрану | `OnSwipeToDetails` | Переход на `TelemetryPage` |
| Тап по полосе состояния | `OnStateTapped` | Подключено+движется → подтверждение (`DisplayAlertAsync`) → `Disconnect()`; подключено+стоит → `Disconnect()` сразу; отключено → `ScanPage`; в режиме реплея — делегирует в `OnReplayClicked` |
| Тап по точке записи | `OnRecordDotTapped` | `RecordingPage` |
| Тап по шестерёнке | `OnSettingsTapped` | `SettingsRootPage` |
| `LightButton.Clicked` | `OnLightClicked` | Тумблер, `WheelCommand.SetLight(bool)` |
| `BeepButton.Clicked` | `OnBeepClicked` | `WheelCommand.Beep()` |
| `RecordButton.Clicked` | `OnRecordClicked` | `_recorder.Toggle()` |
| `DetailsButton.Clicked` / свайп | `OnDetailsClicked` | `TelemetryPage` |
| `ReplayButton.Clicked` (только в режиме реплея) | `OnReplayClicked` | Пуск/стоп воспроизведения; стоп ещё и стирает показания (`ClearReadings`) |
| Аппаратная «Назад» | `OnBackButtonPressed` | Первое нажатие — предупреждение полосой, второе в течение 2 с — реально отключается и завершает приложение (`Application.Current?.Quit()`, `WheelForegroundService.Stop()`) |

### 3.3 Логика обновления — что переносить один в один

Это единственная страница, где явно описан приём, зафиксированный в
[плане 11 §0](../android-plan-11-field-robustness.md#0-что-уже-сделано-правильно-и-это-надо-знать-до-правок)
как «не трогать без отдельной причины»: **правка свойства разметки — только если значение
действительно изменилось**.

- `Text(Label, value)`, `Shown(VisualElement, bool)`, `Fill(VisualElement, Color)` — три
  статических хелпера, которые сравнивают текущее значение перед присваиванием. Причина в
  комментарии кода: правка **любого** свойства разметки рядом с панелью роняла перерисовку
  соседних `GraphicsView`, и обе ленты замирали до конца поездки — найдено перебором на стенде
  (`dashboard-feedback.md`, прогон 3).
- В нативном View этой проблемы, скорее всего, не будет вовсе (нет соседних `GraphicsView` с
  общим механизмом инвалидации разметки — есть один `View.invalidate()` на канву), но **приём всё
  равно стоит перенести**: сравнение перед записью в `TextView.setText`/`setVisibility` — это
  дешёвая гигиена, а не защита от гипотетического сбоя, и её отсутствие — единственное, для чего
  есть прецедент поломки.
- Панель собирается **один раз** в конструкторе/`OnCreate` (`_dashboard = new TwinTapesDashboard(...)`)
  и не пересобирается никогда — только `Show(reading)` на каждый кадр.
- Два независимых таймера: `_stalenessTimer` (1 с, обновляет только текст «сколько прошло с
  последнего кадра») и `_frameTimer` (33 мс = 30 кадров/с, гоняет и панель, и полосы тревоги —
  **один** таймер на оба, чтобы моргание полос не имело своей частоты относительно кадра панели).
  В нативной версии кадровый таймер, скорее всего, не нужен вовсе — `WheelTalk.Native/Drawing/DashboardView.cs`
  просто перерисовывается по `PostInvalidateOnAnimation()` в конце `OnDraw`, синхронно с vsync,
  без отдельного `Handler`/`Timer`. Это меняет механизм, но не смысл: кадр панели и кадр тревожных
  полос остаются одним и тем же вызовом.
- `Render(snapshot)` (на приход телеметрии) и `DrawFrame()` (по таймеру/vsync) **разделены
  намеренно** — панель не трогается на каждый снэпшот, только копит данные (`_trace.Push`); это
  разделение переносится как есть.
- Подписки (`_telemetry`, `_state`, `_alertSubscription`) оформлены через `OnAppearing`/`OnDisappearing`
  — в нативном мире это `OnResume`/`OnPause` (или `OnStart`/`OnStop`, смотря что ближе к семантике
  «страница видна»).
- `_autoConnectTried` — защита от повторного автоподключения при повторных `OnAppearing` (страница
  видна не только при первом запуске, но и при возврате с других экранов) — переносится как есть.

---

## 4. WheelTalk.Dashboard — виджеты и раскладки

### 4.1 Модели данных (переезжают как есть/с точечной правкой)

| Файл | Категория | Публичный API | Правка |
|---|---|---|---|
| `DashboardOptions.cs` | как есть | ~25 настраиваемых свойств (пороги ШИМ/напряжения, тренд, тайминги сглаживания, `Tilt`, `Palette`), `event Changed`, `Notify()`, `Fraction(pwm)`, `FormatSpeed()` | нет MAUI-типов |
| `DashboardReading.cs` | как есть | `record` со снимком кадра панели (скорость/ШИМ/производные/пики/напряжение/температура), `Standing`, `PwmIn()`, `From(TelemetrySnapshot, …)` | нет MAUI-типов |
| `DashboardPalette.cs` | с правками | `record DashboardPalette(Name, Background, Ink, …)`, `Wong`/`WheelLog`/`All`, `ForPwm(pwm, options)` | `Color.FromArgb(string)` → `Android.Graphics.Color.ParseColor` (в `WheelTalk.Native/Drawing/Palette.cs` уже сделано для варианта «Ванг») |
| `DashboardCatalog.cs` | с правками | Реестр вариантов панели (A–G) для стенда, `Func<DashboardOptions, DashboardView>` | Сигнатура зависит от типа `DashboardView` — после его переписывания (см. ниже) меняется тип возврата фабрики |
| `Widgets/Tape/TapeGeometry.cs` | с правками | `readonly struct` — пересчёт «значение → Y», геометрия ленты | Единственная зависимость — `RectF` (Maui) → `Android.Graphics.RectF`; в `WheelTalk.Native/Drawing/TapeRenderer.cs` эта же математика уже инлайнена как локальная функция `Y(value)`, отдельный тип не заведён — так же можно сделать и здесь |
| `Layouts/Tapes.cs` | с правками | Фабрика/конфигуратор `TapeDrawable` под ШИМ/напряжение/скорость из `DashboardReading`+`DashboardOptions` | Единственная MAUI-зависимость — `Colors.Black`; логика эквивалентна `Configure()` в `WheelTalk.Native/Drawing/DashboardView.cs`, которая уже нативна |

### 4.2 Отрисовка (переписывается, `IDrawable`/`ICanvas` → `Canvas`/`Paint`)

| Файл | Что рисует | Используется в приложении? |
|---|---|---|
| `DashboardView.cs` (базовый) | Абстрактный `ContentView` с `Show(reading)`/`Update(reading)` — база всех раскладок | Да, как базовый класс `TwinTapesDashboard` |
| `Layouts/TwinTapesDashboard.cs` | **Единственная раскладка в приложении** — лента напряжения слева, лента ШИМ справа, `SpeedBlockDrawable` в центре | **Да — это и есть главный экран** |
| `Widgets/TapeDrawable.cs` + `Widgets/Tape/{TapeHatchPart,TapeMarkPart,TapeScalePart,TapeTicksPart,TapeTrendPart,TapeWindowPart}.cs` | Сама лента: полосы, штриховка «barber pole», деления/подписи, стрелка тренда, след поездки (мин/макс), окно значения, подпись | Да, через `TwinTapesDashboard` |
| `Widgets/SpeedBlockDrawable.cs` | Крупная цифра скорости + 4 пары справочных значений (макс ШИМ, температура, поездка, заряд/просадка) | Да, центр `TwinTapesDashboard` |
| `Widgets/AlertBarsDrawable.cs` | Две полосы сверху/снизу — мигающая тревога по ШИМ или немигающая мягкая тревога по скорости | Да, отдельная канва поверх `MainPage` (не внутри `TwinTapesDashboard`) |
| `Layouts/{ArcDashboard,AviaDashboard,FillDashboard,SegmentDashboard,SingleTapeDashboard,TapesDashboard}.cs` | Пять альтернативных раскладок (варианты B/C/E/F/D-старый) | **Нет** — используются только в `WheelTalk.Lab`, для приложения не нужны, если не решат портировать и их |
| `Widgets/{ArcDrawable,ChargeBarDrawable,FillDrawable,SegmentStripDrawable,SpeedRingDrawable,VoltageStripDrawable}.cs` | Дуга ШИМ, полоса заряда, заливка, линейка сегментов, кольцо скорости, полоса напряжения без делений | **Нет** — обслуживают только отвергнутые/невыбранные раскладки Lab |
| `Layouts/SpeedDigit.cs` | Переиспользуемый блок «крупная цифра + подпись км/ч» с логикой роста кегля | Косвенно — логика уже повторена в `Centre()` `WheelTalk.Native/Drawing/DashboardView.cs`, отдельный тип не обязателен |

### 4.3 Сверка с `WheelTalk.Native` — что уже портировано

`WheelTalk.Native/Drawing/{DashboardView,TapeRenderer,Palette,FrameClock}.cs` +
`WheelTalk.Native/Telemetry/RideTimeline.cs` — не абстрактный пример, а **черновая версия ровно
того экрана**, который должен получиться на выходе (`TwinTapesDashboard` целиком, включая полосы
тревоги и центр скорости), собранная для замера FPS.

**Уже перенесено, практически 1:1 по набору примитивов:**

| Что | Dashboard (MAUI) | Native (готово) |
|---|---|---|
| Полосы шкалы | `TapeScalePart` | `TapeRenderer.Draw` (цикл по `Bands`) |
| Штриховка barber pole | `TapeHatchPart` | `TapeRenderer.Hatch` — включая клип и шаг диагоналей |
| Деления и подписи | `TapeTicksPart` | `TapeRenderer.Ticks` (урезанно, см. ниже) |
| Стрелка тренда | `TapeTrendPart` | `TapeRenderer.Arrow` |
| След поездки (мин и макс) | `TapeMarkPart` ×2 | `TapeRenderer.Trace`, вызывается дважды |
| Окно значения | `TapeWindowPart` | `TapeRenderer.Window` (без ступени `Critical`, см. ниже) |
| Подпись ленты | `TapeDrawable.DrawCaption` | `TapeRenderer.DrawCaption` |
| Сглаживание хода по реальному времени | `TapeDrawable.Scroll()` | `TapeRenderer.Scroll()` — идентичная формула на `NanoTime()` |
| Центр (скорость + 4 пары) | `SpeedBlockDrawable` | `DashboardView.Centre()` — инлайн, тот же состав |
| Полосы тревоги по ШИМ | `AlertBarsDrawable` (интенсивность) | `DashboardView.AlertBars()` |
| Палитра «Ванг» | `DashboardPalette.Wong` | `Palette.cs` — все 8 цветов совпадают по HEX |

**Осталось портировать/чего не хватает в Native:**

| Пробел | Где в Dashboard | Последствие, если не заметить |
|---|---|---|
| `DashboardOptions` целиком не читается динамически — все пороги (`50/78/92/95/…`) захардкожены литералами в `DashboardView.Configure()` | Весь класс | Настройки «Отображения» (палитра, наклон, скрытие десятых/справочных, автомасштаб просадки и т.д.) при переносе нужно завести заново как runtime-конфигурацию, а не переоткрывать с нуля — сама механика (событие `Changed` → перерисовка) в Dashboard уже продумана |
| Мигание окна на критическом пороге | `TapeWindowPart.Critical` | `TapeRenderer.Window` всегда рисует статичную заливку, без «мигает при выходе за `BarberPolePwm`» |
| Настраиваемый формат подписи делений | `TapeTicksPart.LabelFormat` | В Native жёстко `"F0"` — для шкалы напряжения (нужны десятые) это уже обойдено через `Format` на окне, но не на самих делениях |
| Мягкая тревога по превышению скорости | `AlertBarsDrawable.SpeedExceeded` (жёлтые немигающие полосы) | `DashboardView.AlertBars()` реализует только тревогу по ШИМ (интенсивность, мигание) |
| `HideExtrasAbove`, `HideTenthsAbove` — скрытие справочных/десятых выше порога скорости | `SpeedBlockDrawable`/`DashboardOptions` | В Native всегда показаны все 4 пары и все десятые — упрощение бенчмарка, не решение продукта |
| Личный предел (`PersonalLimit`/`ShowBug`) | Используется в Arc/Fill/Segment-раскладках | Для `TwinTapesDashboard` некритично — там бирки предела на ленте нет и в MAUI-варианте, только след `Mark` |
| Переключение палитры (WheelLog ↔ Ванг) | `DashboardOptions.Palette` | В Native одна палитра зашита, выбора нет |

**Не нужно портировать вовсе** (не входят в `TwinTapesDashboard`, обслуживают только раскладки
`WheelTalk.Lab`, которые в приложение не идут): `ChargeBarDrawable`, `SegmentStripDrawable`,
`VoltageStripDrawable`, `ArcDrawable`, `SpeedRingDrawable`, `FillDrawable`, а также сами раскладки
`ArcDashboard`/`AviaDashboard`/`FillDashboard`/`SegmentDashboard`/`SingleTapeDashboard`/`TapesDashboard`.
Если решат впоследствии перенести и стенд `WheelTalk.Lab` на нативную отрисовку (не входит в эту
опись) — тогда эти виджеты понадобятся тоже.

**Вывод:** порт главного экрана на голом Android жизнеспособен без открытий — геометрия и
примитивы уже воспроизведены тем же набором вызовов `Canvas`/`Paint`/`Path`. Основной остаток
работы — не рисование, а **перенос слоя настроек** `DashboardOptions` с литералов на runtime, плюс
две поведенческие детали (мигание критического окна, тревога по скорости).

---

## 5. MAUI-специфичные сервисы — сводная таблица

| Сервис | Где используется (файл:строка) | Android-аналог |
|---|---|---|
| `Dispatcher.CreateTimer()` | `MainPage.xaml.cs:119,134`, `RecordingPage.xaml.cs:50` | `Handler(Looper.MainLooper).PostDelayed(...)` в цикле, либо `android.os.CountDownTimer`/`Choreographer` для кадрового таймера |
| `MainThread.BeginInvokeOnMainThread` | `AndroidBleClient.cs:256`, `MainPage.xaml.cs:105,106,438`, `TelemetryPage.xaml.cs:90,91` | `new Handler(Looper.MainLooper).Post(...)` |
| `Preferences` | Не используется нигде (0 совпадений) | — |
| `FileSystem.AppDataDirectory` / `OpenAppPackageFileAsync` | `AppConfiguration.cs:14,20` | `Context.FilesDir`; `Context.Assets.Open(...)` |
| `Permissions.RequestAsync<T>` | `BleReadiness.cs:25,34` | `ActivityCompat.RequestPermissions` / `RegisterForActivityResult(new RequestPermission())` — требует ссылку на `Activity` |
| `Shell.Current` / Shell-навигация | `AppShell.xaml(.cs)`, единственный `ShellContent` | Не нужна — одна `Activity` или `Intent`-переходы между `Activity` |
| `DeviceDisplay.Current.KeepScreenOn` | `MainPage.xaml.cs:286,401` | `Window.AddFlags(WindowManagerFlags.KeepScreenOn)` (как уже сделано в `WheelTalk.Native/MainActivity.cs:23`) |
| `Application.Current` | `MainPage.xaml.cs:254` (`.Quit()`), `TelemetryPage.xaml.cs:59` (`.RequestedTheme`) | `Activity.FinishAffinity()`/`Process.KillProcess`; тема — `Resources.Configuration.UiMode` |
| `Navigation.Push(Async)`/`Pop(Async)` | `MainPage`, `RecordingPage`, `RidesPage`, `ScanPage`, `TelemetryPage`, `SettingsRootPage` | `StartActivity(new Intent(this, typeof(TargetActivity)))`; «назад» — системная кнопка/`OnBackPressed` |
| `DisplayAlert(Async)` | `MainPage.xaml.cs:534`, `RidesPage.xaml.cs:148`, `SettingsListPage.xaml.cs:251`, `SettingsRootPage.xaml.cs:53` | `AlertDialog.Builder` |
| `DisplayActionSheet(Async)` | `RidesPage.xaml.cs:110`, `SettingsListPage.xaml.cs:378` | `AlertDialog` со списком либо `BottomSheetDialog` |
| `DisplayPrompt(Async)` | `SettingsListPage.xaml.cs:317` | `AlertDialog.Builder` + `EditText` |
| `Microsoft.Maui.Graphics.Color`/`Colors` | `MainPage.xaml.cs` (166,259,366-369,445,544), `SettingsListPage.xaml.cs`, `TelemetryPage.xaml.cs` (59,182-183) | `Android.Graphics.Color` / `Color.ParseColor(string)` |
| `IDrawable`/`GraphicsView` | Весь `WheelTalk.Dashboard` (кроме моделей данных) | `Android.Views.View.OnDraw(Canvas)` + `Paint` — см. §4 |
| `ContentPage` | Базовый класс всех 8 страниц | `Activity`/`AppCompatActivity` |
| `AppInfo.Current` | `CrashReport.cs:131` | `PackageManager.GetPackageInfo(...)` |
| `Share.Default.RequestAsync` | `DiagnosticsShare.cs:21` | `Intent.ActionSend` + `FileProvider` |
| `ResourceDictionary`/XAML-стили | `App.xaml`, `Resources/Styles/*.xaml`, все `Pages/*.xaml` | `values/styles.xml`, `values/themes.xml`, `values/colors.xml` |

---

## 6. Platforms/Android — что переносится в новый манифест

| Элемент манифеста | Сейчас | При переносе |
|---|---|---|
| `allowBackup="true"` | Разрешает автобэкап Android, каталог с `rides.db` (WAL) в него попадает | **Известный дефект** (см. [план 11 §8.1](../android-plan-11-field-robustness.md#81-allowbackuptrue-и-база-в-wal)), не исправлен — переносить как есть, но не забыть, что он уже стоит в очереди на починку независимо от переписывания |
| `BLUETOOTH`/`BLUETOOTH_ADMIN`/`ACCESS_FINE_LOCATION` (`maxSdkVersion=30`) + `BLUETOOTH_SCAN`/`BLUETOOTH_CONNECT` | Оба набора разрешений — для Android ≤11 и 12+ | Переносится без изменений, логика выбора уже в `BleReadiness` |
| `uses-feature android:name="android.hardware.bluetooth_le" required="true"` | — | Переносится без изменений |
| `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_CONNECTED_DEVICE`, `POST_NOTIFICATIONS`, `VIBRATE` | Для `WheelForegroundService` (тип `TypeConnectedDevice`) и `AlertSignals` (вибро) | Переносится без изменений; `POST_NOTIFICATIONS` заявлено, но не запрашивается в рантайме — известный пробел (план 11 §2.3), актуален и после переписывания |
| Иконка/тема приложения, `roundIcon`, `supportsRtl` | Сейчас в `<application>` внутри фрагмента, остальное (activity-запись, тема сплэша) подставляет MAUI-тулинг | Manifest становится **единственным и полным**: нужно явно дописать `<activity>` для `MainActivity` (и для каждой новой `Activity`, если решат делать их отдельными компонентами, а не одной `Activity` со сменой `View`), `<service>` для `WheelForegroundService` (сейчас регистрируется атрибутом `[Service(...)]` — это остаётся, атрибуты `Android.App.*` не зависят от MAUI) |
| `FileProvider` для диалога «поделиться» | Отсутствует явно — MAUI Essentials `Share` заводит свой временный provider | Нужно добавить `<provider>` с `androidx.core.content.FileProvider` и `file_paths.xml`, если `DiagnosticsShare` переписывается на `Intent.ActionSend` (см. §1.2) |
| `MainActivity` атрибут `[Activity(Theme=..., MainLauncher=true, LaunchMode=SingleTop, ConfigurationChanges=...)]` | `Theme = "@style/Maui.SplashTheme"` | Тема меняется на нативный сплэш-эквивалент (§1.3, `Styles.xaml`→`themes.xml`); `MainLauncher=true`, `LaunchMode`, `ConfigurationChanges` (ScreenSize/Orientation/UiMode/ScreenLayout/SmallestScreenSize/Density) переносятся без изменений — то же самое уже сделано в `WheelTalk.Native/MainActivity.cs:15-16` |
| `MainApplication` атрибут `[Application]` | Наследует `MauiApplication` | Наследует `Android.App.Application`, атрибут `[Application]` остаётся |

---

## 7. Риски и неочевидное

То, что легко потерять при переписывании, потому что оно не бросается в глаза, пока читаешь код
верхнего уровня, а не платформенные комментарии рядом с ним.

| Риск | Где выясняется | Почему легко потерять |
|---|---|---|
| **Канал тревоги — будильник, не медиа** | `AlarmTone.Build()`: `AudioUsageKind.Alarm` + `AudioContentType.Sonification` | Единственное, что пробивает тихий режим на телефоне. При поверхностном переносе легко взять обычный `MediaPlayer`/`SoundPool` с медиа-каналом — тревога станет неслышной именно тогда, когда телефон в кармане и в беззвучном режиме, то есть всегда, когда она реально нужна |
| **Ритм тревоги считается внутри звукового потока, не таймером интерфейса** | `AlarmTone.cs`, комментарий целиком | Автор уже наступил на эти грабли: таймер UI + буфер AudioTrack на 90 мс дают «странный и нестабильный» звук. При переписывании легко вернуться к очевидному «таймер дёргает флаг звучит/не звучит» и получить тот же дефект заново |
| **Foreground-сервис: порядок Start-before-Stop** | `WheelForegroundService.Stop()`, комментарий | `StopService`/остановка до первого `startForeground()` роняла приложение (`RemoteServiceException`) — воспроизведено полевым выходом 28.07.2026. Сервис при любом раскладе сначала становится foreground, потом завершается. Это платформенное поведение, не зависящее от MAUI/native — но легко «упростить» при переписывании и снова сломать |
| **`WheelForegroundService.Stop()` из уведомления (план 10/11 §2.2)** | план 11 §2.2 | Действие в уведомлении приходит **из фона**, а Android 12+ запрещает запуск foreground-сервиса из фона через `Launch`/`StartForegroundService`. Актуально независимо от MAUI — при добавлении кнопки «Отключиться» в уведомление (см. план 10) нужен `BroadcastReceiver` или `OnStartCommand` уже запущенного сервиса, не `Launch` |
| **CCCD `0x2902` пишется явно** | `AndroidBleClient.OnServicesDiscovered` | Без явной записи в дескриптор подключение выглядит успешным, а колесо молчит — это уже нативный Android-код, риск не в MAUI, а в том, что при рефакторинге кто-то «уберёт лишнюю строку», не увидев, что она значит |
| **`DiscoverServices` не из колбэка, с паузой 600 мс** | `AndroidBleClient.cs:34`, `GattCallbackAdapter.OnConnectionStateChange` | Единственное место в файле, которое реально зависит от MAUI (`MainThread.BeginInvokeOnMainThread`) — при замене на `Handler.Post` легко забыть саму паузу `Task.Delay(600ms)`, которая и есть митигация, а не побочный эффект |
| **`gatt.Close()` при любом разрыве** | `AndroidBleClient.CloseGatt()` | Без него следующая попытка подключения падает с `status 133` без явной причины — тоже чисто нативный риск, но именно такие мелочи теряются при переписывании «по памяти», без построчной сверки |
| **Работа при погашенном экране: подписки живут в `App`, не на странице** | `App.xaml.cs:29-42` | `AlertSignals`, `RawFrameRecorder.Apply()`, автозапись — всё подписано в composition root, а не в `MainPage`, потому что «при погашенном экране страниц нет». При переносе на `Activity`-архитектуру велик соблазн переместить эти подписки в `MainActivity.OnCreate` — это будет неправильно: они должны жить в `Application.OnCreate`/аналогичном месте, переживающем уничтожение любой `Activity` |
| **Запись поездки и сырой дамп владеют собой, не страницей** | `RideRecorder`, `RawFrameRecorder` — синглтоны, подписки на сессию | То же самое: поездка обязана продолжаться при свёрнутом приложении. Если при переписывании запись начнут стартовать/стопать из жизненного цикла `Activity` (`OnPause`/`OnDestroy`), запись будет обрываться при каждом сворачивании — прямая регрессия |
| **Присваивания свойств разметки только при изменении** | `MainPage.xaml.cs` — `Text/Shown/Fill` хелперы, план 11 §0 | Причина специфична для MAUI/`GraphicsView` (соседние канвы конфликтовали за инвалидацию), но приём стоит перенести даже туда, где технической причины уже не будет — это дешёвая гигиена, и её отсутствие уже один раз стоило замерших приборов на весь заезд |
| **Один кадровый таймер на панель и полосы тревоги** | `MainPage.xaml.cs` — `_frameTimer`, комментарий про биение частот | Если полосы тревоги и лента будут перерисовываться от двух разных источников (например, полосы — от `Handler`-таймера, а лента — от `PostInvalidateOnAnimation`), их частоты разойдутся и будет заметно «биение» на записи экрана |
| **`allowBackup="true"` + `rides.db` в WAL** | план 11 §8.1, манифест | Не новый риск переписывания, но легко унести в новый манифест бездумно вместе со всем остальным — дефект уже описан и ждёт починки; переписывание — удобный момент решить его попутно, а не тащить дальше. **Риск закрыт (30.07.2026):** так и вышло — в манифест `WheelTalk.Droid` `allowBackup` перенесён как `false`, с разбором причины в комментарии рядом |
| **`POST_NOTIFICATIONS` объявлено, но не запрашивается в рантайме** | план 11 §2.3 | На Android 13+ без runtime-запроса уведомление foreground-сервиса не показывается вовсе — сервис работает, а человек не видит ни того, что приложение активно, ни (после доработки) кнопки «отключиться». Тестовый телефон на Android 11, поэтому это не стреляет в разработке — есть риск унести пробел в новый проект незамеченным. **Риск закрыт (30.07.2026):** запрос добавлен в рантайме рядом с BLE-разрешениями — `BleReadiness.cs:42-49` |
| **Результат `WriteCharacteristic` отбрасывается** | план 11 §3.1, `AndroidBleClient.WriteAsync` | Известный, ещё не исправленный дефект: команда (например, «Бип») может не уйти из-за занятой GATT-операции, а журнал пишет «отправлено» независимо от результата. Не специфично для MAUI — при переписывании велик риск скопировать код как есть и унести дефект дальше, не заметив, что он уже в списке P0 |
| **`async void` без `try` в навигационных обработчиках** | план 11 §1.3, §8.2 | На Android `Activity`-навигация (`StartActivity`) бросает реже, чем MAUI `Navigation.PushAsync` (нет `NullReferenceException` от неготового `ServiceProvider`), но полностью риск не исчезает — обработчики нажатий на Android тоже “async void” по своей природе (event handlers), и правило «весь метод в `try`» стоит перенести, а не считать закрытым автоматически сменой платформы |
| **`TranslateExtension` — не просто MAUI markup extension** | `Resources/Strings/TranslateExtension.cs` | Статический метод `Get(key)` через `CurrentUICulture` — это то, что позволяет менять язык без перезапуска (уже работает правильно). При выбрасывании XAML-обвязки легко выбросить и сам механизм смены языка на лету, а не только его XAML-упаковку |
| **Числа для показа vs для записи форматируются по-разному** | план 11 §5.3 | Строки вида `"{0:F1} км/ч"` уже форматируются текущей культурой (разделитель дробной части едет вместе с языком) — это нормально для экрана. То, что уходит в CSV/базу, использует `InvariantCulture` (`LogFiles.Stamp`, `RideLog`). При переписывании форматирования чисел на нативные `TextView` легко случайно продублировать локаль в оба места или, наоборот, потерять её на экране |

---

## 8. Источники

- [dashboard-fps.md](../dashboard-fps.md) — замер, из-за которого принято решение переписывать
- [android-plan-7-dashboard-design.md](../android-plan-7-dashboard-design.md) — история выбора `TwinTapesDashboard` как главного экрана
- [android-plan-11-field-robustness.md](../android-plan-11-field-robustness.md) — §0 (что не трогать), §2.2/2.3/3.1/8.1 (риски манифеста и BLE, актуальные и после переписывания)
- `WheelTalk.Native/` — единственный уже существующий нативный код в репозитории, образец приёмов для §2 и §4
- `AGENTS.md` — стиль, структура солюшена, `WheelTalk.Dashboard`/`WheelTalk.Lab`
