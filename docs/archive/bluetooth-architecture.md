> **Архив (30.07.2026).** Карта BLE-стека оригинального WheelLog; точна как справка по upstream, роль исчерпала. Наш транспорт — `WheelTalk.Droid/Ble/AndroidBleClient.cs` (профиль FFE0/FFE1 константами, без JSON-топологии), реконнект — `WheelSession` в ядре. Ссылки `../app/...` ведут в репозиторий Wheellog.Android.

# Bluetooth Connection Architecture — карта для выноса в отдельную библиотеку

> Черновой документ. Цель: зафиксировать все точки входа, классы и связи BLE-подсистемы
> WheelLog.Android, чтобы можно было аккуратно вынести её в отдельный модуль/библиотеку.
> UI-слой (Activity/Fragments/Compose) сознательно не описывается — фокус на
> сервисном/доменном слое: подключение → распознавание модели колеса → сбор данных →
> отправка команд → служебные сервисы вокруг этого.

## Статус
- [x] Найдена точка входа в BLE-подключение
- [x] Расписана логика подключения/переподключения (`BluetoothService`)
- [x] Расписан слой распознавания колеса и парсинга протоколов (`WheelData` + `*Adapter`)
- [x] Построена иерархия классов сверху вниз (сервис → data hub → адаптеры → служебные сервисы)
- [ ] Расписана логика сканирования (`ScanActivity`) в деталях
- [ ] Расписана бизнес-логика парсинга байт-протоколов внутри каждого `*Adapter`
- [ ] Определены итоговые границы будущего модуля/библиотеки (какие классы войдут как есть, какие потребуют интерфейсов-абстракций)

## Иерархия классов (сверху вниз)

```
Уровень 0 — DI / bootstrap
  WheelLog (Application, Koin startKoin)

Уровень 1 — транспортный сервис (BLE I/O)
  BluetoothService (Service)
      использует: BluetoothCentralManager/BluetoothPeripheral (Blessed-Android)

Уровень 2 — доменный хаб (данные + распознавание + маршрутизация команд)
  WheelData (singleton)
      ├─ детектирует протокол через BluetoothService.getWheelServices()
      ├─ хранит все поля телеметрии
      └─ getAdapter() → делегирует decode()/команды в BaseAdapter-наследника

Уровень 3 — протокол-специфичные адаптеры (парсинг + формирование команд)
  BaseAdapter (abstract, контракт)
      ├─ KingsongAdapter
      ├─ GotwayAdapter / GotwayVirtualAdapter
      ├─ VeteranAdapter
      ├─ InMotionAdapter
      ├─ InmotionAdapterV2
      ├─ NinebotAdapter
      └─ NinebotZAdapter

Уровень 4 — служебные сервисы вокруг ядра (потребители WheelData/BluetoothService)
  LoggingService      — запись телеметрии в CSV, трипы, локация
  PebbleService        — трансляция данных на Pebble-часы
  GarminConnectIQ       — трансляция данных на Garmin-часы (HTTP-мост NanoHTTPD)
  GearService           — трансляция данных на Samsung Gear (SAAgent)
  ElectroClub            — выгрузка треков в облачный сервис electro.club
  NotificationUtil        — foreground-уведомление приложения
  Alarms                   — проверка телеметрии на превышение лимитов (сигналы тревоги)
  FileUtil / ParserLogToWheelData — файловый I/O логов, восстановление состояния из лога
  AppConfig                — настройки (Koin), читается почти всеми уровнями выше
```

---

## 1. Главная точка входа: `BluetoothService`

**Файл:** [app/src/main/java/com/cooper/wheellog/BluetoothService.kt](../app/src/main/java/com/cooper/wheellog/BluetoothService.kt)

`android.app.Service` (foreground service), инкапсулирует весь жизненный цикл BLE-соединения с колесом.
Использует сторонюю библиотеку **Blessed-Android** (`com.welie.blessed.*`) как обёртку над `BluetoothGatt`.

