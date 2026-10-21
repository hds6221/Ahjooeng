"""Map the user's seven original legend tables into independent, source-backed catalogs.
Regular output uses one selectable facility per row; shared original cells are copied per item.
The connected watershed annotation diagram is preserved as one complete annotation set.
"""
import sys
if '--legacy' not in sys.argv:
    raise SystemExit('이 스크립트는 보관용입니다. tools/rebuild-preview.ps1을 사용하세요. 구형 출력이 필요한 경우에만 --legacy를 지정하세요.')
from pathlib import Path
import json, hashlib, math, collections

root=Path(__file__).resolve().parents[1]
lines=(root/'Template/범례템플릿.dxf').read_text(encoding='utf-8-sig').splitlines()
pairs=[(int(lines[i].strip()),lines[i+1].strip()) for i in range(0,len(lines),2)]
records=[]; section=''; rec=None
for code,value in pairs:
    if code==0:
        if rec is not None:records.append(rec)
        rec={'type':value,'pairs':[],'section':section}
    rec['pairs'].append((code,value))
    if rec['type']=='SECTION' and code==2:section=value
    rec['section']=section
if rec:records.append(rec)
entities=[e for e in records if e['section']=='ENTITIES']
info={e['Handle']:e for e in json.loads((root/'Template/source-entities.json').read_text(encoding='utf8'))}
def g(e,c,d=''):return next((v for k,v in e['pairs'] if k==c),d)
byh={g(e,5):e for e in entities}
def vertices(e):
    out=[]
    for c,v in e['pairs']:
        if c==10:out.append([float(v),0])
        elif c==20 and out:out[-1][1]=float(v)
    return out
def segments(h):
    e=byh[h]
    if e['type']=='LINE':return [((float(g(e,10)),float(g(e,20))),(float(g(e,11)),float(g(e,21))))]
    if e['type']=='LWPOLYLINE':
        vs=vertices(e);return list(zip(vs,vs[1:]+([vs[0]] if int(g(e,70,'0'))&1 else [])))
    return []
def anchor(e):
    if e['Type'] in ('TEXT','MTEXT'):return e['X'],e['Y']
    return (e['MinX']+e['MaxX'])/2,(e['MinY']+e['MaxY'])/2
def cell_center(handles):
    text=[anchor(info[h])[1] for h in handles if info[h]['Type'] in ('TEXT','MTEXT')]
    return sum(text)/len(text) if text else (min(info[h]['MinY'] for h in handles)+max(info[h]['MaxY'] for h in handles))/2
def txt(handles):return ' / '.join(info[h]['Text'].strip().replace('\r','').replace('\n',' ') for h in sorted(handles,key=lambda h:-anchor(info[h])[1]) if info[h]['Type'] in ('TEXT','MTEXT'))
def unique(values,tol=.01):
    out=[]
    for value in sorted(values):
        if not out or abs(value-out[-1])>tol:out.append(value)
    return out

