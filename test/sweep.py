import concurrent.futures as cf, glob, os, re, subprocess, sys, json

ROOT = os.path.dirname(os.path.abspath(__file__))
HARNESS = os.path.join(ROOT, '..', 'harness', 'bin', 'harness.exe')
OUT = os.path.join(ROOT, os.environ.get('SWEEP_OUT', 'sweep'))
KEYS = "2500:-5,5000:53,7500:-5,10000:-6,12500:53,15000:-5,17500:-6,20000:53,22500:-5,25000:-4+600,27000:-1+600"
TIME = int(os.environ.get('SWEEP_TIME', '30000'))


def run(jar):
    name = re.sub(r'[^A-Za-z0-9]+', '_', os.path.splitext(os.path.basename(jar))[0])[:60]
    d = os.path.join(OUT, name)
    os.makedirs(d, exist_ok=True)
    try:
        p = subprocess.run([HARNESS, jar, '--time', str(TIME), '--shots', d, '--keys', KEYS],
                           capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=TIME / 1000 + 40)
        out = p.stdout + p.stderr
    except subprocess.TimeoutExpired as e:
        out = (e.stdout or '') if isinstance(e.stdout, str) else ''
        out += '\n[harness timeout]'
    open(os.path.join(d, 'log.txt'), 'w', encoding='utf-8').write(out)
    frames = re.search(r'\[frames\] (\d+) in (\d+) ms, last frame at (-?\d+) ms', out)
    rec = {'jar': os.path.basename(jar), 'dir': name,
           'frames': int(frames.group(1)) if frames else -1,
           'last': int(frames.group(3)) if frames else -1,
           'exit': (re.findall(r'\[exit\] (.*)', out) or [''])[0],
           'uncaught': re.findall(r'Uncaught exception in [^:]*: (.*)', out)[:3],
           'internal': re.findall(r'(?:internal|event error|crashed): (.*)', out)[:3],
           'timeout': '[harness timeout]' in out}
    return rec


if __name__ == '__main__':
    jars = sorted(glob.glob(os.path.join(os.environ['USERPROFILE'], 'Downloads', 'J2ME Roms', '**', '*.jar'), recursive=True))
    start = os.environ.get('SWEEP_FROM')
    if start:
        jars = [j for j in jars if os.path.basename(j).lower() >= start.lower()]
    stop = os.environ.get('SWEEP_TO')
    if stop:
        jars = [j for j in jars if os.path.basename(j).lower() < stop.lower()]
    if len(sys.argv) > 1:
        jars = [j for j in jars if any(a.lower() in os.path.basename(j).lower() for a in sys.argv[1:])]
    os.makedirs(OUT, exist_ok=True)
    results = []
    with cf.ThreadPoolExecutor(int(os.environ.get('SWEEP_PAR', '8'))) as ex:
        for i, r in enumerate(ex.map(run, jars)):
            results.append(r)
            print(i + 1, len(jars), r['jar'], r['frames'], r['last'], r['exit'][:60], (r['uncaught'] or r['internal'] or [''])[0][:100], flush=True)
    json.dump(results, open(os.path.join(OUT, 'results.json'), 'w'), indent=1)
