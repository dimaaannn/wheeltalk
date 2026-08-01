> **Архив (30.07.2026).** План первого среза порта; роль выполнена — срез Veteran работает, и солюшен давно перерос описанную здесь структуру (11 проектов вместо одного, см. AGENTS.md «Структура кода»). Логика декодера в §5 перенесена точно и остаётся верной; описания структуры, NuGet и Program.cs — исторические. Ссылки `../app/...` ведут в репозиторий Wheellog.Android.

# Задача для субагента: C# тестовый порт (.NET 10 console) — вертикальный срез Veteran / Sherman L

> **Тип задачи:** реализация нового кода (не рефакторинг Android-проекта). Создать отдельный C#-solution.
> **Основание:** план экспорта [wheel-core-extraction-task.md](wheel-core-extraction-task.md),
> текущая архитектура [wheel-core-current-architecture.md](wheel-core-current-architecture.md),
> карта BLE [bluetooth-architecture.md](bluetooth-architecture.md).
> **Цель:** проверить работоспособность подхода на C# — сквозной срез
> «скан BLE → подключение по адресу → приём байт → декод в обобщённые контракты → вывод в консоль →
> отправка команд». Контракты и логика конвертации переносятся **1:1** из Android-исходников.

---

## 0. Зафиксированные решения (согласовано с пользователем)

| Параметр | Решение |
|---|---|
| BLE-стек | **Windows.Devices.Bluetooth (WinRT)** — нативный Windows BLE central, без внешних зависимостей |
| Тестовое колесо | **Sherman L** → протокол **Veteran** (`WHEEL_TYPE.VETERAN`, `mVer == 6`) → первый декодер = **VeteranAdapter** |
| Объём порта | **Один вертикальный срез** (только Veteran); остальные 7 протоколов добавляются позже по тому же паттерну |
| Тип приложения | Консольное приложение .NET 10, ручной вызов методов, сырая консоль (без меню/управления) |
| Логирование | Serilog через `ILogger`, структурированные логи |
| Конфиг и адрес колеса | **`appsettings.json`** — адрес колеса (сырой MAC, скопированный из скана) + дефолты `IWheelConfig` (значения — из Android `AppConfig`, см. §5.6) |
| Пейринг Windows | **Не требуется** — WheelLog работает с этими колёсами без bonding; подключаемся напрямую по адресу |

**Почему Veteran — удачный первый срез:** пассивный поток данных, **нет keep-alive-таймера**, **нет пароля**,
использует те же простые GATT-UUID, что Gotway (HM-10 / FFE0-FFE1), всего 443 строки исходника.

---

## 1. Структура solution

Пользователь просил **одно** консольное приложение с папками. Чтобы при этом ядро осталось
переиспользуемым (глобальная цель — портируемое ядро), держим BLE-специфику **за интерфейсом-портом
`ITransport`** даже внутри одного проекта: код в папке `Core/` не должен использовать `Windows.*`.

