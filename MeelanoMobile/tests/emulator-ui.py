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
    for attempt in range(tries):
        root = dump()
        for node in root.iter('node'):
            if text in (node.get('text', '') + ' ' + node.get('content-desc', '')):
                bounds = list(map(int, re.findall(r'\d+', node.get('bounds', ''))))
                if len(bounds) == 4 and bounds[2] > bounds[0] and bounds[3] > bounds[1]:
                    return ((bounds[0] + bounds[2]) // 2, (bounds[1] + bounds[3]) // 2)
        if scroll:
            if attempt == 0:
                visible = [n.get('text', '') or n.get('content-desc', '') for n in root.iter('node') if not n.get('class', '').endswith('EditText')]
                edits = [f"EditText:{n.get('bounds', '')}:password={n.get('password', '')}" for n in root.iter('node') if n.get('class', '').endswith('EditText')]
                print('::warning title=Scrolling for ' + text + '::' + ' | '.join([x for x in visible if x] + edits)[:900].replace('\n', ' '), flush=True)
            size = re.findall(r'(\d+)x(\d+)', adb('shell', 'wm', 'size'))[-1]
            width, height = map(int, size)
            adb('shell', 'input', 'swipe', str(width // 2), str(int(height * .75)), str(width // 2), str(int(height * .30)), '400')
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
    print('::notice title=Android UI input::Completed ' + placeholder, flush=True)


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
    # Password hints are intentionally hidden by Android accessibility. Locate by role.
    for attempt in range(12):
        nodes = [n for n in dump().iter('node') if n.get('class', '').endswith('EditText') and n.get('password') == 'true']
        if nodes:
            bounds = list(map(int, re.findall(r'\d+', nodes[0].get('bounds', ''))))
            if len(bounds) == 4 and bounds[3] - bounds[1] > 15:
                adb('shell', 'input', 'tap', str((bounds[0] + bounds[2]) // 2), str((bounds[1] + bounds[3]) // 2))
                break
        size = re.findall(r'(\d+)x(\d+)', adb('shell', 'wm', 'size'))[-1]
        width, height = map(int, size)
        adb('shell', 'input', 'swipe', str(width // 2), str(int(height * .75)), str(width // 2), str(int(height * .3)), '400')
        time.sleep(1)
    else:
        raise RuntimeError('Password input is not visible after scrolling.')
    adb('shell', 'input', 'text', os.environ['SQL_TEST_PASSWORD'])
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
        logs = adb('logcat', '-d', '-v', 'brief')
        interesting = [line for line in logs.splitlines() if any(t in line.lower() for t in ('fatal', 'exception', 'dotnet', 'monodroid', 'meelano.executive.mobile'))]
        if interesting:
            safe = ' | '.join(interesting[-18:]).replace(os.environ.get('SQL_TEST_PASSWORD', 'NO_SECRET'), '[masked]')
            print('::error title=Android diagnostic::' + safe[-3500:].replace('\n', ' '), flush=True)
    except Exception:
        pass
    raise
