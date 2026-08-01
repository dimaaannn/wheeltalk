> **Архив (30.07.2026).** План «de-Android in-place» не выполнялся и не будет: выбран порт с нуля на C# (см. csharp-testport-plan.md и AGENTS.md). Взяты только §1–§3 — границы и порты; артефакты §7 не создавались. Ссылки `../app/...` ведут в репозиторий Wheellog.Android.

# Задача для субагента: выделение ядра «Wheel Core» и подготовка к порту на C++ / C#

> **Тип задачи:** архитектурный рефакторинг + подготовка language-neutral спецификации.
> **Исполнитель:** субагент (Plan/general-purpose).
> **Предусловие:** прочитать [bluetooth-architecture.md](bluetooth-architecture.md) (карта BLE-подсистемы) и [wheel-core-current-architecture.md](wheel-core-current-architecture.md) (подробные схемы текущей логики и точки связи с Android). Этот документ уточняет **границу выноса** и план.
>
> **Установка:** цель — **переиспользовать имеющуюся логику как есть**, а не переписывать с нуля. Тесты не запускаются; существующие `*AdapterTest.kt` используются только как справочник ожидаемого поведения протоколов.

---

## 0. Контекст и глобальная цель

Приложение WheelLog.Android содержит логику взаимодействия с моноколёсами (EUC): распознавание протокола, парсинг телеметрии, формирование команд для 8 брендов/протоколов. Сейчас эта логика перемешана с Android-инфраструктурой (BLE-сервис, `Context`, `Intent`-broadcast, Koin DI, `Handler`, `Timber`).

**Глобальная цель:** перенести бизнес-логику взаимодействия с колесом на **два языка**:
- **C++** — для встраиваемых устройств (embedded, ограниченные ресурсы, без Android/JVM, без RTTI/исключений в горячем пути желательно);
- **C#** — для отдельного собственного приложения (.NET).

Android-приложение остаётся, но должно потреблять то же ядро (в идеале — как эталон поведения / генератор тест-векторов).

**Эта задача — НЕ сам порт.** Задача — (1) точно зафиксировать границу модуля, (2) спроектировать language-neutral контракт этой границы, (3) составить пошаговый план извлечения ядра из Android-зависимостей так, чтобы затем его можно было механически переписать на C++ и C#.

---

## 1. Где проходит граница выноса

Граница — **на уровне бизнес-логики взаимодействия с колесом**:

```
        ┌─────────────────────────────────────────────────────────┐
   свеpху│  ХОСТ-ПРИЛОЖЕНИЕ (Android / .NET / embedded firmware)    │
        │  - UI, сервисы-потребители, реальный BLE-стек            │
        └───────────────▲───────────────────────┬─────────────────┘
                        │ унифицированные        │ обобщённые
                        │ ДАННЫЕ (телеметрия     │ КОМАНДЫ (setLight,
                        │ + события)             │ setSpeedLimit, beep…)
        ┌───────────────┴───────────────────────▼─────────────────┐
        │                  ★ WHEEL CORE (выносимый модуль) ★        │
        │                                                          │
        │   WheelType-реестр + распознавание протокола             │
        │   8 конкретных адаптеров (parse телеметрии + build команд)│
        │   Framing/unpacker-автоматы, MathsUtil, SmartBms         │
        │   Состояние телеметрии + производные вычисления (PWM,     │
        │   battery %, distance, charge time, средние)             │
        └───────────────▲───────────────────────┬─────────────────┘
                        │ сырые байты из BLE     │ сырые байты команд
                        │ (feed decode)          │ в BLE + keep-alive
        ┌───────────────┴───────────────────────▼─────────────────┐
   снизу│  ПОРТЫ (host-provided): Transport, Clock/Scheduler,      │
        │  Config, Logger, EventSink                               │
        └──────────────────────────────────────────────────────────┘
```

**Важное требование пользователя:** *все конкретные модели колёс и протоколы должны быть экспортированы.* То есть публичный API ядра не прячет протоколы за единым фасадом — каждый протокол (Kingsong, Gotway, Veteran, InMotion, InMotion V2, Ninebot, Ninebot Z, Gotway-Virtual) остаётся первоклассной экспортируемой единицей, которую хост может выбрать/инстанцировать явно, но при этом взаимодействует с ней через **единый обобщённый контракт** команд и данных.

