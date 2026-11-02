"""Read-only declaration audit; does not restore or execute project/package code."""
import gzip, concurrent.futures, datetime, hashlib, json, pathlib, re, urllib.request, xml.etree.ElementTree as ET
ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / 'audit'
def get(url):
    with urllib.request.urlopen(url, timeout=40) as r:
        data=r.read()
        if data[:2] == b"\x1f\x8b": data=gzip.decompress(data)
        return json.loads(data)
def ver(v):
    if not re.fullmatch(r'\d+(\.\d+){0,3}',v): raise ValueError('Non-stable version: '+v)
    a=tuple(map(int,v.split('.'))); return a+(0,)*(4-len(a))
def contains(r,v):
    if r[0] not in '[(' or r[-1] not in '])': raise ValueError(r)
    lo,hi=r[1:-1].split(','); n=ver(v)
    return (not lo or n>ver(lo.strip()) or (r[0]=='[' and n==ver(lo.strip()))) and (not hi or n<ver(hi.strip()) or (r[-1]==']' and n==ver(hi.strip())))
rows=[]
for p in (ROOT/'upstream').rglob('*'):
    if p.name not in ('packages.config',) and p.suffix not in ('.csproj','.props'): continue
    for el in ET.parse(p).iter():
        tag=el.tag.split('}')[-1]
        if tag=='package': name,v=el.get('id'),el.get('version')
        elif tag=='PackageReference': name,v=el.get('Include'),el.get('Version')
        else: continue
        if name and v: rows.append(dict(package=name,version=v,path=str(p.relative_to(ROOT))))
index=get('https://api.nuget.org/v3/vulnerabilities/index.json')
feeds=[]; vulns={}
for entry in index:
    data=get(entry['@id']); feeds.append(entry)
    for k,vs in data.items(): vulns.setdefault(k,[]).extend(vs)
ids=sorted({r['package'].lower() for r in rows})
def latest(k):
    data=get('https://api.nuget.org/v3-flatcontainer/'+k+'/index.json')
    return k,max((v for v in data['versions'] if re.fullmatch(r'\d+(\.\d+){0,3}',v)),key=ver)
with concurrent.futures.ThreadPoolExecutor(max_workers=10) as ex: newest=dict(ex.map(latest,ids))
for r in rows:
    r['latest_stable']=newest[r['package'].lower()]
    r['advisories']=[v for v in vulns.get(r['package'].lower(),[]) if contains(v['versions'],r['version'])]
result=dict(checked_at=datetime.datetime.now(datetime.timezone.utc).isoformat(),scope='Declared dependencies only, not a restored transitive graph. Includes nested submodule projects not used by parent solution.',feeds=feeds,dependencies=rows)
(OUT/'dependencies.json').write_text(json.dumps(result,indent=2)+'\n')
print('Declarations:',len(rows),'unique IDs:',len(ids))
seen=set()
for r in rows:
    for a in r['advisories']:
        k=(r['package'],r['version'],a['url'])
        if k not in seen: print(*k,'severity='+str(a['severity']),'range='+a['versions']); seen.add(k)
print('Saved audit/dependencies.json')
