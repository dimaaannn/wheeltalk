# Телеметрия наружу: внешние устройства

Снимок анализа оригинала (`C:\Work\repos\Wheellog.Android`) на 28.07.2026. Здесь — **только
исходящее** направление: что WheelLog отдаёт на внешние устройства, в каких контрактах и с какими
особенностями. Входящее (декодеры протоколов колеса) описано в
[bluetooth-architecture.md](archive/bluetooth-architecture.md) и [csharp-testport-plan.md](archive/csharp-testport-plan.md).

План адаптации — [android-plan-10-telemetry-out.md](android-plan-10-telemetry-out.md).
Загрузка треков на сервисы — [telemetry-out-services.md](telemetry-out-services.md).

---

## 0. Главное в одном абзаце

Прямого BLE-вещания у WheelLog **нет**. Приложение не поднимает GATT-сервер, не рекламирует свой
сервис и вообще не выступает BLE-периферией — единственная BLE-роль — центральный, подключённый к
колесу. Всё, что уходит на часы и браслеты, идёт **через чужие транспорты**: PebbleKit (Bluetooth
Classic/BLE под капотом Pebble-приложения), Wear OS Data Layer (Bluetooth или Wi-Fi — решает Google
Play Services), Samsung Accessory Protocol, Garmin Connect IQ (через Garmin Connect Mobile), и
обычные Android-уведомления (Mi Band и любой браслет, зеркалящий шторку). Плюс глобальные
broadcast-интенты, которые де-факто стали публичным API для сторонних приложений на самом телефоне.

Значит **«контракт передачи телеметрии по Bluetooth» у оригинала — это пять разных контрактов
поверх пяти чужих стеков**, а не один свой. Это ключевое для планирования: переносить нечего,
можно только повторять, и каждый повтор тянет за собой чужую SDK.

---

## 1. Внутренняя шина: то, на чём держатся все интеграции

Всё исходящее подключается к одному и тому же источнику — **глобальным broadcast-интентам**,
которые рассылает ядро. Источник: `utils/Constants.kt`, отправка — `WheelData.java`,
`utils/Alarms.kt`, `BluetoothService.kt`, `LoggingService.kt`.

### 1.1. Действия (actions)

| Action | Кто шлёт | Когда |
|---|---|---|
| `com.cooper.wheellog.bluetoothConnectionState` | `BluetoothService` | смена состояния соединения |
| `com.cooper.wheellog.wheelDataAvailable` | `WheelData.decodeResponse` | **каждый декодированный кадр** |
| `com.cooper.wheellog.wheelTypeChanged` | `WheelData.setWheelType` | распознан тип колеса |
| `com.cooper.wheellog.wheelTypeRecognized` | `BluetoothService` | то же, раньше по времени |
| `com.cooper.wheellog.wheelModelChanged` | `WheelData.setModel` | пришло имя модели |
| `com.cooper.wheellog.wheelIsReady` | `WheelData` | адаптер собрал всё нужное для работы |
| `com.cooper.wheellog.wheelNews` | адаптеры | текстовая новость/ошибка от колеса |
| `com.cooper.wheellog.alarmTriggered` | `Alarms.raiseAlarm` | сработала тревога |
| `com.cooper.wheellog.loggingServiceToggled` | `LoggingService` | старт/стоп записи |
| `com.cooper.wheellog.rawLoggingToggled` | | старт/стоп сырого дампа |
| `com.cooper.wheellog.preferenceReset` | | сброс накопленных величин |
| `com.cooper.wheellog.pebbleServiceToggled` / `pebbleAppReady` / `pebbleAppScreen` / `pebblePreferenceChanged` | Pebble-слой | см. §2 |
| `com.cooper.wheellog.notification*Button` | уведомление | кнопки в шторке (входящее) |

### 1.2. Полезная нагрузка `wheelDataAvailable`

`WheelData.java:1096-1130`. Ключи — **строки с заглавной буквы**, значения — сырые целые в том же
представлении, что внутри `WheelData` (сотые доли):

| Extra | Тип | Единица |
|---|---|---|
| `Speed` | int | 1/100 км/ч |
| `PWM` | double | проценты × 100 (`mCalculatedPwm`) |
| `Voltage` | int | 1/100 В |
| `Current` | int | 1/100 А |
| `PhaseCurrent` | int | 1/100 А |
| `Power` | int | 1/100 Вт |
| `Battery` | int | проценты |
| `Temp1`, `Temp2` | int | 1/100 °C |
| `MaxSpeed` | int | 1/100 км/ч |
| `MaxPwm`, `MaxPower`, `MaxTemp` | | максимумы за поездку |
| `Distance` | int | метры |
| `TotalDistance` | long | метры |
| `RideTime` | int | секунды |
| `graph_update_available` | bool | флаг: пора обновить график (не чаще `GRAPH_UPDATE_INTERVAL`) |

