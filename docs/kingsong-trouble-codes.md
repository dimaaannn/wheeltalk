# KingSong: словарь кодов неисправностей

Расшифровка кодов ошибок, которые колесо KingSong присылает числом. **В самом приложении этих
текстов нет** — производитель держит их на сервере и запрашивает при старте, поэтому без словаря
код показать можно, а объяснить нельзя.

**Происхождение.** Снято с сервера производителя 15.08.2026 двумя запросами (плюс девять проверочных — см. ниже)
(`api/equipment/troublecode` и `api/equipment/bmstroublecode`), инструмент —
[`loeuc/tools/fetch-kingsong-troublecodes.py`](../../loeuc/tools/fetch-kingsong-troublecodes.py),
исходные ответы лежат рядом с ним. Разбор запроса — в
[`kingsong-telemetry-comparison.md`](kingsong-telemetry-comparison.md), «Добавление 2».
Авторизация не требуется.

**Оговорки, которые надо знать до использования:**

- **русского перевода нет и не будет** — сервер отдаёт только английский и китайский, поле русской
  локали отсутствует в самой структуре. Переводить придётся своими силами, и тогда это уже наш
  словарь, который надо поддерживать;
- **справочник BMS оказался общим для всех моделей** — проверено 15.08.2026: десять значений
  `carModel` (F22, 18L, KS-S22, KS-S20, KS-N1, KS-N1-B, KS-N8, KS-N10, KS-E1, KS-X1) вернули
  **байт в байт одинаковый набор**, а поле модели внутри записей пустое. Параметр формы обязателен,
  но на выдачу не влияет. **Перебирать модели незачем, таблица ниже полна;**
- тексты приведены **дословно**, включая шероховатости английского оригинала. Правки не вносились;
- у части записей китайский текст **длиннее английского** и содержит указания, что делать
  (особенно в справочнике BMS). Полные тексты — в исходных файлах.

---

## 1. Коды колеса — 66 записей (диапазон 101–2235)

| Код | Описание (en) |
|---|---|
| `101` | E3-Hall Sensor Error |
| `102` | Ir-Over Current |
| `103` | E1-Block Error |
| `105` | be voltage detect need calibration |
| `107` | over ODC |
| `114` | BMS warning |
| `115` | BMS ALARM |
| `116` | BMS no data |
| `117` | BL-Low Voltage. |
| `118` | BH-Over Voltage V1 |
| `119` | E5-Throttle Handle Error |
| `120` | E4-Brake Handle Error |
| `121` | H1-Motor High Temperature |
| `122` | H2-MOS High Temperature T1 |
| `123` | H3-MOS High Temperature T2 |
| `125` | S1-Motor phase short circuit or the main board output current is too large |
| `126` | S2-Drive Cruit is not functioning correctly, please restart or replace drive circuit. |
| `127` | E0-Instrument communication failure, please check if communication is in working order. |
| `128` | Sn-Wrong serial number |
| `131` | BP-Over Voltage V2 |
| `132` | bms err |
| `201` | Motor Hall sensor error, please check the hall ensor and repair accordingly. |
| `202` | Over Current or locked rotor. |
| `203` | The motor is blocked, please check whether the motor is rotating smoothly or remove obstacles before riding. |
| `205` | Drive Cruit is not functioning correctly, please restart or replace drive circuit. |
| `206` | The motherboard output wire has short circuited, please check whether the battery output wire has short circuited or whether the motherboard MOS is damaged. |
| `207` | Gyroscope failure, please replace motherboard. |
| `208` | the coef of batvol have no just |
| `213` | bms setting err |
| `217` | Motor Hall sensor error, please check the hall ensor and repair accordingly. |
| `218` | The output power is too high, please do not accelerate or climb steep slopes in a hurry (please check whether the power alarm parameter settings are too low) |
| `219` | Device is outputting at max. |
| `220` | Motherboard output over current. Please ride with caution. |
| `221` | Motor is experiencing high temperature, please allow Motor to cool down before riding again. |
| `222` | MOS is experiencing high temperature, please allow MOS to cool down before riding again. |
| `223` | Charging is over voltage or over current. |
| `224` | The battery reaches the preset charging value, Adjust the charging ratio to 100% |
| `225` | bms charg high temperature |
| `226` | BMS Warnning |
| `227` | BMS get no data |
| `228` | Serial Number Error. |
| `229` | Low voltage, please charge your device! |
| `230` | Reserve power is missing, please replace motherboard. |
| `231` | Overvoltage, please beware of your safety and avoid riding downhill. |
| `232` | Lift switch is out of order, please release the handlebar or check whether the lift switch sensor has experienced a short circuit. |
| `233` | BMS CELL OVER VOL |
| `234` | Battery High Temperature |
| `235` | BMS mode version wrong |
| `1209` | mttool,vol err |
| `1210` | mttool,over time |
| `1211` | mttool,block err |
| `1212` | mttool,speed err |
| `2222` | The output current is at max, please ride with caution. |
| `2223` | The motherboard temperature is too high, please stop and ride after the it has cooled down. |
| `2224` | The motor temperature is too high, please stop and ride after the it has cooled down. |
| `2225` | No serial number or serial number error |
| `2226` | Please check if the motor hall line connection and is functioning normally. |
| `2227` | The output current of the main board has exceeded. Please check if the motor is damaged or if the phase line is shorted. |
| `2228` | Gyroscope error, please contact your seller and replacement motherboard. |
| `2229` | Low battery, please charge. |
| `2230` | The voltage is too high, please remove the charger. |
| `2231` | The voltage is too high, please do not ride downhill for an extended time. |
| `2232` | Sensor A is not connected or the sensor is damaged, the sensor has been closed for use. |
| `2233` | Sensor data A is reversed, this sensor is closed. |
| `2234` | Sensor B is not connected or the sensor is damaged, the sensor has been closed for use. |
| `2235` | Sensor data is reversed, or line fault, this sensor has now been turned off. |