```
WheelCore.TestPort.sln
└─ WheelCore.TestPort/                (console app)
   ├─ WheelCore.TestPort.csproj       // TFM: net10.0-windows10.0.19041.0 (нужно для WinRT BLE)
   │                                  //     <UseWindowsForms/WPF> = false; <Nullable>enable</Nullable>
   ├─ appsettings.json                 // адрес колеса + дефолты IWheelConfig (§5.6); copy-to-output
   ├─ Program.cs                       // §7 — сырые методы-заготовки, ручной вызов
   │
   ├─ Core/                           // ★ БИЗНЕС-ЛОГИКА (перенос 1:1, без Windows.*/Android) ★
   │  ├─ Contracts/                   // §2 — обобщённые контракты (верхняя граница)
   │  │  ├─ WheelType.cs              //   enum, копия WHEEL_TYPE
   │  │  ├─ TelemetrySnapshot.cs      //   record — унифицированные ДАННЫЕ (выход)
   │  │  ├─ WheelSettings.cs          //   record — отражённые настройки колеса (reported)
   │  │  ├─ WheelCommand.cs           //   обобщённые КОМАНДЫ (вход) — enum/варианты
   │  │  ├─ SmartBms.cs               //   порт SmartBms.kt 1:1
   │  │  └─ WheelEvents.cs            //   типы событий (заменяют ACTION_*)
   │  ├─ Ports/                       // нижняя граница — интерфейсы, реализует хост
   │  │  ├─ ITransport.cs             //   write(bytes) + событие DataReceived(bytes)
   │  │  ├─ IWheelConfig.cs           //   параметры поведения (A) + доступ к reported (B)
   │  │  ├─ IEventSink.cs             //   публикация WheelEvents
   │  │  └─ IClock.cs                 //   now/postDelayed (для Veteran не нужен, но заложить)
   │  ├─ Decoding/                    // §5 — перенос конвертеров 1:1
   │  │  ├─ MathsUtil.cs              //   BE/LE byte-хелперы (только нужные Veteran)
   │  │  ├─ VeteranUnpacker.cs        //   автомат сборки кадра (порт veteranUnpacker)
   │  │  ├─ VeteranDecoder.cs         //   порт VeteranAdapter.decode() + команды
   │  │  └─ WheelState.cs             //   мутабельное состояние (порт нужного из WheelData)
   │  └─ Services/                    // §4
   │     ├─ Decoder.cs               //   сервис: bytes → TelemetrySnapshot (обёртка над VeteranDecoder)
   │     └─ WheelService.cs          //   оркестратор: команды вниз, поток данных в Decoder
   │
   ├─ Ble/                            // §3 — Windows BLE клиент (реализация ITransport)
   │  └─ WindowsBleClient.cs         //   Windows.Devices.Bluetooth: scan/connect/disconnect/write/notify
   │
   └─ Debug/                          // §6 — тестовая обвязка
      ├─ ConsolePresenter.cs         //   красивый вывод снимка телеметрии + событий
      ├─ LoggingSetup.cs             //   конфигурация Serilog → ILogger
      ├─ RawReplayTransport.cs       //   (улучшение) ITransport из RAW_*.csv, тест без колеса
      └─ TestHarness.cs              //   высокоуровневые сценарии для ручного вызова
```

> **NuGet:** `Serilog`, `Serilog.Extensions.Logging`, `Serilog.Sinks.Console`; `System.IO.Hashing`
> (для CRC32 — см. §5.4); `Microsoft.Extensions.Configuration.Json` + `Microsoft.Extensions.Configuration.Binder`
> (для `appsettings.json`). WinRT-типы доступны из TFM `net10.0-windows10.0.19041.0` без пакетов.

---

## 2. Контракты (верхняя граница) — материализация из плана

План описывает контракты концептуально; здесь их надо оформить как конкретные C#-типы.
Источник полей — `WheelData.java` (геттеры) и `BaseAdapter.kt` (команды). **Fixed-point сохранять как в
оригинале** (скорость/ток/напряжение в 1/100, distance в метрах) — не переходить на float, ради 1:1.

### 2.1 `TelemetrySnapshot` (record, неизменяемый — выход Decoder)
Поля из `WheelData` (для Veteran реально заполняются): `Speed`, `Voltage`, `Current`, `PhaseCurrent`,
`Power`, `Pwm`/`MaxPwm`, `Battery`, `Temperature`, `TopSpeed`, `WheelDistance`, `TotalDistance`,
`DistanceFromStart`, `Angle` (pitch), `ChargingStatus`, `SleepTimerSec`, `Version`, `Model`, `WheelType`,
`Bms1`, `Bms2`. Добавить единицы в XML-doc каждого поля. Дать удобные derived-геттеры (`SpeedKmh`,
`VoltageV` и т.п.) как в `WheelData.get*Double()`.

### 2.2 `WheelCommand` (вход)
Обобщённый контракт команд = union из 41 `open fun` `BaseAdapter`. **Рекомендация:** discriminated
union через абстрактный record + наследники (`SetLight(bool)`, `Beep()`, `SetPedalsMode(int)`,
`ResetTrip()`, `Calibrate()` …), а не 41 метод-интерфейс — проще диспетчеризовать и портировать далее в
C++. Для Veteran-среза реально реализуются только команды из §5.5, остальные — заглушки (no-op).

### 2.3 `SmartBms` — порт [SmartBms.kt](../app/src/main/java/com/cooper/wheellog/utils/SmartBms.kt) 1:1
Массив `Cells[]`, min/max/avg cell, cellDiff, temps 1–6, voltage, current, cellNum. Перенести все поля и
`reset()`.

