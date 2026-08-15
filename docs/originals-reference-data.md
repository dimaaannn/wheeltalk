# Справочные данные для этапа архитектурных доработок

Выжимка данных под пункты [`originals-master-plan.md`](originals-master-plan.md) — часть IV
(A1–A8) и связанные пункты частей I–II (аварии P6, доработка BMS и настроек P6, импорт команд
Veteran). **Это таблицы и числа, не пересказ разбора** — рассуждения, история находок и спорные
версии остаются в исходных документах `C:\Work\repos\loeuc`, ссылка стоит у каждого раздела.

**Как читать пометки достоверности.** «Прочитано» — взято из кода/пула констант напрямую.
«Выведено» — восстановлено по соседним признакам (имени, тексту интерфейса, структуре), сам
источник это так и помечает. Где в источнике сказано «в коде нет» или «не установлено» — здесь
написано то же самое, без достройки: единицы измерения, диапазоны, точные формулы, которых нет в
исходнике, здесь не придуманы.

**Уже перенесено в этот репозиторий — здесь не дублируется:**

- [`kingsong-trouble-codes.md`](kingsong-trouble-codes.md) — словарь кодов неисправностей KingSong;
- [`leaperkim-str-field-dictionary.md`](leaperkim-str-field-dictionary.md) — словарь полей режима
  STR, 287 записей;
- [`originals-data/`](originals-data/) — исходные справочники KingSong и инструмент выгрузки.

---

## 1. Каталог настроек колеса DarknessBot — 61 позиция

**Для A3** (каталог настроек колеса — данными, а не перебором).

Источник: [`../../loeuc/darknessbot-analysis.md`](../../loeuc/darknessbot-analysis.md), §14
(«Каталог настроек колеса — все 61 позиция и поддержка по маркам»).

**Достоверность по столбцам.** Ключ, порядковый номер и марки-носители — **прочитаны** из пула
объектов и констант `supportedSettingsItemTypes`. Столбцы «назначение» и «тип» — **выведены**: по
имени ключа, соседним текстам интерфейса и (для InMotion) по методам записи `inmotion_new_adapter`.
Единиц измерения, минимума/максимума, шага и значения по умолчанию **в источнике нет вовсе** —
приложение хранит их не рядом с ключом, а внутри виджета настройки и марочного метода записи;
разбор явно фиксирует это как ограничение подхода DarknessBot, а не пробел разбора.

Сокращения марок: **IM+** — `inmotion_new` (V11…V14, P6), **IM** — старый InMotion (V5…V10),
**GW** — Gotway/Begode, **VT** — Veteran, **KS** — KingSong, **NB** — Ninebot (все три адаптера),
**SM** — Smirnov, **ADO**, **ANT**, **VESC**, **XS** — Xiaomi Skate, **XX** — Xiaoxiang BMS,
**YK** — Yokamura.

| # | ключ | назначение (выведено) | тип (выведено) | марки (прочитано) |
|---|---|---|---|---|
| 0 | `model` | модель устройства | строка, чтение | IM+, IM, GW, VT, KS, NB×3, SM, ADO, ANT, XS, XX, YK |
| 1 | `assembly` | сборка/ревизия | строка, чтение | KS, NB-one, XX |
| 2 | `activation` | активация устройства | действие | — (только demo) |
| 3 | `serialNumber` | серийный номер | строка, чтение | IM+, IM, KS, NB, NB-enc, NB-one, XS |
| 4 | `version` | версия прошивки | строка, чтение | IM+, IM, GW, VT, KS, NB×3, SM, XX |
| 5 | `batteryCapacity` | ёмкость батареи | число (Ач/Втч) | IM+, IM, GW, VT, KS, NB×3, SM, VESC, XS |
| 6 | `ridingMode` | режим езды | выбор из списка | IM+, GW, VT, KS, NB, NB-one, SM, ADO, YK |
| 7 | `maxSpeed` | максимальная скорость | число, км/ч | IM+, IM, GW, VT, KS, NB-one, SM, ADO |
| 8 | `limitSpeed` | скорость наклона педалей (tiltback) | число, км/ч | IM+, IM, GW, VT, KS, NB, NB-one, SM |
| 9 | `limit` | ограничитель вкл/выкл | переключатель | IM+, IM, GW, VT, KS, NB, NB-one, SM |
| 10 | `pincode` | пин-код колеса | строка цифр | IM, KS |
| 11 | `lights` | фара | переключатель/выбор | KS |
| 12 | `torch` | фонарь | переключатель | KS |
| 13 | `handle` | ручка/переноска | переключатель | IM+, IM, KS |
| 14 | `volume` | громкость динамика колеса | число 0…100 | IM+, IM, GW, VT, SM |
| 15 | `gyroLevel` | уровень гироскопа/жёсткость | число | IM+, IM, VT, KS |
| 16 | `alarms` | тревоги колеса | группа | KS |
| 17 | `alarmsMode` | режим тревог колеса | выбор | GW, SM |
| 18 | `batteryInfo` | сведения о батарее | экран, чтение | IM+, VT, KS, GW, NB, NB-enc, ANT, XX |
| 19 | `maxSpeedMode` | режим максимальной скорости | выбор | — (только demo) |
| 20 | `lightsColors` | цвета подсветки | цвет | KS |
| 21 | `lightsMode` | режим подсветки | выбор | GW, KS, NB, SM |
| 22 | `equalizerMode` | эквалайзер звука колеса | выбор | KS |
| 23 | `firmwareUpdate` | обновление прошивки | действие | — (только demo) |
| 24 | `voiceControl` | голосовые подсказки колеса | переключатель | KS |
| 25 | `diagnostic` | диагностика | экран, чтение | KS |
| 26 | `calibration` | калибровка | действие | IM+, GW, KS, SM, XS |
| 27 | `recuperationMode` | рекуперация | выбор | NB, NB-enc |
| 28 | `cruiseControl` | круиз-контроль | переключатель | IM+, NB, NB-enc, ADO, YK |
| 29 | `brakeLights` | стоп-сигнал | переключатель | NB, NB-enc |
| 30 | `shutdownTime` | время автовыключения | число, минуты | IM+, KS |
| 31 | `brightness` | яркость фары/экрана | число 0…100 | IM+, VT, ADO |
| 32 | `safeMode` | безопасный режим | переключатель | IM+, VT |
| 33 | `maxRollAngle` | предельный угол крена | число, градусы | GW, VT, KS, SM |
| 34 | `handleMode` | режим ручки | выбор | KS |
| 35 | `modelSelection` | ручной выбор модели | выбор | GW |
| 36 | `lightSensor` | датчик света | переключатель | KS |
| 37 | `chargeControl` | контроль заряда | число/переключатель | ANT, XX |
| 38 | `bmsSettings` | настройки BMS | группа | ANT |
| 39 | `firmwareUsesMiles` | прошивка считает в милях | переключатель | — (только demo) |
| 40 | `limitPWM` | ограничение по ШИМ | число, % | GW, SM |
| 41 | `pwmCorrection` | поправка ШИМ | число | IM+, IM, GW, VT, NB, NB-one, SM |
| 42 | `batteryCorrection` | поправка процента заряда | число | IM+, IM, GW, VT, KS, SM, VESC |
| 43 | `pwmSoftwareToggle` | программный ШИМ вкл/выкл | переключатель | GW, VT, SM |
| 44 | `extremeMode` | экстремальный режим | переключатель | GW, SM |
| 45 | `rotationControl` | контроль вращения | переключатель/число | GW, SM |
| 46 | `breakingAmperage` | ток торможения | число, А | GW, SM |
| 47 | `customRotationAngle` | угол вращения (тонкая настройка) | число | GW, SM |
| 48 | `customAdvancedSettings` | группа расширенных настроек | группа | GW, SM |
| 49 | `customHorizontalKP` | ПИД горизонтали, P | число | GW, SM |
| 50 | `customHorizontalKI` | ПИД горизонтали, I | число | GW, SM |
| 51 | `customHorizontalKD` | ПИД горизонтали, D | число | GW, SM |
| 52 | `customDynamicCompensation` | динамическая компенсация | число | GW, SM |
| 53 | `customDynamicCompensationFilter` | фильтр динамической компенсации | число | GW, SM |
| 54 | `customAccelerationCompensation` | компенсация ускорения | число | GW, SM |
| 55 | `customTurnCompensation` | компенсация поворота | число | GW, SM |
| 56 | `customCurrentKP` | ПИД тока, P | число | GW, SM |
| 57 | `customCurrentKI` | ПИД тока, I | число | GW, SM |
| 58 | `customCurrentDKP` | ПИД тока, DP | число | GW, SM |
| 59 | `customCurrentDKI` | ПИД тока, DI | число | GW, SM |
| 60 | `trickMode` | трюковый режим | переключатель | SM |

**Четыре позиции не поддержаны ни одной живой маркой** — `activation` (2), `maxSpeedMode` (19),
`firmwareUpdate` (23), `firmwareUsesMiles` (39): встречаются только у `demo_adapter`, который
объявляет 60 из 61 (нет только `lightSensor`).

**Сводка по маркам (число поддержанных позиций):** demo 60, Gotway 34, Smirnov 31, KingSong 27,
InMotion+ 19, Veteran 16, InMotion старый 13, Ninebot 13, Ninebot-one 10, Ninebot-enc 8,
Xiaoxiang 5, Ado 5, Xiaomi Skate 5, Ant 4, Yokamura 2, VESC 2.

Методы записи InMotion+, соответствующие каталогу (прочитано поимённо, `inmotion_new_adapter.dart`):
`changeMaxSpeed`, `changeLimitSpeed`, `changeLimitMode`, `changeRidingLevel`, `changeGyroLevel`,
`changeSafeMode`, `changeCruiseControl`, `changeShutdownTime`, `changeLockMode`,
`changeLightsToggle`, `changeTorchMode`, `changeBrightness`, `changeVolume`, `changeHandle`,
`calibration`, `beep`, `turnOff`.