Ключевые элементы:
- `central: BluetoothCentralManager` (строка 50) — ленивая обёртка Blessed над `BluetoothAdapter`/сканером/GATT.
- `bluetoothCentralManagerCallback` (строка 58) — колбэки уровня "центрального устройства":
  - `onConnectionFailed` (61) — при неудаче, если идёт активный поиск, пробует `autoConnectPeripheral`.
  - `onConnectedPeripheral` (69) — звук подключения, wakelock, широковещательный `ACTION_BLUETOOTH_CONNECTION_STATE`.
  - `onDisconnectedPeripheral` (98) — логика авто-переподключения, сброс адаптеров конкретных колёс (`InMotionAdapter.stopTimer()`, `NinebotZAdapter.getInstance().resetConnection()` и т.п.), broadcast статуса.
- `wheelCallback: BluetoothPeripheralCallback` (строка 156) — колбэки уровня "конкретного периферийного устройства" (аналог `BluetoothGattCallback`):
  - `onMtuChanged` (158) — переговоры о размере MTU (для расширенных фреймов KingSong F22 Pro).
  - `onServicesDiscovered` (173) — **точка распознавания типа колеса**: вызывает `WheelData.getInstance().detectWheel(...)` дважды (обычные и proxy-сервисы из `res/raw/bluetooth_services` / `bluetooth_proxy_services`).
  - `onCharacteristicWrite` / `onCharacteristicUpdate` (203, 215) — приём сырых данных → `readData(...)`.
  - `onDescriptorWrite` (236).
- `readData()` (247) — если включено RAW-логирование, пишет байты в файл; затем по `WheelData.wheelType` направляет данные в `WheelData.decodeResponse()` с фильтрацией по UUID характеристики.
- Публичное API сервиса (используется извне через `LocalBinder`):
  - `connect()` (381), `disconnect()` (409), `toggleConnectToWheel()` (425)
  - `writeWheelCharacteristic(cmd: ByteArray)` (481) — запись команды в колесо, ветвление по `WHEEL_TYPE`
  - `setCharacteristicNotification()`, `getWheelServices()`, `getWheelService()`, `writeWheelDescriptor()`
  - `wheelAddress: String` — MAC адрес, `connectionState`, `isWheelSearch`
- `onBind()` (358) — старт foreground-уведомления, запуск `startReconnectTimer()`.
- `startReconnectTimer()` (333) — таймер watchdog: если данные от колеса не приходят 15 сек, форсирует переподключение.

**Зависимости через DI (Koin):** `AppConfig` (настройки пользователя), `NotificationUtil` (foreground-уведомление).

---

## 2. Точка входа сканирования: `ScanActivity`

**Файл:** [app/src/main/java/com/cooper/wheellog/ScanActivity.kt](../app/src/main/java/com/cooper/wheellog/ScanActivity.kt)

Отдельная `AppCompatActivity`, независимая от `BluetoothService`, со своим `BluetoothCentralManager` (строка 43).
- `onResume()` (135) — проверяет разрешения и включённость BT, запускает `scanLeDevice(true)`.
- `scanLeDevice()` (224) — `central.scanForPeripherals()`, таймаут 10 сек (`scanPeriod`).
- `bluetoothCentralManagerCallback.onDiscoveredPeripheral` (111) — на каждое найденное устройство добавляет его в `DeviceListAdapter` ([DeviceListAdapter.java](../app/src/main/java/com/cooper/wheellog/DeviceListAdapter.java)), парсит manufacturer data из `scanResult.scanRecord`.
- `onItemClickListener` (172) — при выборе устройства из списка возвращает `RESULT_OK` с extras `MAC`/`NAME` через `setResult()` — вызывающая `MainActivity` получает адрес и передаёт его в `BluetoothService.wheelAddress`.

Это **чистая point-to-point активность**: сканирует → возвращает MAC. Не хранит состояние подключения.

> UI-слой (`MainActivity` как биндер к `BluetoothService`/`ScanActivity`) сознательно вынесен
> за скобки этого документа по решению пользователя — интересует только сервисный/доменный слой.

---

## 4. Хаб данных и распознавания протокола: `WheelData`

**Файл:** [app/src/main/java/com/cooper/wheellog/WheelData.java](../app/src/main/java/com/cooper/wheellog/WheelData.java) (singleton, `getInstance()`)

