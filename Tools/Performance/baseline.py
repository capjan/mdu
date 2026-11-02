#!/usr/bin/env python3
"""Dependency-free macOS/Linux investigation harness for issue #188."""
import argparse
import hashlib
import json
import os
import platform
import queue
import re
import shutil
import signal
import socket
import subprocess
import threading
import time
import urllib.error
import urllib.request
from pathlib import Path


HERE = Path(__file__).resolve().parent
MIB = 1024 ** 2


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)


def api_spec():
    return ('openapi: 3.0.3\ninfo:\n  title: Baseline API\n  version: 1.0.0\npaths:\n' +
            ''.join(f'''  /items/{i}:
    get:
      summary: Baseline operation {i}
      responses:
        '200':
          description: Success
          content:
            application/json:
              schema:
                type: object
                properties:
                  id: {{ type: integer }}
                  name: {{ type: string }}
''' for i in range(8)))


def diagram(nodes):
    # Fixed three-container topology, with cross-container and skip links.
    lines = ['@startuml', 'left to right direction']
    for group in range(3):
        lines.append(f'package "Group {group}" {{')
        lines += [f'  component "Service {i}" as N{i}' for i in range(nodes) if i % 3 == group]
        lines.append('}')
    lines += [f'N{i} --> N{i + 1} : Load' for i in range(nodes - 1)]
    lines += [f'N{i} --> N{i + 3}' for i in range(0, nodes - 3, 3)]
    return '\n'.join(lines + ['@enduml'])


def fixtures(root):
    write(root / 'api.yaml', api_spec())
    write(root / 'measure.md', '''# Measurement fixture

## Stable anchor

baseline-search-sentinel :material-home: :simple-github:

[Home](README.md) · [Architecture](diagram.md)

[Consent video](https://youtu.be/hyeCMRQhVOI)

```openapi
./api.yaml
```

```csharp
public sealed record Item(int Id, string Name);
```
''')
    write(root / 'diagram.md', '# Diagram fixture\n\n```plantuml\n' + diagram(12) + '\n```\n')
    with (root / 'README.md').open('a') as stream:
        stream.write('\n\n[Measurement fixture](measure.md)\n\nbaseline-content-sentinel\n')


def generate(repo, work):
    corpora = work / 'corpora'
    if corpora.exists():
        shutil.rmtree(corpora)
    small = corpora / 'small'
    shutil.copytree(repo / 'Docs/Architecture', small)
    write(small / 'mdu.yml', 'site_name: Baseline\n')
    fixtures(small)
    docs = corpora / 'docs'
    shutil.copytree(repo / 'Docs', docs)
    # PDF generation is explicitly outside this investigation. Keep its detection cost.
    for path in docs.rglob('*.md'):
        path.write_text(path.read_text().replace('mdu:pdf', 'https://example.invalid/pdf-disabled'))
    fixtures(docs)
    large = corpora / 'large'
    write(large / 'mdu.yml', 'site_name: Baseline\n')
    write(large / 'README.md', '# Mixed synthetic documentation\n\n' +
          '\n'.join(f'[Section {i}](section-{i}/README.md)' for i in range(10)))
    for group in range(10):
        folder = large / f'section-{group}'
        write(folder / 'README.md', f'# Section {group}\n')
        write(folder / 'api.yaml', api_spec())
        for i in range(49):
            kind = i % 5
            body = [
                '## Reference\n\n' + ('Portable documentation with a [home link](../README.md).\n\n' * 15),
                '```plantuml\n' + diagram(12) + '\n```',
                '```openapi\n./api.yaml\n```',
                '```csharp\n' + '\n'.join(f'public sealed record Item{n}(int Id, string Name);' for n in range(80)) + '\n```',
                '## Operations\n\n:material-home: :fontawesome-solid-book: :simple-github: :octicons-mark-github-16:\n\n' + ('| Key | Value |\n| --- | --- |\n| a | b |\n\n' * 8),
            ][kind]
            write(folder / f'page-{i:02}.md', f'# Topic {group}-{i}\n\n{body}\n')
    fixtures(large)
    for size in (6, 18, 36):
        folder = corpora / f'diagram-{size}'
        write(folder / 'README.md', '# Component complexity\n\n```plantuml\n' + diagram(size) + '\n```\n')
    manifest = {}
    for root in sorted(corpora.iterdir()):
        files = sorted(p for p in root.rglob('*') if p.is_file())
        manifest[root.name] = dict(pages=len(list(root.rglob('*.md'))), bytes=sum(p.stat().st_size for p in files),
                                   sha256=hashlib.sha256(b''.join(str(p.relative_to(root)).encode() + b'\0' + p.read_bytes() for p in files)).hexdigest())
    write(work / 'corpora.json', json.dumps(manifest, indent=2) + '\n')
    return manifest