Записи в колесо сопровождаются подтверждением: `BaseAdapter.wait(predicate)` опрашивает состояние
до 40 раз с шагом 50 мс (потолок 2 с) и возвращает «не дождался», если предикат не выполнился.

---

## 2. Три канала тревог

**Для A1** (разделение по каналам) и **A6** (приоритет + гистерезис, тот же движок).

Источник: [`../../loeuc/darknessbot-analysis.md`](../../loeuc/darknessbot-analysis.md), §12
(«Три канала тревог — параметры каждого»). Все числа — **прочитаны** из ветвлений
`AlarmHelper.processAlarms` (`0x86cdb0`…`0x86d0b4`) и соседних методов.

### 2.1 Пять типов тревог, три порога, уровень 0…3

Типы (`AlarmType`): `speed`, `pwm`, `amperage`, `power`, `temperature`.

Для каждого типа — свой шаблон уровня:

```
уровень = 3, если порог3 задан и значение его достигло
          2, если порог2 задан и значение его достигло
          1, если порог1 задан и значение его достигло
          0 иначе
```

У каждого типа **три независимых порога**, любой выключается обнулением. **Итого 15 порогов на
устройство** (5 типов × 3 ступени). Пороги — поля устройства (`field_1b`, `field_23`, `field_63`,
`field_f7` и соседние), это настройки пользователя, числовых умолчаний в коде тревог нет.

Пять уровней сводятся `reduce()` в один общий — **берётся максимум по всем типам**, и этот один
уровень раздаётся каналам.

### 2.2 Звук — темп сирены по уровню

`if (общийУровень > 0) startSound(); else stopSound();` — звук **состояние**, не событие: пока
хоть одна тревога держится выше нуля, сирена играет непрерывно.

Множитель темпа воспроизведения одного и того же звукового образца (прочитано по ветвлениям
`AlarmHelper.startSound`, `0x872bf*`, через `Soundpool.setRate`):

| уровень | множитель темпа |
|---|---|
| 1 | 0,5× |
| 2 | 1,2× |
| 3 | 2,0× |

### 2.3 Вибрация — число импульсов равно уровню

`AlarmHelper._processVibrationAlarm(уровень)` (`0x87020c`):

- уровень 0 → сброс памяти (последний уровень 0, время `null`), тишина;
- тот же уровень уже вибрировал менее **3 000 000 мкс = 3 с** назад → подавить повтор;
- смена уровня **пробивает подавление немедленно**;
- иначе — `_vibratePattern(уровень)`, уровень зажимается в 1…3.

Пресеты пакета `vibration` (`0x870318`, значения из пула объектов):

| уровень | пресет | ordinal | число импульсов |
|---|---|---|---|
| 1 | `singleShortBuzz` | 0 | 1 |
| 2 | `doubleBuzz` | 1 | 2 |
| 3 | `tripleBuzz` | 2 | 3 |

**Мёртвый путь, не переносить как рабочий образец:** в `MainActivity` есть канал
`darkness_bot/vibration` с узором `[0, 220, (180, 220)×(n−1)]` мс — но Dart-код его ни разу не
вызывает. Числа 220/180 мс относятся только к этому неиспользуемому пути.

### 2.4 Голос — своё подавление, 5 секунд

`AlarmHelper._processVoiceAlarm` (`0x86fb64`): подавление повтора произнесения — порог
**5 000 000 мкс = 5 с**, дальше `_voiceAlarmText()` → `speak()` через `flutter_tts`. Пять
дополнительных аргументов метода (сверх `this`) в источнике не разбирались.

### 2.5 Окна подавления — сводка

| канал | что подавляет | окно |
|---|---|---|
| звук | ничего, это состояние «есть тревога / нет» | — |
| вибрация | повтор **того же уровня** | 3 с |
| голос | повтор произнесения | 5 с |
| информационное сообщение (см. §3) | повтор произнесения | 20 с |

**Приоритета между каналами у DarknessBot нет** — включённые в настройках (`AlarmSettingsType`:
`sound`, `voice`, `vibration`) работают одновременно, каждый по своей логике подавления.

*Отдельно от этого документа, но по той же теме A1/A6:* иерархия приоритетов между классами
событий (`тревоги > ошибки > информационные`) и правило гистерезиса — решение владельца
15.08.2026, зафиксировано в [`originals-master-plan.md`](originals-master-plan.md) §A6, не в
DarknessBot.

---

## 3. Информационные оповещения — семь типов

**Для A2** (развести аварии и информационные).

Источник: [`../../loeuc/darknessbot-analysis.md`](../../loeuc/darknessbot-analysis.md), §13.
Значения и порядковые номера — **прочитаны** из пула объектов (`InformationAlarmType`).

| ordinal | ключ |
|---|---|
| 0 | `speed` |
| 1 | `pwm` |
| 2 | `amperage` |
| 3 | `power` |
| 4 | `temperature` |
| 5 | `battery` |
| 6 | `singleMileage` |

Первые пять совпадают по ordinal с `AlarmType` (§2.1) — это те же величины, что и в авариях;
`battery` и `singleMileage` существуют **только** как информационные.

**Отличие от аварий по обработке:**

| | авария | информационное |
|---|---|---|
| откуда уровень | три порога на тип, уровень 0…3 | значение как есть, порога тяжести нет |
| звук | сирена с темпом по уровню | нет |
| вибрация | 1…3 импульса | нет |
| голос | `_processVoiceAlarm`, окно 5 с | `_processInformationAlarm` → `speak()`, окно 20 с |
| текст | `_voiceAlarmText` | `_informationAlarmText`, `_singleInformationAlarmText`, `_singleMileageText` |

То есть **информационные оповещения только голосовые**: `_processInformationAlarm` (`0x86d97c`)
собирает текст, сверяет время с прошлым разом по порогу **20 000 000 мкс = 20 с** и произносит.
`_singleMileageText` — отдельный шаблон под «проехал столько-то», не общий текст.

**Порядок и включение — пользовательские настройки, не жёсткий приоритет:**

- `isInformationAlarms` — общий выключатель;
- `enabledInformationAlarmTypes` — какие типы включены;
- `informationAlarmOrder` — порядок озвучивания, задаёт пользователь.

Жёстко зашитого приоритета между информационными типами в коде DarknessBot нет.

---

## 4. Опознание колеса по рекламному имени — 16 адаптеров

**Для A8**.

Источник: [`../../loeuc/darknessbot-analysis.md`](../../loeuc/darknessbot-analysis.md), §17
(«Опознание устройства по рекламному имени — полные правила»). Литералы — **прочитаны** из тела
`isCompatible` каждого адаптера в `DeviceSelector.getAdapter`.

**Способ сравнения подтверждён не для всех адаптеров.** У `veteran`, `kingsong`,
`inmotion_new` явно виден `startsWith` (6, 8 и 68 вызовов соответственно) — сравнение по
префиксу. У остальных вызовов `startsWith` в теле нет: имена сверяются через набор/карту, точный
предикат (регистр, префикс/точное равенство) **в источнике не установлен** — только предположение
по строчным литералам (`gotway`, `begode`, `master`, `lk`, `nf`, `sv134`), что имя приводится к
нижнему регистру.

| адаптер | префиксы/имена | службы GATT |
|---|---|---|
| **inmotion_new** (V11…V14, P6) | `V6-`, `V9-`, `V11-`, `V11Y-`, `V12-`, `V12Pro-`, `V12S-`, `V13-`, `V14-`, **`Adventure-`**, `P6-`, `E20-`, `Climber-`, `RS`, `RS-`, `C1`, `S1`, `L9` | Nordic UART `6e400001/2/3` |
| **inmotion** (старый) | `V8F-`, `V10-`, `V10F-` | `ffe0`+`ffe4`, `ffe5`+`ffe9` |
| **gotway** (Begode) | `gotway`, `begode`, `master` | `ffe0`, `ffe1` |
| **veteran** (LeaperKim/Nosfet) | `lk`, `nf` | `ffe0`, `ffe1` |
| **kingsong** | `ks`, `rock`, `rw`, `NO` | `ffe0`+`ffe1`, `fff0` |
| **ninebot** | `N1`, `N1C`, `N1E`, `N1P`, `N1R`, `N1T`, `NO`, `NOC`, `NOE`, `NOP` (плюс отдельные ветки с логом «Found S2» и «Found Z10») | `ffe0`+`ffe1`, Nordic UART |
| **ninebot_one** | `NOC`, `NOE`, `NOP` | `ffe0`, `ffe1` |
| **ninebot_encrypted** | `NAGJC2308C1258` (одно конкретное имя) | Nordic UART |
| **smirnov** | `sv134`, `sv151`, `sv168` | `ffe0`, `ffe1` |
| **ado** | `ado-ebike` | Nordic UART |
| **ant** (BMS) | `ANT-` | `ffe0`, `ffe1`, `ffe2` |
| **vesc** | `VESC`, `UNITY`, `FOCBOX`, `Focstrot`, `Little FOCer`, `BKB` | Nordic UART |
| **xiaomi_skate** | `AR1`, `AX1`, `BLINK`, `BQ4`, `BS2` | `ffe0`, `ffe1` |
| **xiaoxiang** (BMS) | имени нет — только службы | `ff00`, `ff01`, `ff02` |
| **yokamura** | имени нет — только службы | `fff0`, `fff6`, `fff7` |

**Ловушки, зафиксированные разбором:**

- **`Adventure-` — торговое имя V14**, подтверждено третьим независимым источником (LoEUC и
  DarknessBot сходятся). У LoEUC есть варианты с опечаткой (`Adventre`, `Adven`) — у DarknessBot
  их **нет**, ловится только точное написание;