### Что ВХОДИТ в ядро (порт-поверхность)
| Компонент | Текущий файл | Роль в ядре |
|---|---|---|
| `WHEEL_TYPE` enum | [Constants.kt:98](../app/src/main/java/com/cooper/wheellog/utils/Constants.kt) | Реестр моделей/протоколов |
| `BaseAdapter` контракт (41 метод) | [utils/BaseAdapter.kt](../app/src/main/java/com/cooper/wheellog/utils/BaseAdapter.kt) | Обобщённый интерфейс команд + `decode()` |
| 8 адаптеров-протоколов | `utils/*Adapter.java` | Parse телеметрии + формирование байт-команд |
| **Байтовые конвертеры конкретных моделей/прошивок** (внутри адаптеров) | ветки по модели в `decode()`: Gotway FW (Begode/ExtremeBull/Freestyl3r/SmirnoV), Ninebot S2/Mini, InMotion/V2 `enum Model`, Kingsong KS-* , Veteran ver | **Контракты `decode`/`buildCommand` на модель — пометить к экспорту как отдельные единицы** (см. [current-architecture §7-bis](wheel-core-current-architecture.md)) |
| Framing-автоматы (напр. `gotwayUnpacker`) | внутри адаптеров | Сборка кадров из потока байт |
| `MathsUtil` (byte-хелперы: LE/BE, clamp) | [utils/MathsUtil.java](../app/src/main/java/com/cooper/wheellog/utils/MathsUtil.java) | Чистые функции, тривиально портируются |
| `SmartBms` | [utils/SmartBms.kt](../app/src/main/java/com/cooper/wheellog/utils/SmartBms.kt) | Модель данных BMS |
| Телеметрия + derived-вычисления | [WheelData.java](../app/src/main/java/com/cooper/wheellog/WheelData.java) (поля + get*Double, calculatePwm, setBatteryLevel, getChargeTime, средние) | Унифицированные данные + расчёты |
| Логика распознавания по байтам | напр. Gotway `NAME`/`GW`/`CF`/`BF` префиксы; NinebotZ proto | Часть протокола |

### Что НЕ входит (остаётся в хосте / заменяется портом)
| Android-вещь | Где | Чем заменить |
|---|---|---|
| Реальный BLE (`BluetoothService`, Blessed, GATT, UUID-роутинг записи) | [BluetoothService.kt](../app/src/main/java/com/cooper/wheellog/BluetoothService.kt) | Порт **Transport** (байты туда/обратно) |
| `AppConfig` (78 get/set ключей в адаптерах) | [AppConfig.kt](../app/src/main/java/com/cooper/wheellog/AppConfig.kt) | Порт **Config** (см. §3) |
| `sendBroadcast(ACTION_*)` | WheelData, адаптеры | Порт **EventSink** / callbacks / Flow |
| `new Handler().postDelayed`, `Timer/TimerTask` keep-alive | Gotway, InMotion*, Ninebot* | Порт **Scheduler/Clock** |
| `Timber` логирование | везде | Порт **Logger** |
| `Context`, `Intent`, `AudioManager`, Koin `KoinComponent.get()` | BaseAdapter, WheelData | Убрать / внести через порты |
| Распознавание по набору GATT-сервисов + JSON `res/raw/bluetooth_services.json` | WheelData.detectWheel | Вынести топологию BLE в хост; ядро получает уже «тип-кандидат» ИЛИ принимает описание сервисов как данные |

---

## 2. Верхняя граница (host ⇄ core): обобщённые данные и команды

### 2.1 Унифицированные ДАННЫЕ (core → host)
Источник истины — приватные поля `WheelData.java` + публичные геттеры. Спроектировать неизменяемый снимок `TelemetrySnapshot` со **явными единицами** (сейчас в коде фиксированная точка: скорость/ток/напряжение/температура в 1/100, distance в метрах). Минимальный набор полей (не исчерпывающий — свериться с `WheelData`):

