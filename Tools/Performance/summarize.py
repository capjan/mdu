#!/usr/bin/env python3
"""Summarize raw observations; no percentile/tail claims from small samples."""
import argparse
import gzip
import json
import statistics
from collections import defaultdict
from pathlib import Path


def spread(values):
    values = list(values)
    return f'{statistics.median(values):.1f} ({min(values):.1f}–{max(values):.1f}; n={len(values)})'


def summarize(work):
    normal = json.loads((work / 'baseline.json').read_text())
    for name in ('small', 'docs', 'large'):
        runs = [r for r in normal['runs'] if r['workload'] == name]
        render = [r for r in runs if r['command'] == 'render']
        serve = [r for r in runs if r['command'] == 'serve']
        print(f'\n{name}: render ms {spread(r["wallMs"] for r in render)}; peak MiB {spread(r["peakRssMiB"] for r in render)}')
        print(f'startup ms {spread(r["startupMs"] for r in serve)}; lifetime peak MiB {spread(r["peakRssMiB"] for r in serve)}')
        for kind in ('content', 'config', 'diagram', 'spec'):
            edits = [e for r in serve for e in r['edits'] if e['kind'] == kind]
            print(f'{kind}: complete {spread(e["completeMs"] for e in edits)}; visible {spread(e["visibleMs"] for e in edits)}; rewritten {edits[0]["rewrittenHtml"]}')
        for mode in ('diagnostic', 'memory'):
            data = work / f'{mode}.json'
            if not data.exists():
                continue
            measured = [r for r in json.loads(data.read_text())['runs'] if r['workload'] == name]
            if not measured:
                continue
            print(f'{mode} render ms {spread(r["wallMs"] for r in measured if r["command"] == "render")}')
            profiles = [json.loads(p.read_text().splitlines()[-1])
                        for p in sorted((work / 'logs').glob(f'{mode}-{name}-render-*.jsonl'))]
            print(f'{mode} render allocated MiB {spread(p["allocatedBytes"] / 1048576 for p in profiles)}')
            if mode == 'diagnostic':
                stages = defaultdict(list)
                for profile in profiles:
                    aggregate = defaultdict(lambda: defaultdict(float))
                    for path, stat in profile['stages'].items():
                        for key, value in stat.items():
                            aggregate[path.split('/')[-1]][key] += value
                    for stage, stat in aggregate.items():
                        stages[stage].append(stat)
                ordered = sorted(stages.items(), key=lambda pair: -statistics.median(x['exclusiveMs'] for x in pair[1]))
                for stage, stats in ordered[:30]:
                    print(f'  {stage}: calls {statistics.median(s["calls"] for s in stats):g}; incl {statistics.median(s["inclusiveMs"] for s in stats):.2f}; excl {statistics.median(s["exclusiveMs"] for s in stats):.2f}; alloc {statistics.median(s["allocatedBytes"] for s in stats)/1048576:.2f} MiB')
            for server in (r for r in measured if r['command'] == 'serve'):
                checkpoints = server['memory']
                if checkpoints:
                    print('  post GC MiB:', [(p['edit'], round(p['managedBytes'] / 1048576, 2)) for p in checkpoints])
    complexity = work / 'complexity.json'
    if complexity.exists():
        for name in ('diagram-6', 'diagram-18', 'diagram-36'):
            runs = [r for r in json.loads(complexity.read_text())['runs'] if r['workload'] == name]
            print(name, spread(r['wallMs'] for r in runs), 'ms;', spread(r['peakRssMiB'] for r in runs), 'MiB')


def archive(work, output):
    records = {p.name: json.loads(p.read_text()) for p in work.glob('*.json')}
    records['profiles'] = {p.name: [json.loads(line) for line in p.read_text().splitlines()]
                           for p in sorted((work / 'logs').glob('*.jsonl'))
                           if p.name.startswith(('diagnostic-', 'memory-')) and '-warm' not in p.name}
    with output.open('wb') as stream:
        with gzip.GzipFile(fileobj=stream, mode='wb', mtime=0, filename='') as compressed:
            compressed.write(json.dumps(records, separators=(',', ':')).encode())


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('work', type=Path)
    parser.add_argument('--archive', type=Path)
    args = parser.parse_args()
    summarize(args.work)
    if args.archive:
        archive(args.work, args.archive)