- **завершающий дефис значим.** Почти все имена InMotion записаны с завершающим `-` (`V11-`,
  `V14-`, `P6-`), короткие — без (`RS`, `C1`, `S1`, `L9`). Дефис отсекает ложные срабатывания
  вроде `V1` внутри `V14`; там, где его нет, префикс и без того уникален;
- **`NO` — коллизия KingSong / Ninebot напрямую.** Разрешается порядком перебора адаптеров в
  `DeviceSelector` и, по-видимому, службами: у KingSong к `NO` прилагается `fff0`, которого у
  Ninebot нет. **Порядок перебора в источнике не выписан** — при переносе правила его нужно
  установить отдельно, иначе KingSong и Ninebot начнут отбирать друг у друга устройства;
- **имя — не единственное условие.** У каждого адаптера литералы имени лежат рядом с UUID служб,
  опознание двухступенчатое (дерево GATT + имя) везде, кроме Xiaoxiang и Yokamura, которые
  обходятся без имени вовсе.

---

## 5. Автофара по закату

**Для A4**.

Источник: [`../../loeuc/darknessbot-analysis.md`](../../loeuc/darknessbot-analysis.md), §15.
Цепочка: `AutoTorchHelper.processAutoTorch` → `SunsetHelper.isSunset()`.

**Условие включения** — четыре настройки приложения (ключи прочитаны из `model/settings.dart`):

| ключ | смысл |
|---|---|
| `isAutoTorch` | общий выключатель автофары |
| `isAutoTorchOnlySunset` | включать только в тёмное время |
| `autoTorchOnSpeed` | скорость, выше которой фара включается |
| `autoTorchOffSpeed` | скорость, ниже которой гаснет |

Итого — **два порога скорости с гистерезисом** плюс необязательное условие темноты.
`processAutoTorch` дополнительно требует покупки Premium (`InAppHelper.isPurchasedPremium`
вызывается первым). В коде есть константа **1 000 000 мкс = 1 с**, похожая на защиту от дребезга;
на что именно она наложена, источник не устанавливает.

**Как считают закат.** `SunsetHelper.isSunset()`:

1. `LocationService.checkLocationPermission()` → `LocationService.getLocation()`;
2. по координатам — сторонняя библиотека `solar_calculator`: `SolarCalculator.sunriseTime` и
   `SolarCalculator.sunsetTime`;
3. текущий момент сравнивается с ними через `Instant.isAfter`/`Instant.isBefore`.

Своей астрономической формулы у DarknessBot нет — берут готовую библиотеку.

**Запасное правило без координат** — `SunsetHelper.isSunsetSimple()` (`0x86c8f0`), предельно
грубое: «темно, если час местного времени < **4** или ≥ **21**» (прочитано по константам
`cmp x2, #4`, `cmp x2, #0x15`). Никакой сезонности, никакой широты.

---

## 6. Диагностика InMotion P6 — 45 битовых флагов

**Для этапа «аварии P6»** (пункт 1.2 части I, этап 7 очерёдности плана).

Источник: [`../../loeuc/loeuc-inmotion-p6-protocol.md`](../../loeuc/loeuc-inmotion-p6-protocol.md),
§5 («Диагностика, subcmd 3»). Разбор — LoEUC (сторонний клиент), таблица построена по
`kh0.f` (`kh0.java:16`), тип каждой записи — `v30(categoryEn, categoryRu, titleEn, titleRu,
severity)`.

**Разбор кадра.** Payload `m` (нужно ≥4 байта, иначе первые 4 байта считаются нулями):

- `errorCode` (общий код) = `uint32 LE` из `m[0..3]`;
- для индекса `i = 0..44`: `byteIndex = i/8`, `bitIndex = i%8`, `rawBit = (m[byteIndex] >>> bitIndex) & 1`.

**КАПКАН из источника.** Первые 4 байта payload несут двойную нагрузку: как единое `errorCode` и
одновременно как первые 32 бита из 45 флагов — один и тот же кусок памяти, прочитанный двумя
способами.

Флаги общие для **всей линейки InMotion** (не только P6, см. §7 источника), но у P6 — единственная
модель, где эта подкоманда фактически используется в нашем декодере не разобрана вовсе (у нас в
`InMotionP6RealTime.cs` — ноль вызовов установки тревоги).

severity `d` → Error, `n` → Warning (`iy1.java:13,15`).

| # | байт.бит | Категория | Title (EN) | Severity |
|---|---|---|---|---|
| 0 | 0.0 | Driver board | Phase current sensor fault | Error |
| 1 | 0.1 | Driver board | Bus current sensor fault | Error |
| 2 | 0.2 | Motor | Left Hall sensor fault | Error |
| 3 | 0.3 | Motor | Right Hall sensor fault | Error |
| 4 | 0.4 | Battery | Battery fault | Error |
| 5 | 0.5 | Driver board | IMU sensor fault | Error |
| 6 | 0.6 | Communication | Driver board communication fault 1 | Error |
| 7 | 0.7 | Communication | Driver board communication fault 2 | Error |
| 8 | 1.0 | Communication | HMIC communication fault 1 | Error |
| 9 | 1.1 | Communication | HMIC communication fault 2 | Error |
| 10 | 1.2 | Driver board | MOS temperature sensor fault | Error |
| 11 | 1.3 | Motor | Motor temperature sensor fault | Error |
| 12 | 1.4 | Driver board | Board hot-area sensor fault | Error |
| 13 | 1.5 | Cooling | Fan fault | Error |
| 14 | 1.6 | HMIC | HMIC RTC fault | Error |
| 15 | 1.7 | HMIC | HMIC flash fault | Error |
| 16 | 2.0 | Driver board | Bus voltage sensor fault | Error |
| 17 | 2.1 | Battery | Battery voltage sensor fault | Error |
| 18 | 2.2 | Battery | Battery cannot power off | Error |
| 19 | 2.3 | Battery | Battery cannot charge | Error |
| 20 | 2.4 | Battery | Critically low battery | Warning |
| 21 | 2.5 | Battery | Battery overvoltage | Warning |
| 22 | 2.6 | Driver board | Overcurrent | Warning |
| 23 | 2.7 | Battery | Low battery | Warning |
| 24 | 3.0 | Battery | Additional battery fault | Error |
| 25 | 3.1 | Motor | Motor overtemperature | Warning |
| 26 | 3.2 | Temperature | Vehicle overtemperature | Warning |
| 27 | 3.3 | Driver board | CPU overtemperature | Warning |
| 28 | 3.4 | Driver board | IMU overtemperature | Warning |
| 29 | 3.5 | Safety | Locked because of a safety issue | Warning |
| 30 | 3.6 | Safety | Overspeed | Warning |
| 31 | 3.7 | Motor | Unexpected motor spin | Warning |
| 32 | 4.0 | Motor | Motor blocked | Warning |
| 33 | 4.1 | Safety | Fall detected | Warning |
| 34 | 4.2 | Safety | Risky riding behavior | Warning |
| 35 | 4.3 | Motor | Motor no-load protection | Warning |
| 36 | 4.4 | Safety | Required self-check not passed | Warning |
| 37 | 4.5 | Controls | Power key held too long | Warning |
| 38 | 4.6 | Battery | Some batteries are not enabled | Warning |
| 39 | 4.7 | Battery | Battery calibration required | Warning |
| 40 | 5.0 | Compatibility | Software incompatible | Warning |
| 41 | 5.1 | Firmware | Functions limited by incomplete firmware update | Warning |
| 42 | 5.2 | Safety | Remote lock active | Warning |
| 43 | 5.3 | Compatibility | Hardware incompatible | Warning |
| 44 | 5.4 | Cooling | Fan speed too low | Warning |

**Не использовать как источник:** параллельный список `og0.a` (`og0.java:18`) дублирует те же 45
записей под другими именами классов, но ни разу не читается за пределами своего файла — мёртвый
код или след рефакторинга, источник это явно не домысливает.

Итог оборачивается в `pg0`/`ky1` (`InmotionDiagnosticsSnapshot`/`WheelDiagnosticsSnapshot`) —
поля `errorCode, payload, decodedItems, batteries, ...`, устройство снимка — в самом источнике,
§5 и §5.1 (сюда не тянуто — не нужно для таблицы флагов).

---

## 7. Команды LeaperKim — сводная таблица

**Для импорта команд** (пункт 11 части II, план [`veteran-commands-import-plan.md`](veteran-commands-import-plan.md)).

Источник: [`../../loeuc/leaperkim-official-app.md`](../../loeuc/leaperkim-official-app.md), §4
(«Полный каталог команд»). Это **родное приложение производителя** (не LoEUC) — вес источника
выше стороннего клиента. Все опкоды и диапазоны — **прочитаны** из литералов кадра и
`getProgressMax()`/`progressToCmdValue()` соответствующих `*SettingActivity.java`, сверены
автоматически: инвариант «длина кадра (тело+CRC32) = значение байта-опкода» проверен
программным разбором всех 45 литералов кадров, без единого исключения.

**Общий формат.** Заголовок `4C <6B|64> 41 70` (`L`/`k` или `L`/`d`), байт 4 — опкод, CRC32
big-endian в конце. Обычная настройка — одиночный `Ld`-кадр:
`{76, 100, 65, 112, <опкод>, 1, <b6>, 0x80×N, <значение>}` + CRC32.

### 7.1 Обычные настройки (одиночный `Ld`-кадр)