`alarmTriggered` несёт `alarm_type` (**сериализованный enum `ALARM_TYPE`**, а не int — это ловушка
для чужого приёмника) и `alarm_value` (double).

`ALARM_TYPE`: `SPEED1(1) SPEED2(2) SPEED3(3) CURRENT(4) TEMPERATURE(5) PWM(6) BATTERY(7) WHEEL(8)`.

### 1.3. Битовая маска тревог

Отдельно от интента живёт `Alarms.alarm: Int` (`utils/Alarms.kt:52-68`) — её читают Gear и Wear OS:

| Бит | Тревога |
|---|---|
| `0x01` | скорость |
| `0x02` | ток |
| `0x04` | температура |
| `0x08` | заряд |
| `0x10` | тревога самого колеса |

### 1.4. Нюансы шины

- **`sendBroadcast`, а не `LocalBroadcastManager`.** Интенты уходят системе целиком. Для действий
  из §1.1 в манифесте **нет `<intent-filter>` и нет `android:permission`** — значит любое
  приложение на телефоне может подписаться на `wheelDataAvailable` и читать телеметрию. Защита
  (`${applicationId}.permission`) стоит только на Pebble- и Samsung-ресиверах, то есть на входящих.
  Это не задокументированный API, но фактический, и на нём живут сторонние поделки.
- **Частота — частота кадров колеса.** Broadcast уходит на каждый успешный декод: у Veteran/Gotway
  это 5–20 раз в секунду. Каждый такой интент будит всех подписчиков, включая Pebble-сервис,
  обновление уведомления и запись в файл.
- **Никакой батчинг не предусмотрен.** Каждый потребитель сам решает, что делать с потоком:
  Pebble сравнивает с прошлым значением, Wear OS шлёт всё подряд, уведомление перерисовывается.

---

## 2. Pebble

Файлы: `PebbleService.java`, `PebbleBroadcastReceiver.java`, константы в `utils/Constants.kt:74-82`.
Транспорт — библиотека **PebbleKit** (`com.getpebble.android.kit`), которая общается с приложением
Pebble на телефоне через свои broadcast-интенты; сам Bluetooth-канал WheelLog не видит.

### 2.1. Контракт

- **UUID приложения на часах**: `185c8ae9-7e72-451a-a1c7-8f1e81df9a3d`
- **Версия протокола**: `PEBBLE_APP_VERSION = 104`. Часы присылают свою в `KEY_READY`; если она
  меньше — телефон шлёт Pebble-нотификацию «обновите приложение».
- **Формат посылки**: `PebbleDictionary`, все значения `addInt32`.

Исходящие ключи (`PebbleService.java:36-50`):

| Key | Имя | Значение | Экран |
|---|---|---|---|
| 0 | SPEED | `getSpeed()`, 1/100 км/ч | GUI |
| 1 | BATTERY | проценты | GUI |
| 2 | TEMPERATURE | `getTemperature()`, °C | GUI |
| 3 | FAN_STATE | 0/1 | GUI |
| 4 | BT_STATE | 0/1 | GUI |
| 5 | VIBE_ALERT | 0 = «скорость», 1 = «ток» | всегда |
| 6 | USE_MPH | 0/1 | при `refreshAll` |
| 7 | MAX_SPEED | из настроек, км/ч | при `refreshAll` |
| 8 | RIDE_TIME | секунды | DETAILS |
| 9 | DISTANCE | `getDistance()/100` | DETAILS |
| 10 | TOP_SPEED | `getTopSpeed()/10` | DETAILS |
| 11 | READY | 0 при первой посылке | однократно |
| 12 | VOLTAGE | 1/100 В | GUI |
| 13 | CURRENT | 1/100 А | GUI |
| 20 | PWM | **см. дефект ниже** | GUI |

Входящие ключи (`PebbleBroadcastReceiver`): `11` READY (значение = версия приложения на часах),
`10012` LAUNCH_APP (часы просят поднять телефонное приложение), `10013` PLAY_HORN,
`10014` DISPLAYED_SCREEN (`0` = GUI, `1` = DETAILS).

### 2.2. Механика передачи

- **Дельта-передача.** Каждое поле сравнивается с последним отправленным и попадает в словарь
  только если изменилось. `refreshAll` (после `pebbleAppReady`, смены экрана, смены настроек)
  заставляет отправить всё.
