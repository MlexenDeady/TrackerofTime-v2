from __future__ import annotations
import ctypes
from ctypes import wintypes
import json,os,struct,threading,traceback,time
from ootr_host import dispatch,response,RequestCancelled

PIPE_NAME=r"\\.\pipe\TrackerOfTime.V2.OoTR.v1"
MAX_FRAME=16*1024*1024
PROTOCOL_MAJOR=1
if os.name!="nt": raise RuntimeError("OoTR Named Pipe host requires Windows")

kernel32=ctypes.WinDLL("kernel32",use_last_error=True)
INVALID_HANDLE_VALUE=wintypes.HANDLE(-1).value
PIPE_ACCESS_DUPLEX=0x3; PIPE_TYPE_BYTE=0; PIPE_READMODE_BYTE=0; PIPE_WAIT=0
PIPE_UNLIMITED_INSTANCES=255; ERROR_PIPE_CONNECTED=535
GENERIC_READ=0x80000000; GENERIC_WRITE=0x40000000; OPEN_EXISTING=3

kernel32.CreateNamedPipeW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,wintypes.DWORD,wintypes.DWORD,wintypes.DWORD,wintypes.DWORD,wintypes.LPVOID]; kernel32.CreateNamedPipeW.restype=wintypes.HANDLE
kernel32.ConnectNamedPipe.argtypes=[wintypes.HANDLE,wintypes.LPVOID]; kernel32.ConnectNamedPipe.restype=wintypes.BOOL
kernel32.DisconnectNamedPipe.argtypes=[wintypes.HANDLE]; kernel32.DisconnectNamedPipe.restype=wintypes.BOOL
kernel32.ReadFile.argtypes=[wintypes.HANDLE,wintypes.LPVOID,wintypes.DWORD,ctypes.POINTER(wintypes.DWORD),wintypes.LPVOID]; kernel32.ReadFile.restype=wintypes.BOOL
kernel32.WriteFile.argtypes=[wintypes.HANDLE,wintypes.LPCVOID,wintypes.DWORD,ctypes.POINTER(wintypes.DWORD),wintypes.LPVOID]; kernel32.WriteFile.restype=wintypes.BOOL
kernel32.FlushFileBuffers.argtypes=[wintypes.HANDLE]; kernel32.FlushFileBuffers.restype=wintypes.BOOL
kernel32.CloseHandle.argtypes=[wintypes.HANDLE]; kernel32.CloseHandle.restype=wintypes.BOOL
kernel32.CreateFileW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,wintypes.LPVOID,wintypes.DWORD,wintypes.DWORD,wintypes.HANDLE]; kernel32.CreateFileW.restype=wintypes.HANDLE

stop_event=threading.Event()
jobs_lock=threading.Lock()
jobs:dict[str,threading.Event]={}

def _winerr(prefix):
 code=ctypes.get_last_error(); return OSError(code,f"{prefix}: {ctypes.FormatError(code)}")

def _read_exact(h,n):
 out=bytearray()
 while len(out)<n:
  want=min(n-len(out),65536); buf=ctypes.create_string_buffer(want); got=wintypes.DWORD()
  if not kernel32.ReadFile(h,buf,want,ctypes.byref(got),None): raise _winerr("ReadFile")
  if got.value==0: raise EOFError("Named pipe closed")
  out.extend(buf.raw[:got.value])
 return bytes(out)

def _write_all(h,data):
 pos=0
 while pos<len(data):
  chunk=data[pos:pos+65536]; sent=wintypes.DWORD(); buf=ctypes.create_string_buffer(chunk)
  if not kernel32.WriteFile(h,buf,len(chunk),ctypes.byref(sent),None): raise _winerr("WriteFile")
  if sent.value==0: raise OSError("WriteFile wrote zero bytes")
  pos+=sent.value

def _read_frame(h):
 n=struct.unpack("<I",_read_exact(h,4))[0]
 if n<=0 or n>MAX_FRAME: raise ValueError(f"Bad frame {n}")
 obj=json.loads(_read_exact(h,n).decode("utf-8"))
 if not isinstance(obj,dict): raise ValueError("Request must be a JSON object")
 return obj

def _write_frame(h,v):
 data=json.dumps(v,separators=(",",":"),ensure_ascii=False).encode("utf-8")
 if len(data)>MAX_FRAME: raise ValueError(f"Response frame too large: {len(data)}")
 _write_all(h,struct.pack("<I",len(data))+data)
 kernel32.FlushFileBuffers(h)

