"""Compares two folders of xbf-json/1 files, e.g. the runtime templates (`xbf json <xbf dir>`) against the SDK's
design-time generic.xaml and themeresources.xaml (`xbf json generic.xaml themeresources.xaml`).

Usage: python compare-json.py <runtime-json-dir> <sdk-json-dir>

Resources are matched by key and theme dictionary; styles by key, or by target type for implicit styles. Values are
normalised before comparing: numbers and thickness shorthands ("0,9.5,0,9.5" == "0,9.5"), colour case,
"Transparent" == "#00FFFFFF", time spans ("0:0:0.5" == "00:00:00.5"), xmlns prefixes, enum-word case, and the
argument order inside markup extensions ("{Binding Header, RelativeSource=...}" == "{Binding RelativeSource=...,
Path=Header}"). Prints what is only on one side and what differs, then the count of identical styles."""
import json, os, re, sys
from collections import defaultdict

rt_dir, sdk_dir = sys.argv[1], sys.argv[2]

def load(d, names=None):
    out = []
    for f in sorted(os.listdir(d)):
        if f == 'index.json' or not f.endswith('.json') or (names and f not in names):
            continue
        out.append((f, json.load(open(os.path.join(d, f), encoding='utf-8'))))
    return out

num = re.compile(r'^-?\d+(\.\d+)?$')
def norm(v):
    if not isinstance(v, str):
        return v
    v = v.strip()
    parts = v.split(',')
    if all(num.match(p.strip()) for p in parts):
        vals = [float(p) for p in parts]
        if len(vals) == 4 and vals[0] == vals[2] and vals[1] == vals[3]:
            vals = vals[:2]
        if len(vals) == 2 and vals[0] == vals[1]:
            vals = vals[:1]
        return ','.join(f'{x:g}' for x in vals)
    if re.match(r'^#[0-9a-fA-F]{8}$', v):
        return v.upper()
    m = re.match(r'^(\d+):(\d+):(\d+(\.\d+)?)$', v)
    if m:
        s = int(m[1]) * 3600 + int(m[2]) * 60 + float(m[3])
        return '0' if s == 0 else f't{s:g}'
    if v == 'Transparent':
        return '#00FFFFFF'
    if re.match(r'^[A-Za-z]+$', v):
        return v.lower()
    v = re.sub(r'\b\w+:(?=[A-Z])', '', v)  # drop xmlns prefixes like controls:Pivot, x:Null
    if v.startswith('{') and not v.startswith('{}'):
        return markup(v)
    return v

POSITIONAL = {'Binding': 'Path', 'ThemeResource': 'ResourceKey', 'StaticResource': 'ResourceKey',
              'CustomResource': 'ResourceKey', 'TemplateBinding': 'Property', 'RelativeSource': 'Mode'}

def split_top(s):
    out, depth, cur = [], 0, ''
    for c in s:
        if c == '{': depth += 1
        if c == '}': depth -= 1
        if c == ',' and depth == 0:
            out.append(cur.strip()); cur = ''
        else:
            cur += c
    if cur.strip(): out.append(cur.strip())
    return out

def markup(v):
    """Order-independent form of a markup extension: {Binding Header, RelativeSource=...} == {Binding RelativeSource=..., Path=Header}."""
    m = re.match(r'^\{\s*(\w+)\s*(.*)\}$', v, re.S)
    if not m:
        return v
    name, rest = m[1], m[2]
    args = []
    for i, a in enumerate(split_top(rest)):
        if '=' in a and not a.startswith('{'):
            k, val = a.split('=', 1)
        else:
            k, val = POSITIONAL.get(name, '_'), a
        val = val.strip().strip("'")
        args.append(f'{k.strip()}={markup(val) if val.startswith("{") else val}')
    return '{' + name + ' ' + ', '.join(sorted(args)) + '}'

def strip_prefix(t):
    return t.split(':')[-1] if t else t

# ---------- resources ----------
def resources(files):
    r = {}
    for f, j in files:
        for e in j.get('resources', []):
            r[(e.get('key'), e.get('theme'))] = (f, e)
    return r

def rvalue(e):
    a = {k: norm(v) for k, v in (e.get('attributes') or {}).items()}
    return (strip_prefix(e['type']), norm(e.get('text')), tuple(sorted(a.items())))

