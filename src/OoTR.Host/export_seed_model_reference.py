from pathlib import Path
import sys,json,hashlib,logging
OOTR=Path('/mnt/data/m3e2e/ThirdParty/OoTR'); sys.path.insert(0,str(OOTR))
from Settings import Settings
from Main import resolve_settings, generate
from version import __version__
ROM='/mnt/data/Legend of Zelda, The - Ocarina of Time.n64'
SEED='TrackerOfTimeV2M3E2E'
# Same effective defaults as E2E generation request.
s=Settings({'rom':ROM,'output_dir':'/mnt/data/m3e2e-output','world_count':1,'create_patch_file':True,'create_compressed_rom':False,'create_uncompressed_rom':False,'create_spoiler':True})
s.update_seed(SEED)
logging.basicConfig(level=logging.WARNING)
resolve_settings(s)
spoiler=generate(s)
# Generate() creates final worlds; parse_data gives canonical spoiler location/entrance collections.
spoiler.parse_data()
def sid(kind, world, name):
    canonical=f'v2|{kind}|w{world}|{name}'.encode('utf-8')
    return 'v2-'+kind+'-'+hashlib.sha256(canonical).hexdigest()[:24]
worlds=[]
for w in spoiler.worlds:
    regions=[]
    for r in w.regions:
        regions.append({'stableV2Id':sid('region',w.id,r.name),'ooTRName':r.name,'worldId':w.id,'type':getattr(r.type,'name',str(r.type)),'dungeon':r.dungeon_name,'scene':r.scene,'trackerMappingId':None})
    locs=[]
    for l in w.get_locations():
        if getattr(l.disabled, 'name', '') != 'ENABLED' or l.internal or l.type.startswith('Hint') or l.locked:
            continue
        item=l.item.name if l.item else None
        rn=l.parent_region.name if l.parent_region else None
        locs.append({'stableV2Id':sid('location',w.id,l.name),'ooTRName':l.name,'worldId':w.id,'regionId':sid('region',w.id,rn) if rn else None,'regionName':rn,'type':l.type,'scene':l.scene,'item':item,'locked':l.locked,'disabled':str(l.disabled),'trackerMappingId':None})
    ents=[]
    for e in w.get_entrances():
        src=e.parent_region.name if e.parent_region else None; dst=e.connected_region.name if e.connected_region else None
        ents.append({'stableV2Id':sid('entrance',w.id,e.name),'ooTRName':e.name,'worldId':w.id,'sourceRegionId':sid('region',w.id,src) if src else None,'sourceRegionName':src,'targetRegionId':sid('region',w.id,dst) if dst else None,'targetRegionName':dst,'entranceType':e.type,'shuffled':e.shuffled,'primary':e.primary,'trackerMappingId':None})
    mq=sorted([d.name for d in w.dungeons if getattr(d,'mq',False)])
    worlds.append({'worldId':w.id,'locations':locs,'regions':regions,'entrances':ents,'mqDungeons':mq})
model={'schemaVersion':1,'randomizerVersion':__version__,'seed':s.seed,'seedFingerprint':hashlib.sha256((s.get_settings_string()+'|'+__version__+'|'+s.seed).encode()).hexdigest(),'settingsString':s.get_settings_string(),'worldCount':len(worlds),'worlds':worlds,'mapping':{'algorithm':'sha256(v2|kind|world|OoTRName), first 24 hex','trackerMappingsPopulated':False}}
out=Path('/mnt/data/m3e2e-output/TrackerSeedModel.json');out.write_text(json.dumps(model,indent=2),encoding='utf-8')
print(json.dumps({'file':str(out),'worlds':len(worlds),'locations':sum(len(x['locations']) for x in worlds),'regions':sum(len(x['regions']) for x in worlds),'entrances':sum(len(x['entrances']) for x in worlds),'mq':worlds[0]['mqDungeons'],'settingsString':s.get_settings_string(),'version':__version__},indent=2))