### 2.4 Порты (нижняя граница)
- **`ITransport`**: `Task WriteAsync(byte[] cmd)`; `event Action<byte[]> DataReceived`; `ConnectAsync(address)`, `DisconnectAsync()`, `ScanAsync()`. Реализация — `WindowsBleClient` (§3) и `RawReplayTransport` (§6).
- **`IWheelConfig`**: типизированный доступ к параметрам поведения (A) и reported-настройкам (B) — конкретные ключи для Veteran в §5.3. НЕ string-KV, а свойства.
- **`IEventSink`**: `Publish(WheelEvent e)` — заменяет `sendBroadcast(ACTION_*)`.
- **`IClock`**: заложить интерфейс (`Now`, `PostDelayed`), но для Veteran не используется.

---

## 3. BLE-клиент `WindowsBleClient` (реализует `ITransport`)

Пространство имён `Windows.Devices.Bluetooth`, `Windows.Devices.Bluetooth.Advertisement`,
`Windows.Devices.Bluetooth.GenericAttributeProfile`, `Windows.Devices.Enumeration`.

**GATT для Veteran/Sherman L (= Gotway-профиль):**
- Service UUID: `0000ffe0-0000-1000-8000-00805f9b34fb`
- Notify + Write характеристика: `0000ffe1-0000-1000-8000-00805f9b34fb` (одна и та же для чтения и записи)
- CCCD: `00002902-...` (WinRT включает notify через `WriteClientCharacteristicConfigurationDescriptorAsync`)
- Запись команд: `WriteType = WithoutResponse` (как в Android `writeWheelCharacteristic`).

**Требуемые методы:**
- `ScanAsync()` — `BluetoothLEAdvertisementWatcher`; на каждое устройство вывести в консоль `Name` + `BluetoothAddress` (MAC) + RSSI. Идёт до ручной остановки/таймаута.
- `ConnectAsync(string mac)` — конвертировать строку MAC → `ulong` (WinRT `BluetoothLEDevice.FromBluetoothAddressAsync(ulong)`); получить сервис `FFE0`, характеристику `FFE1`; подписаться на `ValueChanged` → прокинуть байты в `DataReceived`. Обработать статус подключения (`ConnectionStatusChanged`).
- `DisconnectAsync()` — снять notify, `Dispose()` `BluetoothLEDevice`.
- `WriteAsync(byte[])` — записать в `FFE1` `WithoutResponse`.

**Важные нюансы Windows BLE (заложить в код и отметить в логах):**
1. MAC ↔ `ulong`: WinRT адрес — 48-битный `ulong`. Нужна конверсия `"D4:5A:..."` ⇄ `ulong` (обе стороны).
2. **Пейринг не требуется** — WheelLog штатно работает с этими колёсами без bonding. Подключаться напрямую по адресу через `BluetoothLEDevice.FromBluetoothAddressAsync`, notify включать без спаривания. Спаривание в Windows Settings **не делать**.
3. Notify может прийти пачкой >20 байт (BMS-кадры Sherman L до ~76 байт) — это ок, `VeteranUnpacker` собирает побайтно из любого размера.
4. Все вызовы WinRT — `async`; оборачивать `IAsyncOperation` в `await ...AsTask()` с `CancellationToken`.

---

## 4. Сервисы `WheelService` + `Decoder`

**`Decoder`** (сервис вокруг `VeteranDecoder`):
- Держит `WheelState` (мутабельное) и активный `VeteranDecoder`.
- `Feed(byte[] bytes)` → вызывает `VeteranDecoder.Decode(bytes)`; при `true` — строит `TelemetrySnapshot` из `WheelState` и эмитит событие `SnapshotUpdated` + `IEventSink` (`WheelDataAvailable`).
- Изолирован от BLE — принимает только байты (как `WheelData.decodeResponse`).

