"""Generate lightweight display geometry from the ORIGINAL DXF (no invented symbols).
Run from project root: python tools/build_preview_catalog.py
The preview draws DXF geometry. External SHX glyphs are not embedded and are not rendered.
"""
import sys
if '--legacy' not in sys.argv:
    raise SystemExit('이 스크립트는 보관용입니다. tools/rebuild-preview.ps1을 사용하세요. 구형 출력이 필요한 경우에만 --legacy를 지정하세요.')
from pathlib import Path
import json, math, re, colorsys, hashlib

root=Path(__file__).resolve().parents[1]
source=root/'Template/범례템플릿.dxf'
manifest=json.loads((root/'Template/legend-template.json').read_text(encoding='utf8'))
lines=source.read_text(encoding='utf-8-sig').splitlines()
assert len(lines)%2==0
pairs=[(int(lines[i].strip()),lines[i+1].strip()) for i in range(0,len(lines),2)]
records=[]; section=''; rec=None
for c,v in pairs:
    if c==0:
        if rec is not None: records.append(rec)
        rec={'type':v,'pairs':[],'section':section}
    rec['pairs'].append((c,v))
    if rec['type']=='SECTION' and c==2: section=v
    rec['section']=section
if rec:records.append(rec)
def get(e,c,default=''): return next((v for k,v in e['pairs'] if k==c),default)
def num(e,c,default=0):return float(get(e,c,default))
entities={get(e,5):e for e in records if e['section']=='ENTITIES' and get(e,5)}
layers={get(e,2):{'color':int(get(e,62,'7')),'line':get(e,6,'Continuous')} for e in records if e['type']=='LAYER'}
blocks={}; bases={}; current=None
for e in records:
    if e['section']!='BLOCKS':continue
    if e['type']=='BLOCK': current=get(e,2); blocks[current]=[]; bases[current]=(num(e,10),num(e,20))
    elif e['type']=='ENDBLK':current=None
    elif current:blocks[current].append(e)

def color(aci):
    aci=abs(aci)
    basic={0:'#243447',1:'#D63737',2:'#A08000',3:'#25833D',4:'#008E99',5:'#345BD2',6:'#A531AB',7:'#253347',8:'#667080',9:'#8690A0'}
    if aci in basic:return basic[aci]
    if 10<=aci<=249:
        group,shade=divmod(aci-10,10)
        values=[1,1,.65,.65,.5,.5,.3,.3,.15,.15]
        rgb=colorsys.hsv_to_rgb(group/24,1 if shade%2==0 else .5,values[shade])
        return '#'+''.join(f'{round(x*255):02X}' for x in rgb)
    return '#667080'
def point(x,y):return {'x':round(x,8),'y':round(y,8)}
def clean_text(text):
    text=re.sub(r'\\U\+([0-9A-Fa-f]{4})',lambda m:chr(int(m[1],16)),text)
    return text.replace('%%c','Ø').replace('%%C','Ø').replace('%%d','°').replace('%%p','±')
identity=(1.,0.,0.,1.,0.,0.)
def xy(t,x,y):a,b,c,d,tx,ty=t;return (a*x+c*y+tx,b*x+d*y+ty)
def multiply(t,u):
    a,b,c,d,x,y=t; e,f,g,h,v,w=u
    return (a*e+c*f,b*e+d*f,a*g+c*h,b*g+d*h,a*v+c*w+x,b*v+d*w+y)
def verts(e):
    out=[]
    for c,v in e['pairs']:
        if c==10:out.append([float(v),0.,0.])
        elif c==20 and out:out[-1][1]=float(v)
        elif c==42 and out:out[-1][2]=float(v)
    return out