Это God-object: держит и телеметрию колеса, и ссылку на `BluetoothService` (`mBluetoothService`, строка 36), и логику распознавания протокола.

- `setBluetoothService(BluetoothService value)` (147) / `getBluetoothService()` (135-ish) — сеттер/геттер ссылки на сервис.
- `bluetoothCmd(byte[] cmd)` (140) — прокси-вызов `mBluetoothService.writeWheelCharacteristic(cmd)`.
- `detectWheel(String deviceAddress, Context mContext, int servicesResId)` (1252) — **сердце распознавания**: сравнивает набор обнаруженных GATT-сервисов/характеристик (полученных через `mBluetoothService.getWheelServices()`) с шаблонами из JSON-ресурсов (`res/raw/bluetooth_services.json`, `bluetooth_proxy_services.json`), определяет `adapterName` → `WHEEL_TYPE`. После распознавания:
  - вызывает `mBluetoothService.getWheelService()/setCharacteristicNotification()/writeWheelDescriptor()` для подписки на notify-характеристику конкретного протокола;
  - для некоторых типов колёс стартует keep-alive таймеры соответствующего адаптера (`InMotionAdapter.getInstance().startKeepAliveTimer(...)`, `InmotionAdapterV2...`).
- `decodeResponse(byte[] data, Context mContext)` (1075) — точка входа для входящих данных из `BluetoothService.readData()`; делегирует парсинг в `getAdapter().decode(data)`, затем обновляет поля телеметрии и шлёт broadcast `ACTION_WHEEL_DATA_AVAILABLE`.
- `getAdapter(): BaseAdapter` (113) — возвращает текущий адаптер протокола в зависимости от `mWheelType`.

---

## 5. Слой протоколов колёс (`BaseAdapter` + реализации)

### 5.1 Модель колеса: `WHEEL_TYPE`

**Файл:** [utils/Constants.kt:98-100](../app/src/main/java/com/cooper/wheellog/utils/Constants.kt)

```kotlin
enum class WHEEL_TYPE {
    Unknown, KINGSONG, GOTWAY, NINEBOT, NINEBOT_Z, INMOTION, INMOTION_V2, VETERAN, GOTWAY_VIRTUAL
}
```

Это единственный "реестр" поддерживаемых моделей/протоколов колёс. `WheelData.mWheelType` хранит
текущее значение, а `WheelData.getAdapter()` ([WheelLog.kt... на самом деле WheelData.java:113](../app/src/main/java/com/cooper/wheellog/WheelData.java)) —
switch по этому enum, возвращающий синглтон нужного адаптера. Добавление новой модели колеса = новое значение enum + новый `*Adapter` + запись в `bluetooth_services.json`.

### 5.2 Контракт адаптера: `BaseAdapter`

**Файл:** [utils/BaseAdapter.kt](../app/src/main/java/com/cooper/wheellog/utils/BaseAdapter.kt)

Абстрактный класс (`KoinComponent`, получает `Context` через DI). Единственный обязательный метод:
```kotlin
abstract fun decode(data: ByteArray?): Boolean   // true = данные валидны, WheelData обновит телеметрию
```
Остальные ~40 методов — `open fun` с пустой реализацией по умолчанию (свет/фары, лимиты скорости,
режимы езды, калибровка, alarm-пороги, PID-параметры и т.д.). Каждый конкретный адаптер переопределяет
только то, что поддерживает его протокол/модель колеса.

### 5.3 Реализации адаптеров (по одной на бренд/протокол)

Каждая — синглтон (`getInstance()`), `extends BaseAdapter`, живёт в `com.cooper.wheellog.utils`:

