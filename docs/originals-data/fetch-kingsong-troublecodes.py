#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Снятие справочников кодов неисправностей KingSong с сервера производителя.

Зачем: код ошибки колеса приходит в кадре телеметрии числом, а расшифровка живёт
только на сервере — в самом приложении текстов нет. Разбор запроса — в
WheelTalk/docs/kingsong-telemetry-comparison.md, «Добавление 2».

Что делает: ДВА запроса — справочник кодов колеса и справочник кодов BMS.

ПРОВЕРЕНО 15.08.2026: справочник BMS ОБЩИЙ для всех моделей. Десять разных
значений carModel (F22, 18L, KS-S22, KS-S20, KS-N1, KS-N1-B, KS-N8, KS-N10,
KS-E1, KS-X1) вернули байт в байт одинаковый набор из 34 записей, причём поле
carModel внутри записей пустое. Параметр на выдачу не влияет — перебирать
модели незачем.

Список моделей с сервера (api/equipment/modelSelect) требует входа в аккаунт
(sid кладётся безусловно, без него "ошибка параметра") — и, как выяснилось,
не нужен вовсе.

Запуск (Windows):
    python fetch-kingsong-troublecodes.py

Аргументы не нужны. Повторный запуск просто обновит оба файла.

Если "python" не найден, попробовать "py -3" вместо него. Сторонних библиотек
скрипт не требует — только то, что идёт с Python.

Результат ложится рядом со скриптом:
    ks-troublecode.json       коды колеса (66 записей)
    ks-bmstroublecode.json    коды BMS (34 записи, общие для всех моделей)

Вежливость к чужому серверу: один запрос на таблицу, пауза между обращениями,
обычный таймаут, при ошибке — остановка, а не перебор вслепую. Перебора страниц
нет: клиент производителя его тоже не делает.
"""

import hashlib
import json
import os
import sys
import time
import urllib.parse
import urllib.request

# Соль подписи — открытый текст из APK (a02.h0), общий для всех клиентов
# приложения. Это не персональный ключ и не секрет сервера.
SALT = "$2a$10$Xw68Ojbxd5FWB1r5qQxDZu"

BASE = "https://www.kingsong.site/index.php/api/"

# Поля формы. Значения соответствуют разобранной версии приложения 4.9.82.
# `lang` в самом приложении всегда zh_CN — метод перевода языка пуст, так что
# на язык ответа этот параметр не влияет (ответ приходит en + zh_cn).
BASE_FIELDS = {
    "apiversions": "4.9.82",
    "lang": "zh_CN",
    "mobiletype": "android",
    "key": "appbiaoshi",
    "apptype": "2",
    "phone_type": "samsung,SM-G991B,13",
}

TIMEOUT_SEC = 30
PAUSE_BETWEEN_CALLS_SEC = 2


def setup_console():
    """Windows: консоль по умолчанию не в UTF-8, и первый же китайский иероглиф
    в примере записи уронил бы скрипт на UnicodeEncodeError — уже после того,
    как данные скачаны, но до того, как их увидели. Файлы пишутся в UTF-8
    всегда, здесь чинится только печать."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass                                    # Python < 3.7 либо перенаправленный вывод


def sign(fields):
    """Подпись запроса: ключи по алфавиту, склейка 'k=v&' (амперсанд после
    каждого, включая последний), в конец — соль, от всего MD5 в нижнем регистре."""
    joined = "".join("{}={}&".format(k, fields[k]) for k in sorted(fields))
    return hashlib.md5((joined + SALT).encode("utf-8")).hexdigest()


def build(extra=None):
    fields = dict(BASE_FIELDS)
    fields["temptime"] = str(int(time.time()))
    if extra:
        fields.update(extra)
    fields["token"] = sign(fields)
    return fields


def call(path, extra, out_path, quiet=False):
    fields = build(extra)
    body = urllib.parse.urlencode(fields).encode("utf-8")
    request = urllib.request.Request(
        BASE + path,
        data=body,
        headers={"Content-Type": "application/x-www-form-urlencoded"},
    )

    try:
        with urllib.request.urlopen(request, timeout=TIMEOUT_SEC) as response:
            raw = response.read().decode("utf-8", "replace")
    except Exception as error:                      # noqa: BLE001 — сообщаем и идём дальше
        print("  {}: ОШИБКА — {}".format(path, error))
        return False

    with open(out_path, "w", encoding="utf-8") as handle:
        handle.write(raw)

    try:
        parsed = json.loads(raw)
    except ValueError:
        print("  {}: ответ не JSON, сохранён как есть → {}".format(path, out_path))
        print("     первые 200 символов: {}".format(raw[:200]))
        return True

    data = parsed.get("data")
    count = len(data) if isinstance(data, list) else "?"
    if quiet:
        print("  {}: status={} записей={}".format(path, parsed.get("status"), count))
        return True
    print("  {}: status={} msg={} записей={} total={}".format(
        path, parsed.get("status"), parsed.get("msg"), count, parsed.get("total")))
    print("     сохранено → {}".format(out_path))

    if isinstance(data, list) and data:
        sample = json.dumps(data[0], ensure_ascii=False)
        print("     пример записи: {}".format(sample[:200]))
        keys = sorted(data[0].keys()) if isinstance(data[0], dict) else []
        if keys:
            print("     поля записи: {}".format(", ".join(keys)))
    return True


def main():
    setup_console()
    here = os.path.dirname(os.path.abspath(__file__))

    print("Справочники кодов неисправностей KingSong")
    print("Два запроса: коды колеса и коды BMS.")
    print()

    print("Коды колеса:")
    call("equipment/troublecode", None, os.path.join(here, "ks-troublecode.json"))

    time.sleep(PAUSE_BETWEEN_CALLS_SEC)
    print()
    print("Коды BMS (справочник общий для всех моделей, carModel на выдачу не влияет):")
    call(
        "equipment/bmstroublecode",
        {"carModel": "F22"},                        # любое значение, лишь бы поле было
        os.path.join(here, "ks-bmstroublecode.json"),
    )

    print()
    print("Готово. Если записей ноль или status не 1 — покажи вывод, разберём.")


if __name__ == "__main__":
    main()