**`WheelService`** (оркестратор верхней границы):
- Подписан на `ITransport.DataReceived` → передаёт в `Decoder.Feed`.
- `SendCommand(WheelCommand cmd)` → через `VeteranDecoder`/командный билдер формирует байты → `ITransport.WriteAsync`. (В Android это `WheelData.updateX → adapter → bluetoothCmd`; здесь тот же путь без синглтонов.)
- Экспонирует удобные методы среза: `SetLight(bool)`, `Beep()`, `SetPedalsMode(int)`, `ResetTrip()`.
- Хранит последний `TelemetrySnapshot`, поднимает событие для `ConsolePresenter`.

> Никаких синглтонов/`getInstance()` — всё через конструкторы (ручной DI в `Program.cs` достаточно, контейнер не обязателен).

---

## 5. Перенос Veteran-декодера 1:1 (ядро задачи)

Источник: [VeteranAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/VeteranAdapter.java) (443 строки) + зависимые методы `WheelData`. Переносить **буква-в-букву по логике**, меняя только обвязку (см. §5.6).

### 5.1 Формат кадра (unpacker)
Автомат `veteranUnpacker` (VeteranAdapter.java:338–433):
- Заголовок: `DC 5A 5C`, далее байт длины `len`, далее payload; кадр завершён при `bufferSize == len+3`.
- Сброс состояния, если между пакетами прошло > `WAITING_TIME = 100 мс` (потеря пакета).
- Проверки на позициях 22/23/30 (валидация нулевых/статусных байт).
- **CRC32**: если `len > 38` или формат уже распознан как CRC (`usingCrc`) — последние 4 байта после payload = CRC32 (big-endian через `intFromBytesBE`), сверять с `java.util.zip.CRC32` payload'а. Sherman L (с BMS) — CRC-формат. См. §5.4.

### 5.2 Поля кадра (BE, offset'ы из decode())
`voltage@4`, `speed@6 (*10)`, `distance@8 (intFromBytesRevBE)`, `totalDistance@12 (intFromBytesRevBE)`,
`phaseCurrent@16 (*10)`, `temperature@18`, `autoOffSec@20`, `chargeMode@22`, `speedAlert@24 (*10)`,
`speedTiltback@26 (*10)`, `ver@28` → `mVer = ver/1000`, `pedalsMode@30`, `pitchAngle@32`, `hwPwm@34`.
Версия-строка: `String.format("%03d.%01d.%02d", ver/1000, (ver%1000)/100, ver%100)`.

### 5.3 SmartBMS (mVer ≥ 5, Sherman L = 6) — VeteranAdapter.java:56–128
Пакеты по `pnum = buff[46]`: `bmsnum = pnum<4 ? 1 : 2`. Ветки pnum 0/4 (ток BMS), 1/5 (ячейки 0–14),
2/6 (ячейки 15–29), 3/7 (ячейки 30–41 + temps 1–6 + расчёт min/max/avg/diff/voltage). Перенести целиком,
включая `getCellsForWheel()` (Sherman L → 36 ячеек).

### 5.4 CRC32
Java `java.util.zip.CRC32` = стандартный zlib/IEEE CRC32. В .NET использовать `System.IO.Hashing.Crc32`
(NuGet `System.IO.Hashing`): `Crc32.HashToUInt32(payloadSpan)`. **Внимание к порядку байт:** в оригинале
`provided_crc` читается через `intFromBytesBE(buffer, len)` (big-endian), а `Crc32.HashToUInt32` возвращает
`uint` (значение). Сравнивать как `uint`-значения, аккуратно собрав provided из 4 байт BE. Залогировать
ok/fail (как `Timber.i("CRC32 ok/fail")`).

### 5.5 Команды Veteran (app → wheel)
- `wheelBeep()`: `mVer<3` → `"b"`; иначе фикс. байтовая последовательность (VeteranAdapter.java:333). Sherman L (mVer 6) → байтовая последовательность.
- `setLightState(bool)`: `"SetLightON"` / `"SetLightOFF"`.
- `switchFlashlight()`: тогглит `lightEnabled` в конфиге, зовёт `setLightState`.
- `updatePedalsMode(int)`: `"SETh"` / `"SETm"` / `"SETs"` (0/1/2).
- `resetTrip()`: `"CLEARMETER"`.
Все команды в оригинале → `WheelData.bluetoothCmd(bytes.getBytes())`; здесь → `ITransport.WriteAsync`.