- Скорость, напряжение, ток, фазный ток, мощность, PWM (calculated + max)
- Батарея (%, lowest, start), voltage sag
- Температура 1/2, max temp, CPU/IMU temp
- Дистанция: wheel distance, total distance, user distance, distance-from-start
- Углы: angle (tilt), roll
- Время: ride time, riding time
- Идентификация: name, model, serial, version, wheelType, modeStr
- Флаги: connected, wheelIsReady, wheelAlarm, charging status, fan status
- Максимумы: topSpeed, maxCurrent, maxPhaseCurrent, maxPower, maxPwm
- BMS1/BMS2 (`SmartBms`: cells[], min/max/avg cell, temps, voltage)
- Derived (вынести в чистые функции): `calculatePwm`/`updatePwm`, `setBatteryLevel` (custom percents), `getChargeTime`, `getAverageSpeed`, `getRemainingDistance`, `getBatteryPerKm`, `getAvgVoltagePerCell`.

### 2.2 Обобщённые КОМАНДЫ (host → core)
Источник — 41 `open fun` в `BaseAdapter` (единый обобщённый контракт) + расширенные протокол-специфичные методы (напр. Gotway `updateProportionalFactor` и др. Alexovik-PID). Спроектировать команду как:
- либо один union-интерфейс `IWheelCommands` (как `BaseAdapter`), где неподдерживаемые no-op;
- либо enum/tagged-union `WheelCommand` + `dispatch`. **Рекомендация для C++/C#:** tagged-union/`std::variant` / discriminated union — легче портируется и сериализуется, чем 41 виртуальный метод.

Команда на выходе даёт **байты для отправки** (через порт Transport) — иногда с отложенной последовательностью (Gotway: `postDelayed` цепочки «W» → param → «b»). Эти задержки — часть протокола, их надо смоделировать через Scheduler-порт, а не терять.

### 2.3 События (core → host)
Заменить broadcast-actions на типизированные события:
`ACTION_WHEEL_DATA_AVAILABLE` (+ graph-update флаг), `ACTION_WHEEL_IS_READY`, `ACTION_WHEEL_TYPE_CHANGED`, `ACTION_WHEEL_MODEL_CHANGED`, `ACTION_WHEEL_NEWS_AVAILABLE` (+ текст алерта), `ACTION_PREFERENCE_RESET`.

---

## 3. Нижняя граница (порты, host-provided)

Спроектировать 5 портов (интерфейсы, реализуемые хостом). Держать их узкими — это то, что придётся реализовать заново на каждой платформе.

1. **Transport** — `write(bytes)` (запись команды в колесо) + подача входящих `feed(bytes, characteristicId?)`. Убирает цикл `WheelData ↔ BluetoothService`. UUID-роутинг записи (сейчас switch по `WHEEL_TYPE` в `writeWheelCharacteristic`) — вынести описание характеристик в метаданные протокола, отдаваемые ядром.
2. **Scheduler / Clock** — `now()`, `postDelayed(delayMs, action)`, `schedulePeriodic(periodMs, task)`, `cancel`. Нужен для keep-alive (InMotion/InMotion V2/Ninebot/NinebotZ, `scheduleAtFixedRate`) и отложенных командных цепочек (Gotway). На embedded — маппинг на таймеры RTOS; на C# — `System.Timers`/`Task.Delay`.
3. **Config** — доступ к настройкам (см. ниже). Абстрагировать как типизированный интерфейс, НЕ как string-KV, чтобы порты C++/C# были типобезопасны.
4. **Logger** — уровни + формат-строка (замена `Timber`). На embedded может быть no-op.
5. **EventSink** — публикация событий из §2.3.

### 3.1 Config: 78 ключей — разобрать на два класса
Адаптеры и читают, и пишут `AppConfig`. Это два РАЗНЫХ по смыслу потока — их надо разделить:

