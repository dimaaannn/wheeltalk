# Напряжения и ряды ячеек современных моноколёс

> **Собрано 08.08.2026** ресерчем по [плану 27](android-plan-27-cell-count.md) §27.1. Отправная
> точка — [eucer.ru](https://eucer.ru/) от владельца, каждое число сверено вторым источником.
> Здесь только данные; логика определения — в самом плане, кода по этой таблице пока нет.
> Охват: **колёса от 64 В и выше**, пять марок — Begode/Gotway, Veteran/Leaperkim, KingSong,
> InMotion, Ninebot.

## Две ловушки. Прочесть до таблицы

**1. «Напряжение» в спеках EUC — это напряжение полного заряда, а не номинал.** Отрасль печатает
`S × 4,2`. Проверено на сходимости: Sherman L «151,2 В» = 36 × 4,2; Patton «126 В» = 30 × 4,2;
Master «134,4 В» = 32 × 4,2; P6 «235 В» = 56 × 4,2. Деление заявленных вольт на 4,2 даёт **целое во
всех без исключения найденных случаях** — на этом и построена колонка «ряд S».

Номинал (3,6–3,7 В на ячейку) магазины тоже иногда печатают, и тогда число выглядит чужим:
KingSong S22 Pro у одного продавца «111V Li-Ion» — это 30 × 3,7, при официальных «2220Wh 126V» и
зарядке «DC 126V × 6A». Begode Master у другого — «134.4V / 102.4V nominal», где 102,4 = 32 × 3,2 и
это вообще напряжение отсечки, а не номинал. **Спекам магазинов верить только по трём совпавшим.**

**2. «50S» в названии — это тип банки Samsung INR21700-50S, а не число ячеек в ряду.** «Sherman L
50S», «InMotion V14 50S», «Blitz Pro 3000Wh 50S», «X-Max … 50S cell configuration» — всё про химию
ячейки. Совпадение злое: у Begode Race «210V 50S» ряд действительно равен 50 (210 / 4,2), и два
разных смысла сошлись в одной строке. На этом спотыкается и eucer.ru: у него Sherman L и Lynx
помечены «50S» как ряд.

*Отсюда правило справочника: тип банки и число параллелей (`P`) здесь не пишутся вовсе. Они множат
ёмкость и ток, а напряжение не меняют — 20S4P и 20S8P дают одни и те же 84 В.*

## Рядов, которых не бывает — это ценнее самой таблицы

Встречается **одиннадцать** значений `S`, и только они:

```
16  20  24  30  32  36  40  42  50  56  60
```

Отсюда прямые отбраковки:

- **Все ряды чётные.** Нечётного нет ни одного.
- **Не встречаются: 22, 26, 28, 34, 38, 44, 46, 48, 52, 54, 58.** Между 42 и 50 пусто, между 32 и
  36 пусто.
- **117,6 В (28S) не подтверждено ничем.** Искал по «117.6V», по «116.8V», по зарядным
  устройствам, по модельным рядам Begode — ни одного колеса, ни одного зарядника. При этом
  настройка `"3"` у нас подписана «117,6 В» (`WheelTalk.Droid/Resources/Strings/AppStrings.resx`),
  множитель в `GotwayDecoder.GetScaledVoltage` даёт **116,8 В**, а ячеек берётся **32**. Три числа,
  и все три расходятся между собой.
- **151,2 В (36S) у Begode не подтверждено.** Настройка `"6"` такой ряд предлагает, живого колеса
  нет: Master Pro оказался 134,4 В, а не 151,2. У Veteran и KingSong ряд 36 существует.
- **126 В (30S) у Begode нет вовсе** — ни в настройке, ни в модельном ряду.
- **48S (201,6 В) не найдено ни у кого.** Зато найдены три ряда, которых нет ни в одном нашем
  декодере: **50 (210 В), 56 (235,2 В), 60 (252 В)**.

## Таблица

Колонка **«ряд S»**: <u>«прямо»</u> — конфигурация названа источником словами (`56S4P`, `32s4p`);
<u>«вывод»</u> — получен делением полного напряжения на 4,2. Это разные уровни доверия.

Версии одной модели с разным напряжением — **разные строки**, напряжение в имени.

### Begode / Gotway

| Модель | Полное, В | Ряд S | Откуда |
|---|---|---|---|
| A2 | 84 | 20 (вывод) | [ewheels: «A2/A2+, 750Wh», 84V](https://ewheels.com/products/begode-a2) |
| C8 | 84 | 20 (вывод) | [oneride: «C8 50GB 84V 2500W 1500Wh»](https://oneride.eu/en/begode/1855-begode-c8-50gb-84v-2500w-1500wh.html) |
| Mten3 84 | 84 | 20 (вывод) | [3euc: «Mten3 84V»](https://www.3euc.com/begode/) |
| MSX 84 | 84 | 20 (вывод) | — вторым источником не сверял |
| Nikola 84 | 84 | 20 (вывод) | — вторым источником не сверял |
| Falcon | 100,8 | 24 (вывод) | [ewheels: «900Wh, 100.8V»](https://ewheels.com/products/begode-falcon-900wh-battery-2800w-peak-power) |
| Falcon Pro | 100,8 | 24 (вывод) | [escooterclinic: «1800Wh 100.8V»](https://escooterclinic.co.uk/en/products/begode-falcon-pro-electric-unicycle-3000w-power-17-8ah-battery-65mph-speed-50mi-range-9246) |
| MSP | 100,8 | 24 (вывод) | [ewheels, зарядник 100.8V для MSX/Nikola/Monster](https://ewheels.com/products/100-8v-8a-rapid-charger-gotway-msx-nikola-monster) |
| RS | 100,8 | 24 (вывод) | там же |
| Monster Pro | 100,8 | 24 (вывод) | там же |
| Hero | 100,8 | 24 (вывод) | там же |
| T4 | 100,8 | 24 (вывод) | [voltride: «100.8V, 1800Wh»](https://voltride.com/begode-t4/) |
| Master | 134,4 | **32 (прямо)** | [voltride: «32s4p, max 134.4V»](https://voltride.com/begode-master/) |
| Master Pro (V2) | 134,4 | 32 (вывод) | [Alien Rides: «134.4 Volts Peak, 4800Wh»](https://alienrides.com/products/begode-master-pro-v2-electric-unicycle) |
| EX30 | 134,4 | 32 (вывод) | [ewheels: «134V», зарядник «134V 3a»](https://ewheels.com/products/begode-ex30) |
| Extreme | 134,4 | 32 (вывод) | [ewheels](https://ewheels.com/products/begode-extreme) |
| Blitz | 134,4 | 32 (вывод) | [ewheels](https://ewheels.com/products/begode-blitz-2-400wh-battery-3-500w-motor-8kw-peak) |
| X-Way 134 | 134,4 | 32 (вывод) | [Alien Rides: «2400Wh (134 Version)»](https://alienrides.com/products/begode-x-way-electric-unicycle) |
| Blitz Pro | 168 | 40 (вывод) | [ветка форума «2025 Begode Blitz Pro [3000Wh, 168V]»](https://forum.electricunicycle.org/topic/40645-2025-begode-blitz-pro-3000wh-168v-20-926-lbs/) |
| X-Way 168 | 168 | 40 (вывод) | [begode.com: «X Way 168v 3000wh»](https://www.begode.com/collections/electric-unicycles) |
| ET Max | 168 | 40 (вывод) | [smartwheel: «3000Wh/168V»](https://smartwheel.us/begode-gotway-et-max-4500w-motor-electric-unicycle-3000wh-168v/) |
| Panther | 168 | 40 (вывод) | [begode.com: «Panther 168V 4400Wh»](https://www.begode.com/products/begode-panther-168v-4400wh-long-range-high-speed-euc) |
| Race | 210 | **50 (прямо)** | [smartwheel: «210V 50S Battery»](https://www.smartwheel.us/begode-gotway-race-5000w-motor-210v-50s-battery-electric-unicycle/) + [top-wheel: «210V 3800Wh 50S»](https://top-wheel.com/products/begode-race-electric-unicycle-5000w-210v-3800wh-50s-20inch-new-48-mosfet-balance-wheel) |
| X-Max | 252 | 60 (вывод) | [begode.com: «252 V»](https://www.begode.com/collections/electric-unicycles/products/x-max) + [ветка форума «XMax 4400wh: 252v»](https://forum.electricunicycle.org/topic/41160-begode-xmax-4400wh-252v-20-tire-90mph-115lb/) |

*Ряд 36 (151,2 В) настройка `"6"` предлагает — модели не найдено. Ряд 28 (117,6 В) настройки `"3"`
— не найдено (см. выше).*

### Veteran / Leaperkim

| Модель | Полное, В | Ряд S | Откуда |
|---|---|---|---|
| Sherman | 100,8 | 24 (вывод) | [e-smartway: «100.8V 3200Wh»](https://e-smartway.com/products/leaperkim-veteran-sherman-electric-unicycle-100-8v-3200wh-motor-power-2500w-off-road-20-inch-ncr18650ga-battery-max-70km-h) |
| Sherman Max | 100,8 | 24 (вывод) | — вторым источником не сверял |
| Sherman S | 100,8 | 24 (вывод) | [ewheels](https://www.ewheels.com/product/veteran-sherman-s/) |
| Abrams | 126 | 30 (вывод) | — вторым источником не сверял |
| Patton | 126 | **30 (прямо)** | [форум: «126V Samsung packs … 30 strings, 2 parallel»](https://forum.electricunicycle.org/topic/24765-veteran-sherman-battery-info-thread/) |
| Patton-S | 126 | 30 (вывод) | [ewheels: «2220Wh/126V»](https://ewheels.com/products/new-veteran-patton-s-2-220wh-battery-3-000w-motor-suspension) |
| Sherman L | 151,2 | 36 (вывод) | [ewheels: «4000Wh/151V»](https://ewheels.com/products/veteran-sherman-l-4-000wh-battery-3-200w-motor-8kw-peak) |
| Lynx | 151,2 | 36 (вывод) | [ewheels: «2700Wh/151V»](https://ewheels.com/products/veteran-lynx-2700wh-battery-3200w-motor-8kw-peak) |
| Lynx-S | 151,2 | 36 (вывод) | — подтверждено только по родству с Lynx, отдельного источника нет |
| Oryx | 176,4 | 42 (вывод) | [ewheels: «176.4v, 4700Wh»](https://ewheels.com/products/new-veteran-oryx-4-700wh-battery-4-200w-motor-suspension) |

### KingSong

| Модель | Полное, В | Ряд S | Откуда |
|---|---|---|---|
| KS-16X | 84 | 20 (вывод) | — вторым источником не сверял |
| KS-S18 | 84 | 20 (вывод) | [официальная спека S18](https://www.kingsong.com/pages/s18-spec) |
| KS-S19 | 100,8 | 24 (вывод) | [kingsong.com: зарядник «Output 100.8V 5A»](https://www.kingsong.com/products/kingsong-s19-pro) |
| KS-S20 | 126 | 30 (вывод) | — вторым источником не сверял |
| KS-S22 | 126 | 30 (вывод) | [официальная спека: «2220Wh 126V», «Charger DC 126V × 6A»](https://www.kingsong.com/pages/s22-pro-spec) |
| KS-S22 Pro | 126 | 30 (вывод) | там же |
| KS-S22 Eagle | 126 | 30 (вывод) | — вторым источником не сверял |
| KS-F18 | 151,2 | 36 (вывод) | [ewheels: «2700Wh/151.2V»](https://ewheels.com/products/king-song-f18-2-700wh-battery-5-000w-motor) |
| KS-F22 Pro | 176,4 | 42 (вывод) | [ewheels: «3108Wh/176.4V»](https://ewheels.com/products/king-song-f22-pro-3-108wh-battery-5-500w-motor) |

### InMotion

| Модель | Полное, В | Ряд S | Откуда |
|---|---|---|---|
| V11 | 84 | 20 (вывод) | [ветка форума «INMOTION V11 (2020)»](https://forum.electricunicycle.org/topic/16554-inmotion-v11-2020/) |
| V12 | 100,8 | 24 (вывод) | [myinmotion](https://www.myinmotion.com/products/inmotion-v12-electric-unicycle) |
| V12S | 100,8 | 24 (вывод) | [inmotionworld](https://inmotionworld.com/products/inmotion-v12s) |
| V13 Challenger | 126 | 30 (вывод) | [myinmotion: «126V»](https://www.myinmotion.com/inmotion-v13) |
| V13 Pro | 126 | 30 (вывод) | [Alien Rides](https://alienrides.com/products/inmotion-v13-electric-unicycle) |
| V14 Adventure | 134,4 | 32 (вывод) | [ewheels: «2400Wh (134V)»](https://ewheels.com/products/inmotion-adventure-v14-2400wh-battery-4000w-motor) |
| P6 | 235,2 | **56 (прямо)** | [пресс-релиз INMOTION: «235V», «56S,4P»](https://www.prnewswire.com/news-releases/inmotion-launches-p6-a-235v-high-voltage-performance-electric-unicycle-that-redefines-the-limits-of-speed-302581342.html) |

*P6 сходится и с нашим собственным дампом: 56 ячеек, carType 131.*

### Ninebot

| Модель | Полное, В | Ряд S | Откуда |
|---|---|---|---|
| One Z10 | 67,2 | 16 (вывод) | [форум: полка CV зарядки 67,3 В при 16S](https://forum.electricunicycle.org/topic/10983-ninebot-z10-power-supply-charge-doctor-mod/); каталог даёт 63 В номинала |
| One Z6 | 67,2 | 16 (вывод) | — отдельного источника нет, только родство с Z10 |

## Годы выпуска

Найдены плохо, и догадками не заполнялись. Твёрдо подтверждено источниками только это:

| Модель | Год | Откуда |
|---|---|---|
| InMotion V11 | 2020 | [ветка форума](https://forum.electricunicycle.org/topic/16554-inmotion-v11-2020/) |
| InMotion V12 | 2021 | [ветка форума (пре-релиз)](https://forum.electricunicycle.org/topic/21617-inmotion-v12-pre-release/) |
| Begode Master | 2022 | [ветка форума](https://forum.electricunicycle.org/topic/26527-begode-master-134v-2400wh-suspension/) |
| Ninebot One Z10 | 2018 | [обзор ElectricUnicycles.eu](https://www.electricunicycles.eu/new_ninebot_one_z_(z6_z8_z10)_electric_unicycle_what_about_the_range_and_power-c__275) |
| Begode Blitz Pro | 2025 | [ветка форума](https://forum.electricunicycle.org/topic/40645-2025-begode-blitz-pro-3000wh-168v-20-926-lbs/) |
| InMotion P6 | 2025 (анонс), «новинка 2026» | [пресс-релиз](https://www.prnewswire.com/news-releases/inmotion-launches-p6-a-235v-high-voltage-performance-electric-unicycle-that-redefines-the-limits-of-speed-302581342.html), [inmotion-france](https://www.inmotion-france.fr/en/unicycles/866-inmotion-p6-electric-unicycle-3701522013000.html) |

Для остальных моделей год **не найден**.

## Чего не нашёл

- Годы выпуска большинства моделей.
- Живого колеса на **117,6 В (28S)** — ни у Begode, ни у кого ещё.
- Живого Begode на **151,2 В (36S)**.
- Явной записи «`S` в последовательности» для большинства моделей: словами она подтверждена только
  у четырёх — P6 (`56S4P`), Master (`32s4p`), Race (`50S`), 126-вольтовый пак Veteran
  (`30 strings`). Остальные ряды — **вывод делением на 4,2**; метод проверен на этих четырёх и
  сошёлся везде, но это вывод, а не цитата.
- Официальной спеки Ninebot с числом ячеек: 16S держится на полке зарядки и номинале из каталога.
