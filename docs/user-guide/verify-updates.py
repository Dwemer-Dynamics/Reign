"""Isolated regression checks for guide change detection and review gates."""
import copy
from pathlib import Path
import runpy

HERE=Path(__file__).resolve().parent
m=runpy.run_path(str(HERE/'update-guide.py'))
cfg=m['config']()
diff=m['changes_between']
validate=m['validate_review']
checks=0


def ok(value):
    global checks
    assert value
    checks+=1


def rejects(f):
    global checks
    try:
        f()
    except RuntimeError:
        checks+=1
    else:
        raise AssertionError('Expected review gate to reject')


p='ReignBeta/src/Modules/Dialogue/UI/ReignActionBuilder.cs'
q='ReignServer/src/Modules/Dialogue/NewPlayerAction.cs'
r='ReignBeta/src/Modules/NewFeature/Behavior.cs'
old={p:'aaa','ReignBeta/ModuleData/old.json':'old'}
new={p:'bbb',q:'new',r:'future'}
rows=diff(old,new,cfg)
ok({(x['path'],x['change']) for x in rows}=={(p,'modified'),('ReignBeta/ModuleData/old.json','removed'),(q,'added'),(r,'added')})
ok(next(x for x in rows if x['path']==p)['candidateChapters']==['your-first-conversation'])
ok(next(x for x in rows if x['path']==r)['needsClassification'])
ok(not diff(old,dict(old),cfg))
ok(m['watched']('ReignBeta/src/Modules/WorldSimulation/Runtime/ReignCalendarPatches.cs'))
ok(m['watched']('ReignBeta/src/Modules/NewFeature/Behavior.cs'))
ok(m['watched']('src/Modules/Dialogue/Behavior.cs',True))
ok(m['watched']('ui/index.html',True))
ok(not m['watched']('data/settings.json',True))
ok(not m['watched']('runtime/current/ReignServer.exe',True))
ok(not m['watched']('docs/user-guide/index.html'))
ok(not m['watched']('src/Modules/Dialogue/obj/generated.cs',True))
ok(not m['watched']('src/Modules/Dialogue/settings.json',True))
base={'inputs':{p:'old'},'sourceSnapshotId':m['stable_id']({p:'old'}),'guideDigest':m['guide_digest']()}
now={'inputs':{p:'new'},'sourceSnapshotId':m['stable_id']({p:'new'})}
report={'schema':m['REPORT_SCHEMA'],'baselineId':base['sourceSnapshotId'],'sourceSnapshotId':now['sourceSnapshotId'],'changes':diff(base['inputs'],now['inputs'],cfg)}
rejects(lambda:validate(report,base,now,cfg))
reviewed=copy.deepcopy(report)
reviewed['changes'][0]['review']={'outcome':'no-player-impact','reason':'Only internal comments changed; keyboard behavior and labels are identical.'}
validate(reviewed,base,now,cfg);checks+=1
stale=copy.deepcopy(reviewed);stale['sourceSnapshotId']='another-source'
rejects(lambda:validate(stale,base,now,cfg))
omitted=copy.deepcopy(reviewed);omitted['changes']=[]
rejects(lambda:validate(omitted,base,now,cfg))
deferred=copy.deepcopy(reviewed);deferred['changes'][0]['review']['outcome']='deferred'
rejects(lambda:validate(deferred,base,now,cfg))
unsupported=copy.deepcopy(reviewed);unsupported['changes'][0]['review']={'outcome':'updated','reason':'Revised the user-facing shortcut explanation.','topics':['nonexistent-topic']}
rejects(lambda:validate(unsupported,base,now,cfg))
unchanged=copy.deepcopy(reviewed);unchanged['changes'][0]['review']={'outcome':'updated','reason':'Revised the user-facing shortcut explanation.','topics':['asking-for-a-real-action']}
rejects(lambda:validate(unchanged,base,now,cfg))
changed_base=copy.deepcopy(base);changed_base['guideDigest']='prior-guide-content'
validate(unchanged,changed_base,now,cfg);checks+=1
new_report=copy.deepcopy(reviewed);new_report['changes'][0]['after']='other-terms'
rejects(lambda:validate(new_report,base,now,cfg))
print({'ok':True,'checks':checks,'scope':'Pure synthetic snapshots; no product-source, baseline, provider, game or runtime mutation'})
