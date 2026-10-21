"""배포 묶음 만들기(1.0) — 표준 라이브러리만 사용.

1. 검증 기록(verification/evidence-native.json, evidence-ui.json)의 성공 표식과 해시를 확인합니다.
   → 검증한 바로 그 DLL·템플릿만 묶습니다(해시가 하나라도 다르면 중단).
2. 설치 묶음: 배포/Ahjooeng_v1.0_설치.zip  (설치.bat / 제거.bat / Ahjooeng.bundle)
3. 전체 소스: 배포/Ahjooeng_v1.0_전체소스.zip  (Autocad_auto/ 아래 LegendAuto + Shared + docs, 상대 경로 그대로)
   → 빈 폴더에 압축을 풀어 build.ps1로 실제 빌드되는지 확인합니다(개발 폴더 없이 빌드 가능).
4. 결과: verification/package-verification.txt
실행: python LegendAuto/tools/package_release.py
"""
from pathlib import Path
import hashlib, json, re, shutil, subprocess, sys, tempfile, zipfile

VERSION = '1.0'
legend = Path(__file__).resolve().parents[1]
repo = legend.parent
out = legend / '배포'
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
report = []

def check(ok, label):
    if not ok:
        raise SystemExit('FAIL: ' + label)
    report.append('PASS: ' + label)

def load(name):
    p = legend / 'verification' / name
    check(p.exists(), name + ' 있음')
    return json.loads(p.read_text(encoding='utf-8-sig'))

native, ui = load('evidence-native.json'), load('evidence-ui.json')
check(any(re.match(r'SUCCESS: \d+ native checks', r) for r in native['results']), '네이티브 검증 성공 표식 ' + ' / '.join(native['results']))
check(any(r.startswith('PASS: UNDO') for r in native['results']), 'UNDO 검증 성공 표식')
check(any(re.match(r'SUCCESS: \d+ concurrency checks', r) for r in native['results']), '두 프로세스 동시 분석 검증 성공 표식')
check(any(re.match(r'SUCCESS: \d+ picker checks', r) for r in ui['result']), '화면 검증 성공 표식 ' + ' '.join(ui['result']))
built = Path(native['releaseFolder'])
check(Path(ui['releaseFolder']).resolve() == built.resolve(), '화면 검증과 네이티브 검증이 같은 빌드 폴더(' + str(built) + ')')
for name, digest in native['files'].items():
    if name == 'StormLegend.Verify.dll':
        continue
    check(sha(built / name) == digest, '검증한 파일 그대로: ' + name + ' ' + digest[:12])
for name, digest in ui['files'].items():
    check(sha(built / name) == digest, '화면 검증 파일 그대로: ' + name)
check(any('StormLegend.Core' in l for l in native['loadedInAutoCAD']), 'AutoCAD에서 불러온 Core 위치 기록됨')

# ---------------------------------------------------------------- 설치 묶음
out.mkdir(exist_ok=True)
stage = Path(tempfile.mkdtemp(prefix='ahjooeng-stage-'))
bundle = stage / 'Ahjooeng.bundle'
contents = bundle / 'Contents' / 'LegendAuto'
contents.mkdir(parents=True)
for name in ('StormLegend.dll', 'StormLegend.Core.dll'):
    shutil.copy2(built / name, contents / name)
# legend-template.json·source-entities.json은 보관용 Python 도구(tools/*.py --legacy)만 쓰고 플러그인은 읽지 않으므로 설치본에서 뺍니다.
LEGACY_ONLY = ('legend-template.json', 'source-entities.json')
shutil.copytree(built / 'Template', contents / 'Template', ignore=shutil.ignore_patterns(*LEGACY_ONLY))
shutil.copytree(built / 'Help', contents / 'Help')
check(not any((contents / 'Template' / n).exists() for n in LEGACY_ONLY), '보관용 도구 전용 파일은 설치본에서 제외')
shutil.copytree(repo / 'Help', bundle / 'Contents' / 'Help')  # Ahjooeng 도움말 홈(명령 목록)
check((bundle / 'Contents' / 'Help' / 'index.html').exists() and (bundle / 'Contents' / 'Help' / 'nav.js').exists(), '도움말 홈(명령 목록) 포함')
for page in re.findall(r'page:\s*"([^"]+)"', (repo / 'Help' / 'commands.js').read_text(encoding='utf8')):
    check((bundle / 'Contents' / 'Help' / page).resolve().exists(), '도움말 홈의 명령 설명서 링크 확인: ' + page)
