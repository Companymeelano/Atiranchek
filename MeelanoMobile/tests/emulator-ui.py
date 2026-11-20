#!/usr/bin/env python3
"""Exercise the installed APK through Android accessibility/UI, including a disposable SQL fixture."""
import os
import re
import subprocess
import time
import xml.etree.ElementTree as ET


def adb(*args):
    return subprocess.run(['adb', *args], check=True, capture_output=True, text=True).stdout


def dump():
    adb('shell', 'uiautomator', 'dump', '/sdcard/window.xml')
    return ET.fromstring(adb('shell', 'cat', '/sdcard/window.xml'))


def find(text, tries=8, scroll=False):
    for _ in range(tries):
        root = dump()
        for node in root.iter('node'):
            if text in (node.get('text', '') + ' ' + node.get('content-desc', '')):
                bounds = list(map(int, re.findall(r'\d+', node.get('bounds', ''))))
                if len(bounds) == 4 and bounds[2] > bounds[0] and bounds[3] > bounds[1]:
                    return ((bounds[0] + bounds[2]) // 2, (bounds[1] + bounds[3]) // 2)
        if scroll:
            adb('shell', 'input', 'swipe', '500', '1600', '500', '650', '400')
        time.sleep(1)
    raise RuntimeError(f'Could not find expected UI label: {text}')


def tap(text, **kwargs):
    x, y = find(text, **kwargs)
    adb('shell', 'input', 'tap', str(x), str(y))
    time.sleep(1)


def section(name):
    tap('منو')
    tap(name)


def fill(placeholder, value):
    tap(placeholder, scroll=True)
    adb('shell', 'input', 'text', value)
    # BACK exits the app when the IME is already hidden (e.g. hardware keyboard).
    state = adb('shell', 'dumpsys', 'input_method')
    if 'mInputShown=true' in state or 'isInputViewShown=true' in state:
        adb('shell', 'input', 'keyevent', 'KEYCODE_BACK')


def run_tests():
    find('MEELANO', tries=15)
    section('مرکز چک')
    find('RC-2401')
    section('فروش')
    find('جمع فروش')
    section('رادار نقدینگی')
    find('مانده')
    section('تحلیل خزانه')
    find('نسبت پوشش')
    section('هشدار سررسید')
    find('محاسبه')
    section('طرف حساب')
    find('مانده')
    section('بانک و مغایرت')
    find('انتخاب CSV بانک')
    section('اتصال SQL')
    fill('IP یا نام میزبان SQL', '10.0.2.2')
    fill('نام پایگاه داده', 'MEELANO_TEST')
    fill('نام کاربری SQL', 'meelanoci')
    fill('رمز SQL', os.environ['SQL_TEST_PASSWORD'])
    tap('بارگذاری فقط', scroll=True)
    tap('ادامه')
    find('بارگذاری موفق', tries=90)
    tap('باشه')
    find('snapshot SQL', tries=15)
    section('مرکز چک')
    find('T-001')
    print('UI navigation and read-only SQL fixture import passed.')


try:
    run_tests()
except Exception as exc:
    print('::error title=Android UI test::' + str(exc).replace('\n', ' '), flush=True)
    try:
        root = dump()
        labels = []
        for node in root.iter('node'):
            if node.get('class', '').endswith('EditText'):
                continue
            text = node.get('text', '') or node.get('content-desc', '')
            if text:
                labels.append(text[:100])
        print('::error title=Visible UI::' + ' | '.join(labels)[:1200].replace('\n', ' '), flush=True)
    except Exception:
        pass
    raise
