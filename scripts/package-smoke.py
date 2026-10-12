"""Exercise the published API with real JWT identities and SQL persistence. Never prints tokens."""
import json,os,urllib.request,urllib.error,uuid,base64
from pathlib import Path
base=os.environ.get('SHIPMENT_API_URL','http://localhost:5001')
results=[]
def request(path,body=None,token=None,expected=200,method=None,headers=None):
    values={'Content-Type':'application/json',**(headers or {})}
    if token:values['Authorization']='Bearer '+token
    raw=body if isinstance(body,bytes) else None if body is None else json.dumps(body).encode()
    req=urllib.request.Request(base+path,data=raw,headers=values,method=method)
    try:
        with urllib.request.urlopen(req,timeout=30) as response:
            if response.status!=expected:raise RuntimeError(f'{path}: expected {expected}, got {response.status}')
            content=response.read();return json.loads(content) if content and response.headers.get_content_type()=='application/json' else content
    except urllib.error.HTTPError as error:
        if error.code==expected:return None
        raise RuntimeError(f'{path}: expected {expected}, got {error.code}') from None

def upload(shipment,token,expected=201):
    boundary='synthetic-'+uuid.uuid4().hex
    pdf=(Path(__file__).resolve().parents[1]/'src/WebAPI/wwwroot/demo-manifest.pdf').read_bytes()
    raw=(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="demo-manifest.pdf"\r\nContent-Type: application/pdf\r\n\r\n'.encode()+pdf+f'\r\n--{boundary}--\r\n'.encode())
    return request(f'/api/shipments/{shipment}/documents',raw,token,expected,headers={'Content-Type':'multipart/form-data; boundary='+boundary})
request('/health');request('/api/shipments',expected=401);results.append('health and authentication')
password=os.environ['DEMO_DISPATCHER_PASSWORD']
token=request('/api/auth/login',{'userName':'demo-dispatcher','password':password})['accessToken']
assert request('/api/shipments',token=token)['totalCount']>=3
body={'senderName':'Synthetic Sender','senderAddress':'Demo A','recipientName':'Synthetic Recipient','recipientAddress':'Demo B','description':'Package smoke '+uuid.uuid4().hex,'weightKg':1.5}
key=uuid.uuid4().hex
created=request('/api/shipments',body,token,201,headers={'Idempotency-Key':key});sid=created['id']
retry=request('/api/shipments',body,token,201,headers={'Idempotency-Key':key});assert retry['id']==sid
request('/api/shipments',{**body,'description':'Different request'},token,409,headers={'Idempotency-Key':key})
request('/api/shipments',body,expected=401,headers={'Idempotency-Key':key});results.append('same-key replay, conflicting-body rejection, no unauthenticated replay')
request(f'/api/shipments/{sid}',token=token) # populate detail cache
uploaded=upload(sid,token);did=uploaded['documentId']
assert request(f'/api/shipments/{sid}',token=token)['documents'][0]['id']==did
assert request(f'/api/documents/{did}/download',token=token).startswith(b'%PDF-');results.append('PDF upload/download and cache invalidation')
request(f'/api/shipments/{sid}/status',{'newStatus':4,'notes':'Invalid direct delivery'},token,422,method='PUT')
for status in [1,2]:request(f'/api/shipments/{sid}/status',{'newStatus':status,'notes':'Synthetic lifecycle'},token,204,method='PUT')
def event(eid,kind,time,reference='visit',expected=201):
 return request(f'/api/shipments/{sid}/events',{'eventId':eid,'referenceId':reference,'type':kind,'occurredAt':f'2026-10-12T{time}:00Z','observedAt':f'2026-10-12T{time}:30Z','source':'synthetic-smoke','detail':kind},token,expected)
event('plan','arrival_plan','08:00');event('in','gate_in','08:20');event('in','gate_in','08:20',expected=200)
event('hold','hold_opened','08:30','customs')
event('out','gate_out','10:20',expected=409)
request(f'/api/shipments/{sid}/status',{'newStatus':3,'notes':'Blocked by hold'},token,422,method='PUT')
event('release','hold_released','09:00','customs');event('out','gate_out','10:20')
for status in [3,4]:request(f'/api/shipments/{sid}/status',{'newStatus':status,'notes':'Synthetic lifecycle'},token,204,method='PUT')
assert request(f'/api/shipments/{sid}',token=token)['statusLabel']=='Delivered';results.append('hold restriction, release, event replay and full status recovery')
registration={'userName':'smoke-'+uuid.uuid4().hex[:12],'email':uuid.uuid4().hex+'@example.invalid','password':password,'customerId':'synthetic-demo','roles':['Administrator']}
customer=request('/api/auth/register',registration,expected=201)['accessToken']
payload=json.loads(base64.urlsafe_b64decode(customer.split('.')[1]+'=='))
assert payload['customer_id']!='synthetic-demo'
assert request('/api/shipments',token=customer)['totalCount']==0
request(f'/api/shipments/{sid}',token=customer,expected=404)
request(f'/api/shipments/{sid}/documents',token=customer,expected=404)
request(f'/api/documents/{did}/download',token=customer,expected=404)
upload(sid,customer,404)
request(f'/api/shipments/{sid}/events',token=customer,expected=404)
request(f'/api/shipments/{sid}/status',{'newStatus':1,'notes':'Forbidden'},customer,403,method='PUT')
request('/api/shipments',body,customer,403,headers={'Idempotency-Key':key})
assert request('/api/operations',token=customer)['shipments']==[]
results.append('real JWT customer isolation, cache boundary, document boundary and no client-selected membership/role escalation')
out=Path(__file__).resolve().parents[1]/'demo/evidence';out.mkdir(parents=True,exist_ok=True)
(out/'api-proof.json').write_text(json.dumps({'passed':True,'synthetic':True,'checks':results},indent=2)+'\n')
print('PASS:', '; '.join(results))