| Ключ | Опкод (dec/hex) | b6 | Диапазон | Файл |
|---|---|---|---|---|
| `pedalHardness` (плавный, новые колёса) | 15 / `0x0F` | 2 | 0..100 | `PedalSoftnessSettingActivity.java:37` |
| `stopSpeed` (tiltback) | 17 / `0x11` | **2** | 10..120 | `StopSpeedSettingActivity.java:42` |
| `stopPowerRate` (порог PWM) | 18 / `0x12` | 2 | 30..100 | `StopPowerSettingActivity.java:30` |
| `screenBacklightRate` | 20 / `0x14` | 2 | 0..100 | `ScreenBacklightSettingActivity.java:30` |
| `gyro` (калибровка, см. §7.4) | 21 / `0x15` | 2 | фикс. `1` | `GyroscopeSettingActivity.java:122` |
| `transportMode` (toggle) | 22 / `0x16` | **2** | 0/1 | `ControlActivity.java:439` |
| `unit` (toggle km/mi) | 23 / `0x17` | 2 | 0/1 | `ControlActivity.java:443`, `UnitSwitchActivity.java:76` |
| `vol` (voltage_correction) | 24 / `0x18` | 2 | −15..15 (÷10 → %) | `VolLightSettingActivity.java:31` |
| `lowVolMode` (toggle) | 25 / `0x19` | **2** | 0/1 | `ControlActivity.java:447` |
| `highSpeedMode` (toggle) | 26 / `0x1A` | 2 | 0/1 | `ControlActivity.java:451` |
| `keyTone` | 28 / `0x1C` | 2 | 0..100 | `KeyToneSettingActivity.java:30` |
| `maxChargeVol` | 29 / `0x1D` | 2 | 0..120 | `MaxChargePowerSettingActivity.java:31` |
| `upOrDownSpeedHelper` (acc/dec helper) | 31 / `0x1F` | 2 | 0..100 | `SetUpDownSpwwdHelpActivity.java:30` |
| `upSpeedCul` (accelerometer reduction) | 33 / `0x21` | 2 | 0..100 | `SetUpSpeedCulActivity.java:30` |
| `brakePressureAlarm` | 34 / `0x22` | 2 | 80..125 | `BrakeSettingActivity.java:30` |

Жирным в столбце `b6` — опкоды с коллизией (см. §7.2).

### 7.2 Особые случаи — парные кадры (`Lk`+`Ld`) и текстовые команды

| Команда | Опкод | Формат | Диапазон/значение | Ссылка |
|---|---|---|---|---|
| `angle_trim` (наклон педалей) | 16 / `0x10` | пара `Lk`+`Ld` | −80..80 (÷10 → °) | `SetAngelActivity.java:69` |
| Свет вкл/выкл | 13 / `0x0D` | пара, `sendData("SetLightON"/"SetLightOFF")` | on/off | `BtManager.java:74-75,83-84` |
| Гудок/сигнал («Alarm») | 14 / `0x0E` | пара, `sendData("OLDCMDb")` | без значения | `BtManager.java:73,82` |
| Сброс поездки (`CLEARMETER`) | **11** (старый) / **13** (новый!) | пара, `sendData("CLEARMETER")` | без значения | `BtManager.java:76,85` |
| Ride mode, 3 уровня (`SETs`/`SETm`/`SETh`, старые Sherman) | 12 / `0x0C` | пара, `sendData` | 1/2/3 | `BtManager.java:77-79,86-88` |
| `speed_alarm` | 17 / `0x11` | пара (см. §7.3) | 10..120 | `SetAlarmSpeedActivity.java:67` |
| `fallProtectionAngle` | 22 / `0x16` | пара | 35..75° | `SetFallProtectionAngleActivity.java:64` |
| Питание / удержание (10 с до выключения) | 22 / `0x16` | пара, без значения | — | `BtManager.java:81,90` |

**Сброс поездки меняет опкод между поколениями прошивки**, а не только заголовок:
`CMD_CLEAR_METER` (старый, `Lk`) = опкод `11`, `CMD_CLEAR_METER_NEW` (новый, `Ld`) = опкод `13` —
тот же опкод, что у света.

### 7.3 Коллизии опкодов — полная карта

Один опкод-байт (4) обслуживает по 2–3 разных команды; диспетчеризация у колеса идёт по
нескольким байтам сразу, не по одному опкоду.

| Опкод | Команда A | Команда B | Команда C | Чем отличаются |
|---|---|---|---|---|
| 12 | Ride mode (3 уровня, старые Sherman) | — | — | одна команда, значение 1/2/3 — не коллизия |
| 13 | Свет вкл/выкл | `CLEARMETER` (новая прошивка!) | — | b5: свет=1, clear-meter=**0** |
| 17 | `stopSpeed` (tiltback) | `speed_alarm` | — | b6: stopSpeed=**2**, alarm=**0**; alarm шлётся парой `Lk+Ld`, stopSpeed — одиночным `Ld` |
| 18 | `stopPowerRate` | Синхронизация времени | — | b5/b6: stopPower=1/2, sync-time=**0/5**; тело тоже разное (дата вместо значения) |
| 20 | `screenBacklightRate` | `CMD_READ_LOG` (чтение журнала, §7.5) | — | b6: backlight=**2**, read-log=**0** (Ld) / хвост фикс. `MIN,1` (Lk) |
| **22** | **`transportMode` (toggle)** | **Питание/удержание** | **`fallProtectionAngle`** | b6: transportMode=**2**, оба остальных=**0**; питание vs fallProtection различаются только формой хвоста: у питания последние два байта тела всегда `01 80`, у fallProtection последний байт — переменное значение угла (35..75), предпоследний — заполнитель `0x80` |
| 25 | `lowVolMode` (toggle) | Пароль/блокировка | — | b5/b6: lowVolMode=1/2, пароль=**0/5** |

**ТРОЙНАЯ КОЛЛИЗИЯ ОПКОДА 22 — самая опасная пара в протоколе.** Питание/удержание и запись
`fallProtectionAngle` совпадают по байтам 0–6 полностью (заголовок, опкод 22, `b5=1`, `b6=0`) и по
общей длине (18 байт). Отличает их **только форма последних двух байт тела**: у команды питания
это жёстко зашитая пара `01 80`, у записи угла — заполнитель `0x80` и переменное значение
(практически не совпадёт с `0x80`=−128, т.к. вне диапазона 35..75, но конструктивно кадры
«выключить колесо» и «записать угол защиты от падения» неотличимы по первым семи байтам). Третий
смысл того же опкода — `transportMode`, различается уже на шестом байте (`b6=2`).

### 7.4 Калибровка гироскопа (опкод 21)

```
{76, 100, 65, 112, 21, 1, 2, 0x80×9, 1}   — GyroscopeSettingActivity.java:121-123
```

Одиночный `Ld`-кадр, значение всегда фиксированный байт `1`. **Одна и та же команда** запускает и
останавливает калибровку — разница не в кадре, а в состоянии, которое приложение читает обратно
из телеметрии (`ControlSettingData.getGyro()`: 0=не калибруется, 1=калибруется/ждём, 2=готово).
Предохранитель: команда не уходит, если `getSpeed() > 0` — калибровка разрешена только на стоящем
колесе.

### 7.5 Служебные и опасные команды

| Команда | Опкод | Формат | Ссылка |
|---|---|---|---|
| Чтение журнала (`CMD_READ_LOG`/`CMD_READ_LOG_NEW`) | 20 (коллизия с `screenBacklightRate`) | пара | `BtManager.java:80,89`, `LogUploadActivity.java:129,149` |
| Пароль/блокировка (`genPwdCmd`) | 25 (коллизия с `lowVolMode`) | переиспользует построитель синхронизации времени, опкод+7 (18→25) | `Util.java:257-273` |
| Вход в режим прошивки | текстовая AT-команда `"AT+RINTOPRO"`, не кадр | уходит сырым текстом всегда, минуя `Lk`/`Ld` | `BtManager.java:39`, `UpgradeActivity.java:245-249` |
| Передача образа прошивки | `sendBinData`, без CRC, без нумерации блоков | поток 20-байтных порций | §7 источника целиком |

**Опасность (не пробовать, только знать):** прошивка идёт по открытому HTTP без хэша и подписи —
зафиксировано, решение по этому риску за владельцем ([`originals-master-plan.md`](originals-master-plan.md),
«Что нужно решить владельцу», п. 6). Пароль без сохранённого способа восстановления может
заблокировать владельца от собственного колеса. Ни одна из этих команд не отправлялась на живое
колесо при разборе.

### 7.6 Что LoEUC переврал в этих же командах

Для соотнесения с уже перенесённым словарём LoEUC (`loeuc-leaperkim-commands.md`, если понадобится
сверка при импорте):

- **`speed_alarm`**: LoEUC приписал опкод `19` — такого опкода в официальном приложении **нет
  вовсе**. Настоящий — `17`, тот же, что у `stop_speed` (см. §7.3);
- байт 5 в команде питания/удержания у LoEUC — `0x80`, у производителя — `1` (не критично, но не
  идентично оригиналу);
- калибровка гироскопа, `fallProtectionAngle`, чтение журнала, пароль/блокировка, смена опкода
  сброса поездки между поколениями, отсутствие keep-alive у производителя — этого всего в разборе
  LoEUC нет вовсе (подробности — §9 источника).

---

## 8. BMS InMotion P6

**Для этапа «доработки протокола: BMS»** (пункт 9 части II, строка «BMS» — «распознаётся, не
разбирается»).

Источники: [`../../loeuc/loeuc-inmotion-p6-protocol.md`](../../loeuc/loeuc-inmotion-p6-protocol.md)
§4 (LoEUC, сторонний клиент — числа и формулы) и собственный
[`inmotion-p6-protocol.md`](inmotion-p6-protocol.md) §1 (наши записи — подтверждение числа ячеек и
поведения двух паков на живых дампах). Всё из LoEUC — **прочитано** из кода, ссылки на файл:строку
даны в исходнике.

### 8.1 Периодическая сводка — subcmd 5 (только carType==131)

