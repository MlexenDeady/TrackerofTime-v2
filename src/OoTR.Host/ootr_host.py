#!/usr/bin/env python3
from pathlib import Path
import sys,json,hashlib,tempfile,subprocess,os,traceback,io,contextlib,time,signal
MAJOR,MINOR=1,0
ROOT=Path(__file__).resolve().parents[2]
OOTR=ROOT/"ThirdParty"/"OoTR"
sys.path.insert(0,str(OOTR))

class RequestCancelled(Exception): pass

def resp(r,ok,p=None,e=None):
 return {"requestId":r.get("requestId",""),"success":ok,"messageType":r.get("messageType",""),"payload":p,"error":e,"protocolMajor":MAJOR,"protocolMinor":MINOR}

def response(r,ok,payload=None,code=None,message=None,detail=None):
 e=None if ok else {"code":code or "ERROR","message":message or "","detail":detail}
 return resp(r,ok,payload,e)

def _terminate_process_tree(proc):
 if proc is None or proc.poll() is not None: return
 try:
  if os.name=="nt":
   # OoTR can spawn helper executables. Kill the complete child tree on Cancel/Shutdown.
   subprocess.run(["taskkill","/PID",str(proc.pid),"/T","/F"],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=10)
  else:
   os.killpg(proc.pid,signal.SIGTERM)
   try: proc.wait(timeout=5)
   except subprocess.TimeoutExpired:
    os.killpg(proc.pid,signal.SIGKILL)
 except Exception:
  try: proc.kill()
  except OSError: pass
 try: proc.wait(timeout=5)
 except Exception: pass

def dispatch(r,cancel_event=None):
 if r.get("protocolMajor")!=MAJOR: raise ValueError("Protocol major mismatch")
 t=r.get("messageType"); x=r.get("payload") or {}
 if t=="QueryMetadata":
  from version import __version__
  return {"randomizerVersion":__version__,"pythonVersion":".".join(map(str,sys.version_info[:3])),
          "protocolMajor":MAJOR,"protocolMinor":MINOR,
          "capabilities":["QueryMetadata","ValidateRom","ConvertSettings","GenerateSeed","Cancel","Shutdown"]}
 if t=="ConvertSettings":
  from Settings import Settings
  s=Settings(dict(x.get("settings") or {}))
  if x.get("settingsString"): s.update_with_settings_string(x["settingsString"])
  return {"settingsString":s.get_settings_string(),"settings":s.to_json()}
 if t=="ValidateRom":
  q=Path(x.get("romPath",""))
  if not q.is_file(): raise FileNotFoundError("ROM file does not exist")
  if q.suffix.lower() not in (".z64",".n64"): raise ValueError("OoTR accepts .z64 or .n64")
  h=hashlib.sha256(q.read_bytes()).hexdigest()
  from Rom import Rom
  try: Rom(str(q)); valid=True; detail="Validated by OoTR Rom loader"
  except Exception as ex: valid=False; detail=str(ex)
  return {"valid":valid,"format":q.suffix[1:].lower(),"sizeBytes":q.stat().st_size,"sha256":h,"detail":detail}
 if t=="GenerateSeed":
  rom=Path(x["romPath"]).resolve()
  if not rom.is_file(): raise FileNotFoundError("ROM file does not exist")
  out=Path(x["outputDirectory"]).resolve(); out.mkdir(parents=True,exist_ok=True)
  s=dict(x.get("settings") or {})
  s.update({"rom":str(rom),"output_dir":str(out),"world_count":int(x.get("worldCount",1)),
      "create_patch_file":bool(x.get("createPatchFile",True)),
      "create_compressed_rom":bool(x.get("createCompressedRom",False)),
      "create_uncompressed_rom":bool(x.get("createUncompressedRom",False)),
      "create_spoiler":bool(x.get("createSpoiler",True))})
  if x.get("distributionFile"): s["distribution_file"]=str(Path(x["distributionFile"]).resolve())
  before={p.resolve() for p in out.rglob("*") if p.is_file()}
  f=tempfile.NamedTemporaryFile("w",suffix=".json",delete=False,encoding="utf-8")
  json.dump(s,f); f.close()
  proc=None; outlog=None; errlog=None
  try:
   cmd=[sys.executable,str(OOTR/"OoTRandomizer.py"),"--no-venv","--settings",f.name,"--no_log"]
   if x.get("settingsString"): cmd+=["--settings_string",x["settingsString"]]
   if x.get("seed"): cmd+=["--seed",x["seed"]]
   # IMPORTANT: never leave stdout/stderr as unread PIPEs while polling.
   # OoTR logs enough data to fill an OS pipe buffer; RC7/RC8 then deadlocked
   # because the child blocked on logging while this host waited in poll().
   outlog=tempfile.TemporaryFile(mode="w+t",encoding="utf-8")
   errlog=tempfile.TemporaryFile(mode="w+t",encoding="utf-8")
   popen_kwargs={"cwd":OOTR,"text":True,"stdout":outlog,"stderr":errlog}
   if os.name=="nt":
    popen_kwargs["creationflags"]=getattr(subprocess,"CREATE_NEW_PROCESS_GROUP",0)|getattr(subprocess,"CREATE_NO_WINDOW",0)
   else:
    popen_kwargs["start_new_session"]=True
   proc=subprocess.Popen(cmd,**popen_kwargs)
   while proc.poll() is None:
    if cancel_event is not None and cancel_event.is_set():
     _terminate_process_tree(proc)
     raise RequestCancelled("Seed generation cancelled")
    time.sleep(0.05)
   outlog.seek(0); stdout=outlog.read()
   errlog.seek(0); stderr=errlog.read()
   if proc.returncode: raise RuntimeError(stderr.strip() or stdout.strip())
   from Settings import Settings
   eff=Settings(s)
   if x.get("settingsString"): eff.update_with_settings_string(x["settingsString"])
   if x.get("seed"): eff.update_seed(x["seed"])
   from version import __version__
   created=sorted(str(p) for p in ({p.resolve() for p in out.rglob("*") if p.is_file()}-before))
   return {"seed":eff.seed,"settingsString":eff.get_settings_string(),"randomizerVersion":__version__,
           "outputDirectory":str(out),"outputFiles":created,"standardOutput":stdout,"standardError":stderr}
  finally:
   if proc is not None and proc.poll() is None:
    _terminate_process_tree(proc)
   for stream in (outlog,errlog):
    if stream is not None:
     try: stream.close()
     except Exception: pass
   try: os.unlink(f.name)
   except OSError: pass
 if t=="Shutdown": return {"shutdown":True}
 raise ValueError("Unsupported message type: "+str(t))

def main():
 for line in sys.stdin:
  if not line.strip(): continue
  r={}
  try:
   r=json.loads(line); cap=io.StringIO()
   with contextlib.redirect_stdout(cap): p=dispatch(r)
   z=resp(r,True,p)
   if cap.getvalue(): z["diagnostics"]=cap.getvalue()
  except RequestCancelled as ex:
   z=response(r,False,code="CANCELLED",message=str(ex))
  except Exception as ex:
   z=response(r,False,code=type(ex).__name__.upper(),message=str(ex),detail=traceback.format_exc())
  print(json.dumps(z,separators=(",",":")),flush=True)
  if r.get("messageType")=="Shutdown": break

if __name__=="__main__":
 main()