- **Отправка только для активного экрана.** GUI и DETAILS не пересекаются — на DETAILS скорость
  не шлётся вовсе.
- **Окно 500 мс + ACK/NACK.** Пока предыдущая посылка не подтверждена и не прошло `MESSAGE_TIMEOUT`,
  новые данные только помечаются флагом `data_available`; ACK очищает словарь и шлёт накопленное,
  NACK — повторяет.
- **Тревога сжата до двух значений.** `CURRENT` и `TEMPERATURE` → `1`, всё остальное → `0`.
  В коде это прямо названо «костылём под legacy-приложение часов».

### 2.3. Дефекты

- `PebbleService.java:146` — `outgoingDictionary.addInt32(KEY_PWM, lastCurrent)`: под ключ ШИМ
  кладётся ток. Сравнение при этом идёт по ШИМ, то есть значение обновляется тогда, когда меняется
  ШИМ, а уезжает — ток. **На часах ШИМ показывается неверно уже сейчас.**

---

## 3. Wear OS

Файлы: `companion/WearOs.kt` (телефон), модуль `wearos/` (часы), общий контракт — модуль `shared/`
(`com/wheellog/shared/Constants.kt`, `WearPage.kt`, `SmartDouble.kt`).

Транспорт — **Wearable Data Layer API** из Google Play Services. Физический канал (Bluetooth или
Wi-Fi) выбирает сама GMS; приложение о нём не знает и повлиять не может.

### 3.1. Пути

| Путь | Тип | Направление | Назначение |
|---|---|---|---|
| `/wheel_data` | DataItem | телефон → часы | телеметрия |
| `/page_settings` | DataItem | телефон → часы | набор включённых страниц |
| `/messages` | Message | оба | ping/pong/finish/horn/light |
| `/start/wearos` | Message | телефон → часы | запустить приложение на часах |

Сообщения (`/messages`, тело — UTF-8 строка): `ping`, `pong`, `finish`, `horn`, `light`.

### 3.2. Поля DataItem `/wheel_data`

В отличие от Pebble — **человеческие единицы, а не сотые**:

| Ключ | Тип | Единица |
|---|---|---|
| `speed`, `max_speed` | double | км/ч |
| `voltage` | double | В |
| `current`, `max_current` | double | А |
| `power`, `max_power` | double | Вт |
| `pwm`, `max_pwm` | double | % |
| `temperature`, `max_temperature` | double | °C |
| `battery`, `battery_lowest` | int | % |
| `distance` | double | км |
| `main_unit` | string | локализованное «км/ч» или «миль/ч» |
| `current_on_dial` | bool | что рисовать на циферблате |
| `alarm` | int | битовая маска §1.3 |
| `timestamp` | long | мс, `wd.lastLifeData` |
| `time_string` | string | `HH:mm` |
| `alarm_factor1`, `alarm_factor2` | int | пороги ШИМ (80/90) — часы рисуют зоны сами |

`/page_settings`: `pages` — сериализованный `EnumSet<WearPage>` через `;`
(`Main;PWM;Temperature;Current;Voltage;Power;Distance`), плюс `timestamp`.

### 3.3. Механика

- **Рукопожатие.** При старте телефон шлёт `ping`; если за 500 мс не пришёл `pong` — шлёт пустое
  сообщение на `/start/wearos`, чтобы часы подняли приложение. До получения `pong`
  `sendUpdateData()` данных не пишет вовсе, только повторяет `ping`.
- **Отправка на каждый `wheelDataAvailable`** (`MainActivity.kt:431-434`) — без прореживания.
- **`setUrgent()`** на каждом `PutDataRequest` — просит GMS доставить немедленно, а не пачкой.
- **`timestamp` в каждом наборе обязателен.** Data Layer дедуплицирует одинаковые DataItem: без
  меняющегося поля стоящее колесо просто перестало бы обновлять часы.
- **Пороги тревог уезжают на часы**, а не решение о тревоге. Часы сами красят шкалу.

### 3.4. Дефекты

- `WearOs.kt:44-45` — `max_current` пишется дважды: сначала `maxCurrentDouble`, затем поверх
  `maxPhaseCurrentDouble`. Ключ для фазного тока в контракте отсутствует, так что часы показывают
  максимум фазного тока под видом максимума тока.
- `WearActivity.kt:130-131` — на стороне часов маска разбирается неверно:
  `alarmCurrent = alarmInt and 2 == 1` и `alarmTemp = alarmInt and 4 == 1` не могут быть истинны
  никогда (`and 2` даёт 0 или 2). Работает только тревога по скорости.