| Класс | `WHEEL_TYPE` | Файл |
|---|---|---|
| `KingsongAdapter` | KINGSONG | [KingsongAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/KingsongAdapter.java) |
| `GotwayAdapter` | GOTWAY | [GotwayAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/GotwayAdapter.java) |
| `GotwayVirtualAdapter` | GOTWAY_VIRTUAL | [GotwayVirtualAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/GotwayVirtualAdapter.java) |
| `VeteranAdapter` | VETERAN | [VeteranAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/VeteranAdapter.java) |
| `InMotionAdapter` | INMOTION | [InMotionAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/InMotionAdapter.java) |
| `InmotionAdapterV2` | INMOTION_V2 | [InmotionAdapterV2.java](../app/src/main/java/com/cooper/wheellog/utils/InmotionAdapterV2.java) |
| `NinebotAdapter` | NINEBOT | [NinebotAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/NinebotAdapter.java) |
| `NinebotZAdapter` | NINEBOT_Z | [NinebotZAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/NinebotZAdapter.java) |

### 5.4 Два направления потока данных внутри адаптера (общий паттерн)

**Приём телеметрии (wheel → app):**
`BluetoothService.readData()` → `WheelData.decodeResponse(bytes)` → `getAdapter().decode(bytes)` →
адаптер парсит байты протокола конкретного бренда → пишет результат обратно в поля `WheelData`
через сеттеры (`WheelData.getInstance().setSpeed(...)`, `setVoltage(...)` и т.п.) → возвращает `true/false`.

**Отправка команд (app → wheel):**
UI/бизнес-логика вызывает метод адаптера (например `KingsongAdapter.getInstance().setLightMode(...)`)
→ адаптер формирует байтовый пакет команды в формате своего протокола →
`WheelData.getInstance().bluetoothCmd(data)` ([WheelData.java:140](../app/src/main/java/com/cooper/wheellog/WheelData.java)) →
`BluetoothService.writeWheelCharacteristic(cmd)` ([BluetoothService.kt:481](../app/src/main/java/com/cooper/wheellog/BluetoothService.kt)) —
выбирает нужный `SERVICE_UUID`/`CHARACTERISTIC_UUID` по `WheelData.wheelType` и пишет в GATT-характеристику через Blessed.

Пример из `KingsongAdapter.java`: почти каждый командный метод (setLightMode, wheelBeep, updateAlarmMode, ...)
заканчивается вызовом `WheelData.getInstance().bluetoothCmd(data)` — т.е. **адаптер не имеет прямого доступа
к Bluetooth**, весь физический ввод-вывод идёт исключительно через `WheelData` → `BluetoothService`.

Некоторые адаптеры (InMotion, InmotionAdapterV2, NinebotZ) дополнительно держат keep-alive таймеры
(`startKeepAliveTimer()`/`stopTimer()`), которые периодически шлют служебные пакеты для поддержания
сессии — их запускает `WheelData.detectWheel()` сразу после распознавания протокола (см. §4), а
останавливает `BluetoothService.onDisconnectedPeripheral` (см. §1) при разрыве связи.

---

## 6. UUID-константы GATT-сервисов

**Файл:** [app/src/main/java/com/cooper/wheellog/utils/Constants.kt](../app/src/main/java/com/cooper/wheellog/utils/Constants.kt) (строки 37–70)

Каждый бренд имеет свой `SERVICE_UUID` / `READ_CHARACTER_UUID` / `WRITE_CHARACTER_UUID` (иногда read==write характеристика). Используются и в `BluetoothService`, и в `WheelData.detectWheel()`.

Также в `res/raw/`:
- `bluetooth_services.json` — шаблоны сервисов/характеристик для прямого подключения.
- `bluetooth_proxy_services.json` — шаблоны для подключения через прокси/эмулятор.

---

## 6.5 Служебные сервисы вокруг ядра (Уровень 4)

Все эти сервисы **не участвуют в самом BLE-подключении** — они потребители данных из `WheelData`
(или транслируют команды в неё) и работают параллельно, каждый в своём Android `Service`/классе.
Связь с ядром — не прямая (не наследование/DI-инъекция интерфейса), а через `WheelData.getInstance()`
как общий singleton и через broadcast-события (`Constants.ACTION_*`).