`wc.e(byte[] bArr, boolean z)` (`wc.java:80-111`), вызывается как `wc.e(m, true)` **только когда
`carType==131`**; для прочих моделей InMotion суб-сообщение 5 декодер получает, но результат
никуда не сохраняется.

Формат — ровно **2 слота батарей** («паки»), по 8 байт каждый, начиная с `m[0]` и `m[8]`:

| Смещение (в слоте) | Тип | Поле | Формула |
|---|---|---|---|
| +0-1 | u16 LE | voltage | `/100.0` → В |
| +2-3 | i16 LE | chargeCurrent | `/100.0` → А |
| +4-5 | i16 LE | dischargeCurrent | `/100.0` → А |
| +6-7 | u16 LE | flags | см. ниже |

Биты `flags` (слово, младший байт = +6, старший = +7):

| Бит | Значение |
|---|---|
| 0 | detected (пак обнаружен) |
| 1 | enabled |
| 2 | charging |
| 6 | errorLogic |
| 7 | errorSignal |
| 8 (бит0 старшего байта) | errorVoltageSensor |
| 9 | errorCurrentSensor |
| 10 | errorCannotCharge |
| 11 | warning |
| 12 | protectionActive |
| 13 | error |

`hasFault` = «хоть один из битов 6-13 установлен».

Результат оборачивается в `mg0` (`InmotionBmsSnapshot`) — список `lg0` (`InmotionBmsBattery`,
поля `index(1|2), detected, enabled, charging, voltage, chargeCurrent, dischargeCurrent, hasFault,
rawData[8]`).

### 8.2 Адресные BMS-запросы — путь к «до 56 ячеек»

Отдельный, более точный источник данных о BMS — «прямые» запросы-ответы через внешний конверт
`cmd=0x16` (**не** `0x14`, которым идёт вся остальная телеметрия/настройки). Разбирает
`kg0.y(ly1)` (`kg0.java:508-521`):

```
payload[4]        = sourceAddress   ∈ {36, 37, 38, 39, 50, 52}   — шесть плат/адресов
payload[5]        = targetAddress   должен быть == 2
payload[6] & 0x1F = selector        ∈ {1, 2, 4}                  — три вида ответа
данные            = payload[7 .. min(payload[3]+4, len-1))
```

Запросы на чтение приложение шлёт как `kg0.b(source, selector) = kg0.e([source,selector], 22, 2)`
— то есть **как суб-сообщение с subcmd=2** (тот же код, что несёт анонс `carType`!), но по
конверту `cmd=0x16` и с двумя байтами payload. При подключении приложение перебирает **все 6
адресов × 3 селектора = 18 запросов**.

`selector` определяет формат ответа:

- **`selector=1`** → `kg0.w(byte[])` → `qg0` (`InmotionDirectBmsRealtime`): требует ≥28 байт,
  проверка правдоподобности «напряжение пака в [50,300] В» на смещении 6-7 (`/100.0`); поля:
  `chargeCurrentAmps`/`dischargeCurrentAmps` (смещения 8-9 и 10-11, i16 LE `/100`, `0`→`null`),
  `temperatures` (список, смещение 28+, формула `байт+80`, валидны `[-40,120]`),
  `maxCellVoltage`/`minCellVoltage` (смещения 18-19, 20-21, `/1000.0`),
  `fullCapacityAh`/`remainCapacityAh` (смещения 12-13, 14-15, `/1000.0`),
  `fullChargeCycles` (смещение 16-17, `0`→`null`);
- **`selector=2`** → `kg0.v(byte[])` → **список напряжений ячеек**: весь payload читается парами
  байт (uint16 LE, `/1000.0` → В), **всё-или-ничего**: если хоть одно значение выходит за диапазон
  `[2.0, 5.0]` В — отбрасывается целиком весь список. Порядок в списке = порядок в payload,
  никакой явной индексации ячеек нет. **Число ячеек = `payload.length/2`, ничем в протоколе не
  ограничено** — отсюда и «до 56 ячеек» в черновом плане;
- **`selector=4`** — присутствует в списке допустимых значений, но **нигде в проверенном коде не
  разбирается**. Источник прямо пишет: «не домысливаю назначение» — здесь то же самое.

### 8.3 Сборка снимка BMS для экрана

`jh0.L(List)` **жёстко** возвращает `profileId="inmotion_p6"`, `modelName="Inmotion P6"`
**независимо от реального carType источника**. Строит непустой результат только если сводка из
§8.1 (subcmd 5) не `null` — а она заполняется только при `carType==131`. **Следствие: экран
BMS/ячеек в LoEUC работоспособен только для P6.**

Сопоставление адресных ответов (§8.2) с паками из §8.1 — **позиционное по порядку возрастания
`sourceAddress`** (через `TreeMap`), не по явному номеру пака. Если адресных ответов пришло
меньше или больше, чем физических паков, сопоставление съедет. Для не сматченных индексов
`detected`/`enabled` жёстко проставляются `true`, `charging` — `false` — **это угадано кодом
LoEUC, не декодировано**.

### 8.4 Подтверждено нашими записями

Из [`inmotion-p6-protocol.md`](inmotion-p6-protocol.md) §1 (два дампа с разных живых колёс P6,
02.08.2026 и 09.08.2026):

- **56 ячеек** в паке — выведено по трём точкам (напряжение / заряд из телеметрии), не при 30
  (7,2 В/ячейка) и не при 20 (10,9 В/ячейка) сходится, только при 56 (3,78…4,11 В/ячейка —
  правдоподобный диапазон LiFePO4/Li-ion);
- **паки два, и они расходятся** — на одном из двух колёс смещения телеметрии `[34]` и `[36]`
  (заряд пака 1 и пака 2, 0,01 %) весь дамп держат разницу около полутора процентов, постоянно, не
  шумом. В состояние сейчас идёт среднее по обоим — так же, как у V13;
- эти два поля (`[34]`, `[36]`) идут из **основного кадра телеметрии** (subcmd 4, полная раскладка
  — [`inmotion-p6-protocol.md`](inmotion-p6-protocol.md) §3), а не из подкоманды 5 — то есть грубая
  оценка заряда по пакам доступна уже сейчас, без разбора BMS. Подробная картина (напряжения по
  ячейкам, температуры, циклы) требует подкоманды 5 и адресных запросов §8.1–8.2, которых наш
  декодер сегодня не шлёт и не разбирает.

---

## 9. Настройки P6 — подкоманда чтения `0x20`, запись `0x60`

**Для этапа «доработки протокола: настройки»** (пункт 9 части II, строка «настройки» — «около 50
полей, богатейший каталог во всей линейки InMotion») и для экрана настроек колеса
([`wheel-settings-architecture.md`](wheel-settings-architecture.md), [план 34](android-plan-34-wheel-settings.md)).

Источник: [`../../loeuc/loeuc-inmotion-p6-protocol.md`](../../loeuc/loeuc-inmotion-p6-protocol.md)
§6. Все смещения, опкоды и формулы чтения — **прочитаны** из `kg0.x` (`kg0.java(scratch):753-806`)
и таблицы `jh0.D()`/`jh0.C()`. Столбец «диапазон» там, где он есть, — из виджета настройки
(`android:max` разметки и т.п.), это тоже прочитанное, не наше предположение.

### 9.1 Условие входа и место P6 среди моделей InMotion

Вход в разбор: `carType==131` **и** `payload.length ≥ 49` (иначе список настроек пуст). Единственный
источник данных для конструирования каталога — суб-сообщение subcmd `0x20`.

**Сравнение с другими моделями InMotion (важно, чтобы не спутать, что переносим):**

| Модель | Что даёт subcmd 0x20 | Что даёт subcmd 8 |
|---|---|---|
| V8 (carType 61) | другой, 23-элементный каталог (`kh0.j`) | — |
| V11/V12/V13/V14 (кроме P6) | **пусто** | узкий блок «подсветка»: 4 поля (`carType∈{91,92}`, т.е. V14) или 5 полей (V12-семейство `{71,72,73,111}`); для V13 — ничего |
| **P6 (131)** | **~50 полей, таблица ниже** | 4 поля (P6 входит в тот же набор `{91,92}∪{131}`, но не отправляется повторно при хендшейке — см. §8.3 источника) |

Когда оба источника (8 и 0x20) пусты, `jh0.D()` откатывается на список сырых байт настроечного
блока без имён (`inmotion_setting_byte_N`, только для чтения) — для V13 и любой немаркированной
модели. **Итог источника: P6 — модель с самым богатым читаемым каталогом настроек среди всей
линейки InMotion**, богаче даже V13/V14.

### 9.2 Служебные слова payload

- Флаги 32-битного слова на смещении 45: `i33 = m[45] | m[46]<<8 | m[47]<<16 | m[48]<<24` (LE);
- Байт 24 (`b4 = m[24]`): старший нибл (`>>4 & 0xF`) = `inmotion_ride_mode` (режим педалей, **на
  запись**, опкод 36), младший нибл (`&0xF`) = `inmotion_driver_mode` (только чтение).

### 9.3 Полная таблица — ключ, смещение, опкод записи, диапазон, тип, формула

Пустой опкод = поле нередактируемо **в этом каталоге** (см. асимметрию §9.4 — часть таких полей
всё же имеет код записи в другом месте).