def fetch(url):
    try:
        with urllib.request.urlopen(url, timeout=2) as response:
            return response.read().decode('utf-8-sig')
    except (urllib.error.URLError, TimeoutError, ConnectionError):
        return ''


class Process:
    def __init__(self, command, log, env):
        self.lines = queue.Queue()
        self.log = log
        self.started = time.perf_counter()
        self.proc = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                     env=env, start_new_session=True)
        def read():
            with log.open('wb') as stream:
                for line in self.proc.stdout:
                    stream.write(line)
                    stream.flush()
                    self.lines.put((time.perf_counter(), line.decode(errors='replace').strip()))
        self.reader = threading.Thread(target=read, daemon=True)
        self.reader.start()

    def finish(self, stop=False):
        if stop:
            os.killpg(self.proc.pid, signal.SIGINT)
        _, status, usage = os.wait4(self.proc.pid, 0)
        self.proc.returncode = os.waitstatus_to_exitcode(status)
        self.reader.join(timeout=5)
        if self.proc.returncode:
            raise RuntimeError(f'Command failed ({self.proc.returncode}): {self.log}\n{self.log.read_text()[-3000:]}')
        return dict(wallMs=(time.perf_counter() - self.started) * 1000,
                    peakRssMiB=usage.ru_maxrss / (MIB if platform.system() == 'Darwin' else 1024),
                    cpuMs=(usage.ru_utime + usage.ru_stime) * 1000)

    def wait_line(self, prefix, timeout=180):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            timestamp, line = self.lines.get(timeout=max(0.01, deadline - time.monotonic()))
            if line.startswith(prefix):
                return timestamp
        raise TimeoutError(prefix)


def validate(out):
    html = (out / 'measure.html').read_text(encoding='utf-8-sig')
    diagram_html = (out / 'diagram.html').read_text(encoding='utf-8-sig')
    index = json.loads((out / 'search-index.json').read_text(encoding='utf-8-sig'))
    assert 'id="stable-anchor"' in html
    assert 'href="index.html"' in html and 'href="diagram.html"' in html
    assert 'sidebar' in html and 'breadcrumb' in html
    assert '<svg' in diagram_html and 'Service 0' in diagram_html
    assert 'openapi' in html and '/items/0' in html
    assert 'class="keyword"' in html and '<svg' in html
    assert 'data-embed-url=' in html and 'data-media-consent=' in html
    assert not re.search(r'<iframe\b', html), 'provider must require consent'
    matches = [entry for entry in index if 'baseline-search-sentinel' in entry.get('text', '')]
    assert len(matches) == 1 and matches[0]['url'] == 'measure.html#stable-anchor'
    assert not list(out.rglob('*.pdf')), 'PDF workload must be disabled'
    return dict(htmlPages=len(list(out.rglob('*.html'))), searchEntries=len(index),
                outputBytes=sum(p.stat().st_size for p in out.rglob('*') if p.is_file()))


def render(dll, root, out, log, env):
    if out.exists():
        shutil.rmtree(out)
    process = Process(['dotnet', str(dll), 'render', str(root), str(out)], log, env)
    return process.finish()


def gc_checkpoint(probe):
    before = len(probe.read_text().splitlines())
    probe.with_suffix(probe.suffix + '.collect').touch()
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        records = probe.read_text().splitlines()
        if len(records) > before:
            for line in records[before:]:
                record = json.loads(line)
                if record['reason'] == 'idle-post-gc':
                    return record
        time.sleep(0.02)
    raise TimeoutError('idle diagnostic collection')