rt_all, sdk_all = load(rt_dir), load(sdk_dir)
rt_res, sdk_res = resources(rt_all), resources(sdk_all)
print(f'resources: runtime {len(rt_res)}, sdk {len(sdk_res)}')
only_sdk = sorted(k for k in sdk_res if k not in rt_res)
only_rt = sorted(k for k in rt_res if k not in sdk_res)
diff = sorted(k for k in sdk_res if k in rt_res and rvalue(sdk_res[k][1]) != rvalue(rt_res[k][1]))
print(f'  only in SDK ({len(only_sdk)}):', [f'{k}[{t}]' for k, t in only_sdk][:40])
print(f'  only at runtime ({len(only_rt)}):', [f'{k}[{t}]' for k, t in only_rt][:40])
print(f'  different value ({len(diff)}):')
for k in diff[:60]:
    print(f'    {k[0]}[{k[1]}]: SDK {rvalue(sdk_res[k][1])}  runtime {rvalue(rt_res[k][1])} ({rt_res[k][0]})')

# ---------- styles ----------
def styles(files):
    s = {}
    for f, j in files:
        for st in j.get('styles', []):
            key = st.get('key') or ('implicit:' + strip_prefix(st.get('targetType') or ''))
            s[(key, st.get('theme'))] = (f, st)
    return s

def flat_tree(node, path='', out=None):
    out = [] if out is None else out
    if isinstance(node, list):
        for n in node:
            flat_tree(n, path, out)
        return out
    if not isinstance(node, dict):
        out.append((path, 'text', norm(node)))
        return out
    here = f"{path}/{strip_prefix(node['type'])}" + (f"#{node['name']}" if node.get('name') else '')
    for k, v in sorted((node.get('attributes') or {}).items()):
        out.append((here, k, norm(v)))
    if node.get('text'):
        out.append((here, 'text', norm(node['text'])))
    for p, vals in sorted((node.get('properties') or {}).items()):
        flat_tree(vals, here + '.' + strip_prefix(p.split('.')[-1]), out)
    for i, c in enumerate(node.get('children') or []):
        flat_tree(c, here + f'[{i}]', out)
    return out

def anims(st):
    out = set()
    t = st.get('template') or {}
    for g in t.get('visualStateGroups', []):
        for s in g.get('states', []) + [{'name': f"{x.get('from')}->{x.get('to')}", **x} for x in g.get('transitions', [])]:
            for a in (s.get('storyboard') or {}).get('animations', []):
                kfs = tuple((kf['type'], tuple(sorted((k, norm(v)) for k, v in (kf.get('attributes') or {}).items())),
                             json.dumps(kf.get('easing'), sort_keys=True) if kf.get('easing') else None)
                            for kf in a.get('keyFrames', []))
                attrs = tuple(sorted((k, norm(v)) for k, v in (a.get('attributes') or {}).items()))
                out.add((g.get('name'), s.get('name'), a['type'], a.get('target'), norm(a.get('property')), attrs, kfs))
    return out

rt_st, sdk_st = styles(rt_all), styles(sdk_all)
print(f'\nstyles: runtime {len(rt_st)}, sdk {len(sdk_st)}')
print('  only in SDK:', sorted(k[0] for k in sdk_st if k not in rt_st))
print('  only at runtime:', sorted(k[0] for k in rt_st if k not in sdk_st))
same = 0
for k in sorted(k for k in sdk_st if k in rt_st):
    (sf, s), (rf, r) = sdk_st[k], rt_st[k]
    sset = {(x.get('property'), norm(x.get('value'))) for x in s.get('setters', [])}
    rset = {(x.get('property'), norm(x.get('value'))) for x in r.get('setters', [])}
    st_, rt_ = set(flat_tree((s.get('template') or {}).get('tree'))), set(flat_tree((r.get('template') or {}).get('tree')))
    sa, ra = anims(s), anims(r)
    if sset == rset and st_ == rt_ and sa == ra:
        same += 1
        continue
    print(f'  {k[0]} [{rf}]')
    for x in sorted(sset - rset, key=str): print('     setter SDK only    ', x)
    for x in sorted(rset - sset, key=str): print('     setter runtime only', x)
    for x in sorted(st_ - rt_, key=str)[:12]: print('     tree SDK only      ', x)
    for x in sorted(rt_ - st_, key=str)[:12]: print('     tree runtime only  ', x)
    if len(st_ ^ rt_) > 24: print(f'     ... {len(st_ ^ rt_)} tree differences in all')
    for x in sorted(sa - ra, key=str)[:6]: print('     anim SDK only      ', x[:5], x[5], x[6][:3])
    for x in sorted(ra - sa, key=str)[:6]: print('     anim runtime only  ', x[:5], x[5], x[6][:3])
print(f'  identical after normalisation: {same}')