| Ключ | Offset | Opcode | Диапазон | Тип | Формула чтения |
|---|---|---|---|---|---|
| `inmotion_ride_mode` | 24 (бит 4-7) | 36 | 0-2 | slider | `(m[24]>>4)&0xF` |
| `inmotion_driver_mode` | 24 (бит 0-3) | — | — | readonly | `m[24]&0xF` |
| `inmotion_pedal_sensitivity_1` | 25 | 37 (общий с sens.2) | 0-100 | slider | `m[25]&0xFF` |
| `inmotion_pedal_sensitivity_2` | 26 | 37 | 0-100 | slider | `m[26]&0xFF` |
| `inmotion_voice_volume` | 27 | 38 | 0-100 | slider | `m[27]&0xFF` |
| `inmotion_light_effect_mode` | 28 | 51 | 0-255 | slider | `m[28]&0xFF` |
| `inmotion_auto_light_low_thr` | 29 | 42 (общий с high_thr) | 0-255 | slider | `m[29]&0xFF` |
| `inmotion_auto_light_high_thr` | 30 | 42 | 0-255 | slider | `m[30]&0xFF` |
| `inmotion_low_beam_brightness` | 31 | 43 (общий с high_beam) | 0-100 | slider | `m[31]&0xFF` |
| `inmotion_high_beam_brightness` | 32 | 43 | 0-100 | slider | `m[32]&0xFF` |
| `inmotion_auto_light_state` | 45, бит3 | 47 | 0-1 | toggle | `(i33>>3)&1` |
| `inmotion_drl_state` | 45, бит21 | 78 | 0-1 | toggle | `(i33>>21)&1` |
| `inmotion_berm_angle_mode` | 45, бит17 | 67 | 0-1 | toggle | `(i33>>17)&1` |
| `inmotion_speed_limit` | 8-9 | 33 | 0-150 | slider, км/ч | `u16LE(m,8)/100` |
| `inmotion_speed_warning_level_1` | 10-11 | 62 (общий с level_2) | 0-150 | slider, км/ч | `u16LE(m,10)/100` |
| `inmotion_speed_warning_level_2` | 12-13 | 62 | 0-150 | slider, км/ч | `u16LE(m,12)/100` |
| `inmotion_pitch_zero` | 20-21 | 34 | −80…80 | slider, 0.1° | `i16LE(m,20)/10` |
| `inmotion_output_tiltback_threshold` | 14-15 | — | — | readonly, % | `i16LE(m,14)/100` |
| `inmotion_output_warning_threshold_1` | 16-17 | — | — | readonly, % | `i16LE(m,16)/100` |
| `inmotion_output_warning_threshold_2` | 18-19 | — | — | readonly, % | `i16LE(m,18)/100` |
| `inmotion_standby_time` | 22-23 | — (запись есть, см. §9.4) | — | readonly, мин | `u16LE(m,22)/60` |
| `inmotion_active_sound_sensitivity` | 33 | — | — | readonly | `m[33]&0xFF` |
| `inmotion_high_beam_auto_switch_speed` | 34-35 | — | — | readonly, км/ч | `u16LE(m,34)/100` |
| `inmotion_speeding_feedback` | 36 | — (запись есть, см. §9.4) | — | readonly, %, знаковый | `(sbyte)m[36]` |
| `inmotion_braking_feedback` | 37 | — (запись есть, см. §9.4) | — | readonly, %, знаковый | `(sbyte)m[37]` |
| `inmotion_tpms_low_alarm_threshold` | 38-39 | 77 | 0-**в источнике нет** | slider, мбар | `u16LE(m,38)/10` |
| `inmotion_logo_light_brightness` | 40 | 68 | 0-100 | slider, % | `m[40]&0xFF` |
| `inmotion_berm_angle` | 41 | 58 | 0-90 | slider, ° | `m[41]&0xFF` |
| `inmotion_charge_cut_off_percent` | 42 | — | — | readonly, % | `m[42]&0xFF` |
| `inmotion_max_charge_current_ac220` | 43 | — | — | readonly, 0.1А | `m[43]&0xFF` |
| `inmotion_max_charge_current_ac110` | 44 | — | — | readonly, 0.1А | `m[44]&0xFF` |
| `inmotion_audio_switch` | 45, бит0 | 44 | 0-1 | toggle | `i33&1` |
| `inmotion_turn_signal_light` | 45, бит1 | — | — | readonly | `(i33>>1)&1` |
| `inmotion_lift_up_detection` | 45, бит2 | — (запись есть, см. §9.4) | — | readonly | `(i33>>2)&1` |
| `inmotion_auto_brightness` | 45, бит4 | — | — | readonly | `(i33>>4)&1` |
| `inmotion_lock_mode` | 45, бит5 | — (запись есть, см. §9.4) | — | readonly | `(i33>>5)&1` |
| `inmotion_transport_mode` | 45, бит6 | — (запись есть, см. §9.4) | — | readonly | `(i33>>6)&1` |
| `inmotion_load_detect` | 45, бит7 | — | — | readonly | `(i33>>7)&1` |
| `inmotion_no_load_detect` | 45, бит8 | — (запись есть, см. §9.4) | — | readonly | `(i33>>8)&1` |
| `inmotion_low_battery_ride` | 45, бит9 | — (запись есть, см. §9.4) | — | readonly | `(i33>>9)&1` |
| `inmotion_active_sound` | 45, бит10 | — | — | readonly | `(i33>>10)&1` |
| `inmotion_touch_key` | 45, бит11 | — | — | readonly | `(i33>>11)&1` |
| `inmotion_usb_power_switch` | 45, бит12 | — | — | readonly | `(i33>>12)&1` |
| `inmotion_auto_close_screen` | 45, бит13 | 61 | 0-1 | toggle | `(i33>>13)&1` |
| `inmotion_range_estimate` | 45, бит14 | — | — | readonly | `(i33>>14)&1` |
| `inmotion_assist_balance` | 45, бит15 | — | — | readonly | `(i33>>15)&1` |
| `inmotion_acce_feedback` | 45, бит16 | — | — | readonly | `(i33>>16)&1` |
| `inmotion_logo_light_status` | 45, бит18 | — | — | readonly | `(i33>>18)&1` |
| `inmotion_tbox_low_battery_wakeup` | 45, бит19 | — | — | readonly | `(i33>>19)&1` |
| `inmotion_show_tbox_info` | 45, бит20 | — | — | readonly | `(i33>>20)&1` |
| `inmotion_shield_tps_error` | 45, бит22 | — | — | readonly | `(i33>>22)&1` |
| `inmotion_turn_light_mode` | 45, бит23-25 | 48 | 0-4 | slider | `(i33>>23)&7` |
| `inmotion_tail_light_mode` | 45, бит26 | 59 | 0-1 | toggle | `(i33>>26)&1` |
| `inmotion_auto_lock` | 45, бит27 | — | — | readonly | `(i33>>27)&1` |

**КАПКАН из источника про `inmotion_tpms_low_alarm_threshold`.** Верхняя граница диапазона в
исходнике распознана jadx как `AutofillUtils_androidKt.MAX_AUTOFILL_TEXT_LENGTH` — это артефакт
декомпиляции (R8 схлопнул пул целочисленных констант, jadx подставил первое попавшееся публичное
имя с тем же числовым значением), **не настоящая константа протокола**. Реальный максимум **из
этого кода не восстановить достоверно** — здесь оставлено как «в источнике нет», брать эмпирически
или другим способом, не переносить имя константы как есть.

**Ещё 4 поля добавляются к каталогу безусловно, вне зависимости от модели** (в таблицу выше не
входят, т.к. не относятся к смещениям блока настроек):

- `inmotion_settings_payload_length` — информационное, длина блока настроек, всегда;
- для V12-семейства (`carType∈{71,72,73,111}`) дополнительно `inmotion_turn_light_state` (offset
  32, бит1) и `inmotion_auto_low_high_beam_switch_speed_thr` (offset 28-29, масштаб **не
  подтверждён** — источник в исходнике буквально помечен строкой `"raw, scale unconfirmed"`) — **эти
  два поля для P6 не добавляются** (P6 не входит в множество V12-семейства).

### 9.4 Асимметрия чтение/запись — важно для реализации отказа с причиной (A7)

Ряд полей помечен в каталоге P6 как `readonly` (нет кода в `jh0.D()`), но у построителя команды
записи `jh0.C()` (`jh0.java:54-326`) для **того же** ключа команда есть и формально
собирается: `inmotion_standby_time`(опкод 40), `inmotion_speeding_feedback`(63, в паре с
braking), `inmotion_braking_feedback`(63), `inmotion_lift_up_detection`(46),
`inmotion_lock_mode`(49), `inmotion_transport_mode`(50), `inmotion_no_load_detect`(54),
`inmotion_low_battery_ride`(55).

Источник уточняет: на практике это **не дыра в рантайме** — `C()` сверяется со списком
возможностей, собранным из `D()`, и там у этих ключей код записи (`sz1`) равен `null`, так что
прямой вызов не пройдёт. Асимметрия видна только при чтении исходников. **Для порта это всё же
ловушка**: правка одного места (например, «разрешить чтение как редактируемое») без синхронизации
со вторым — типичный способ провести правку мимо ограничения, которое существует в оригинале не
просто так.

### 9.5 Запись — единый формат subcmd `0x60` (не специфика P6)

Общий для всей линейки InMotion. Конверт: `cmd=0x14`, суб-заголовок фиксирован `[0x60]`
(subcmd 96), суб-payload = `[opcode] + data`, где `opcode` — однобайтный код из таблицы §9.3 (или
общий код для сдвоенных настроек), `data` — 1-2 байта нового значения (для парных настроек вроде
`pedal_sensitivity_1/2` — оба байта пары уходят вместе, второе значение подтягивается из текущих
возможностей).

**Write-only опкоды вне таблицы §9.3** (не соответствуют ни одному читаемому полю каталога):

| Опкод | Назначение |
|---|---|
| 80 | переключатель ближнего света (отдельный метод `cz1.o/z`) |
| 130 | пустой payload — назначение **в источнике не подтверждено** |
| 22 / 2 | адресные BMS-запросы (см. §8.2) — не запись, чтение |
| 3 / 5 / 8 / 32 | запросы блоков данных (диагностика/BMS/подсветка/настройки) — не запись, чтение |

