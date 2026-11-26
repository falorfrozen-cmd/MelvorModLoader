"""
Action Queue's words for translators (0.14.0): collects every Lang.T / Lang.F / Lang.P and Targets.W call in
Mods/ActionQueue (and the pick list's own words in the Cheat Menu files it builds in), and writes
Mods/ActionQueue/lang/en.json: the English of each text under its id, with notes on what each {place} holds.

    python Dev/lang-extract.py                 write en.json, and say what was added, changed or removed since the last one
    python Dev/lang-extract.py --check         only check: the same id with two different English texts is an error
    python Dev/lang-extract.py --pseudo FILE   also write a test language: every text in Cyrillic look-alike letters,
                                               a third longer, in [brackets] (text still in plain English on screen was
                                               missed; text cut off is too long for its place)
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, '..', 'Mods', 'ActionQueue')
CHEAT = os.path.join(HERE, '..', 'Mods', 'CheatMenu')
OUT = os.path.join(SRC, 'lang', 'en.json')
FILES = ['ActionQueueMod.cs', 'Catalog.cs', 'GameButtons.cs', 'Mastery.cs', 'Offline.cs', 'Planner.cs', 'Queue.cs',
         'QueueWindow.cs', 'Rares.cs', 'Share.cs', 'Targets.cs']
PICKLIST = ['PickList.cs', 'ItemPicks.cs']
CALL = re.compile(r'(Lang\.T|Lang\.F|Lang\.P|Targets\.W|(?<![\w.])W)\(\s*"([^"]+)"\s*,\s*')
PICK = re.compile(r'PickListText\.(T|F)\(\s*"((?:[^"\\]|\\.)*)"')


def read_string(s, i):
    """A C# regular string literal starting at s[i] == '"': (its value, index after it)."""
    assert s[i] == '"', s[i:i + 40]
    j, out = i + 1, []
    while s[j] != '"':
        if s[j] == '\\':
            nxt = s[j + 1]
            out.append({'n': '\n', 't': '\t', '"': '"', '\\': '\\', '0': '\0'}.get(nxt, '\\' + nxt))
            j += 2
            continue
        out.append(s[j])
        j += 1
    return ''.join(out), j + 1


def skip_ws_comma(s, i):
    while s[i] in ' \t\r\n,':
        i += 1
    return i


def args_of(s, i):
    """The ("name", expression) pairs after the texts, up to the call's closing ')'."""
    depth, k, start = 1, i, i
    while depth:
        c = s[k]
        if c == '"':
            _, k = read_string(s, k)
            continue
        if c == '$' and s[k + 1] == '"':
            k += 1
            depth_i, k = 0, k + 1
            while not (s[k] == '"' and depth_i == 0):
                if s[k] == '\\':
                    k += 2
                    continue
                if s[k] == '{':
                    depth_i += 1
                elif s[k] == '}':
                    depth_i -= 1
                k += 1
            k += 1
            continue
        if c == "'":
            k += 1
            while s[k] != "'":
                k += 2 if s[k] == '\\' else 1
            k += 1
            continue
        if c == '(':
            depth += 1
        elif c == ')':
            depth -= 1
        k += 1
    body = s[start:k - 1]
    pairs = []
    for m in re.finditer(r'\(\s*"([A-Za-z_]\w*)"\s*,\s*', body):
        d, e = 1, m.end()
        while d and e < len(body):
            if body[e] == '"':
                e += 1
                while e < len(body) and body[e] != '"':
                    e += 2 if body[e] == '\\' else 1
            elif body[e] == '(':
                d += 1
            elif body[e] == ')':
                d -= 1
            e += 1
        pairs.append((m.group(1), ' '.join(body[m.end():e - 1].split())))
    return pairs