def serve(dll, root, out, log, env, rounds, probe=None, memory=False):
    if out.exists():
        shutil.rmtree(out)
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = sock.getsockname()[1]
    url = f'http://127.0.0.1:{port}/'
    sources = {p: p.read_text() for p in [root / 'README.md', root / 'mdu.yml', root / 'api.yaml', root / 'diagram.md']}
    process = Process(['dotnet', str(dll), 'serve', str(root), str(out), '--port', str(port), '--no-browser'], log, env)
    events = queue.Queue()
    stream = None
    observations, checkpoints = [], []
    try:
        process.wait_line('Serving ')
        assert 'baseline-content-sentinel' in fetch(url)
        startup = (time.perf_counter() - process.started) * 1000
        validation = validate(out)
        stream = urllib.request.urlopen(url + '__mdu/events', timeout=180)
        def read_events():
            try:
                for line in stream:
                    if line.startswith(b'data: '):
                        events.put((time.perf_counter(), json.loads(line[6:])))
            except (OSError, ValueError):
                pass
        threading.Thread(target=read_events, daemon=True).start()
        events.get(timeout=10)  # initial revision; no edit yet
        if probe and memory:
            checkpoints.append(dict(edit=0, **gc_checkpoint(probe)))
        # No restoration between saves: each save is one observed, unique mutation.
        kinds = ['content', 'config', 'diagram', 'spec'] * rounds
        if memory:
            kinds = ['content'] * 60 + ['config', 'diagram', 'spec'] * 5
        for number, kind in enumerate(kinds, 1):
            marker = f'perfchange{number:04}{kind}'
            if kind == 'content':
                path, target = root / 'README.md', 'index.html'
                text = sources[path].replace('baseline-content-sentinel', marker)
            elif kind == 'config':
                path, target = root / 'mdu.yml', 'measure.html'
                text = re.sub(r'^site_name:.*$', f'site_name: {marker}', sources[path], flags=re.M)
            elif kind == 'diagram':
                path, target = root / 'diagram.md', 'diagram.html'
                text = sources[path].replace('Load', marker)
            else:
                path, target = root / 'api.yaml', 'measure.html'
                text = sources[path].replace('Baseline operation 0', marker)
            before = {str(p.relative_to(out)): p.stat().st_mtime_ns for p in out.rglob('*.html')}
            start = time.perf_counter()
            path.write_text(text)
            deadline = time.monotonic() + 180
            while marker not in fetch(url + target):
                if time.monotonic() > deadline:
                    raise TimeoutError(f'{kind}: changed content never served: {log}')
                time.sleep(0.01)
            visible = time.perf_counter()
            completed, event = events.get(timeout=180)
            assert event['reloadAll'] == (kind in ('config', 'spec')), (kind, event)
            if not event['reloadAll']:
                assert '/' + target in event['paths'], (kind, event)
            assert marker in fetch(url + target)
            after = {str(p.relative_to(out)): p.stat().st_mtime_ns for p in out.rglob('*.html')}
            updated = sorted(key for key, value in after.items() if before.get(key) != value)
            expected = {'content': 1, 'diagram': 2}.get(kind, validation['htmlPages'])
            assert len(updated) == expected, (kind, updated, expected)
            index = (out / 'search-index.json').read_text(encoding='utf-8-sig')
            if kind in ('content', 'spec'):
                assert marker in index
            observations.append(dict(edit=number, kind=kind, visibleMs=(visible - start) * 1000,
                                     completeMs=(completed - start) * 1000, rewrittenHtml=len(updated)))
            if probe and memory and (number % 10 == 0 or number == len(kinds)):
                checkpoints.append(dict(edit=number, **gc_checkpoint(probe)))
            time.sleep(0.1)  # quiet interval outside timing; do not coalesce independent saves
        validation = validate(out)
    finally:
        usage = process.finish(stop=True)
        if stream:
            stream.close()
        for path, text in sources.items():
            path.write_text(text)
    return dict(startupMs=startup, edits=observations, memory=checkpoints, validation=validation, **usage)