---

## 10. Begode: канал ошибок BMS — восемь состояний

Кадр `type=1`, 16-битное слово на смещении `[16:17]`. **Мы этот канал не разбираем вовсе.**

Полная таблица с подписями на двух языках, значениями и тяжестью — в
[`begode-comparison.md`](begode-comparison.md), раздел 6. Здесь — то, что нужно при реализации.

| Биты | Что означает | К чему относится | Тяжесть |
|---|---|---|---|
| 0 | записывается, но **не читается ни одним экраном** — мёртвое поле | ? | — |
| 1–2 | не используются вовсе | — | — |
| 3 | балансировка между пакетами | пакет | низкая, диагностика |
| **4–6** | **код неисправности**: `1` связь потеряна, `2` модуль физически отключён, `4` аномалия одной ячейки, `5` аномалия при зарядке, `0` норма | **конкретный модуль** | `2` и `4` — **высокая**; `1` и `5` — средняя |
| 7 | балансировка ячеек внутри модуля | модуль | низкая, диагностика |
| **8–9** | состояние силового МОП-ключа | модуль | **высокая, если разомкнут на ходу** |
| **10–11** | напряжение модуля вне диапазона: `00` перенапряжение, `01` пониженное | модуль | **высокая в обе стороны** |
| **12–13** | температура вне диапазона: `00` перегрев, `01` переохлаждение | модуль | перегрев **высокая**, переохлаждение средняя |
| 14–15 | идёт зарядка | модуль | статус, не авария |

**Тексты — в ресурсах самого приложения** (`res/values/strings.xml` и `values-en`), не на сервере.
Забирать неоткуда — можно переводить прямо оттуда.

**Что не доходит до райдера сейчас:** из восьми состояний пять не видны **никак** — потеря связи с
модулем, физическое отключение модуля, аномалия ячейки, аномалия заряда, разомкнутый силовой ключ.
Три остальных, возможно, просачиваются через общую тревогу, но **без указания, какой именно модуль**.

**⚠ Развилка, не разрешённая источником.** Два класса одного и того же приложения разбирают это
слово **в разном порядке бит** — обычном и перевёрнутом. Для двухбитного кода температуры это даёт
разное сопоставление биту смысла: перегрев против переохлаждения. Ни одна из четырёх наших записей
MTen3 не содержит кадра с настоящим отклонением температуры, чтобы рассудить. **Перед реализацией
проверить на живом колесе либо на записи с реальным событием.**

*Ограничение достоверности:* кадр `type=1` **на MTen3 не встречается ни разу** за четыре записи —
вся таблица прочитана по коду приложения, данными не подтверждена.

---

## 11. InMotion V1: коды ошибок — расшифровки не существует

**Отрицательный результат, доказанный, а не предположенный.** Подробности —
[`inmotion-v1-comparison.md`](inmotion-v1-comparison.md), разделы 6.1–6.7.

**Что известно точно:**

- диапазон кодов — `0x11`–`0x27`, всего 23 значения;
- из них **19 реальных**, четыре слота пустуют: `0x14`, `0x18`, `0x20`, `0x21`;
- **у нас разбирается 7 кодов**, пять численно совпадают с диапазоном производителя, **а два стоят
  на пустых слотах** (`0x20`, `0x21`) — то есть там, где у изготовителя ничего нет. **Возможная
  ошибка нашего порта, требует проверки.**

**Чего не существует:** соответствия «код → текст» **нигде в приложении**. Найдено место, которое
вместо локальной таблицы **отправляет код на сервер производителя** вместе с серийным номером
колеса и показывает то, что вернётся.

**Отсюда два следствия для нас:**

1. **Выкачать таблицу нечем** — в отличие от KingSong, где справочник лежал целиком и снялся двумя
   запросами. Здесь это диагностика по конкретному случаю, а не словарь;
2. **Показывать номер** — единственное, что мы можем сегодня. Свой словарь придётся набирать по
   мере встреч с ошибками на живых колёсах.

*Отклонено при разборе:* сопоставление кодов с текстами из пула строк по порядку следования —
доказано, что пул склеен из почти-дублей по моделям, и соседство в нём ничего не значит.
Девятнадцать номеров без имён честнее девятнадцати неверных имён.

---

## 12. KingSong: время поездки и флаги режима

Отдельного словаря не требуют — это обычные смещения телеметрии, которые мы просто не читаем.
Раскладка — в [`kingsong-telemetry-comparison.md`](kingsong-telemetry-comparison.md), часть 2
(кадр ходовых данных): время поездки и два флага режима перечислены среди полей, которых нет в
нашем разборе.

---

## 13. Что ещё увидено в разборе — на заметку

Сквозной проход по всем девятнадцати документам `loeuc` выявил три места, которых плану сейчас не
хватает. Данных под них здесь нет (задание их не запрашивало, и лишний домысел — грех), но источник
известен на случай, если этап дойдёт до реализации:

- **InMotion V1 — 23 кода ошибок против наших 7** (пункт 12 части II плана). В отличие от P6, здесь
  **нет готовой таблицы имён**: `inmotion-v1-comparison.md` фиксирует только диапазон (`0x11–0x27`,
  23 значения) и открытый вопрос, один это канал с нашим `Alert` или два разных сообщения —
  **сами коды ошибок в разборе не расшифрованы**. Если этап дойдёт до V1, тут нужен не перенос
  данных, а отдельный заход в `real_time_data.dart:264-734` (артефакт `blutter-inmotion-protocol/`);
- **Begode — отдельный канал ошибок BMS** (перегрев, недогрев, обрыв, аномалия ячейки) — упомянут в
  пункте 12 части II плана как «не разбирается», но ни в `begode-comparison.md`, ни в рубрикаторе
  `loeuc/AGENTS.md` нет таблицы кодов под него — только факт, что канал существует;
- **KingSong — время поездки и два флага режима**, не читаемые нами (тот же пункт 12) — в
  `kingsong-telemetry-comparison.md` есть смещения самих полей (это телеметрия, не отдельный
  словарь), их можно взять напрямую из документа при реализации; отдельного извлечения сюда не
  делал, так как задание перечисляло только семь конкретных позиций, а это восьмая и девятая, не
  запрошенные.

---

## 14. LeaperKim — оригинальные подписи настроек (родное приложение)

**Для экрана настроек колеса** (план [`android-plan-34-wheel-settings.md`](android-plan-34-wheel-settings.md),
замысел — [`wheel-settings-architecture.md`](wheel-settings-architecture.md)). Источник — родное
приложение производителя (`com.laoniao.leaperkim`, не LoEUC), три места:
`src_leaper/resources/res/values/strings.xml` (314 строк, **единственный языковой набор** — других
`values-*` в ресурсах нет, второго перевода не существует), `res/layout/*.xml` (разметка экранов —
чей `android:id` стоит рядом с чьим `android:text`), `sources/com/laoniao/leaperkim/setting/**`
(код, который читает эти же строки через `getString(R.string.…)`/`R.layout.…`). Опкоды и диапазоны —
из §7 этого документа (уже прочитаны и сверены там же).

**Связь «подпись ↔ ключ» — только из разметки и кода, не из соседства строк в файле.** Два пути
подтверждения, оба показаны ниже как ссылка на файл и строку:

1. **Программный путь** (десять слайдер-экранов на общей `BaseSetProgressActivity`):
   `getCustomTitle()` возвращает `getString(R.string.X)` — заголовок экрана жёстко связан с кодом,
   который в этом же файле строит команду записи (см. §7.1/7.2 — тот же класс, тот же файл).
2. **Разметочный путь** (экраны на собственной разметке — `angle_trim`, `speed_alarm`,
   `fallProtectionAngle`, `unit`, ride-mode-3-уровня, калибровка гироскопа): заголовок —
   `android:text="@string/X"` у `TextView android:id="@+id/tv_title"` в файле разметки, который
   `setContentView` этого же экрана подключает явно.
3. **Строка меню** (главный экран настроек `layout_car_control.xml`, `car_control`=«Vehicle
   Control»): у каждой настройки — блок `android:id="@+id/layout_<ключ>"`, и **следующий же**
   `android:text="@string/…"` внутри того же блока — это подпись пункта меню, которая может
   отличаться от заголовка отдельного экрана той же настройки (пример — `unit`, см. таблицу).

### 14.1 Таблица — двадцать позиций с найденной командой

Перевод в столбце «Мой перевод» — **не оригинал**, добавлен для удобства чтения этой таблицы и
явно отделён форматированием (курсив). Где оригинал невнятен для русскоязычного читателя —
отдельная пометка после таблицы, не в самой строке (правило: не править чужую строку).