| Сервис | Файл | Роль | Как связан с ядром |
|---|---|---|---|
| `LoggingService` | [LoggingService.kt](../app/src/main/java/com/cooper/wheellog/LoggingService.kt) | Пишет CSV-лог поездки (телеметрия + опционально GPS), хранит трипы через `TripDao` | Читает `WheelData.getInstance()` в `updateFile()`; получает `ConnectionState` извне через `updateConnectionState()`; по завершении может залить трек в `ElectroClub` |
| `PebbleService` | [PebbleService.java](../app/src/main/java/com/cooper/wheellog/PebbleService.java) | Транслирует телеметрию на умные часы Pebble | Слушает broadcast `ACTION_WHEEL_DATA_AVAILABLE` и т.п. через свой `BroadcastReceiver` |
| `GarminConnectIQ` | [GarminConnectIQ.kt](../app/src/main/java/com/cooper/wheellog/GarminConnectIQ.kt) | Транслирует телеметрию на Garmin через Connect IQ SDK; поднимает локальный `NanoHTTPD`-веб-сервер как мост | Аналогично — читает `WheelData`, реагирует на broadcast-события ядра |
| `GearService` | [GearService.java](../app/src/main/java/com/cooper/wheellog/GearService.java) | Транслирует телеметрию на Samsung Gear (`extends SAAgent`, Samsung Accessory SDK) | Аналогично |
| `ElectroClub` | [ElectroClub.kt](../app/src/main/java/com/cooper/wheellog/ElectroClub.kt) | Клиент облачного сервиса electro.club: `uploadTrack()`, привязка гаража по MAC колеса | Вызывается из `LoggingService.onDestroy()` после закрытия файла лога; не зависит от BLE напрямую |
| `NotificationUtil` | [utils/NotificationUtil.kt](../app/src/main/java/com/cooper/wheellog/utils/NotificationUtil.kt) | Строит/обновляет foreground-уведомление (обязательно для foreground `Service` в Android) | Инжектится в `BluetoothService` и `LoggingService` через Koin |
| `Alarms` | [utils/Alarms.kt](../app/src/main/java/com/cooper/wheellog/utils/Alarms.kt) | Проверяет телеметрию на превышение порогов (скорость/ток/температура/батарея) и поднимает тревогу | `object` (синглтон), читает поля `WheelData`, вызывается из цикла обновления телеметрии |
| `FileUtil` / `ParserLogToWheelData` | [utils/FileUtil.kt](../app/src/main/java/com/cooper/wheellog/utils/FileUtil.kt), [utils/ParserLogToWheelData.kt](../app/src/main/java/com/cooper/wheellog/utils/ParserLogToWheelData.kt) | Низкоуровневый файловый I/O CSV-логов; парсер, который умеет восстановить состояние `WheelData` из уже записанного лога (для продолжения лога в тот же день) | Используются `BluetoothService` (RAW-лог) и `LoggingService` (трип-лог) |
| `AppConfig` | [AppConfig.kt](../app/src/main/java/com/cooper/wheellog/AppConfig.kt) | Централизованное хранилище настроек пользователя (Koin `single`) | Инжектится практически во все вышеперечисленные классы (звук при подключении, авто-реконнект, автозагрузка в electro.club и т.д.) |

Все Уровень-4-сервисы регистрируются как отдельные Koin-модули/DI-инъекции и стартуют независимо от
`BluetoothService` (обычно из `MainActivity`, что здесь не описываем). Для будущей библиотеки это
значит: **ядро (Уровень 1–3) физически не зависит от Уровня 4** — зависимость односторонняя
(Уровень 4 → читает `WheelData`), что упрощает выделение Уровня 1–3 в отдельный модуль. Единственное
устройство связи, которое надо будет заменить — Android broadcast intents, на которые подписываются
некоторые из этих сервисов.

---

## 7. Внешняя библиотека для BLE

Проект **не использует** стандартный Android `BluetoothGatt` напрямую — вместо этого обёртка **Blessed-Android** (`com.welie.blessed:blessed-android`, пакет `com.welie.blessed.*`):
- `BluetoothCentralManager` — сканирование + управление подключениями (обёртка над `BluetoothLeScanner`/`BluetoothAdapter`).
- `BluetoothPeripheral` — обёртка над конкретным `BluetoothGatt`/`BluetoothDevice`.
- `BluetoothCentralManagerCallback`, `BluetoothPeripheralCallback` — колбэки.
- Важно для выноса в библиотеку: **вся BLE-логика уже зависит от Blessed**, а не только от Android SDK — при переносе в отдельный модуль эту зависимость нужно тащить с собой (или абстрагировать).

