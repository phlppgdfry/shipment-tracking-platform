"""Start the released local demo with fresh private configuration if needed."""
import os,secrets,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
check=subprocess.run(['docker','info','--format','{{.ServerVersion}}'],capture_output=True,text=True)
if check.returncode:raise SystemExit('Start Docker Desktop first, then run this script again.')
config=root/'.env'
if not config.exists():
    config.write_text('SQL_SA_PASSWORD=DemoSqlA1!'+secrets.token_urlsafe(18)+'\nJWT_SECRET='+secrets.token_hex(32)+'\nDEMO_DISPATCHER_PASSWORD=DemoUserA1!'+secrets.token_urlsafe(18)+'\nSHIPMENT_IMAGE=ghcr.io/phlppgdfry/shipment-tracking-api:0.2.0\n')
    try:config.chmod(0o600)
    except OSError:pass
subprocess.run(['docker','compose','--env-file',str(config),'-p','shipment-desk','-f','docker-compose.package.yml','up','-d','--wait','--wait-timeout','300'],cwd=root,check=True)
print('Open http://localhost:5001. User: demo-dispatcher. Your local password is stored in .env as DEMO_DISPATCHER_PASSWORD.')
print('The .env file is private and excluded from Git and Docker builds. Existing configuration is preserved.')