### 5.6 Config-ключи Veteran (для `IWheelConfig`) + дефолты из `appsettings.json`
- **Читает (A, поведение):** `useBetterPercents` (bool), `hwPwm` (bool), `gotwayNegative` (string "0"/"1"/"-1" → int).
- **Пишет (B, reported):** `setHwPwm(true)` при `mVer≥2` (в `getVer()`), `setLightEnabled` (в switchFlashlight).
- **Батарея по mVer-группам** (VeteranAdapter.java:132–213): формулы для групп `<4` / `4,7,43` / `5,6,9,42,44` / `8` / прочие. Перенести все ветки; Sherman L (6) — группа `5,6,9,42,44`.

**Фактические дефолты из Android `AppConfig` (вписать в `appsettings.json`):**

```jsonc
{
  "WheelAddress": "",              // сырой MAC, скопированный из скана (§7 сценарий 1)
  "WheelConfig": {
    // (A) поведение парсинга/расчёта — читает VeteranDecoder
    "GotwayNegative": "0",         // AppConfig default "0" (speed/current по abs)
    "UseBetterPercents": false,
    "HwPwm": false,                // но VeteranDecoder форсит true при mVer>=2 (getVer)
    // derived-расчёты WheelState (setBatteryLevel / calculatePwm)
    "CustomPercents": false,
    "CellVoltageTiltback": 330,    // /100 = 3.30 В
    "RotationSpeed": 500,          // /10 = 50.0
    "RotationVoltage": 840,        // /10 = 84.0
    "PowerFactor": 90,             // /100 = 0.90
    "BatteryCapacity": 0,
    "LightEnabled": false
  }
}
```
> Значения взяты из [AppConfig.kt](../app/src/main/java/com/cooper/wheellog/AppConfig.kt) (getValue/getSpecific defaults). `IWheelConfig` биндится из `appsettings.json`; менять руками при отладке.

### 5.7 Зависимые методы `WheelData` (перенести в `WheelState`)
Декод зовёт: `resetRideTime`, `setSpeed`, `setTopSpeed`, `setWheelDistance`, `setTotalDistance` (логика `mStartTotalDistance`), `setTemperature`, `setPhaseCurrent`, `setVoltage`, `setBatteryLevel` (custom-percents ветка — читает `customPercents`, `cellVoltageTiltback`, `batteryCapacity`; для среза можно с дефолтами, но перенести логику), `setChargingStatus`, `setSleepTimer`, `setAngle`, `setOutput`/`updatePwm` (`mOutput/10000`), `calculatePwm` (читает `rotationSpeed/rotationVoltage/powerFactor` — Sherman L идёт по `updatePwm`, но перенести и это), `calculateCurrent` (`pwm*phaseCurrent`), `calculatePower` (`current*voltage`), `setModel`, `setVersion`, `getBms1/2`. Таблица моделей `getModel()` по mVer (Sherman/Abrams/Sherman S/Patton/Lynx/**Sherman L=6**/Patton S/Oryx/Lynx S/Nosfet*).

### 5.8 Таблица моделей внутри Veteran (пометить к экспорту, см. план §7-bis)
`mVer → модель`: 0–1 Sherman · 2 Abrams · 3 Sherman S · 4 Patton · 5 Lynx · **6 Sherman L** · 7 Patton S ·
8 Oryx · 9 Lynx S · 42 Nosfet Apex · 43 Nosfet Aero · 44 Nosfet Aeon. Влияет на battery %, `getCellsForWheel`,
формат beep. Это тот самый «конвертер конкретной модели» — оформить как явную, экспортируемую единицу.

---

## 6. Debug-обвязка + Serilog

- **`LoggingSetup`**: сконфигурировать Serilog (`Console` sink, шаблон с timestamp/level/props), отдать как `ILogger`/`ILoggerFactory`. Структурированные события: `Scan.DeviceFound {Name, Mac, Rssi}`, `Ble.Connected {Mac}`, `Frame.Received {Hex, Len}`, `Frame.Decoded {Snapshot}`, `Frame.CrcFail`, `Cmd.Sent {Name, Hex}`.
- **`ConsolePresenter`**: подписан на `Decoder.SnapshotUpdated` → печатает читаемый блок (скорость км/ч, вольты, ток, PWM %, батарея %, temp, дистанции, угол, модель/версия, BMS min/max/diff). Реже — таблицей; троттлинг ~1 Гц как `GRAPH_UPDATE_INTERVAL`.
- **`RawReplayTransport`** *(улучшение, реализовать):* `ITransport` поверх `RAW_*.csv` (формат `HH:mm:ss.SSS,<hex>`) из Android-логов — позволяет проверить декодер **без реального колеса**, подавая записанные байты в `Decoder.Feed`. Резко ускоряет ручную отладку логики конвертации.
- **`TestHarness`**: высокоуровневые сценарии-обёртки, которые дёргает `Program.cs` (см. §7).