---

## 4. Garmin Connect IQ

Файл: `GarminConnectIQ.kt`. Два слоя: SDK Connect IQ для обнаружения устройства и **локальный
HTTP-сервер** для собственно данных.

### 4.1. Контракт

- **ID приложения на часах**: стабильное `487e6172-972c-4f93-a4db-26fd689f935a`,
  бета `433c30dc-f316-4d11-a16e-de153d297705`; ID в магазине `35719a02-8a5d-46bc-b474-f26c54c4e045`.
- Через Connect IQ передаётся **ровно одно сообщение — номер порта** локального сервера.
- Сервер — `NanoHTTPD` на `127.0.0.1`, порт **0 (система выдаёт свободный)**. Часы ходят на него
  сами через Garmin Connect Mobile.

### 4.2. Эндпоинты

| Метод | Путь | Ответ |
|---|---|---|
| GET | `/data/main` | `speed`, `topSpeed`, `speedLimit`, `useMph`, `battery`, `temp`, `pwm`, `maxPwm`, `connectedToWheel`, `wheelModel` |
| GET | `/data/details` | `useMph`, `avgRidingSpeed`, `avgSpeed`, `topSpeed`, `voltage`, `maxVoltage`, `battery`, `ridingTime`, `distance`, `pwm`, `maxPwm`, `torque`, `power`, `maxPower`, `connectedToWheel` |
| GET | `/data/alarms` | голое число — маска §1.3 |
| POST | `/actions/triggerHorn` | `Executed!` |
| POST | `/actions/frontLight/enable` | `Executed!` |
| POST | `/actions/frontLight/disable` | `Executed!` |

### 4.3. Нюансы

- **Единицы конвертируются на телефоне** по `useMph`: скорость → мили, температура → °F. Флаг
  `useMph` всё равно передаётся, чтобы часы подписали шкалу.
- **Типы полей неоднородны.** `speed`, `topSpeed`, `voltage`, `pwm`, `maxPwm` — **строки**
  (`pwm` форматируется `"%02.0f"`), остальное — числа и булевы. Разбор на часах обязан это знать.
- **Опрос, а не push.** Частоту задают часы; телефон только держит сервер.
- **Авторизации нет.** Защита — привязка к `127.0.0.1`: любой процесс на самом телефоне может
  прочитать телеметрию и нажать сигнал.
- `topSpeed` считается как `((topSpeed / 10).toFloat() / 10)` — целочисленное деление первым
  действием, то есть теряется десятая доля.
- `POST /actions/frontLight/disable` **ничего не делает** — возвращает `Executed!` и всё.
- **Интеграция фактически выключена**: `MainActivity.kt:533` — вызов `toggleGarminConnectIQ()`
  закомментирован, `garminConnectIqEnable` по умолчанию `false`.

---

## 5. Samsung Gear

Файлы: `GearService.java`, `GearSAPServiceProviderConnection.java`, описание сервиса —
`res/xml/sapservices.xml`. Транспорт — **SAP (Samsung Accessory Protocol)** через Samsung
Accessory SDK; канал `SAP_SERVICE_CHANNEL_ID = 142` (число обязано совпадать в `sapservices.xml`
по обе стороны).

### 5.1. Контракт

Полезная нагрузка — **JSON-строка в UTF-8 байтах**, собранная вручную через `String.format`
(`Locale.ROOT`), без библиотеки:

```
{ "speed":%.2f,"voltage":%.2f,"current":%.2f,"power":%.2f,
  "batteryLevel":%d,"distance":%d,"totalDistance":%d,"temperature":%d,
  "temperature2":%d,"angle":%.2f,"roll":%.2f,"isAlarmExecuting":%d,
  "gpsEnabled":%b,"hasSpeed":%b,"gpsSpeed":%1.2f,"hasBearing":%b,"bearing":%1.4f,
  "latitude":%f,"longitude":%f,"hasAltitude":%b,"altitude":%1.3f }
```

- Единицы — человеческие (км/ч, В, А, Вт, метры, °C, градусы).
- `isAlarmExecuting` — маска §1.3.
- `mode` и `alert` закомментированы в коде и не передаются.

### 5.2. Нюансы

- **Это единственный канал, куда уходит GPS.** `GearService` держит собственный
  `LocationListener` (`GPS_PROVIDER`, 1000 мс / 1 м) — независимо от `LoggingService`. Если колесо
  не подключено, часам уходит **только** GPS-блок; если нет и его — пустой `{}`.
