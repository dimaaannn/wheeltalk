# Архитектура — карта проектов и поток данных

> Снимок на 01.08.2026, по ревью архитектуры ([план 19](android-plan-19-ui-seams.md)).
> При расхождении с кодом прав код; сюда вносить правки тем же коммитом, что двигает границы.

## Проекты и зависимости

Стрелка — «ссылается на». Ядро не знает ни про Android, ни про Windows, ни про SQLite —
это проверяется компилятором (TFM `net10.0` без платформенных суффиксов).

```mermaid
graph TB
    subgraph Переносимое
        Core["WheelTalk.Core<br/>контракты · декодеры · сессия ·<br/>тревоги · настройки · плеер"]
        Storage["WheelTalk.Storage<br/>SQLite: поездки, слои настроек"]
    end

    subgraph "Боевое (Android)"
        Droid["WheelTalk.Droid<br/>приложение com.wheeltalk.droid<br/>Activity · BLE · запись · DI"]
        DashDroid["WheelTalk.Dashboard.Droid<br/>панель и композиция главного экрана"]
    end

    subgraph Инструменты
        Console["WheelTalk.Test.ConsoleConnection<br/>песочница: BLE на Windows, сценарии<br/>(на неё никто не ссылается)"]
        LabDroid["WheelTalk.Lab.Droid<br/>стенд панели на записях"]
        Tests["WheelTalk.Tests<br/>xUnit, TFM net10.0"]
    end

    Droid --> Core
    Droid --> Storage
    Droid --> DashDroid
    DashDroid -.->|только DashboardReading| Core
    LabDroid --> DashDroid
    Console --> Core
    Console --> Storage
    Tests --> Core
    Tests --> Storage
    Storage --> Core
```

Ключевое свойство: **`Dashboard.Droid` не знает про сессию, транспорт и базу** — панель кормят
record'ом `DashboardReading`, и поэтому один и тот же класс экрана (`Screen/MainScreenView`)
показывают и боевое приложение, и стенд на записанной поездке.

## Поток данных: от байтов до экрана

```mermaid
graph LR
    subgraph Источники
        BLE["Колесо по BLE<br/>AndroidBleClient /<br/>WindowsBleClient"]
        Replay["ReplayTransport<br/>сырой дамп CSV"]
    end

    BLE -- "ITransport" --> Session
    Replay -- "ITransport" --> Session

    subgraph "WheelTalk.Core"
        Session["WheelSession<br/>владелец соединения,<br/>единственный цикл повторов,<br/>сторож данных"]
        Detect["WheelDetector + AutoDecoder<br/>опознание по GATT и первому кадру"]
        Decoder["Декодеры пяти протоколов:<br/>Veteran · Gotway · KingSong ·<br/>InMotion V1 · InMotion V2<br/>байты → WheelState"]
        Snapshot["TelemetrySnapshot<br/>IObservable, ~5 Гц"]
        Alerts["AlertEvaluator<br/>пик за окно → AlertState"]
        Trace["RideTrace<br/>пики, просадка, тренды"]
        Player["RidePlayer<br/>кадры из базы"]
    end

    Session --> Detect --> Decoder --> Snapshot
    Snapshot --> Alerts
    Snapshot --> Trace

    subgraph "WheelTalk.Droid"
        Recorder["RideRecorder → RideStore<br/>очередь, фоновый писатель"]
        Signals["AlertSignals<br/>звук · вибрация · вспышка<br/>(живут у CrashGuard, не у экрана)"]
        Main["MainActivity<br/>проводка главного экрана"]
        Playback["PlaybackActivity"]
    end

    Snapshot --> Recorder
    Alerts --> Signals
    Snapshot --> Main
    Trace --> Main
    Player --> Playback

    subgraph "WheelTalk.Dashboard.Droid"
        Frame["DashboardFrame →<br/>DashboardReading"]
        Screen["MainScreenView<br/>панель + шторка + полоса тревоги"]
    end

    Main --> Frame --> Screen
    Playback --> Frame

    Recorder --> DB[("rides.db<br/>WheelTalk.Storage")]
    DB --> Player
```

Команды идут обратным ходом: шторка → `WheelSession.SendCommand` → `SequentialWriteQueue`
(одна GATT-запись в полёте) → декодер строит кадр протокола → транспорт. Отказ без связи —
исключение, а не молчаливый успех.

## Настройки — три слоя

```mermaid
graph LR
    Factory["Заводские<br/>appsettings.json в пакете"] --> Layered
    User["Общие пользовательские"] --> Layered
    Wheel["Слой конкретного колеса"] --> Layered
    Layered["LayeredSettings + SettingsBinder<br/>(ядро)"] --> Live["Живые объекты опций:<br/>AlertOptions, DashboardOptions, …<br/>читаются в момент использования"]
    Store[("SqliteSettingsStore<br/>WheelTalk.Storage")] <--> Layered
    Debug["usersettings.json<br/>отладочный файл поверх"] --> Layered
```

Пороги тревог панель читает **источником**, а не копией: `DashboardOptions.Thresholds` — это
интерфейс `IDashboardThresholds` (план 19 Б3); приложение отдаёт реализацию поверх живого
`AlertOptions`, стенд крутит мутабельное умолчание своими ручками. Тем же приёмом след поездки
читает сглаживание (`RideTrace.SmoothingSecondsSource`, план 19 Б5) — зеркалирования по кадру
нет нигде.

## Где проходит граница «логика — показ»

| Решение | Кто принимает | Экран делает |
|---|---|---|
| Фазы связи (5 штук) | `LinkStatus.Evaluate` (ядро, тесты) | перевод в тексты и цвета |
| Свежесть кадра, вуаль | `LinkStatus.IsStale` | присваивание `IsStale` |
| Тревога, интенсивность | `AlertEvaluator` (ядро, тесты) | полосы, звук, вибрация |
| Пики и тренды поездки | `RideTrace` (ядро, тесты) | стрелки и бирки на лентах |
| Повторы подключения | `WheelSession` (ядро, тесты) | ничего — только смотрит `State` |
| Причина «подключиться нечем» | `LinkProblem` + `LinkStatus` (ядро, тесты) | тексты из `AppStrings`, деталь отказа — из исключения |
| Режим реплея | `ITransport.IsReplay` (контракт, план 19 Б1) | читает свойство, конкретных транспортов не знает |
| Покадровый хром панели: моргание, вуаль, наклон | `PanelDriver` (библиотека, план 19 Б2) | экран отвечает на вопросы `IPanelSource` |
| Состав шторки | `MainActivity` — осознанно (план 19 §3: лямбды по живым объектам, выделение не окупается) | — |