---

## 2. Коды BMS, модель F22 — 34 записей (диапазон 5001–5042)

Тексты здесь длиннее: к описанию неисправности добавлено объяснение защиты и что делать.
Приведено первое предложение; полный текст — в исходном файле.

| Код | Описание (en) |
|---|---|
| `5001` | Hardware short-circuit and software short-circuit protection instructions and solutions: battery protection circuit short-circuit protection, please stop using the machine immediately, check… |
| `5002` | Over-charging protection instructions and solutions: If the charging current of the battery exceeds (reference value: 7A), please stop charging immediately, unplug the charger, wait for abou… |
| `5003` | Over-discharge protection instructions and solutions: If the battery discharge current exceeds (reference value: 40A), please stop the machine immediately, or you can also plug in the charge… |
| `5004` | Single-cell overvoltage protection description and treatment plan: If the voltage of the single-cell battery exceeds (reference value: 4.25V), please stop charging immediately, remove the ch… |
| `5005` | Total voltage over-discharge protection description and treatment plan: If the total battery voltage is lower than (reference value: 72.0V), please stop using the machine immediately, and ch… |
| `5006` | Ambient low temperature, high ambient temperature Protection instructions and treatment plan: battery temperature The ambient temperature (reference value: -30℃~70℃) has seriously exceeded t… |
| `5007` | High temperature discharge protection instructions and treatment plan: When the battery temperature is higher than (reference value: 60&deg;C) during discharge, please stop using the machine… |
| `5008` | High temperature charging protection instructions and solutions: If the charging temperature of the battery exceeds (reference value: 55&deg;C), please stop charging immediately |
| `5009` | MOSFET high temperature Protection instructions and treatment plan: The charging or discharging circuit temperature (reference value: 95℃) is seriously higher than the battery protection lim… |
| `5010` | Sampling failure, cell failure Fault description and solution: There is a failure in the protection board circuit, please stop using the machine immediately, check and repair it. |
| `5011` | NTC fault Fault description and solution: The temperature sensor is faulty |
| `5012` | Charging MOS fault Fault description and solution: If the charging circuit of the protection board is faulty, please stop using the machine and check and repair |
| `5013` | Total voltage and low voltage alarm description and processing plan: the total voltage (reference value: 90.0V~126.2V) is low (less than 90V), please charge it in time, and when the single v… |
| `5014` | Total voltage and overvoltage alarm description and solution: the total voltage (reference value: 90.0V~126.2V) is too high (greater than 126.2V), please stop charging in time, and use it no… |
| `5015` | Cell low voltage Alarm description and solution: The single battery voltage (reference value: 3.000V~4.22V) is low (less than 3V), please charge it in time, wait until the single cell voltag… |
| `5016` | Low battery alarm Alarm description and solution: The battery is low (reference value: 10%), please charge it in time, and the alarm will be automatically cleared when the battery power is h… |
| `5017` | Cell high voltage Alarm description and solution: The single battery voltage (reference value: 3.000V~4.22V) is too high (greater than 4.22V), please stop charging in time, and use it normal… |
| `5018` | Discharge high temperature Alarm description and solution: When the battery temperature exceeds 55&deg;C when riding or the motor is running, please stop riding or stop using the machine in… |
| `5019` | Charging high temperature Alarm description and solution: When the battery temperature exceeds 53℃ during charging, please stop charging in time, or strengthen the heat dissipation of the ma… |
| `5020` | Excessive voltage difference alarm description and solution: The voltage difference between the highest voltage and the lowest voltage of the battery cell exceeds the limit (reference value:… |
| `5024` | Total voltage and overvoltage protection instructions and solutions: when charging, the total voltage is higher than (reference value: 126.5V), please stop charging immediately, remove the c… |
| `5025` | Single-cell over-discharge protection instructions and solutions: If the voltage of the single-cell battery is lower than (reference value: 2.50V), charge the machine in time |
| `5027` | Discharge low temperature protection instructions and treatment plan: When the battery temperature is lower than (reference value: -20℃) during discharge, please stop using the machine immed… |
| `5028` | Low charging temperature Protection instructions and treatment plan: If the charging temperature of the battery is lower than (reference value: -5℃), please stop charging and improve the amb… |
| `5033` | Working power failure Fault description and solution: If the working power supply of the protection board is faulty, please stop using the machine immediately and check and repair it. |
| `5034` | MOS high temperature Alarm description and solution: Please confirm whether the MOS temperature is higher than (reference value: 75&deg;C) in time |
| `5035` | Ambient low temperature and high ambient temperature Alarm description and solution: The ambient temperature of the battery (reference value: -20°C~65°C) exceeds the limit, please confirm th… |
| `5036` | Discharge low temperature, discharge high temperature Alarm description and solution: The battery temperature (reference value: -10℃~57℃) exceeds the limit, please stop using the machine in… |
| `5037` | Charging low temperature, charging high temperature Alarm description and solution: The battery temperature (reference value: 3℃~45℃) exceeds the limit, please stop charging in time, or chec… |
| `5038` | Discharge overcurrent Alarm description and solution: Please confirm in time that the discharge current of the left and right batteries exceeds -35A (the negative sign indicates discharge) |
| `5039` | Charging overcurrent alarm description and solution: Please confirm whether the charging current (reference value: 6A) is too large (greater than 6A) in time, as long as the charging current… |
| `5040` | Low voltage fault fault description and treatment plan: single section voltage voltage is lower than 2.5V, and the total voltage is lower than the minimum limit value, the discharge has been… |
| `5041` | The internal humidity of the battery is too high (reference value: 90%), which may cause water ingress or battery leakage, posing a safety hazard |
| `5042` | The high-side battery communication is abnormal, charging is prohibited, posing a safety hazard |

---

## Как этим пользоваться

Код приходит в кадре предела скорости, смещение 14–15 — сейчас мы его **читаем и выбрасываем**
(см. план, часть I). Работа сводится к тому, чтобы вместо числа показать строку отсюда.

**Чего словарь не решает.** Он объясняет, *что* случилось, но не говорит, *насколько срочно*:
уровня важности в ответе сервера нет. Разложить эти коды по классам «авария / ошибка /
информационное» придётся самим — и это прямо ложится на иерархию из части IV плана.
