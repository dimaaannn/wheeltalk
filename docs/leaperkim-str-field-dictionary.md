# Словарь полей STR-режима LeaperKim

**Это словарь стороннего клиента (LoEUC, `pw.vasilevskiy.loeuc`), не производителя.** У официального
приложения LeaperKim (`com.laoniao.leaperkim`) диагностического STR-режима нет вовсе — проверено:
ни строки `CHANGESTRORPACK`/`U0Slow`, ни маркера `StR` в его декомпиляте не нашлось (разбор — в
`loeuc/leaperkim-str-diagnostics.md`, там же — как режим включается, как выглядит поток и чем
рискован). **Значит все имена полей, единицы измерения и даже пометка «подтверждено» — чужая
реконструкция**, а не документация изготовителя. LoEUC читает сырой отладочный UART прошивки и сам
пытается понять, что означает каждое поле; уровень доверия в последнем столбце — это честная
оценка самого LoEUC, а не гарантия истинности.

Источник — статический словарь `xn0.java` (287 записей, `defpackage.xn0`,
`C:\Work\repos\loeuc\src\java\defpackage\xn0.java:9-46`) и карта «страница → теги»
`ao0.java` (`defpackage.ao0`, `C:\Work\repos\loeuc\src\java\defpackage\ao0.java:8`) — оба
класса читаются целиком, без ретуши декомпилятора (простые статические инициализаторы, jadx
разобрал их без пропусков). Уровень доверия хранится в отдельном перечислении `yn0`
(`C:\Work\repos\loeuc\src\java\defpackage\yn0.java:14-19`): `Confirmed`/`Likely`/`Guess` —
переведено как «подтверждено»/«вероятно»/«догадка», один к одному, без ужесточения и без
смягчения формулировок.

**Поправка к предыдущему разбору.** В `loeuc/leaperkim-str-diagnostics.md` заявлено число «269
полей» — оно оказалось **заниженным на 18**: при первом подсчёте не попали восемнадцать полей,
объявленных в начале статического блока `xn0.java` (строки 14–32, до общего списка) —
`dKM`, `AA`, `AB`, `AC`, `V`, `Cmo`, `Cmt`, `FdHz`, `Out`, `TC`, `FTC`, `VD`, `IM`, `Fs`, `VQ`,
`Vqd`, `AQ`, `AD`. Верное число — **287** (подтверждено дважды: прямым подсчётом записей словаря и
перекрёстной сверкой со списком страниц, где после исправления не осталось ни одного тега без
определения). Заодно исправлена ошибка того же подсчёта: полный кватернион ориентации и углы
тангажа/крена/курса на странице `Imu` помечены в коде как **«вероятно»**, не «подтверждено» — в
предыдущем документе было указано иначе.

Все 287 записей — ниже, без отбора. Считаны программно (не набраны от руки) построчным разбором
`xn0.java`, дважды сверены: подсчётом (90 подтверждено + 96 вероятно + 101 догадка = 287) и
перекрёстной проверкой с картой страниц `ao0.java` (все теги из десяти списков страниц нашли
определение в словаре — расхождений не осталось).

---

## 1. Формат кадрового потока — коротко

Полный разбор — `loeuc/leaperkim-str-diagnostics.md`. Здесь только то, что нужно, чтобы пользоваться
словарём:

- Маркер записи в двоичном (`Framed`) варианте потока — 6 байт: `53 74 52 7F 00 40`
  (`"StR"` + `0x7F 0x00 0x40`, `defpackage.mo0.java:8`).
- В `Framed`-варианте записи идут с шагом **ровно 70 байт** между соседними вхождениями маркера
  (`defpackage.mo0.java:95-127`) — раскладка этих 70 байт по отдельным полям изнутри в
  декомпиляте не нашлась (классификатор смотрит только на шаг, не на содержимое).
- В текстовом (`RawText`) варианте записи разделены двойным переводом строки `\r\n\r\n`
  (`defpackage.sc2.java:28`).
- **Как поле сопоставляется тегу**: каждая запись потока (`go0`, «LeaperKimStrRecord») несёт карту
  `Map<тег, значение>` (`defpackage.go0.java:9,43`) — тег из этой карты и есть первый столбец
  таблицы ниже. Расшифровка тега (название, единица, уровень доверия) берётся отдельным поиском в
  словаре `xn0.b` — статической `Map<тег, wn0>`, построенной из того же списка, что приведён здесь
  целиком (`defpackage.xn0.java:35-40`).
- Один и тот же тег может относиться к нескольким страницам одновременно (см. столбец «Страницы»
  ниже) — это не ошибка данных, а свойство словаря: часть каналов (например, заполнители `U1`-`U5`
  или температурные каналы `Cmo`/`ChgAD`) присутствует в нескольких видах вывода прошивки сразу.

---

## 2. Карта «страница → поля»

Десять страниц (`defpackage.zn0.java:22-42`), список тегов на каждой — из `defpackage.ao0.java:8`
дословно, порядком как в коде. Одиннадцатое значение перечисления, `Unknown`, — служебное, страницей
данных не является.

### Main (сводка) — 40 полей