| Ключ | Опкод | Оригинальное название | Оригинальная подсказка/предупреждение | Диапазон | Единица | *Мой перевод названия* |
|---|---|---|---|---|---|---|
| `pedalHardness` | 15 | **«Ride mode setting»** (`padle_soft_setting`, экран; опечатка в самом имени ресурса — «padle» вместо «paddle», значение строки без опечатки) | нет | 0..100 | % | *Настройка режима педалей* |
| `angle_trim` | 16 (пара) | **«Angle adjustment»** (`setting_gryo_angle_adjust`) — и как заголовок отдельного экрана, и как подпись в меню (`layout_gro`) | нет | −80..80 (÷10 → °) | ° | *Настройка угла* |
| `stopSpeed` | 17 | **«Tiltback speed setting»** (`stop_speed_setting`, экран и меню); на приборной панели то же значение подписано отдельно — **«Over-speed Tilt-back»** (`stop_speed`, `frag_second.xml:138`, только чтение) | нет | 10..120 | км/ч | *Настройка скорости отклонения педалей* |
| `speed_alarm` | 17 (пара, коллизия с `stopSpeed`) | **«Alarm speed setting»** (`setting_speed_setting`, экран и меню); на приборной панели — **«Over-speed Alarm»** (`danger_speed`, `frag_second.xml:159`, только чтение) | нет | 10..120 | км/ч | *Настройка скорости тревоги* |
| `stopPowerRate` | 18 | **«PWM value setting»** (`stop_power_setting`) | нет | 30..100 | % | *Настройка значения ШИМ* |
| `screenBacklightRate` | 20 | **«Backlight adjustment»** (`screen_backlight_setting`) | нет | 0..100 | % | *Настройка подсветки экрана* |
| `gyro` (калибровка) | 21 | **«Calibration»** (`gyrocrope_setting` — опечатка в имени ресурса, «gyrocrope» вместо «gyroscope», значение без опечатки) | кнопка меняет текст по состоянию: **«Adjust\nattitude»** (`gyro_setting_start`, ожидание) / **«Start\ncalibration»** (`gyro_setting_stop`, готово к следующему шагу) / **«Wait 1s To Stop»** (`gyro_setting_stopwaiting`, неиспользуемая третья строка — не нашёл, откуда вызывается); предупреждение-тост **«Cant set while riding!»** (`set_gro_hint`) при попытке калибровки в движении | фикс. значение `1`, состояние 0/1/2 | — | *Калибровка* |
| `transportMode` | 22 | **«Transportation mode»** (`setting_mode`, подпись в меню, `layout_transport_mode`); в словаре строк есть и второй, не используемый в коде дубль с тем же текстом — `transport_mode` (см. §14.3) | нет | 0/1 | toggle | *Транспортный режим* |
| `fallProtectionAngle` | 22 (пара, коллизия с `transportMode` и питанием) | **«Lateral cut off angle adjustment»** (`set_save_angle_title`, экран и меню) | нет | 35..75 | ° | *Настройка угла бокового отключения* |
| `unit` | 23 | В меню — **«Unit switch»** (`car_unit_setting`, `layout_unit`); на отдельном экране заголовок другой — **«Unit Switch»** (`unit_switch_title`); кнопки выбора — **«km \| kph»** (`unit_switch_kilomiter`) / **«mi \| mph»** (`unit_switch_mar`) | нет | 0/1 | toggle | *Переключение единиц* |
| `vol` (voltage_correction) | 24 | **«Voltage correction»** (`setting_vol_adjust`) | нет | −15..15 (÷10 → %) | % | *Коррекция напряжения* |
| `lowVolMode` | 25 (коллизия с паролем) | **«Low battery mode»** (`low_power_setting`) | нет | 0/1 | toggle | *Режим низкого заряда* |
| `highSpeedMode` | 26 | **«High speed mode»** (`high_speed_setting`) | нет | 0/1 | toggle | *Высокоскоростной режим* |
| `keyTone` | 28 | **«Button volume adjustment»** (`key_click_cound_setting` — опечатка в имени ресурса, «cound» вместо «count», значение без опечатки) | нет | 0..100 | % | *Настройка громкости кнопок* |
| `maxChargeVol` | 29 | **«Max charging voltage setting»** (`chareg_max_power` — опечатка в имени ресурса, «chareg» вместо «charge», значение без опечатки) | нет | 0..120 (+ смещение базового напряжения, см. §7) | В | *Настройка максимального напряжения заряда* |
| `upOrDownSpeedHelper` | 31 | **«Acceleration and deceleration assistt»** (`acc_dec_asssit` — опечатка **в самом отображаемом тексте**: лишняя «t» в конце «assistt», и в имени ресурса «asssit» с лишней «s»; не путать с опечаткой в id — здесь опечатка именно в том, что видит пользователь) | нет | 0..100 | % | *Помощь при разгоне и торможении* |
| `upSpeedCul` | 33 | **«Accelerometer reduction»** (`acc_reduction`) | нет | 0..100 | % | *Снижение акселерометра* |
| `brakePressureAlarm` | 34 | **«Brake overpressure alarm setting»** (`brake_overpressure_alarm`) | нет | 80..125 | % | *Настройка тревоги избыточного торможения* |
| Ride mode, 3 уровня (старые Sherman, `padle_soft_setting`-экран не используется) | 12 (пара) | **«Ride Mode setting»** (`ride_mode`, отдельный экран `SetRideModeActivity`, отличается от `pedalHardness`) — кнопки **«Soft»** / **«Medium»** / **«Hard»** (`mode_soft`/`mode_medium`/`mode_hard`; короткие варианты `mode_soft_short`/`mode_hard_short` — тоже «Soft»/«Hard», без сокращений в самом тексте) | нет | 1/2/3 | preset | *Настройка режима езды* |

### 14.2 Действия без диапазона — тоже с оригинальными подписями

Не настройки со значением, а команды-действия; оригинальные подписи находятся тем же способом
(разметка/код), приведены для полноты, раз экран настроек соседствует с ними в меню/на приборной
панели:

| Действие | Опкод | Оригинальная подпись | Оригинальная подсказка/предупреждение |
|---|---|---|---|
| Гудок/сигнал | 14 (пара) | **«Alarm»** (`horn`, кнопка на главном экране, `HomepageFragment`) | нет |
| Свет вкл/выкл | 13 (пара) | **«Light»** (`light`); индикатор состояния — **«:on»** / **«:off»** (`light_on`/`light_off`) | нет |
| Сброс поездки (`CLEARMETER`) | 11 (старая прошивка) / 13 (новая) | **«Reset Meter»** (`reset_short_meter`, главный экран) | подтверждение: **«Are you sure to reset short meter?»** (`reset_short_meter_confirm`) |
| Питание/удержание (10 с до выключения) | 22 (пара, коллизия) | **«Shut Down in 10s»** (`shut_in_10`) / вариант **«Close in 10s»** (`close_in_10_hint`) — обе строки есть в ресурсах, какая где именно показывается — не устанавливал | предупреждение при попытке во время езды/заряда: **«Cant Set while riding or charging»** (`set_close_in_10_hint`) |
| Пароль/блокировка колеса | 25 (коллизия с `lowVolMode`) | **«Locking Password»** (`setting_pwd`, пункт меню) → экран **«Password Setting»** (`set_pwd`) / **«Modify Password»** (`modify_pwd`) / **«Clear Password»** (`clear_pwd`); автоблокировка — **«Auto Lock When Shutdown»** (`auto_lock`) | **«Please Input Password with 6 digits»** (`old_pwd`), **«Please Input New Password with 6 digits»** (`new_pwd`), **«Please Confirm New Password»** (`confrim_pwd` — опечатка в имени ресурса, «confrim» вместо «confirm»); ошибки: **«Password must be 6 digits»** (`password_length_error`), **«Two passwords are inconsistent»** (`password_confirm_not_same`), **«Incorrect Password,Please try again»** (`pwd_weeor` — опечатка и в имени ресурса, и, вероятно, задумывался «pwd_error»); **«No Password Set»** (`no_pwd`) |
| Чтение журнала ошибок | 20 (коллизия с `screenBacklightRate`) | **«Upload error log»** (`setting_upload_log`, пункт меню) / **«Download Log»** (`upload_log`, кнопка на самом экране — названо противоположно пункту меню: там «Upload», тут «Download») / **«Get Latest Log»** (`get_latest_log`) | нет |

### 14.3 Капкан 2 — обе несостыковки, как просили

**Подписи без команды (найдены в разметке/строках, кода-отправителя нет):**

- **«Long endurance mode»** (`setting_long_endurance_mode`) — строка и целая строка разметки под
  неё существуют в `layout_setting.xml:304-316`, но у этой строки разметки
  `android:visibility="gone"` (жёстко скрыта) и в `SettingActivity.java` нет ни одного упоминания
  «endurance» — ни `findViewById`, ни обработчика клика. Настройка задумывалась (место в макете
  зарезервировано), но не подключена ни к какому опкоду в этой версии приложения (сборка 59).
- **«Limited»** (`current_speed_limit`) — используется на главном экране (`frag_home.xml:58`) как
  живой индикатор, но ни разу не встречается в коде как `R.string.current_speed_limit` — то есть в
  разметке подпись стоит статически, а какое поле телеметрии (или отсутствие поля) должно менять
  рядом стоящий индикатор — из разобранного кода не определяется. Это не настройка колеса
  (не пишется, судя по всему — читается), но подпись есть, а откуда берётся значение — нет.
- **`transport_mode`** (строка ресурса) — дублирует по тексту («Transportation mode») реально
  используемую `setting_mode`, но сама нигде не читается кодом (`grep` по `R.string.transport_mode`
  не находит совпадений). Похоже на переименование в ходе разработки, где старое имя ресурса
  осталось неиспользуемым, а не на отдельную настройку.

**Команда без подписи** — не нашёл ни одной среди двадцати позиций §14.1/14.2: у каждого опкода,
разобранного в §7, нашлась хотя бы одна оригинальная строка интерфейса. Специально искал обратный
случай и не нашёл — фиксирую как отрицательный результат, не как пропуск.

### 14.4 Прочее увиденное по пути — не относится к настройкам колеса

Для полноты (не потребовалось для таблицы, но всплыло при чтении `strings.xml`): единицы
дисплея (`unit_switch_kilomiter`/`unit_switch_mar`), опознание моделей в интерфейсе
(`car_sherman`/`car_abrams`/`car_patton`/`car_sherman_max`/`car_sherman_s` = «Sherman»/«Abrams»/
«Patton»/«Sherman Max»/«Sherman-S» — те же имена, что в `CAR_DATA_JSON`, `leaperkim-official-app.md`
§5.1), общие статусы подключения и сообщения об ошибках прошивки — всё это не подписи настроек,
переносить в этот словарь не стал.