- **(A) Входные параметры поведения (core читает):** влияют на парсинг/расчёт. Примеры: `gotwayNegative`, `useRatio`, `useBetterPercents`, `hwPwm`, `isAlexovikFW`, `gotwayVoltage`, `autoVoltage`, `lightMode`, `alarmMode`, `ledMode`, `highBeamEnabled`, `lightEnabled`, `lowBeamEnabled`. → часть входного контракта / конфигурации протокола.
- **(B) Отражённые настройки колеса (core пишет):** колесо в своих кадрах сообщает свои текущие настройки, адаптер их записывает обратно (`setPedalsMode`, `setWheelMaxSpeed`, `setRollAngle`, PID-факторы `setProportionalFactor`… , `setWheelAlarmNSpeed`, `setLightBrightness`, `setSpeakerVolume`, `setTransportMode` и т.д.). → это на самом деле **часть унифицированных ДАННЫХ** (reported wheel settings), а не «настройки приложения». Вынести в отдельную структуру `WheelSettings` в снимке телеметрии, а запись в пользовательский `AppConfig` оставить хосту как подписчику события.

> Полный список 78 ключей получить: `grep -ohE "appConfig\.(get|set)[A-Za-z0-9]+" app/src/main/java/com/cooper/wheellog/utils/*Adapter*.java | sort -u`. Классифицировать каждый на (A) вход-поведение или (B) reported-setting.

---

## 4. Ключевые архитектурные узлы, которые надо распутать

1. **Циклическая связь `BluetoothService ↔ WheelData`** (главный узел, см. bluetooth-architecture.md §9.1). Разорвать через порт Transport: адаптеры больше не вызывают `WheelData.getInstance().bluetoothCmd()`, а возвращают/эмитят команду.
2. **Синглтоны везде** (`WheelData.getInstance()`, `*Adapter.getInstance()`, статические `stopTimer()`). Для C++/C# и тестируемости — убрать глобальное состояние, сделать экземпляр `WheelCore`, владеющий текущим адаптером и состоянием. Адаптеры хранят изрядное внутреннее состояние (Gotway: `trueVoltage/trueCurrent/bmsCurrent/attempt/lock_Changes/model/fw`…) — оно должно стать полями экземпляра, не статикой.
3. **God-object `WheelData`** (1464 строки): смешивает состояние, распознавание (`detectWheel`), маршрутизацию команд (десятки `updateX` прокси), broadcast, derived-расчёты, музыку/AudioManager. Расщепить на: `TelemetryState`, `DerivedCalculations` (чистые), `WheelDetector`, `CommandRouter`. `CheckMuteMusic`/AudioManager — целиком в хост.
4. **Распознавание колеса** двухуровневое: (а) по набору GATT-сервисов + advData (BLE-топология, оставить хосту/передавать как данные) и (б) по содержимому байтов внутри `decode` (Gotway префиксы, NinebotZ proto S2/Mini) — это часть протокола, входит в ядро.
5. **Keep-alive и отложенные команды** — не терять тайминги (§3.2 Scheduler). InMotion `scheduleAtFixedRate(…, 200, 25)` и т.п. — параметры тайминга протокола.

---

## 5. Пошаговый план (deliverable субагента)

**Фаза 0 — Фиксация эталонного поведения (как справочник, без запуска).**
Существующие JVM-тесты адаптеров (`app/src/test/.../*AdapterTest.kt`) и RAW-логи (`RAW_*.csv`) — документированный эталон поведения протоколов. Использовать их как **справочник** при переносе (сверка форматов кадров, ожидаемых значений), опционально — оформить несколько показательных пар «вход hex + config → снимок телеметрии» как платформо-независимые примеры для будущей сверки C++/C#. Запускать тесты не требуется.

**Фаза 1 — Language-neutral спецификация границы (документ, без кода).**
Зафиксировать: `WheelType`, `TelemetrySnapshot` (все поля + единицы + fixed-point), `WheelSettings` (reported), обобщённый `WheelCommand` контракт, 5 портов, события. Для каждого из 8 протоколов — формат кадров (у Gotway уже есть в хвосте [GotwayAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/GotwayAdapter.java); дореверсить/задокументировать остальные из кода адаптеров).