`AA`, `AB`, `AC`, `AD`, `AI`, `AP`, `AQ`, `Bem`, `CC`, `CM`, `CdA`, `ChgAD`, `Cmo`, `Cmt`, `FTC`, `FdHz`, `Fs`, `GC`, `IM`, `LDpp`, `Lm1`, `MdKM`, `Out`, `RA`, `TC`, `U1`, `U2`, `U3`, `U4`, `U5`, `V`, `VD`, `VQ`, `Vmi`, `Vmx`, `Vnp`, `Vp`, `Vpp`, `Vqd`, `dKM`

### MotorAndPhases (мотор и фазы) — 37 полей

`6Dpp`, `AC`, `AD`, `ApS`, `BC`, `BD`, `BpS`, `CC`, `CD`, `CpS`, `DS`, `DerC`, `Di`, `Dif`, `Dir`, `FA`, `HPS`, `Ha`, `HdHz`, `Her8`, `IA`, `JA`, `LDpp`, `Pole`, `SDr`, `U1`, `U2`, `U3`, `U4`, `U5`, `di1`, `di2`, `diOis`, `dpOis`, `fyOis`, `isH`, `rAB`

### Imu (инерциальный блок) — 40 полей

`ASqt`, `AX`, `AY`, `AZ`, `FyT`, `GGY`, `GX`, `GY`, `GZ`, `Kg`, `Kp`, `LiC`, `MP`, `ObT`, `OmC`, `PAref`, `Ptb`, `RSqt`, `Rtc`, `TeT`, `U1`, `U2`, `U3`, `U4`, `U5`, `ex`, `ey`, `ez`, `fMS`, `iuki`, `iukp`, `obtT`, `p`, `q0`, `q1`, `q2`, `q3`, `r`, `tCNT`, `y`

### Adc (АЦП) — 45 полей

`6TcC`, `A`, `AD0`, `AD1`, `AD16T`, `AD17R`, `AD2`, `AD3`, `AD4`, `AD5`, `AD6`, `AD7`, `AD8`, `AD9`, `AOs`, `B`, `BOs`, `BajT`, `BtL`, `BtR`, `BtS`, `C`, `COs`, `ChgAD`, `Cmo`, `DOs`, `GdcC`, `HdT`, `HeT`, `NlT`, `ObT`, `Off`, `OmT`, `R`, `RC`, `Rnd`, `S`, `Se`, `SkM`, `TkM`, `V`, `Vgd`, `iAD4`, `iAD8`, `iAD9`

### TemperatureAndPower (температуры и мощность) — 35 полей

`AQ`, `Asc`, `CTr`, `Cci`, `Cma`, `Cmo`, `Cnt`, `Cres`, `FA`, `Gres`, `IA`, `LwC`, `OfC`, `Pat`, `Ple`, `Psl`, `TA`, `Top`, `U1`, `U2`, `U3`, `U4`, `U5`, `VD`, `VQ`, `Vgd`, `Vqd`, `Wcm`, `Wht`, `Wma`, `bit`, `cR`, `mO`, `mWH`, `uEc`

### Cells (ячейки) — 39 полей

`01`, `02`, `03`, `04`, `05`, `06`, `07`, `08`, `09`, `10`, `11`, `12`, `13`, `14`, `15`, `16`, `17`, `18`, `19`, `20`, `21`, `22`, `23`, `24`, `25`, `26`, `27`, `28`, `29`, `30`, `31`, `32`, `33`, `34`, `35`, `36`, `BL`, `Mi`, `Mx`

### LoopRates (частоты контуров) — 12 полей

`A`, `B`, `C`, `HzAdc`, `HzF10`, `HzF25`, `HzF50`, `HzF5K`, `HzPid`, `HzWav`, `U`, `dHzDTE`

### FlashLog (журнал флеша) — 12 полей

`EvIdx`, `ExiEvN`, `Flock`, `Ka`, `Kc`, `Kg`, `LsVol`, `NxtRdy`, `PgEvN`, `PgIdx`, `RxN`, `WtN`

### Comms (связь) — 35 полей

`AD2`, `Btn`, `DWTT`, `Fs`, `Het`, `Len`, `Ncnt`, `NoLV`, `Pbtn`, `RxR`, `RxS`, `U0RxDm`, `U0RxId`, `U0TxId`, `U1`, `U2`, `U3`, `U4`, `U5`, `Vb`, `Vd`, `Vdm`, `Vf`, `Vn`, `Vp`, `Vum`, `cAD0`, `cAD1`, `cAD2`, `hAD0`, `hAD1`, `hAD2`, `lAD`, `rAD`, `type`

### BmsSummary (сводка BMS) — 29 полей

`Aa`, `Ac`, `Aco`, `Ap`, `BLN`, `BrR`, `BrS`, `Cc`, `Co`, `Cs`, `Len`, `Os`, `Rqt`, `RxM`, `RxR`, `RxS`, `StcC`, `StmV`, `T0`, `T1`, `T2`, `T3`, `T4`, `T5`, `Vbu`, `Vo`, `Vp`, `Vsc`, `rM`


### Вне карты страниц — 10 полей

Эти теги есть в словаре, но не входят ни в один из десяти списков `ao0.java` — то есть, что бы
они ни значили, штатный экран диагностики LoEUC их не показывает: `iAD0`, `iAD1`, `iAD2`, `hAD4`,
`hAD8`, `hAD9`, `cAD4`, `cAD8`, `cAD9`, `T`. Не выбрасываю их из таблицы ниже — они такая же часть
словаря, как и остальные 277, просто не привязаны к экрану.

---

## 3. Полный словарь — все 287 полей

