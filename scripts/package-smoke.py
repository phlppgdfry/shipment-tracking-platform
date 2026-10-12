"""Exercise the real packaged API. Synthetic data only; tokens are never printed."""
import json, os, urllib.request, urllib.error, uuid
base=os.environ.get('SHIPMENT_API_URL','http://localhost:5001')
def request(path, body=None, token=None, expected=200):
    headers={'Content-Type':'application/json'}
    if token: headers['Authorization']='Bearer '+token
    req=urllib.request.Request(base+path, data=None if body is None else json.dumps(body).encode(), headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            assert response.status == expected, response.status
            raw=response.read()
            return json.loads(raw) if raw and response.headers.get_content_type()=='application/json' else None
    except urllib.error.HTTPError as error:
        if error.code == expected: return None
        raise RuntimeError(f'{path} returned HTTP {error.code}') from None
request('/health')
request('/api/shipments', expected=401)
password=os.environ['DEMO_DISPATCHER_PASSWORD']
login=request('/api/auth/login', {'userName':'demo-dispatcher','password':password})
token=login['accessToken']
listed=request('/api/shipments',token=token)
assert listed['totalCount'] >= 1, 'Synthetic seed missing'
created=request('/api/shipments', {'senderName':'Synthetic Sender','senderAddress':'Demo A',
    'recipientName':'Synthetic Recipient','recipientAddress':'Demo B','description':'Package smoke '+str(uuid.uuid4()),'weightKg':1.5},token,201)
detail=request('/api/shipments/'+created['id'],token=token)
assert detail['trackingNumber']==created['trackingNumber']
customer=request('/api/auth/register', {'userName':'smoke-'+uuid.uuid4().hex[:12],
    'email':uuid.uuid4().hex+'@example.invalid','password':password},expected=201)['accessToken']
request('/api/shipments', {'senderName':'Synthetic Sender','senderAddress':'Demo A',
    'recipientName':'Synthetic Recipient','recipientAddress':'Demo B','description':'Should be forbidden','weightKg':1.5},customer,403)
print('PASS: health, unauthorized access, synthetic seed, dispatcher create/read, customer write restriction')