shutil.copy2(legend / 'deploy' / 'PackageContents.xml', bundle / 'PackageContents.xml')
for name in ('install.ps1', 'uninstall.ps1', '설치.bat', '제거.bat'):
    shutil.copy2(legend / 'deploy' / name, stage / name)
# 지금까지 배포한 원본 템플릿 해시(설치할 때 '사용자가 고친 템플릿'과 구분)
known = [l.strip().lower() for l in (legend / 'deploy' / 'shipped-template-hashes.txt').read_text(encoding='utf8').splitlines() if l.strip()]
current = sha(contents / 'Template' / '범례템플릿.dwg')
if current not in known:
    known.append(current)
    (legend / 'deploy' / 'shipped-template-hashes.txt').write_text('\n'.join(known) + '\n', encoding='utf8')  # 배포 이력에 추가
(stage / 'shipped-templates.txt').write_text('\n'.join(known) + '\n', encoding='utf8')
(stage / '사용설명서.html').write_text('<!doctype html><meta charset="utf-8"><meta http-equiv="refresh" content="0; url=Ahjooeng.bundle/Contents/Help/index.html"><a href="Ahjooeng.bundle/Contents/Help/index.html">사용 설명서 열기</a>', encoding='utf8')
(stage / '먼저읽기.txt').write_text(
    'Ahjooeng ' + VERSION + ' (AutoCAD 2020 범례 생성)\r\n\r\n'
    '1. AutoCAD를 닫습니다.\r\n2. 이 폴더의 "설치.bat"을 두 번 클릭합니다.\r\n3. AutoCAD를 켜면 메뉴 막대에 [Ahjooeng]이 생깁니다. 명령줄에 LGD를 입력해도 됩니다.\r\n\r\n'
    '이제 NETLOAD는 하지 않아도 됩니다. 지우려면 "제거.bat"을 두 번 클릭하세요.\r\n자세한 사용 방법: 사용설명서.html\r\n', encoding='utf-8-sig')
for p in contents.rglob('*'):
    if p.is_file() and p.parent.name in ('', 'LegendAuto') and p.suffix == '.dll':
        check(sha(p) == native['files'][p.name], '설치 묶음 DLL = 검증한 DLL: ' + p.name)
# 설치 시험: 이 묶음 그대로 가짜 사용자 폴더에 설치·재설치·실패·재시도·잠금·백업 정리(실제 설치 위치는 건드리지 않음)
install_test = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(legend / 'tests' / 'install-test.ps1'), '-Package', str(stage)],
                              capture_output=True, text=True, encoding='utf-8', errors='replace')