- **Собственный таймер 200 мс**, а не подписка на broadcast. Комментарий в коде («cada 500ms»)
  расходится с числом. Это единственная интеграция с фиксированной частотой.
- Часы **всегда инициируют соединение**; телефон только принимает (`onServiceConnectionRequested`
  → `acceptServiceConnectionRequest`).
- При подключении шлётся строка-заглушка `"Mensaje inicial"` — не JSON. Приёмник обязан это
  пережить.
- Требует Samsung-специфичных разрешений (`com.samsung.wmanager.APP`,
  `com.samsung.accessory.permission.ACCESSORY_FRAMEWORK` и др.) и `ACCESS_BACKGROUND_LOCATION`.

---

## 6. Mi Band и любой браслет со шторкой

Файлы: `utils/NotificationUtil.kt`, `utils/MiBandEnum.kt`.

**Отдельного протокола нет.** Телеметрия пишется в **текст обычного Android-уведомления**, а
браслет зеркалит шторку средствами своего приложения. Поэтому «поддержка Mi Band» — это,
строго говоря, поддержка любого устройства, показывающего уведомления.

### 6.1. Режимы

`MiBandEnum`: `Alarm(0)`, `Min(1)`, `Medium(2)`, `Max(3)`. Различаются только объёмом текста —
экран браслета маленький, и это способ уложиться:

| Режим | Что в тексте |
|---|---|
| `Alarm` | заголовок «Тревога» + текст сработавшей тревоги; телеметрии нет вовсе |
| `Min` | скорость, максимальная скорость, заряд, пробег |
| `Medium` | скорость, средняя, ШИМ, заряд, температура, пробег |
| `Max` | скорость, максимальная, средняя, заряд, напряжение, мощность, температура, пробег |

Строки-шаблоны: `notification_text_min` / `_med` / `_max` в `res/values/strings.xml`.

### 6.2. Нюансы

- В режиме `Alarm` уведомление **не обновляется на каждый кадр** (`MainActivity.kt:436`) — иначе
  тревожный текст сразу затирался бы телеметрией.
- `mibandFixRs` — таймер, дёргающий `update()` раз в секунду при ненулевой скорости
  ([PR #249](https://github.com/Wheellog/Wheellog.Android/pull/249)). Обход бага конкретных
  прошивок браслета, где зеркало не подхватывает изменения текста. В коде честно назван «kostil».
- Канал уведомления — `IMPORTANCE_MIN`, чтобы шторка не шумела на каждое обновление.

---

## 7. Сводная таблица

| Канал | Транспорт | Формат | Частота | Обратный канал | Настройка |
|---|---|---|---|---|---|
| Pebble | PebbleKit | `PebbleDictionary`, int32 | по изменению, окно 500 мс | сигнал, смена экрана, запуск приложения | кнопка «часы» |
| Wear OS | GMS Data Layer | DataMap (double/int/string) | каждый кадр | ping/pong, сигнал, фара | `autoWatch`, `wearOsPages` |
| Garmin | Connect IQ + локальный HTTP | JSON (смешанные типы) | опрос часами | сигнал, фара | `garminConnectIqEnable` (выключено) |
| Samsung Gear | SAP, канал 142 | JSON-строка вручную | 200 мс, свой таймер | нет | — |
| Mi Band | Android-уведомление | локализованный текст | каждый кадр (или 1 Гц) | нет | `mibandMode`, `mibandFixRs` |
| Сторонние приложения | глобальный broadcast | Intent extras, сырые сотые | каждый кадр | нет | нет (всегда включено) |

---

## 8. Что из этого — контракт, а что случайность

**Контракт** (менять нельзя без ломки чужого кода):

- UUID и номера ключей Pebble, `PEBBLE_APP_VERSION`.
- Пути и имена ключей Wear OS из модуля `shared/` — модуль на то и общий.
- ID приложений Connect IQ, набор URL и имена полей JSON.
- Номер SAP-канала 142 и имена полей JSON Gear.
- Битовая маска тревог — её читают три канала независимо.
- Имена broadcast-действий и extras: на них подписаны чужие приложения.

**Случайность** (следствие истории, не решение):

- Сотые доли в broadcast и человеческие единицы в Wear OS — потому что писалось в разное время.
- Строковый `speed` в Garmin — следствие форматирования на телефоне.
- `Locale.ROOT` в Gear и `Locale.US` в журнале — одно и то же, записано по-разному.
- Пять разных способов решить «когда отправлять»: дельта+ACK, каждый кадр, опрос, свой таймер,
  таймер-костыль.