---

## 8. Граф связей (входящие/исходящие точки, без UI)

```
BluetoothService (Уровень 1, транспорт)
  ├─ central: BluetoothCentralManager (Blessed) — connect/disconnect/GATT/scan
  ├─ вызывает WheelData.getInstance().detectWheel(...) при onServicesDiscovered
  ├─ вызывает WheelData.getInstance().decodeResponse(...) при входящих данных (readData)
  ├─ writeWheelCharacteristic(cmd) ── принимает готовый байт-пакет команды и пишет в GATT
  ├─ шлёт sendBroadcast(...): ACTION_BLUETOOTH_CONNECTION_STATE, ACTION_WHEEL_TYPE_RECOGNIZED, ACTION_RAW_LOGGING_TOGGLED
  ├─ AppConfig (Koin) — настройки (звуки, raw-логирование, реконнект)
  ├─ NotificationUtil (Koin) — foreground notification
  └─ FileUtil — запись RAW BLE-данных на диск

WheelData (Уровень 2, singleton-хаб)
  ├─ держит обратную ссылку на BluetoothService (для отправки команд/чтения GATT-сервисов)
  ├─ detectWheel() ── определяет WHEEL_TYPE по набору GATT-сервисов, запускает keep-alive адаптера
  ├─ decodeResponse(bytes) → getAdapter().decode(bytes) → обновление полей телеметрии
  ├─ bluetoothCmd(bytes) ← вызывается адаптерами для отправки команд, прокси в BluetoothService
  ├─ decodeResponse() → sendBroadcast(ACTION_WHEEL_DATA_AVAILABLE, ACTION_WHEEL_IS_READY)
  └─ getAdapter() → BaseAdapter (Уровень 3)

BaseAdapter-наследники (Уровень 3, per-brand протоколы)
  ├─ decode(bytes) ── парсинг телеметрии, пишет через сеттеры WheelData
  ├─ команды (setLightMode/wheelBeep/...) ── формируют байт-пакет → WheelData.bluetoothCmd(...)
  └─ (для InMotion*/NinebotZ) keep-alive таймеры, стартуют/стопают через WheelData/BluetoothService

Служебные сервисы (Уровень 4, потребители)
  ├─ LoggingService ── читает WheelData на каждый тик → пишет CSV; по завершении → ElectroClub.uploadTrack()
  ├─ PebbleService / GarminConnectIQ / GearService ── читают WheelData / слушают broadcast → шлют на внешние устройства
  ├─ Alarms ── читает WheelData → проверка порогов → уведомления/звук
  └─ AppConfig / NotificationUtil / FileUtil ── общая инфраструктура, инжектятся почти во все вышеперечисленные классы
```

---

## 9. Наблюдения для будущего выноса в библиотеку

Зафиксировано, но не проработано:
1. **Циклическая связь `BluetoothService` ↔ `WheelData`**: сервис вызывает методы `WheelData` (detectWheel, decodeResponse), а `WheelData` держит обратную ссылку на сервис (`mBluetoothService`) и вызывает его методы (`writeWheelCharacteristic`, `getWheelServices` и т.д.). Это главный узел, который нужно распутать при выделении библиотеки.
2. Коммуникация с остальным приложением идёт через **Android broadcast intents** (`sendBroadcast`/`Constants.ACTION_*`) — не через прямые колбэки/Flow. При переносе в чистую Kotlin/Java библиотеку это, вероятно, стоит заменить на слушатели/callback-интерфейсы или Kotlin Flow, а broadcast оставить как адаптер в app-модуле.
3. Зависимости от Android-специфичных вещей внутри BLE-слоя: `Context` (через Koin `KoinComponent.get()` в `BaseAdapter`), `PowerManager.WakeLock`, `Service`, звуковые эффекты (`SomeUtil.playSound`) — их нужно либо абстрагировать интерфейсами, либо оставить в app-модуле как "обвязку" вокруг чистой BLE-библиотеки.
4. `AppConfig` (Koin DI) используется внутри `BluetoothService` для настроек логирования/звука/реконнекта — тоже кандидат на интерфейс-абстракцию при выносе.
5. Определение типа колеса (`detectWheel`) завязано на JSON-ресурсы Android (`R.raw.bluetooth_services`) — при переносе in a library эти данные нужно либо встраивать как ассеты библиотеки, либо передавать извне.