def environment(repo):
    def command(*args):
        return subprocess.check_output(args, text=True).strip()
    return dict(commit=command('git', '-C', str(repo), 'rev-parse', 'HEAD'),
                platform=platform.platform(), machine=platform.machine(),
                dotnet=command('dotnet', '--info'), python=platform.python_version(),
                runtimeSettings={k: v for k, v in os.environ.items() if k.startswith(('DOTNET_', 'COMPlus_'))},
                invocation=os.sys.argv, utc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=HERE.parent.parent)
    parser.add_argument('--work', type=Path, default=Path('/tmp/mdu-performance-188'))
    parser.add_argument('--mode', choices=['generate', 'baseline', 'diagnostic', 'memory', 'complexity', 'compare', 'startup'], default='baseline')
    parser.add_argument('--samples', type=int, default=7)
    parser.add_argument('--servers', type=int, default=3)
    parser.add_argument('--rounds', type=int, default=3)
    parser.add_argument('--workloads', nargs='+', default=['small', 'docs', 'large'])
    args = parser.parse_args()
    args.repo = args.repo.resolve()
    args.work = args.work.resolve()
    args.work.mkdir(parents=True, exist_ok=True)
    logs = args.work / 'logs'
    logs.mkdir(exist_ok=True)
    if args.mode == 'generate':
        print(json.dumps(generate(args.repo, args.work), indent=2))
        return
    assert (args.work / 'corpora.json').exists(), 'Run --mode generate first'
    env = dict(os.environ)
    dll = args.repo / 'Source/Mdu/bin/Release/net10.0/mdu.dll'
    if args.mode == 'startup':
        runs = []
        for sample in range(args.samples + 1):
            measurement = Process(['dotnet', str(dll), '--help'], logs / f'startup-{sample}.log', env).finish()
            if sample:
                runs.append(measurement)
        write(args.work / 'startup.json', json.dumps(dict(environment=environment(args.repo), runs=runs), indent=2) + '\n')
        print(runs)
        return
    if args.mode == 'compare':
        diagnostic = args.work / 'diagnostic-source/Source/Mdu/bin/Release/net10.0/mdu.dll'
        comparisons = {}
        for name in args.workloads:
            root, out = args.work / 'corpora' / name, args.work / 'output' / name
            manifests = []
            for label, executable in [('normal', dll), ('diagnostic', diagnostic)]:
                run_env = dict(env)
                if label == 'diagnostic':
                    run_env['MDU_PERF_OUTPUT'] = str(logs / f'{name}-compare.jsonl')
                render(executable, root, out, logs / f'compare-{name}-{label}.log', run_env)
                validate(out)
                manifests.append({str(p.relative_to(out)): hashlib.sha256(p.read_bytes()).hexdigest()
                                  for p in out.rglob('*') if p.is_file()})
            assert manifests[0] == manifests[1], f'Instrumented output differs: {name}'
            comparisons[name] = dict(files=len(manifests[0]), identical=True,
                                     manifestSha256=hashlib.sha256(json.dumps(manifests[0], sort_keys=True).encode()).hexdigest())
        write(args.work / 'compare.json', json.dumps(comparisons, indent=2) + '\n')
        print(json.dumps(comparisons, indent=2))
        return
    diagnostic = args.mode in ('diagnostic', 'memory')
    if diagnostic:
        import instrument
        dll = instrument.build(args.repo, args.work, light=args.mode == 'memory')
    results = dict(environment=environment(args.repo), corpora=json.loads((args.work / 'corpora.json').read_text()), runs=[])
    names = [f'diagram-{n}' for n in (6, 18, 36)] if args.mode == 'complexity' else args.workloads
    result_path = args.work / f'{args.mode}.json'
    for name in names:
        root, out = args.work / 'corpora' / name, args.work / 'output' / name
        # One unreported process warms filesystem/runtime image caches, not the measured process's JIT.
        warm_env = {k: v for k, v in env.items() if k != 'MDU_PERF_OUTPUT'}
        if diagnostic:
            warm_env['MDU_PERF_OUTPUT'] = str(logs / f'{args.mode}-{name}-warm.jsonl')
        render(dll, root, out, logs / f'{args.mode}-{name}-warm.log', warm_env)
        for sample in range(args.samples):
            run_env = dict(env)
            probe = logs / f'{args.mode}-{name}-render-{sample}.jsonl'
            if diagnostic:
                probe.unlink(missing_ok=True)
                run_env['MDU_PERF_OUTPUT'] = str(probe)
            measurement = render(dll, root, out, logs / f'{args.mode}-{name}-render-{sample}.log', run_env)
            validation = validate(out) if not name.startswith('diagram-') else dict(svg='<svg' in (out / 'index.html').read_text())
            results['runs'].append(dict(workload=name, command='render', sample=sample, validation=validation, **measurement))
            write(result_path, json.dumps(results, indent=2) + '\n')
            print(name, 'render', sample, round(measurement['wallMs']), 'ms', flush=True)
        if args.mode == 'complexity':
            continue
        for sample in range(1 if diagnostic else args.servers):
            run_env = dict(env)
            probe = None
            if diagnostic:
                probe = logs / f'{args.mode}-{name}-serve-{sample}.jsonl'
                probe.unlink(missing_ok=True)
                run_env['MDU_PERF_OUTPUT'] = str(probe)
            measurement = serve(dll, root, out, logs / f'{args.mode}-{name}-serve-{sample}.log', run_env,
                                args.rounds, probe, memory=args.mode == 'memory')
            results['runs'].append(dict(workload=name, command='serve', sample=sample, **measurement))
            write(result_path, json.dumps(results, indent=2) + '\n')
            print(name, 'serve', sample, round(measurement['startupMs']), 'ms', flush=True)


if __name__ == '__main__':
    main()
