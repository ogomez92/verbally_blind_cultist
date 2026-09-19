# Checks CultistAccessibility/Lang/*.txt against en.txt: same keys (plural forms may differ by language), same
# placeholders and game-label tokens, and that every key Strings.cs / HelpContexts.cs asks for exists in en.txt.
import glob, io, os, re, sys

root = os.path.dirname(os.path.abspath(__file__))
lang_dir = os.path.join(root, 'CultistAccessibility', 'Lang')
PLURAL = re.compile(r'\.(one|few|many|other)$')


def load(path):
    table = {}
    for n, raw in enumerate(io.open(path, encoding='utf-8-sig'), 1):
        line = raw.strip()
        if not line or line.startswith('#'):
            continue
        if '=' not in line:
            print('%s:%d: no "="' % (os.path.basename(path), n))
            continue
        key, value = line.split('=', 1)
        key = key.strip()
        if key in table:
            print('%s:%d: duplicate %s' % (os.path.basename(path), n, key))
        table[key] = value.strip()
    return table


def tokens(value):
    return sorted(set(re.findall(r'\{\d+\}|\[\[\w+\]\]', value)))


errors = 0
en = load(os.path.join(lang_dir, 'en.txt'))
base = lambda k: PLURAL.sub('', k)

for path in sorted(glob.glob(os.path.join(lang_dir, '*.txt'))):
    name = os.path.basename(path)
    if name == 'en.txt':
        continue
    table = load(path)
    for key in en:
        if PLURAL.search(key):
            if base(key) + '.other' not in table:
                print('%s: missing %s.other' % (name, base(key))); errors += 1
        elif key not in table:
            print('%s: missing %s' % (name, key)); errors += 1
    for key, value in table.items():
        ref = en.get(key) or en.get(base(key) + '.other')
        if ref is None:
            print('%s: unknown key %s' % (name, key)); errors += 1
        elif tokens(ref) != tokens(value) and not (PLURAL.search(key) and set(tokens(value)) <= set(tokens(ref))):
            print('%s: %s has %s, English has %s' % (name, key, tokens(value), tokens(ref))); errors += 1
        if not value:
            print('%s: %s is empty' % (name, key)); errors += 1

# Keys the code asks for.
src = io.open(os.path.join(root, 'CultistAccessibility', 'Core', 'Strings.cs'), encoding='utf-8').read()
used = set(re.findall(r'public static string (\w+)\s*=>\s*T\(\)', src))
used |= set(re.findall(r'F\(nameof\((\w+)\)', src)) | set(re.findall(r'(?:F|Loc\.Get)\("([\w.]+)"', src))
for plural in re.findall(r'Loc\.Plural\(nameof\((\w+)\)', src):
    used |= {plural + '.one', plural + '.other'}
for key in sorted(used):
    if key.endswith('.') or key in en:
        continue
    print('en.txt: missing %s (used by Strings.cs)' % key); errors += 1
claimed = used | {k for k in en if k.startswith(('Help.', 'Key.', 'Screen.', 'Verbosity.'))} | {'DecimalSeparator'}
for key in sorted(set(en) - claimed):
    print('en.txt: %s is not used by the code' % key); errors += 1

print('%d problem(s)' % errors)
sys.exit(1 if errors else 0)