---

## Легенда файлов (сводная таблица)

| Уровень | Файл | Роль |
|---|---|---|
| 0 | [WheelLog.kt](../app/src/main/java/com/cooper/wheellog/WheelLog.kt) | `Application`, инициализация Koin DI-модулей |
| 1 | [BluetoothService.kt](../app/src/main/java/com/cooper/wheellog/BluetoothService.kt) | Транспортный сервис: жизненный цикл BLE-соединения, GATT callbacks, физическая отправка команд |
| 1 | [ScanActivity.kt](../app/src/main/java/com/cooper/wheellog/ScanActivity.kt) | Сканирование BLE-устройств (независимо от `BluetoothService`) |
| 2 | [WheelData.java](../app/src/main/java/com/cooper/wheellog/WheelData.java) | Singleton-хаб: телеметрия, распознавание протокола (`detectWheel`), маршрутизация decode/command в адаптеры |
| 3 | [utils/BaseAdapter.kt](../app/src/main/java/com/cooper/wheellog/utils/BaseAdapter.kt) | Контракт парсера протокола конкретного бренда/модели колеса |
| 3 | [utils/KingsongAdapter.java](../app/src/main/java/com/cooper/wheellog/utils/KingsongAdapter.java), `GotwayAdapter`, `GotwayVirtualAdapter`, `VeteranAdapter`, `InMotionAdapter`, `InmotionAdapterV2`, `NinebotAdapter`, `NinebotZAdapter` | Реализации протоколов по брендам: parse телеметрии + формирование команд |
| — | [utils/Constants.kt](../app/src/main/java/com/cooper/wheellog/utils/Constants.kt) | `WHEEL_TYPE` enum, UUID сервисов/характеристик GATT, имена broadcast-actions |
| — | [DeviceListAdapter.java](../app/src/main/java/com/cooper/wheellog/DeviceListAdapter.java) | Список найденных BLE-устройств для `ScanActivity` |
| 4 | [LoggingService.kt](../app/src/main/java/com/cooper/wheellog/LoggingService.kt) | CSV-логирование поездки, трипы, GPS |
| 4 | [PebbleService.java](../app/src/main/java/com/cooper/wheellog/PebbleService.java) | Трансляция телеметрии на Pebble |
| 4 | [GarminConnectIQ.kt](../app/src/main/java/com/cooper/wheellog/GarminConnectIQ.kt) | Трансляция телеметрии на Garmin (Connect IQ + локальный NanoHTTPD) |
| 4 | [GearService.java](../app/src/main/java/com/cooper/wheellog/GearService.java) | Трансляция телеметрии на Samsung Gear |
| 4 | [ElectroClub.kt](../app/src/main/java/com/cooper/wheellog/ElectroClub.kt) | Загрузка треков в облако electro.club |
| 4 | [utils/NotificationUtil.kt](../app/src/main/java/com/cooper/wheellog/utils/NotificationUtil.kt) | Foreground-уведомление |
| 4 | [utils/Alarms.kt](../app/src/main/java/com/cooper/wheellog/utils/Alarms.kt) | Проверка телеметрии на превышение лимитов |
| 4 | [utils/FileUtil.kt](../app/src/main/java/com/cooper/wheellog/utils/FileUtil.kt), [utils/ParserLogToWheelData.kt](../app/src/main/java/com/cooper/wheellog/utils/ParserLogToWheelData.kt) | Файловый I/O логов, восстановление состояния из лога |
| 4 | [AppConfig.kt](../app/src/main/java/com/cooper/wheellog/AppConfig.kt) | Централизованные настройки пользователя (Koin) |