def collect():
    entries, problems = {}, []

    def put(sid, value, where, places):
        if sid in entries and entries[sid]['value'] != value:
            problems.append(f'{sid}: two different English texts ({where} and {entries[sid]["where"][0]})')
            return
        e = entries.setdefault(sid, {'value': value, 'where': [], 'places': {}})
        if where not in e['where']:
            e['where'].append(where)
        for n, x in places:
            e['places'].setdefault(n, x)

    for name in FILES:
        s = open(os.path.join(SRC, name), encoding='utf-8').read()
        for m in CALL.finditer(s):
            kind, sid = m.group(1), m.group(2)
            line = s.count('\n', 0, m.start()) + 1
            i = m.end()
            if kind == 'Lang.P':
                # P(id, count, one, other, args): the count expression first
                depth, k = 0, i
                while not (s[k] == ',' and depth == 0):
                    if s[k] in '([':
                        depth += 1
                    elif s[k] in ')]':
                        depth -= 1
                    k += 1
                count = ' '.join(s[i:k].split())
                k = skip_ws_comma(s, k)
                one, k = read_string(s, k)
                k = skip_ws_comma(s, k)
                other, k = read_string(s, k)
                places = [('count', count)] + args_of(s, k)
                put(sid, {'one': one, 'other': other}, f'{name}:{line}', places)
            else:
                text, k = read_string(s, i)
                places = args_of(s, k) if kind != 'Lang.T' else []
                put(sid, text, f'{name}:{line}', places)
    for name in PICKLIST:
        s = open(os.path.join(CHEAT, name), encoding='utf-8').read()
        for m in PICK.finditer(s):
            text, _ = read_string(s, m.start(2) - 1)
            line = s.count('\n', 0, m.start()) + 1
            put('picklist:' + text, text, f'CheatMenu/{name}:{line}', [])
    return entries, problems


def places_note(e):
    if not e['places']:
        return ''
    return '; '.join(f'{{{n}}} = {x}' for n, x in e['places'].items())


def write(entries):
    data = {
        '_meta': {'name': 'English', 'culture': 'en-GB', 'version': version(), 'translator': ''},
        '_how': ('To translate Action Queue: copy this file in the same lang folder, name it after the language '
                 '(ru.json, de.json...), put the language\'s own name in _meta.name ("Русский") and its culture in '
                 '_meta.culture ("ru-RU"), then translate the texts. Keep every {place} as it is (you may move it '
                 'or leave it out). A text with "one" and "other" depends on a number: give the forms your language '
                 'has (Russian: one, few, many, other). Remove what you have not translated yet, or leave it in '
                 'English: either way it shows in English. Names of items, skills and monsters come from the game. '
                 'Choose the language in the Action Queue window (top right).'),
    }
    for sid in sorted(entries):
        data[sid] = entries[sid]['value']
    data['_notes'] = {sid: places_note(e) for sid, e in sorted(entries.items()) if places_note(e)}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write('\n')


def version():
    m = re.search(r'<Version>([^<]+)</Version>', open(os.path.join(SRC, 'ActionQueue.csproj'), encoding='utf-8').read())
    return m.group(1) if m else ''


PSEUDO = str.maketrans('AaBbCcEeHhKkMmOoPpTtXxYyRrNnDdUuIiGgLl', 'АаВЬСсЕеНһКкМмОоРрТтХхУуЯгИпДдЦцІіБбЛл')


def pseudo_text(t):
    parts = re.split(r'(\{[^{}]*\}|\\n|\n)', t)
    out = ''.join(p if (p.startswith('{') or p in ('\\n', '\n')) else p.translate(PSEUDO) for p in parts)
    return '[' + out + '~' * max(1, len(t) // 3) + ']'


def write_pseudo(entries, path):
    data = {'_meta': {'name': 'Псевдо (test)', 'culture': 'ru-RU', 'plural': 'ru'}}
    for sid in sorted(entries):
        v = entries[sid]['value']
        if isinstance(v, dict):
            data[sid] = {c: pseudo_text(v['one'] if c == 'one' else v['other']).replace('[', f'[{c[:2]}:', 1)
                         for c in ('one', 'few', 'many', 'other')}
        else:
            data[sid] = pseudo_text(v)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def main():
    entries, problems = collect()
    for p in problems:
        print('ERROR', p)
    if '--check' in sys.argv:
        print(len(entries), 'texts;', len(problems), 'problems')
        sys.exit(1 if problems else 0)
    before = {}
    if os.path.exists(OUT):
        try:
            before = {k: v for k, v in json.load(open(OUT, encoding='utf-8')).items() if not k.startswith('_')}
        except ValueError:
            before = {}
    write(entries)
    now = {k: e['value'] for k, e in entries.items()}
    added = sorted(k for k in now if k not in before)
    changed = sorted(k for k in now if k in before and before[k] != now[k])
    removed = sorted(k for k in before if k not in now)
    print(f'{len(now)} texts written to {os.path.normpath(OUT)}')
    if before:
        for title, ids in (('added', added), ('changed', changed), ('removed', removed)):
            print(f'{title}: {len(ids)}' + ''.join(f'\n  {i}' for i in ids[:200]))
    if '--pseudo' in sys.argv:
        path = sys.argv[sys.argv.index('--pseudo') + 1]
        write_pseudo(entries, path)
        print('pseudo language written to', path)
    sys.exit(1 if problems else 0)


main()