**Фаза 2 — «De-Android» эталонной реализации in-place (JVM остаётся эталоном).**
Отвязать ядро от Android, НЕ меняя поведение: заменить `Timber`→Logger-порт, `Handler/Timer`→Scheduler-порт, `AppConfig`→Config-порт (A) + `WheelSettings` (B), `sendBroadcast`→EventSink, `bluetoothCmd`→Transport, убрать `Context`/Koin из `BaseAdapter`/адаптеров. Убрать синглтоны → экземпляр `WheelCore`. **Критерий готовности фазы:** ядро компилируется как чистый Kotlin/JVM-модуль без `android.*` / Koin / Timber импортов (проверяется грепом и сборкой, без запуска тестов).

**Фаза 3 — Извлечение чистых декодеров/энкодеров + выделение конвертеров моделей.**
Каждый протокол → чистые функции `decode(state, bytes) -> (newState, events, telemetry)` и `buildCommand(cmd) -> outgoing bytes (+ scheduled follow-ups)`. Явное состояние вместо мутабельных полей синглтона. **Дополнительно: выделить байтовый конвертер каждой конкретной модели/прошивки в самостоятельную экспортируемую единицу** (сейчас это ветки `if (bIsAlexovikFW)`, `protoVersion`, `enum Model`, `KS-*` внутри `decode()`) — с явным контрактом и пометкой к экспорту, при сохранении единого обобщённого контракта данных/команд сверху. Это тот код, что 1:1 ляжет в C++/C#.

**Фаза 4 — Порт на C++ и C#.**
Из очищенного эталона (прямой перенос кода адаптеров). Рекомендации: общий фиксированный формат данных; tagged-union команд; никакого динамического аллока в горячем decode-пути (C++ embedded). Опциональные показательные примеры из Фазы 0 можно использовать для ручной сверки совпадения реализаций.

---

## 6. Ограничения и рекомендации
- **Не менять поведение** на фазах 0–3 — только развязка зависимостей (переиспользование логики как есть). Сверка — по коду/справочным тестам, без их запуска.
- **fixed-point арифметика** (1/100 и т.п.) сохранить как есть — не переходить на float в ядре, чтобы embedded-порт был детерминирован и совпадал по байтам с эталоном.
- **Endianness**: протоколы смешивают BE/LE (`MathsUtil` имеет и те, и те) — при порте на C++ не полагаться на нативный порядок байт, только явные хелперы.
- Каждый из 8 протоколов **экспортируется отдельно** (требование пользователя) — публичный реестр `WheelType`→протокол, но единый обобщённый контракт данных/команд сверху.
- Начать с **Gotway** (уже документирован, среднего размера, богатая логика: BMS, PID, Alexovik-FW) как эталонный вертикальный срез через все фазы, затем размножить на остальные.

## 7. Артефакты, которые субагент должен произвести
1. `docs/wheel-core-boundary-spec.md` — language-neutral контракт границы (§2, §3, форматы кадров всех протоколов).
2. `docs/wheel-core-config-classification.md` — таблица 78 config-ключей, разбитых на (A) вход-поведение / (B) reported-setting, с указанием адаптеров-потребителей.
3. Обновлённый план фазы 2 (конкретные файлы/классы, порядок рефакторинга, список `android.*`/Koin/Timber точек, которые надо срезать).
4. Схема каталога будущего модуля (`wheelcore/` Kotlin-модуль) + список файлов для переноса.
5. `docs/wheel-core-model-converters.md` — реестр экспортируемых **контрактов байтовых конвертеров по конкретным моделям/прошивкам** (протокол → модель → формат кадра → контракт `decode`/`buildCommand`), на основе матрицы из [current-architecture §7-bis](wheel-core-current-architecture.md).

---

### Быстрые команды для старта
```bash
# config-поверхность адаптеров
grep -ohE "appConfig\.(get|set)[A-Za-z0-9]+" app/src/main/java/com/cooper/wheellog/utils/*Adapter*.java | sort -u
# Android-зависимости в ядре
grep -rnE "^import (android|com.welie|org.koin)|Timber|sendBroadcast|getInstance\(\)" app/src/main/java/com/cooper/wheellog/utils/*Adapter*.java app/src/main/java/com/cooper/wheellog/WheelData.java
# эталонные тесты
ls app/src/test/java/com/cooper/wheellog/utils/*AdapterTest.kt
```