install_lines = [l for l in (install_test.stdout or '').splitlines() if l.startswith(('PASS', 'FAIL', 'SUCCESS'))]
check(install_test.returncode == 0 and any(l.startswith('SUCCESS') for l in install_lines), '설치 시험 ' + (install_lines[-1] if install_lines else '') + ('' if install_test.returncode == 0 else ' :: ' + (install_test.stdout or '')[-1200:] + (install_test.stderr or '')[-400:]))
report.extend('  ' + l for l in install_lines)
installer = out / ('Ahjooeng_v' + VERSION + '_설치.zip')
with zipfile.ZipFile(installer, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for p in sorted(stage.rglob('*')):
        if p.is_file():
            z.write(p, 'Ahjooeng_v' + VERSION + '_설치/' + p.relative_to(stage).as_posix())
with zipfile.ZipFile(installer) as z:
    check(z.testzip() is None, '설치 ZIP CRC')
    for p in stage.rglob('*'):
        if p.is_file():
            check(hashlib.sha256(z.read('Ahjooeng_v' + VERSION + '_설치/' + p.relative_to(stage).as_posix())).hexdigest() == sha(p), '설치 ZIP 항목 ' + p.relative_to(stage).as_posix())

# ---------------------------------------------------------------- 전체 소스(공용 Shared 포함)
skip_dirs = {'bin', 'obj', 'work', '배포', '.vs', '__pycache__', '.claude'}
def wanted(p):
    rel = p.relative_to(repo).parts
    if any(part in skip_dirs for part in rel):
        return False
    return p.suffix.lower() not in ('.bak', '.dwl', '.dwl2', '.zip', '.user')
files = [p for top in ('README.md', 'HANDOFF.md', 'Help', 'docs', 'Shared', 'LegendAuto') for p in ([repo / top] if (repo / top).is_file() else sorted((repo / top).rglob('*'))) if p.is_file() and wanted(p)]
check(any(p.relative_to(repo).as_posix().startswith('Shared/CadUi/') for p in files) and any(p.relative_to(repo).as_posix().startswith('Shared/Ahjooeng/') for p in files), '공용 소스(Shared/CadUi, Shared/Ahjooeng) 포함')
source_zip = out / ('Ahjooeng_v' + VERSION + '_전체소스.zip')
with zipfile.ZipFile(source_zip, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for p in files:
        z.write(p, 'Autocad_auto/' + p.relative_to(repo).as_posix())
with zipfile.ZipFile(source_zip) as z:
    check(z.testzip() is None, '전체 소스 ZIP CRC (' + str(len(files)) + '개 파일)')

# 새 폴더에 풀어 실제 빌드(개발 폴더·이전 빌드 결과 없이)
build_root = Path(tempfile.mkdtemp(prefix='ahjooeng-build-'))
with zipfile.ZipFile(source_zip) as z:
    z.extractall(build_root)
script = build_root / 'Autocad_auto' / 'LegendAuto' / 'build.ps1'
result = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(script), '-OutDir', 'bin\\CleanBuild'], capture_output=True, text=True, encoding='mbcs', errors='replace')
clean = build_root / 'Autocad_auto' / 'LegendAuto' / 'bin' / 'CleanBuild'
check(result.returncode == 0 and (clean / 'StormLegend.dll').exists() and (clean / 'StormLegend.Core.dll').exists() and (clean / 'Template' / '범례템플릿.dwg').exists() and (clean / 'Help' / 'index.html').exists(),
      '새 폴더에 풀어 빌드 성공(StormLegend.dll·Core·Template·Help)' + ('' if result.returncode == 0 else ' :: ' + result.stdout[-800:] + result.stderr[-400:]))
shutil.rmtree(build_root, ignore_errors=True)

# 이전 버전 묶음은 혼동하지 않게 따로 둡니다.
old = out / '배포전_개발묶음'
for p in list(out.glob('LegendAuto_*v[34]*.zip')):
    old.mkdir(exist_ok=True)
    shutil.move(str(p), str(old / p.name))
if old.exists():
    (old / '읽어보기.txt').write_text('첫 배포(1.0) 전에 개발하면서 만든 묶음입니다(배포용 아님, NETLOAD 방식). 배포본은 상위 폴더의 Ahjooeng_v' + VERSION + '_설치.zip입니다.\r\n', encoding='utf-8-sig')

report.append(sha(installer) + '  ' + installer.name)
report.append(sha(source_zip) + '  ' + source_zip.name)
report.append('검증 빌드 폴더: ' + str(built))
(legend / 'verification' / 'package-verification.txt').write_text('\n'.join(report) + '\nSUCCESS: package verified\n', encoding='utf8')
shutil.rmtree(stage, ignore_errors=True)
print('\n'.join(report[-3:]))
print('SUCCESS: package verified')