Сортировка — по тегу (лексикографически). Столбец «Страницы» — все страницы, где тег встречается
(может быть несколько, может быть пусто — см. раздел выше). Единица «—» означает, что в словаре для
поля единица не указана (`unit=null` в исходнике) — это тоже дословный перенос, не пропуск.

| Тег | По-русски | По-английски | Единица | Доверие | Страницы |
|---|---|---|---|---|---|
| `01` | Напряжение ячейки 1 | Cell 1 voltage | мВ | подтверждено | Cells |
| `02` | Напряжение ячейки 2 | Cell 2 voltage | мВ | подтверждено | Cells |
| `03` | Напряжение ячейки 3 | Cell 3 voltage | мВ | подтверждено | Cells |
| `04` | Напряжение ячейки 4 | Cell 4 voltage | мВ | подтверждено | Cells |
| `05` | Напряжение ячейки 5 | Cell 5 voltage | мВ | подтверждено | Cells |
| `06` | Напряжение ячейки 6 | Cell 6 voltage | мВ | подтверждено | Cells |
| `07` | Напряжение ячейки 7 | Cell 7 voltage | мВ | подтверждено | Cells |
| `08` | Напряжение ячейки 8 | Cell 8 voltage | мВ | подтверждено | Cells |
| `09` | Напряжение ячейки 9 | Cell 9 voltage | мВ | подтверждено | Cells |
| `10` | Напряжение ячейки 10 | Cell 10 voltage | мВ | подтверждено | Cells |
| `11` | Напряжение ячейки 11 | Cell 11 voltage | мВ | подтверждено | Cells |
| `12` | Напряжение ячейки 12 | Cell 12 voltage | мВ | подтверждено | Cells |
| `13` | Напряжение ячейки 13 | Cell 13 voltage | мВ | подтверждено | Cells |
| `14` | Напряжение ячейки 14 | Cell 14 voltage | мВ | подтверждено | Cells |
| `15` | Напряжение ячейки 15 | Cell 15 voltage | мВ | подтверждено | Cells |
| `16` | Напряжение ячейки 16 | Cell 16 voltage | мВ | подтверждено | Cells |
| `17` | Напряжение ячейки 17 | Cell 17 voltage | мВ | подтверждено | Cells |
| `18` | Напряжение ячейки 18 | Cell 18 voltage | мВ | подтверждено | Cells |
| `19` | Напряжение ячейки 19 | Cell 19 voltage | мВ | подтверждено | Cells |
| `20` | Напряжение ячейки 20 | Cell 20 voltage | мВ | подтверждено | Cells |
| `21` | Напряжение ячейки 21 | Cell 21 voltage | мВ | подтверждено | Cells |
| `22` | Напряжение ячейки 22 | Cell 22 voltage | мВ | подтверждено | Cells |
| `23` | Напряжение ячейки 23 | Cell 23 voltage | мВ | подтверждено | Cells |
| `24` | Напряжение ячейки 24 | Cell 24 voltage | мВ | подтверждено | Cells |
| `25` | Напряжение ячейки 25 | Cell 25 voltage | мВ | подтверждено | Cells |
| `26` | Напряжение ячейки 26 | Cell 26 voltage | мВ | подтверждено | Cells |
| `27` | Напряжение ячейки 27 | Cell 27 voltage | мВ | подтверждено | Cells |
| `28` | Напряжение ячейки 28 | Cell 28 voltage | мВ | подтверждено | Cells |
| `29` | Напряжение ячейки 29 | Cell 29 voltage | мВ | подтверждено | Cells |
| `30` | Напряжение ячейки 30 | Cell 30 voltage | мВ | подтверждено | Cells |
| `31` | Напряжение ячейки 31 | Cell 31 voltage | мВ | подтверждено | Cells |
| `32` | Напряжение ячейки 32 | Cell 32 voltage | мВ | подтверждено | Cells |
| `33` | Напряжение ячейки 33 | Cell 33 voltage | мВ | подтверждено | Cells |
| `34` | Напряжение ячейки 34 | Cell 34 voltage | мВ | подтверждено | Cells |
| `35` | Напряжение ячейки 35 | Cell 35 voltage | мВ | подтверждено | Cells |
| `36` | Напряжение ячейки 36 | Cell 36 voltage | мВ | подтверждено | Cells |
| `6Dpp` | Шесть шагов на электрический оборот | Six steps per electrical revolution | — | догадка | MotorAndPhases |
| `6TcC` | Счётчик | Counter | — | догадка | Adc |
| `A` | Одиночный канал, назначение зависит от страницы | Single channel, meaning depends on the page | — | догадка | Adc, LoopRates |
| `Aa` | Ток BMS, канал a | BMS current, channel a | А | вероятно | BmsSummary |
| `AA` | Ток фазы A | Phase A current | 0.1 А | подтверждено | Main |
| `AB` | Ток фазы B | Phase B current | 0.1 А | подтверждено | Main |
| `Ac` | Ток BMS, канал c | BMS current, channel c | А | вероятно | BmsSummary |
| `AC` | Ток фазы C | Phase C current | 0.1 А | подтверждено | Main, MotorAndPhases |
| `Aco` | Накопленный заряд | Accumulated charge | — | догадка | BmsSummary |
| `AD` | Ток по оси d | d-axis current | А | вероятно | Main, MotorAndPhases |
| `AD0` | Канал АЦП 0 | ADC channel 0 | — | вероятно | Adc |
| `AD1` | Канал АЦП 1 | ADC channel 1 | — | вероятно | Adc |
| `AD16T` | Канал АЦП 16, температура | ADC channel 16, temperature | — | вероятно | Adc |
| `AD17R` | Канал АЦП 17, сопротивление | ADC channel 17, resistance | — | вероятно | Adc |
| `AD2` | Канал АЦП 2 | ADC channel 2 | — | вероятно | Adc, Comms |
| `AD3` | Канал АЦП 3 | ADC channel 3 | — | вероятно | Adc |
| `AD4` | Канал АЦП 4 | ADC channel 4 | — | вероятно | Adc |
| `AD5` | Канал АЦП 5 | ADC channel 5 | — | вероятно | Adc |
| `AD6` | Канал АЦП 6 | ADC channel 6 | — | вероятно | Adc |
| `AD7` | Канал АЦП 7 | ADC channel 7 | — | вероятно | Adc |
| `AD8` | Канал АЦП 8 | ADC channel 8 | — | вероятно | Adc |
| `AD9` | Канал АЦП 9 | ADC channel 9 | — | вероятно | Adc |
| `AI` | Ток, третий канал | Current, third channel | А | догадка | Main |
| `AOs` | Смещение нуля канала A | Channel A zero offset | — | вероятно | Adc |
| `Ap` | Ток BMS, пиковый | BMS current, peak | А | догадка | BmsSummary |
| `AP` | Оценка тока по нагреву: спадает после нагрузки | Thermal current estimate: decays after load | — | вероятно | Main |
| `ApS` | Сырой отсчёт токового датчика фазы A | Raw phase A current sense | — | вероятно | MotorAndPhases |
| `AQ` | Ток по оси q | q-axis current | А | вероятно | Main, TemperatureAndPower |
| `Asc` | Ток, оценка | Current estimate | А | догадка | TemperatureAndPower |
| `ASqt` | Норма акселерометра | Accelerometer magnitude | — | вероятно | Imu |
| `AX` | Акселерометр X | Accelerometer X | — | подтверждено | Imu |
| `AY` | Акселерометр Y | Accelerometer Y | — | подтверждено | Imu |
| `AZ` | Акселерометр Z, около 1000 в покое | Accelerometer Z, about 1000 at rest | — | подтверждено | Imu |
| `B` | Одиночный канал, назначение зависит от страницы | Single channel, meaning depends on the page | — | догадка | Adc, LoopRates |
| `BajT` | Признак, 0 или 1 | Flag, 0 or 1 | — | подтверждено | Adc |
| `BC` | Смещение нуля АЦП фазы B, 32768 в покое | Phase B ADC zero offset, 32768 at rest | — | вероятно | MotorAndPhases |
| `BD` | Накопитель АЦП фазы B | Phase B ADC accumulator | — | вероятно | MotorAndPhases |
| `Bem` | Противо-ЭДС, растёт со скоростью | Back-EMF, rises with speed | — | вероятно | Main |
| `bit` | Битовая маска | Bit mask | — | догадка | TemperatureAndPower |
| `BL` | Маска балансировки, шесть байт | Balancing mask, six bytes | — | вероятно | Cells |
| `BLN` | Число балансируемых ячеек | Cells being balanced | — | догадка | BmsSummary |
| `BOs` | Смещение нуля канала B | Channel B zero offset | — | вероятно | Adc |
| `BpS` | Сырой отсчёт токового датчика фазы B | Raw phase B current sense | — | вероятно | MotorAndPhases |
| `BrR` | Реле или тормоз, второй канал | Relay or brake, second channel | — | догадка | BmsSummary |
| `BrS` | Реле или тормоз, состояние | Relay or brake, state | — | догадка | BmsSummary |
| `BtL` | Константа 8111 | Constant 8111 | — | подтверждено | Adc |
| `Btn` | Кнопка | Button | — | вероятно | Comms |
| `BtR` | Константа 8111 | Constant 8111 | — | подтверждено | Adc |
| `BtS` | Константа 81111 | Constant 81111 | — | подтверждено | Adc |
| `C` | Одиночный канал, назначение зависит от страницы | Single channel, meaning depends on the page | — | догадка | Adc, LoopRates |
| `cAD0` | АЦП ячейка, канал 0 | ADC cell, channel 0 | — | догадка | Comms |
| `cAD1` | АЦП ячейка, канал 1 | ADC cell, channel 1 | — | догадка | Comms |
| `cAD2` | АЦП ячейка, канал 2 | ADC cell, channel 2 | — | догадка | Comms |
| `cAD4` | АЦП ячейка, канал 4 | ADC cell, channel 4 | — | догадка | *(ни на одной странице)* |
| `cAD8` | АЦП ячейка, канал 8 | ADC cell, channel 8 | — | догадка | *(ни на одной странице)* |
| `cAD9` | АЦП ячейка, канал 9 | ADC cell, channel 9 | — | догадка | *(ни на одной странице)* |
| `Cc` | Счётчик | Counter | — | догадка | BmsSummary |
| `CC` | Всегда 0 во всех захватах | Zero in every capture | — | подтверждено | Main, MotorAndPhases |
| `Cci` | Температура мотора | Motor temperature | °C | подтверждено | TemperatureAndPower |
| `CD` | Накопитель АЦП фазы C | Phase C ADC accumulator | — | вероятно | MotorAndPhases |
| `CdA` | Канал АЦП, тот же что page 0/4 @63 | ADC channel, same as page 0/4 @63 | — | вероятно | Main |
| `ChgAD` | АЦП зарядного входа | Charger input ADC | — | вероятно | Main, Adc |
| `CM` | Всегда 0 во всех захватах | Zero in every capture | — | подтверждено | Main |
| `Cma` | Медленная тепловая масса: +0.16 °C там, где обмотка +7.3 | Slow thermal mass: +0.16 °C where the winding gained 7.3 | — | подтверждено | TemperatureAndPower |
| `Cmo` | Температура MOS | MOS temperature | °C | подтверждено | Main, Adc, TemperatureAndPower |
| `Cmt` | Не температура мотора; что это — неизвестно | Not the motor temperature; unidentified | — | подтверждено | Main |
| `Cnt` | Счётчик | Counter | — | догадка | TemperatureAndPower |
| `Co` | Счётчик, печатается дважды | Counter, printed twice | — | догадка | BmsSummary |
| `COs` | Смещение нуля канала C | Channel C zero offset | — | вероятно | Adc |
| `CpS` | Сырой отсчёт токового датчика фазы C | Raw phase C current sense | — | вероятно | MotorAndPhases |
| `cR` | Сопротивление, вычисленное | Computed resistance | — | догадка | TemperatureAndPower |
| `Cres` | Сопротивление, вычисленное | Computed resistance | — | догадка | TemperatureAndPower |
| `Cs` | Счётчик | Counter | — | догадка | BmsSummary |
| `CTr` | Сырой АЦП термистора, обратен Cci | Raw thermistor ADC, inverse of Cci | — | подтверждено | TemperatureAndPower |
| `DerC` | Счётчик ошибок производной | Derivative error counter | — | догадка | MotorAndPhases |
| `dHzDTE` | Частота DTE | DTE rate | Гц | догадка | LoopRates |
| `Di` | Отладочный счётчик | Debug counter | — | догадка | MotorAndPhases |
| `di1` | Отладочный вход 1 | Debug input 1 | — | догадка | MotorAndPhases |
| `di2` | Отладочный вход 2 | Debug input 2 | — | догадка | MotorAndPhases |
| `Dif` | Разность | Difference | — | догадка | MotorAndPhases |
| `diOis` | Счётчик рассогласования | Mismatch counter | — | догадка | MotorAndPhases |
| `Dir` | Направление вращения | Rotation direction | — | подтверждено | MotorAndPhases |
| `dKM` | Скорость со знаком | Signed speed | 0.1 км/ч | подтверждено | Main |
| `DOs` | Смещение нуля канала D | Channel D zero offset | — | вероятно | Adc |
| `dpOis` | Счётчик рассогласования | Mismatch counter | — | догадка | MotorAndPhases |
| `DS` | Накопитель, большие значения | Accumulator, large values | — | догадка | MotorAndPhases |
| `DWTT` | Счётчик циклов DWT | DWT cycle counter | — | вероятно | Comms |
| `EvIdx` | Индекс события | Event index | — | вероятно | FlashLog |
| `ex` | Ошибка регулятора по X | Controller error, X | — | догадка | Imu |
| `ExiEvN` | Число событий выхода | Exit event count | — | догадка | FlashLog |
| `ey` | Ошибка регулятора по Y | Controller error, Y | — | догадка | Imu |
| `ez` | Ошибка регулятора по Z | Controller error, Z | — | догадка | Imu |
| `FA` | Ток, канал F | Current, channel F | А | догадка | MotorAndPhases, TemperatureAndPower |
| `FdHz` | Частота привода, 41.9 на км/ч | Drive frequency, 41.9 per km/h | — | подтверждено | Main |
| `Flock` | Блокировка флеша | Flash lock | — | вероятно | FlashLog |
| `fMS` | Счётчик миллисекунд | Millisecond counter | — | догадка | Imu |
| `Fs` | Заглушка, всегда 60001 | Placeholder, always 60001 | — | подтверждено | Main, Comms |
| `FTC` | То же задание, фильтрованное | The same command, filtered | — | подтверждено | Main |
| `fyOis` | Счётчик рассогласования | Mismatch counter | — | догадка | MotorAndPhases |
| `FyT` | Всегда 0 | Always 0 | — | подтверждено | Imu |
| `GC` | Счётчик простоя: +5 за запись в покое | Idle counter: +5 per record at rest | — | вероятно | Main |
| `GdcC` | Счётчик | Counter | — | догадка | Adc |
| `GGY` | Гироскоп Y, фильтрованный | Gyroscope Y, filtered | — | вероятно | Imu |
| `Gres` | Сопротивление, второй канал | Resistance, second channel | — | догадка | TemperatureAndPower |
| `GX` | Гироскоп X | Gyroscope X | — | вероятно | Imu |
| `GY` | Гироскоп Y | Gyroscope Y | — | вероятно | Imu |
| `GZ` | Гироскоп Z; прошивка печатает это имя дважды | Gyroscope Z; the firmware prints this name twice | — | подтверждено | Imu |
| `Ha` | Состояние датчиков Холла | Hall sensor state | — | вероятно | MotorAndPhases |
| `hAD0` | АЦП верхний, канал 0 | ADC high, channel 0 | — | догадка | Comms |
| `hAD1` | АЦП верхний, канал 1 | ADC high, channel 1 | — | догадка | Comms |
| `hAD2` | АЦП верхний, канал 2 | ADC high, channel 2 | — | догадка | Comms |
| `hAD4` | АЦП верхний, канал 4 | ADC high, channel 4 | — | догадка | *(ни на одной странице)* |
| `hAD8` | АЦП верхний, канал 8 | ADC high, channel 8 | — | догадка | *(ни на одной странице)* |
| `hAD9` | АЦП верхний, канал 9 | ADC high, channel 9 | — | догадка | *(ни на одной странице)* |
| `HdHz` | Частота по датчикам Холла | Hall-derived frequency | Гц | вероятно | MotorAndPhases |
| `HdT` | Температура, канал не заведён: всегда 0 | Temperature channel, unwired: always 0 | — | подтверждено | Adc |
| `Her8` | Счётчик ошибок датчиков Холла | Hall error counter | — | вероятно | MotorAndPhases |
| `Het` | Счётчик | Counter | — | догадка | Comms |
| `HeT` | Температура, канал не заведён: всегда 0 | Temperature channel, unwired: always 0 | — | подтверждено | Adc |
| `HPS` | Шаги датчиков Холла | Hall steps | — | догадка | MotorAndPhases |
| `HzAdc` | Частота АЦП, 10 кГц | ADC rate, 10 kHz | Гц | подтверждено | LoopRates |
| `HzF10` | Задача 10 Гц | 10 Hz task | Гц | подтверждено | LoopRates |
| `HzF25` | Задача 25 Гц | 25 Hz task | Гц | подтверждено | LoopRates |
| `HzF50` | Задача 50 Гц | 50 Hz task | Гц | подтверждено | LoopRates |
| `HzF5K` | Задача 5 кГц | 5 kHz task | Гц | подтверждено | LoopRates |
| `HzPid` | Частота контура управления, 499 Гц | Control loop rate, 499 Hz | Гц | подтверждено | LoopRates |
| `HzWav` | Задача генерации волны | Waveform task | Гц | вероятно | LoopRates |
| `IA` | Ток, канал I | Current, channel I | А | догадка | MotorAndPhases, TemperatureAndPower |
| `iAD0` | АЦП ток, канал 0 | ADC current, channel 0 | — | догадка | *(ни на одной странице)* |
| `iAD1` | АЦП ток, канал 1 | ADC current, channel 1 | — | догадка | *(ни на одной странице)* |
| `iAD2` | АЦП ток, канал 2 | ADC current, channel 2 | — | догадка | *(ни на одной странице)* |
| `iAD4` | АЦП ток, канал 4 | ADC current, channel 4 | — | догадка | Adc |
| `iAD8` | АЦП ток, канал 8 | ADC current, channel 8 | — | догадка | Adc |
| `iAD9` | АЦП ток, канал 9 | ADC current, channel 9 | — | догадка | Adc |
| `IM` | Заглушка, всегда 60001 | Placeholder, always 60001 | — | подтверждено | Main |
| `isH` | Признак работы по датчикам Холла | Hall-driven flag | — | вероятно | MotorAndPhases |
| `iuki` | Интегратор, интегральная часть | Integrator, integral part | — | догадка | Imu |
| `iukp` | Интегратор, пропорциональная часть | Integrator, proportional part | — | догадка | Imu |
| `JA` | Ток, канал J | Current, channel J | — | догадка | MotorAndPhases |
| `Ka` | Коэффициент регулятора | Controller gain | — | догадка | FlashLog |
| `Kc` | Коэффициент регулятора | Controller gain | — | догадка | FlashLog |
| `Kg` | Коэффициент регулятора | Controller gain | — | догадка | Imu, FlashLog |
| `Kp` | Пропорциональный коэффициент | Proportional gain | — | вероятно | Imu |
| `lAD` | АЦП, левый | ADC, left | — | вероятно | Comms |
| `LDpp` | Растёт со скоростью, знак по направлению | Rises with speed, signed by direction | — | догадка | Main, MotorAndPhases |
| `Len` | Длина кадра | Frame length | — | вероятно | Comms, BmsSummary |
| `LiC` | Предел тока | Current limit | — | вероятно | Imu |
| `Lm1` | Всегда 0 во всех захватах | Zero in every capture | — | подтверждено | Main |
| `LsVol` | Объём файловой системы | Filesystem volume | — | вероятно | FlashLog |
| `LwC` | Нижний предел тока | Lower current limit | — | догадка | TemperatureAndPower |
| `MdKM` | Всегда 0 во всех захватах | Zero in every capture | — | подтверждено | Main |
| `Mi` | Минимальная ячейка | Lowest cell | мВ | подтверждено | Cells |
| `mO` | Смещение | Offset | — | догадка | TemperatureAndPower |
| `MP` | Отладочная маска | Debug mask | — | догадка | Imu |
| `mWH` | Милливатт-часы, печатается дважды | Milliwatt-hours, printed twice | — | вероятно | TemperatureAndPower |
| `Mx` | Максимальная ячейка | Highest cell | мВ | подтверждено | Cells |
| `Ncnt` | Счётчик кадров | Frame counter | — | вероятно | Comms |
| `NlT` | Температура, канал не заведён: всегда 0 | Temperature channel, unwired: always 0 | — | подтверждено | Adc |
| `NoLV` | Признак низкого напряжения | Low-voltage flag | — | догадка | Comms |
| `NxtRdy` | Готовность следующей записи лога | Next log record ready | — | вероятно | FlashLog |
| `ObT` | Температура, канал не заведён: всегда 0 | Temperature channel, unwired: always 0 | — | подтверждено | Imu, Adc |
| `obtT` | Температура, канал не заведён: всегда 0 | Temperature channel, unwired: always 0 | — | подтверждено | Imu |
| `OfC` | Смещение тока | Current offset | — | догадка | TemperatureAndPower |
| `Off` | Смещение | Offset | — | догадка | Adc |
| `OmC` | Предел, второй канал | Limit, second channel | — | догадка | Imu |
| `OmT` | Температура, канал не заведён: всегда 0 | Temperature channel, unwired: always 0 | — | подтверждено | Adc |
| `Os` | Счётчик | Counter | — | догадка | BmsSummary |
| `Out` | То же, что Vqd, в 1.3145 раза | Same as Vqd, scaled by 1.3145 | — | подтверждено | Main |
| `p` | Наклон вперёд-назад | Pitch | — | вероятно | Imu |
| `PAref` | Задание угла педалей | Pedal angle setpoint | — | вероятно | Imu |
| `Pat` | Признак | Flag | — | догадка | TemperatureAndPower |
| `Pbtn` | Кнопка, предыдущее состояние | Button, previous state | — | вероятно | Comms |
| `PgEvN` | Число событий страницы | Page event count | — | догадка | FlashLog |
| `PgIdx` | Индекс страницы флеша | Flash page index | — | вероятно | FlashLog |
| `Ple` | Признак | Flag | — | догадка | TemperatureAndPower |
| `Pole` | Электрическая позиция или номер пары полюсов | Electrical position or pole pair index | — | вероятно | MotorAndPhases |
| `Psl` | Признак | Flag | — | догадка | TemperatureAndPower |
| `Ptb` | Наклон назад, tilt-back | Tilt-back | — | вероятно | Imu |
| `q0` | Кватернион ориентации, w | Attitude quaternion, w | — | вероятно | Imu |
| `q1` | Кватернион ориентации, x | Attitude quaternion, x | — | вероятно | Imu |
| `q2` | Кватернион ориентации, y | Attitude quaternion, y | — | вероятно | Imu |
| `q3` | Кватернион ориентации, z | Attitude quaternion, z | — | вероятно | Imu |
| `r` | Наклон боковой | Roll | — | вероятно | Imu |
| `R` | Одиночный канал АЦП | Single ADC channel | — | догадка | Adc |
| `RA` | Небольшое целое, знак меняется | Small signed integer | — | догадка | Main |
| `rAB` | Сопротивление обмотки A-B | A-B winding resistance | — | догадка | MotorAndPhases |
| `rAD` | АЦП, правый | ADC, right | — | вероятно | Comms |
| `RC` | Счётчик | Counter | — | догадка | Adc |
| `rM` | Режим | Mode | — | догадка | BmsSummary |
| `Rnd` | Разрядность АЦП, 4095 | ADC full scale, 4095 | — | вероятно | Adc |
| `Rqt` | Запросов к BMS | BMS requests | — | вероятно | BmsSummary |
| `RSqt` | Норма угловой скорости | Angular rate magnitude | — | вероятно | Imu |
| `Rtc` | Часы колеса, unix-время | Wheel clock, unix time | с | подтверждено | Imu |
| `RxM` | Ответов от BMS | BMS replies | — | вероятно | BmsSummary |
| `RxN` | Принято записей | Records received | — | догадка | FlashLog |
| `RxR` | Ошибок приёма | Receive errors | — | догадка | Comms, BmsSummary |
| `RxS` | Принято по UART | UART bytes received | — | вероятно | Comms, BmsSummary |
| `S` | Счётчик | Counter | — | догадка | Adc |
| `SDr` | Заданное направление | Commanded direction | — | вероятно | MotorAndPhases |
| `Se` | Одиночный канал АЦП | Single ADC channel | — | догадка | Adc |
| `SkM` | Пробег сессии | Session distance | км | вероятно | Adc |
| `StcC` | Счётчик состояния, печатается дважды | State counter, printed twice | — | вероятно | BmsSummary |
| `StmV` | Напряжение состояния, печатается дважды | State voltage, printed twice | мВ | догадка | BmsSummary |
| `T` | Счётчик | Counter | — | догадка | *(ни на одной странице)* |
| `T0` | Сырой отсчёт термистора BMS 0 | Raw BMS thermistor 0 | — | вероятно | BmsSummary |
| `T1` | Сырой отсчёт термистора BMS 1 | Raw BMS thermistor 1 | — | вероятно | BmsSummary |
| `T2` | Сырой отсчёт термистора BMS 2 | Raw BMS thermistor 2 | — | вероятно | BmsSummary |
| `T3` | Сырой отсчёт термистора BMS 3 | Raw BMS thermistor 3 | — | вероятно | BmsSummary |
| `T4` | Сырой отсчёт термистора BMS 4 | Raw BMS thermistor 4 | — | вероятно | BmsSummary |
| `T5` | Сырой отсчёт термистора BMS 5 | Raw BMS thermistor 5 | — | вероятно | BmsSummary |
| `TA` | Ток, канал A | Current, channel A | А | догадка | TemperatureAndPower |
| `TC` | Задание момента | Torque command | — | подтверждено | Main |
| `tCNT` | Счётчик | Counter | — | догадка | Imu |
| `TeT` | Температура, канал не заведён: всегда 0 | Temperature channel, unwired: always 0 | — | подтверждено | Imu |
| `TkM` | Пробег всего | Total distance | км | вероятно | Adc |
| `Top` | Верхний предел контура | Loop upper limit | — | догадка | TemperatureAndPower |
| `type` | Тип кадра | Frame type | — | вероятно | Comms |
| `U` | Загрузка CPU этой задачей | CPU load of this task | % | подтверждено | LoopRates |
| `U0RxDm` | DMA приёма UART0 | UART0 receive DMA | — | вероятно | Comms |
| `U0RxId` | Индекс приёма UART0 | UART0 receive index | — | вероятно | Comms |
| `U0TxId` | Индекс передачи UART0 | UART0 transmit index | — | вероятно | Comms |
| `U1` | Не используется, всегда 0 | Unused, always 0 | — | подтверждено | Main, MotorAndPhases, Imu, TemperatureAndPower, Comms |
| `U2` | Не используется, всегда 0 | Unused, always 0 | — | подтверждено | Main, MotorAndPhases, Imu, TemperatureAndPower, Comms |
| `U3` | Не используется, всегда 0 | Unused, always 0 | — | подтверждено | Main, MotorAndPhases, Imu, TemperatureAndPower, Comms |
| `U4` | Не используется, всегда 0 | Unused, always 0 | — | подтверждено | Main, MotorAndPhases, Imu, TemperatureAndPower, Comms |
| `U5` | Не используется, всегда 0 | Unused, always 0 | — | подтверждено | Main, MotorAndPhases, Imu, TemperatureAndPower, Comms |
| `uEc` | Счётчик ошибок | Error counter | — | догадка | TemperatureAndPower |
| `V` | Напряжение батареи | Pack voltage | В | подтверждено | Main, Adc |
| `Vb` | Напряжение, канал b | Voltage, channel b | В | догадка | Comms |
| `Vbu` | Напряжение шины BMS, совпадает с V | BMS bus voltage, matches V | В | вероятно | BmsSummary |
| `Vd` | Напряжение, канал d | Voltage, channel d | В | догадка | Comms |
| `VD` | Напряжение по оси d; минус на разгоне | d-axis voltage; negative under acceleration | — | подтверждено | Main, TemperatureAndPower |
| `Vdm` | Напряжение, разность | Voltage, difference | В | догадка | Comms |
| `Vf` | Напряжение, канал f | Voltage, channel f | В | догадка | Comms |
| `Vgd` | Напряжение драйвера затворов | Gate driver voltage | В | вероятно | Adc, TemperatureAndPower |
| `Vmi` | Минимум напряжения шины | Bus voltage minimum | В | вероятно | Main |
| `Vmx` | Максимум напряжения шины | Bus voltage maximum | В | вероятно | Main |
| `Vn` | Напряжение, канал n | Voltage, channel n | В | догадка | Comms |
| `Vnp` | Отрицательный пик напряжения шины | Bus voltage negative peak | В | вероятно | Main |
| `Vo` | Напряжение, канал o | Voltage, channel o | — | догадка | BmsSummary |
| `Vp` | Константа 81123.5 во всех захватах | Constant 81123.5 in every capture | — | подтверждено | Main, Comms, BmsSummary |
| `Vpp` | Пик напряжения шины | Bus voltage peak | В | вероятно | Main |
| `VQ` | Напряжение по оси q | q-axis voltage | — | вероятно | Main, TemperatureAndPower |
| `Vqd` | Напряжение по оси q, второй канал | q-axis voltage, second channel | — | вероятно | Main, TemperatureAndPower |
| `Vsc` | Напряжение шины, второй канал | Bus voltage, second channel | В | вероятно | BmsSummary |
| `Vum` | Напряжение, сумма | Voltage, sum | В | догадка | Comms |
| `Wcm` | Потреблено, второй счётчик | Consumed, second counter | Втч | вероятно | TemperatureAndPower |
| `Wht` | Потреблено за сессию | Consumed this session | Втч | вероятно | TemperatureAndPower |
| `Wma` | Максимум мощности | Peak power | — | догадка | TemperatureAndPower |
| `WtN` | Записано записей | Records written | — | догадка | FlashLog |
| `y` | Курс | Yaw | — | вероятно | Imu |

---

## 4. О достоверности этого документа

Извлечение — не ручной набор: оба файла-источника (`xn0.java`, `ao0.java`) читаны целиком,
287 записей словаря разобраны программным разбором по образцу конструктора (`new wn0(...)`/
локальный алиас `a(...)`, оба строят один и тот же класс `wn0`, `defpackage.xn0.java:44-46`), без
отбора и без исправления текста. Совпадение сумм (90+96+101=287, ни одного тега без уровня доверия,
ни одного тега из карты страниц без определения в словаре) — проверка полноты, не оценка на глаз.

Уровень доверия, названия и единицы измерения — **дословный перенос из кода LoEUC**, включая
странности (`"Всегда 0 во всех захватах"`, `"Не температура мотора; что это — неизвестно"`,
`"Заглушка, всегда 60001"`) — они не сглажены и не убраны, это часть значения словаря: список того,
что сам LoEUC считает мусорными/непонятыми каналами, не менее ценен, чем список понятых.

Ничего не отправлялось на живое колесо. Документ — данные, не код.