def bulge_samples(a,b):
    x,y,q=a; X,Y,_=b
    if abs(q)<1e-10:return [(x,y)]
    chord=math.hypot(X-x,Y-y)
    cx=(x+X)/2-(Y-y)*(1-q*q)/(4*q)
    cy=(y+Y)/2+(X-x)*(1-q*q)/(4*q)
    radius=chord*(1+q*q)/(4*abs(q)); angle=math.atan2(y-cy,x-cx); sweep=4*math.atan(q)
    n=max(8,int(abs(sweep)*24))
    return [(cx+radius*math.cos(angle+sweep*i/n),cy+radius*math.sin(angle+sweep*i/n)) for i in range(n)]

def flatten(e,t=identity,parent_layer='0',parent_aci=7,depth=0):
    if depth>12:raise ValueError('Recursive source block')
    typ=e['type']; layer=get(e,8,'0'); layer=parent_layer if layer=='0' else layer
    ldef=layers.get(layer,{'color':7,'line':'Continuous'}); aci=int(get(e,62,'256'))
    aci=ldef['color'] if aci==256 else (parent_aci if aci==0 else aci)
    line=get(e,6,'BYLAYER'); line=ldef['line'] if line.upper()=='BYLAYER' else line
    base={'color':color(aci),'dash':line.upper() not in ('CONTINUOUS','BYBLOCK'),'kind':typ.lower()}
    def poly(points,closed=False,fill=''):
        return dict(base,kind='polyline',points=[point(*xy(t,x,y)) for x,y in points],closed=closed,fill=fill)
    if typ=='INSERT':
        name=get(e,2); bx,by=bases[name]; r=math.radians(num(e,50)); sx=num(e,41,1);sy=num(e,42,1)
        a,b,c,d=math.cos(r)*sx,math.sin(r)*sx,-math.sin(r)*sy,math.cos(r)*sy
        u=(a,b,c,d,num(e,10)-a*bx-c*by,num(e,20)-b*bx-d*by)
        return [p for child in blocks[name] for p in flatten(child,multiply(t,u),layer,aci,depth+1)]
    if typ=='LINE':return [poly([(num(e,10),num(e,20)),(num(e,11),num(e,21))])]
    if typ=='LWPOLYLINE':
        vs=verts(e);closed=bool(int(get(e,70,'0'))&1); points=[]
        for i in range(len(vs)-(0 if closed else 1)):points.extend(bulge_samples(vs[i],vs[(i+1)%len(vs)]))
        if not closed:points.append(tuple(vs[-1][:2]))
        return [poly(points,closed)]
    if typ in ('CIRCLE','ARC'):
        cx,cy,r=num(e,10),num(e,20),num(e,40); a=math.radians(num(e,50)); b=math.radians(num(e,51,360))
        sweep=(b-a)%(2*math.pi) if typ=='ARC' else 2*math.pi
        n=max(24,int(sweep*30))
        return [poly([(cx+r*math.cos(a+sweep*i/n),cy+r*math.sin(a+sweep*i/n)) for i in range(n+1)],typ=='CIRCLE')]
    if typ=='ELLIPSE':
        cx,cy=num(e,10),num(e,20); ax,ay=num(e,11),num(e,21); ratio=num(e,40)
        a,b=num(e,41),num(e,42,2*math.pi); sweep=(b-a)%(2*math.pi) or 2*math.pi
        return [poly([(cx+ax*math.cos(a+sweep*i/160)-ay*ratio*math.sin(a+sweep*i/160),cy+ay*math.cos(a+sweep*i/160)+ax*ratio*math.sin(a+sweep*i/160)) for i in range(161)],abs(sweep-2*math.pi)<1e-6)]
    if typ=='SOLID':
        return [poly([(num(e,c),num(e,c+10)) for c in (10,11,13,12)],True,'solid')]
    if typ=='MTEXT':
        text=''.join(v for c,v in e['pairs'] if c in (1,3)).replace('\\P','\n')
        text=re.sub(r'\\[A-Za-z][^;]*;','',text).replace('{','').replace('}','')
        x,y=xy(t,num(e,10),num(e,20)); height=num(e,40)
        return [dict(base,kind='text',x=x,y=y-i*height*1.4,text=clean_text(line),height=height,widthFactor=1,rotation=0,horizontal=0,vertical=3) for i,line in enumerate(text.splitlines())]
    if typ=='TEXT':
        ha=int(get(e,72,'0'));va=int(get(e,73,'0'));x=num(e,11) if ha or va else num(e,10);y=num(e,21) if ha or va else num(e,20)
        sx=math.hypot(t[0],t[1]); sy=math.hypot(t[2],t[3]); X,Y=xy(t,x,y)
        return [dict(base,kind='text',x=X,y=Y,text=clean_text(get(e,1)),height=num(e,40)*sy,widthFactor=num(e,41,1)*sx/max(sy,1e-9),rotation=num(e,50)+math.degrees(math.atan2(t[1],t[0])),horizontal=ha,vertical=va)]
    if typ=='HATCH':
        ps=e['pairs']; starts=[i for i,(c,v) in enumerate(ps) if c==92]; out=[]
        for index,start in enumerate(starts):
            end=starts[index+1] if index+1<len(starts) else next((i for i in range(start+1,len(ps)) if ps[i][0] in (75,97)),len(ps))
            flags=int(ps[start][1]); section=ps[start+1:end]; coords=[]
            if flags&2:
                for c,v in section:
                    if c==10:coords.append([float(v),0])
                    elif c==20 and coords:coords[-1][1]=float(v)
            else:
                edges=[];edge=None
                for c,v in section:
                    if c==72:
                        if edge:edges.append(edge)
                        edge={'type':int(v),'values':{}}
                    elif edge:edge['values'][c]=float(v)
                if edge:edges.append(edge)
                for edge in edges:
                    v=edge['values']
                    if edge['type']==1:coords.extend([(v[10],v[20]),(v[11],v[21])])
                    elif edge['type']==2:
                        a=math.radians(v.get(50,0));b=math.radians(v.get(51,360));s=(b-a)%(2*math.pi) or 2*math.pi
                        if not v.get(73,1):s=-s
                        coords.extend([(v[10]+v[40]*math.cos(a+s*i/80),v[20]+v[40]*math.sin(a+s*i/80)) for i in range(81)])
                    elif edge['type']==3:
                        a=math.radians(v.get(50,0)); b=math.radians(v.get(51,360)); ratio=v[40]
                        a=math.atan2(math.sin(a)/ratio,math.cos(a)); b=math.atan2(math.sin(b)/ratio,math.cos(b))
                        sweep=(b-a)%(2*math.pi) or 2*math.pi
                        if not v.get(73,1): sweep=-((a-b)%(2*math.pi) or 2*math.pi)
                        coords.extend([(v[10]+v[11]*math.cos(a+sweep*i/100)-v[21]*ratio*math.sin(a+sweep*i/100),v[20]+v[21]*math.cos(a+sweep*i/100)+v[11]*ratio*math.sin(a+sweep*i/100)) for i in range(101)])
                    else:raise ValueError('Unsupported hatch edge '+str(edge['type']))
            if coords:out.append(poly(coords,True,'solid' if int(get(e,70,'0')) else 'hatch'))
        return out
    if typ=='WIPEOUT':return []
    raise ValueError('Unsupported original entity '+typ)

data={'schemaVersion':1,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),
      'note':'Display geometry extracted from original DXF. External SHX glyphs are omitted; final output still uses AutoCAD original cloning.',
      'entities':[{'handle':h,'primitives':flatten(entities[h])} for h in entities]}
assert data['sourceSha256']==manifest['sourceSha256']
lookup={e['handle']:e['primitives'] for e in data['entities']}
assert all(lookup[h] for r in manifest['rows'] for h in r['symbolHandles'])
(root/'Template/preview-catalog.json').write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf8')
print('PASS: original preview catalog;',len(data['entities']),'source entities;',sum(len(e['primitives']) for e in data['entities']),'primitives')
