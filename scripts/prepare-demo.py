"""Restore published demo dependencies; verify the NuGet artifact before use."""
import hashlib,json,subprocess,urllib.request,shutil
from pathlib import Path
root=Path(__file__).resolve().parents[1]
manifest=json.loads((root/'demo/dependencies.json').read_text())
contract=manifest['contracts'];feed=root/'artifacts/feed';feed.mkdir(parents=True,exist_ok=True)
package=feed/f"PortOps.Contracts.{contract['version']}.nupkg"
if not package.exists() or hashlib.sha256(package.read_bytes()).hexdigest()!=contract['sha256']:
    request=urllib.request.Request(contract['url'],headers={'User-Agent':'shipment-desk-dependency-bootstrap'})
    with urllib.request.urlopen(request,timeout=60) as response:content=response.read()
    if hashlib.sha256(content).hexdigest()!=contract['sha256']:raise RuntimeError('Released NuGet artifact checksum mismatch')
    package.write_bytes(content)
kpi=manifest['kpi'];archive=root/'artifacts/npm'/f"phlppgdfry-logistics-kpi-utils-{kpi['version']}.tgz";archive.parent.mkdir(parents=True,exist_ok=True)
if not archive.exists() or hashlib.sha256(archive.read_bytes()).hexdigest()!=kpi['sha256']:
    request=urllib.request.Request(kpi['url'],headers={'User-Agent':'shipment-desk-dependency-bootstrap'})
    with urllib.request.urlopen(request,timeout=60) as response:content=response.read()
    if hashlib.sha256(content).hexdigest()!=kpi['sha256']:raise RuntimeError('Released npm artifact checksum mismatch')
    archive.write_bytes(content)
subprocess.run(['npm','ci','--ignore-scripts'],cwd=root/'demo',check=True)
source=root/'demo/node_modules/@phlppgdfry/logistics-kpi-utils'
target=root/'src/WebAPI/wwwroot/vendor/logistics-kpi-utils';target.mkdir(parents=True,exist_ok=True)
for name in ['index.js','index.d.ts']:shutil.copy(source/'src'/name,target/name)
shutil.copy(source/'LICENSE',target/'LICENSE')
print('PASS: released contracts checksum verified; locked npm package copied into the dashboard.')