frames=sorted([e for e in info.values() if e['Type']=='LWPOLYLINE' and e['Layer']=='Defpoints' and e['MaxY']-e['MinY']>1000],key=lambda e:e['MinX'])
assert len(frames)==7
names=[('construction','공사'),('watershed','유역'),('storm','우수'),('sewage','오수'),('water','상수'),('paving','포장'),('auxiliary','부대')]
sha=hashlib.sha256((root/'Template/범례템플릿.dxf').read_bytes()).hexdigest()
index={'schemaVersion':1,'defaultCategoryId':'storm','sourceDwg':'범례템플릿.dwg','sourceDwgSha256':hashlib.sha256((root/'범례템플릿.dwg').read_bytes()).hexdigest(),'categories':[]}
(root/'Template/categories').mkdir(exist_ok=True)
summary=[]; assigned=set()
for (category,name),frame in zip(names,frames):
    own=[e for e in info.values() if e['MinX']>=frame['MinX']-.1 and e['MaxX']<=frame['MaxX']+.1 and e['MinY']>=frame['MinY']-.1 and e['MaxY']<=frame['MaxY']+.1]
    assigned.update(e['Handle'] for e in own)
    wipe=next(e for e in own if e['Type']=='WIPEOUT')
    left,right=wipe['MinX'],wipe['MaxX'];top=wipe['MaxY'];bottom=wipe['MinY']; originY=frame['MaxY']
    border=[e for e in own if '범례' in e['Layer'] and e['Type'] in ('LINE','LWPOLYLINE')]
    m={'schemaVersion':2,'categoryId':category,'categoryName':name,'sourceFile':'범례템플릿.dxf','sourceSha256':sha,'baseScaleDenominator':100,'origin':{'x':frame['MinX'],'y':originY,'z':0},'frameWidth':frame['MaxX']-frame['MinX'],'frameBottomY':frame['MinY'],'bottomMargin':bottom-frame['MinY'],'tableWidth':right-left,'tableLeftX':left,'tableRightX':right,'titleTopY':top,'sourceBottomY':bottom,'rowHeight':450,'outerBorderHandle':frame['Handle'],'wipeoutHandle':wipe['Handle'],'allHandles':[e['Handle'] for e in own],'groups':[],'rows':[],'headerHandles':[],'verticalBorderHandles':[],'auxBorderHandles':[],'rowBoundaryHandles':[],'splitSeparatorHandles':[]}
    if category=='watershed':
        # This source has connecting leaders from the manholes to watershed boundary/area labels.
        # Splitting these into independent rows would break the original explanation diagram.
        body_top=17247.009321456462
        content=[e['Handle'] for e in own if e['Handle'] not in (frame['Handle'],wipe['Handle'],'71D','73E')]
        m.update(rowsTopY=body_top,headerTopY=body_top,hasQuantityColumn=False,quantityCenterX=0,quantityCellWidth=650,fullSeparatorHandle=None,fullSeparatorY=body_top,auxBorderHandles=['71D'],headerHandles=['73E'])
        m['rows']=[{'id':'watershed_annotation_set','name':'유역 표기 세트','spec':'관로·맨홀·유역경계·유역면적','remark':'연결된 원본 설명 도형 전체','quantity':'-','unit':'','groupId':'watershed_annotation_set','sourceTopY':body_top,'height':body_top-bottom,'contentHandles':content,'symbolHandles':content,'quantityHandle':None,'quantitySourceCenterY':0,'cells':[{'column':0,'handles':content,'sourceCenterY':(body_top+bottom)/2}]}]
    else:
        rowtop=top-900; m['rowsTopY']=rowtop; m['headerTopY']=top-450; m['hasQuantityColumn']=True
        vertical=[e for e in border if e['MaxX']-e['MinX']<.01 and e['MaxY']-e['MinY']> (rowtop-bottom)*.8]
        xs=unique([(e['MinX']+e['MaxX'])/2 for e in vertical]);assert len(xs)==5,(name,xs)
        boundaries=[left]+xs+[right]
        m['verticalBorderHandles']=[e['Handle'] for e in vertical]
        m['quantityCenterX']=(boundaries[4]+boundaries[5])/2;m['quantityCellWidth']=boundaries[5]-boundaries[4]-100
        m['auxBorderHandles']=[e['Handle'] for e in border if e['MaxX']-e['MinX']>m['tableWidth']*.99 and e['MaxY']-e['MinY']>900]
        m['headerHandles']=[e['Handle'] for e in own if e['Handle'] not in (frame['Handle'],wipe['Handle']) and e['MinY']>=rowtop-.01]
        horizontal=[]
        for e in border:
            for a,b in segments(e['Handle']):
                if abs(a[1]-b[1])<.01 and min(a[1],b[1])<rowtop+.01:horizontal.append((min(a[0],b[0]),max(a[0],b[0]),(a[1]+b[1])/2,e['Handle']))
        full=next(v for v in horizontal if v[1]-v[0]>m['tableWidth']*.99 and info[v[3]]['Type']=='LINE')
        m['fullSeparatorHandle']=full[3];m['fullSeparatorY']=full[2]
        m['rowBoundaryHandles']=list(dict.fromkeys(v[3] for v in horizontal))
        excluded={frame['Handle'],wipe['Handle']}|{e['Handle'] for e in border}
        body=[e for e in own if e['Handle'] not in excluded and anchor(e)[1]<rowtop-.01]
        nominal=round((rowtop-bottom)/450)
        # Ignore an entirely blank trailing source row, keeping the original source available separately.
        last=max(min(nominal-1,max(0,int((rowtop-anchor(e)[1])/450))) for e in body)
        count=last+1
        def column(e):
            x,y=anchor(e)
            return next((i for i in range(6) if boundaries[i]-.1<=x<=boundaries[i+1]+.1),None)
        cols={c:[e for e in body if column(e)==c] for c in range(6)}
        def row_index(e):return min(nominal-1,max(0,int((rowtop-anchor(e)[1])/450)))
        def span(c,r):
            center=(boundaries[c]+boundaries[c+1])/2
            cut=[any(a-.1<=center<=b+.1 and abs(y-(rowtop-i*450))<.1 for a,b,y,h in horizontal) for i in range(1,nominal)]
            start=r;end=r
            while start>0 and not cut[start-1]:start-=1
            while end<nominal-1 and not cut[end]:end+=1
            return start,end
        def choose(c,r,allow_inherit=True):
            exact=[e['Handle'] for e in cols[c] if row_index(e)==r]
            a,b=span(c,r); within=[e['Handle'] for e in cols[c] if a<=row_index(e)<=b]
            if c in (0,):
                # Distinct symbols inside a visually merged column remain distinct per row.
                if exact:return exact
                if within:
                    target=rowtop-(r+.5)*450; distances=[abs(anchor(info[h])[1]-target) for h in within]; near=min(distances)
                    return [h for h,d in zip(within,distances) if abs(d-near)<.1]
            if c==5:
                if exact:return exact
                if len(within)==1:return within
                return []
            if c in (1,2) and within:return within
            if exact:return exact
            if within:return within
            if allow_inherit:
                a,b=span(1,r)
                shared=[e['Handle'] for e in cols[c] if a<=row_index(e)<=b]
                if shared:
                    target=rowtop-(r+.5)*450; dist=min(abs(anchor(info[h])[1]-target) for h in shared)
                    return [h for h in shared if abs(abs(anchor(info[h])[1]-target)-dist)<.1]
            return []
        qty_templates=[e['Handle'] for e in cols[4] if e['Type']=='TEXT']
        assert qty_templates,name
        for i in range(count):
            fields={c:choose(c,i) for c in range(6)}
            if not fields[1]:
                # The source's road-lettering row leaves its repeated facility name blank.
                previous=[e for e in cols[1] if row_index(e)<i]
                if previous:
                    nearest=max(row_index(e) for e in previous)
                    fields[1]=[e['Handle'] for e in previous if row_index(e)==nearest]
            # Exact quantity cells are cloned once even if the original accidentally contains duplicates.
            qty=next((h for h in fields[4] if info[h]['Type']=='TEXT'),qty_templates[0])
            cells=[{'column':c,'handles':fields[c],'sourceCenterY':cell_center(fields[c]) if fields[c] else rowtop-(i+.5)*450,'centerX':(boundaries[c]+boundaries[c+1])/2,'width':boundaries[c+1]-boundaries[c]-100} for c in (0,1,2,3,5) if fields[c] or c in (1,2)]
            name_text=txt(fields[1]);spec=txt(fields[2]);note=txt(fields[5]);unit=txt(fields[3])
            if not name_text:raise ValueError((name,i,'missing original facility name'))
            if not fields[0]:raise ValueError((name,i,'missing original symbol'))
            m['rows'].append({'id':category+'_'+str(i+1).zfill(3),'name':name_text,'spec':spec or '원본 기호','remark':note,'quantity':'-','unit':unit,'groupId':category+'_'+str(i+1).zfill(3),'sourceTopY':rowtop-i*450,'height':450,'contentHandles':list(dict.fromkeys(h for c in cells for h in c['handles'])),'symbolHandles':fields[0],'quantityHandle':qty,'quantitySourceCenterY':anchor(info[qty])[1],'cells':cells})
    filename='categories/'+category+'.json'
    (root/'Template'/filename).write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf8')
    index['categories'].append({'id':category,'name':name,'manifestFile':filename,'itemCount':len(m['rows'])})
    summary.append({'category':name,'items':len(m['rows']),'originalEntities':len(m['allHandles']),'rows':[(r['name'],r['spec'],r['remark']) for r in m['rows']]})
    if category=='storm':(root/'Template/legend-template.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf8')
assert len(assigned)==len(info),(len(assigned),len(info),set(info)-assigned)
(root/'Template/catalog-index.json').write_text(json.dumps(index,ensure_ascii=False,indent=2),encoding='utf8')
(root/'work/mapping-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf8')
for s in summary:print(s['category'],s['items'],'items',s['originalEntities'],'source entities')