---

## 7. `Program.cs` — сырые методы-заготовки (первоначальный план действий пользователя)

Без меню и циклов управления. Набор `static async Task`-методов; пользователь раскомментирует нужный в
`Main`, запускает, смотрит консоль. Composition root (`Build()`) — в одном месте: читает `appsettings.json`,
собирает `IWheelConfig`, Serilog `ILogger`, `WindowsBleClient` (как `ITransport`), `Decoder`, `WheelService`,
`ConsolePresenter`. Адрес колеса берётся из `appsettings.json` (`WheelAddress`), не из аргументов.

Реализовать **ровно эти 4 сценария** (это стартовый план ручной проверки):

```csharp
// Сценарий 1 — скан окружения
static async Task Scan();
//   Запустить BLE-скан, печатать по каждому устройству: Name + MAC (в формате для копирования) + RSSI.
//   MAC вывести готовым к вставке в appsettings.json -> WheelAddress. Идёт до Ctrl-C или ~15 c.

// Сценарий 2 — подключение + сырые данные + отключение
static async Task RawDump();
//   Взять адрес из appsettings, ConnectAsync, включить notify,
//   печатать КАЖДЫЙ входящий кадр как hex (Frame.Received {Hex, Len}) — без декодирования,
//   подержать N секунд (или до Ctrl-C), затем корректно DisconnectAsync.

// Сценарий 3 — подключение + живые значения в цикле (троттлинг 0.3 c) + graceful Ctrl-C
static async Task LiveSpeedPwmVoltage();
//   ConnectAsync (адрес из appsettings), поток данных -> Decoder -> TelemetrySnapshot.
//   В консоль печатать ТОЛЬКО Speed (км/ч), PWM (%), Voltage (В), не чаще 1 раза в 300 мс.
//   Ctrl-C -> отмена через CancellationToken -> корректный DisconnectAsync и выход (без «висящего» BLE).

// Сценарий 4 — подключение + команда «фара ВКЛ» + отключение
static async Task HeadlightOn();
//   ConnectAsync (адрес из appsettings), WheelService.SetLight(true)
//   (Veteran -> "SetLightON" -> ITransport.WriteAsync), небольшая пауза, DisconnectAsync.

// (улучшение) офлайн-проверка декодера без колеса
static async Task ReplayRawFile(string path);   // прогнать RAW_*.csv через Decoder.Feed
```

**Требования к реализации сценариев:**
- **Ctrl-C:** через `Console.CancelKeyPress` / `PosixSignalRegistration` → отменить общий `CancellationTokenSource`; в сценариях 2 и 3 это должно гарантированно приводить к `DisconnectAsync` (снять notify, `Dispose` устройства) до выхода — никаких «залипших» подключений.
- **Троттлинг 0.3 c (сценарий 3):** печатать не чаще раза в 300 мс (сравнение по таймстампу последнего вывода, как `GRAPH_UPDATE_INTERVAL` в Android). Снимки приходят чаще — лишние пропускать.
- **Формат вывода 3-го сценария:** одна строка, перезаписываемая/компактная, например `speed=12.3 km/h  pwm=41.2%  volt=100.85 V`.
- Адрес колеса — **только из `appsettings.json`**; между запусками пользователь правит файл руками.

---

## 8. Порядок реализации (для субагента)