def _new_pipe():
 h=kernel32.CreateNamedPipeW(PIPE_NAME,PIPE_ACCESS_DUPLEX,PIPE_TYPE_BYTE|PIPE_READMODE_BYTE|PIPE_WAIT,
     PIPE_UNLIMITED_INSTANCES,65536,65536,0,None)
 if h==INVALID_HANDLE_VALUE: raise _winerr("CreateNamedPipeW")
 return h

def _validate_envelope(req):
 if req.get("protocolMajor")!=PROTOCOL_MAJOR: raise ValueError("Protocol major mismatch")
 if not req.get("requestId"): raise ValueError("requestId is required")
 if not req.get("messageType"): raise ValueError("messageType is required")

def _wake_accept_loop():
 # Shutdown may arrive after the accept loop has already blocked on the next instance.
 # A short local connection wakes that ConnectNamedPipe so the loop can observe stop_event.
 for _ in range(20):
  h=kernel32.CreateFileW(PIPE_NAME,GENERIC_READ|GENERIC_WRITE,0,None,OPEN_EXISTING,0,None)
  if h!=INVALID_HANDLE_VALUE:
   kernel32.CloseHandle(h); return
  time.sleep(0.025)

def _serve_client(handle):
 req={}
 try:
  req=_read_frame(handle); _validate_envelope(req)
  typ=req["messageType"]; rid=req["requestId"]

  if typ=="Cancel":
   target=(req.get("payload") or {}).get("targetRequestId","")
   if not target: raise ValueError("Cancel requires targetRequestId")
   with jobs_lock:
    ev=jobs.get(target)
    if ev is not None: ev.set()
   _write_frame(handle,response(req,True,{"cancelled":ev is not None,"targetRequestId":target,"state":"active" if ev is not None else "not-active"}))
   print(f"[Cancel] target={target} accepted={ev is not None}",flush=True)
   return

  if typ=="Shutdown":
   stop_event.set()
   with jobs_lock:
    for ev in jobs.values(): ev.set()
   _write_frame(handle,response(req,True,{"shutdown":True}))
   _wake_accept_loop()
   return

  cancel_event=threading.Event()
  if typ=="GenerateSeed":
   with jobs_lock:
    if rid in jobs: raise ValueError("Duplicate active requestId")
    jobs[rid]=cancel_event
  started=time.monotonic()
  print(f"[{typ}] {rid} START",flush=True)
  try:
   payload=dispatch(req,cancel_event)
   res=response(req,True,payload)
  except RequestCancelled as ex:
   res=response(req,False,code="CANCELLED",message=str(ex))
  except Exception as ex:
   res=response(req,False,code=type(ex).__name__.upper(),message=str(ex),detail=traceback.format_exc())
  finally:
   if typ=="GenerateSeed":
    with jobs_lock:
     jobs.pop(rid,None)
  print(f"[{typ}] {rid} END success={res.get('success')} elapsed={time.monotonic()-started:.2f}s",flush=True)
  try: _write_frame(handle,res)
  except OSError: pass  # expected if the client intentionally disconnected
 except (EOFError,BrokenPipeError,ConnectionResetError,OSError):
  pass
 except Exception as ex:
  try: _write_frame(handle,response(req,False,code=type(ex).__name__.upper(),message=str(ex),detail=traceback.format_exc()))
  except Exception: pass
 finally:
  kernel32.DisconnectNamedPipe(handle)
  kernel32.CloseHandle(handle)

def serve():
 print(f"OoTR Named Pipe host listening: {PIPE_NAME}",flush=True)
 while not stop_event.is_set():
  handle=_new_pipe()
  connected=kernel32.ConnectNamedPipe(handle,None)
  if not connected and ctypes.get_last_error()!=ERROR_PIPE_CONNECTED:
   kernel32.CloseHandle(handle)
   if stop_event.is_set(): break
   raise _winerr("ConnectNamedPipe")
  if stop_event.is_set():
   kernel32.DisconnectNamedPipe(handle); kernel32.CloseHandle(handle); break
  threading.Thread(target=_serve_client,args=(handle,),daemon=True).start()
 print("OoTR Named Pipe host stopped.",flush=True)

if __name__=="__main__": serve()