1. Создать solution + csproj (TFM `net10.0-windows10.0.19041.0`, `Nullable=enable`), подключить NuGet.
2. `Core/Contracts` + `Core/Ports` — типы и интерфейсы (§2).
3. `Core/Decoding/MathsUtil` — только методы, нужные Veteran (`shortFromBytesBE`, `signedShortFromBytesBE`, `intFromBytesRevBE`, `intFromBytesBE`). Портировать 1:1, endianness — явно.
4. `Core/Decoding/WheelState` — перенос нужного из `WheelData` (§5.7) + `SmartBms`.
5. `Core/Decoding/VeteranUnpacker` + `VeteranDecoder` — перенос 1:1 (§5.1–5.5), CRC32 через `System.IO.Hashing`.
6. `Core/Services/Decoder` + `WheelService` (§4).
7. `Debug/RawReplayTransport` + `ConsolePresenter` + `LoggingSetup` — **сначала проверить декодер на RAW-логе** (без BLE).
8. `Ble/WindowsBleClient` (§3) — scan/connect/notify/write; `appsettings.json` (адрес + дефолты §5.6).
9. `Program.cs` — 4 сценария-метода (§7) + composition root `Build()`.

> Шаг 7 раньше шага 8 намеренно: логику конвертации проверяем на записанных данных до возни с железом.

---

## 9. Улучшения (приняты, включены в план)

1. **RAW-replay транспорт** — тест декодера без колеса на существующих `RAW_*.csv`. (§6, §7)
2. **`ITransport`-граница внутри одного проекта** — код `Core/` без `Windows.*`, что делает будущее выделение в `net10.0` class library тривиальным (и приближает к общему ядру для C++/C#). (§1)
3. **`record`-снимки + Nullable + fixed-point как в оригинале** — детерминизм и байт-в-байт совпадение с Android-эталоном.
4. **`System.IO.Hashing.Crc32`** вместо ручной реализации — совпадает с `java.util.zip.CRC32`. (§5.4)
5. **Дамп кадров в файл** (hex) при приёме — для последующего оффлайн-разбора.
6. **`CancellationToken` во всех async BLE-вызовах** — чистое прерывание сканирования/подключения и graceful Ctrl-C (§7).
7. **`appsettings.json` для адреса колеса и дефолтов конфига** — правка руками между запусками. (§5.6)
8. (опц.) `System.Threading.Channels`/`IObservable` вместо C#-events для потока снимков — если захочется backpressure.

---

## 10. Открытые вопросы / риски (уточнить при реализации)

1. **Пейринг:** не требуется (решено) — подключение по адресу без bonding. Если WinRT неожиданно вернёт пустой список характеристик — это не повод спаривать; сначала проверить корректность адреса/сервиса.
2. **MAC ⇄ ulong:** WinRT оперирует `ulong`-адресом; в `appsettings.json` — строка MAC. Реализовать двустороннюю конверсию; в скане (§7 сценарий 1) печатать строку MAC, готовую к копированию в `appsettings.json`.
3. **CRC32 порядок байт:** свериться, что `provided_crc` (BE в оригинале) корректно сравнивается со значением `Crc32.HashToUInt32`. Это самое тонкое место порта.
4. **Дефолты `IWheelConfig`:** конкретные значения зафиксированы в §5.6 (из Android `AppConfig`) и биндятся из `appsettings.json`. `HwPwm` в конфиге `false`, но декодер форсит `true` при `mVer≥2` — сохранить это поведение.
5. **Размер notify / MTU:** Windows согласует MTU сам; убедиться, что крупные BMS-кадры Sherman L приходят целиком либо корректно собираются unpacker'ом из чанков.

---

### Быстрые ссылки на исходники для порта
- Декодер: [VeteranAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/VeteranAdapter.java)
- Байт-хелперы: [MathsUtil.java](../app/src/main/java/com/cooper/wheellog/utils/MathsUtil.java)
- BMS-модель: [SmartBms.kt](../app/src/main/java/com/cooper/wheellog/utils/SmartBms.kt)
- Состояние/derived: [WheelData.java](../app/src/main/java/com/cooper/wheellog/WheelData.java) (setBatteryLevel, calculatePwm/Current/Power, setTotalDistance)
- GATT-запись/роутинг (референс для BLE-клиента): [BluetoothService.kt:481](../app/src/main/java/com/cooper/wheellog/BluetoothService.kt)
- UUID: [Constants.kt:41-43](../app/src/main/java/com/cooper/wheellog/utils/Constants.kt) (GOTWAY_SERVICE/READ = FFE0/FFE1)
