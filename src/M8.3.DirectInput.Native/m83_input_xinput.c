#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <ctype.h>

#define EXPORT __declspec(dllexport)
#define CALL __cdecl
#define M64ERR_SUCCESS 0
#define M64PLUGIN_INPUT 4
#define PLUGIN_MEMPAK 2
#define CONT_TYPE_STANDARD 0

typedef void (CALL *debug_cb)(void*, int, const char*);
typedef struct { int Present, RawData, Plugin, Type; } CONTROL;
typedef struct { CONTROL *Controls; } CONTROL_INFO;
typedef union { unsigned int Value; struct { unsigned short buttons; signed char X_AXIS; signed char Y_AXIS; }; } BUTTONS;
typedef struct { WORD wButtons; BYTE bLeftTrigger,bRightTrigger; SHORT sThumbLX,sThumbLY,sThumbRX,sThumbRY; } XINPUT_GAMEPAD;
typedef struct { DWORD dwPacketNumber; XINPUT_GAMEPAD Gamepad; } XINPUT_STATE;
typedef DWORD (WINAPI *xgetstate_fn)(DWORD, XINPUT_STATE*);

static HMODULE gxlib=NULL; static xgetstate_fn gxget=NULL; static DWORD gindex=0; static int gconnected=0; static debug_cb gdebug=NULL; static void* gctx=NULL;
static void logmsg(int level,const char* msg){ if(gdebug) gdebug(gctx,level,msg); }
static int ax(SHORT v){ const int dz=7849; int n=v; if(n>-dz&&n<dz)return 0; n=(n*80)/32767; if(n>80)n=80;if(n<-80)n=-80;return n; }
static int findpad(void){ if(!gxget)return 0; XINPUT_STATE s; for(DWORD i=0;i<4;i++){ZeroMemory(&s,sizeof(s));if(gxget(i,&s)==0){gindex=i;gconnected=1;return 1;}}gconnected=0;return 0; }

enum { STICK_UP,STICK_DOWN,STICK_LEFT,STICK_RIGHT,DPAD_UP,DPAD_DOWN,DPAD_LEFT,DPAD_RIGHT,N64_START,N64_Z,N64_B,N64_A,C_UP,C_DOWN,C_LEFT,C_RIGHT,N64_R,N64_L,BIND_COUNT };
static const char* names[BIND_COUNT]={"STICK_UP","STICK_DOWN","STICK_LEFT","STICK_RIGHT","DPAD_UP","DPAD_DOWN","DPAD_LEFT","DPAD_RIGHT","START","Z","B","A","C_UP","C_DOWN","C_LEFT","C_RIGHT","R","L"};
typedef struct { int vk; char pad[32]; } binding;
static binding binds[BIND_COUNT];
static char gcfgpath[2048]={0}; static FILETIME gcfgtime={0}; static ULONGLONG glastcfgcheck=0;

static int keyvk(const char* s){
 if(!s||!_stricmp(s,"None"))return 0;
 if(strlen(s)==1){char c=(char)toupper((unsigned char)s[0]);if((c>='A'&&c<='Z')||(c>='0'&&c<='9'))return c;}
 if(!_stricmp(s,"Up"))return VK_UP;if(!_stricmp(s,"Down"))return VK_DOWN;if(!_stricmp(s,"Left"))return VK_LEFT;if(!_stricmp(s,"Right"))return VK_RIGHT;
 if(!_stricmp(s,"Enter"))return VK_RETURN;if(!_stricmp(s,"Space"))return VK_SPACE;if(!_stricmp(s,"LeftShift"))return VK_LSHIFT;if(!_stricmp(s,"RightShift"))return VK_RSHIFT;if(!_stricmp(s,"LeftCtrl"))return VK_LCONTROL;if(!_stricmp(s,"RightCtrl"))return VK_RCONTROL;
 if(!_strnicmp(s,"NumPad",6)&&s[6]>='0'&&s[6]<='9'&&!s[7])return VK_NUMPAD0+(s[6]-'0'); return 0;
}
static void defaults(void){
 const char* k[BIND_COUNT]={"Up","Down","Left","Right","T","G","F","H","Enter","S","X","C","I","K","J","L","D","A"};
 const char* p[BIND_COUNT]={"LeftStickUp","LeftStickDown","LeftStickLeft","LeftStickRight","DPadUp","DPadDown","DPadLeft","DPadRight","Start","LT","X","A","RightStickUp","RightStickDown","RightStickLeft","RightStickRight","RB","LB"};
 for(int i=0;i<BIND_COUNT;i++){binds[i].vk=keyvk(k[i]);strcpy_s(binds[i].pad,sizeof(binds[i].pad),p[i]);}
}
static void trim(char* s){char* p=s;while(*p&&isspace((unsigned char)*p))p++;if(p!=s)memmove(s,p,strlen(p)+1);size_t n=strlen(s);while(n&&isspace((unsigned char)s[n-1]))s[--n]=0;}
static void loadcfg(void){
 defaults(); char path[2048]={0}; DWORD n=GetEnvironmentVariableA("TOT_M85_INPUT_CONFIG",path,sizeof(path)); if(!n||n>=sizeof(path)){gcfgpath[0]=0;logmsg(3,"M8.5 INPUT: default bindings active");return;} strncpy_s(gcfgpath,sizeof(gcfgpath),path,_TRUNCATE);
 FILE* f=NULL;if(fopen_s(&f,path,"r")||!f){logmsg(2,"M8.5 INPUT: profile path supplied but file unavailable; defaults active");return;} char line[256];
 while(fgets(line,sizeof(line),f)){trim(line);if(!line[0]||line[0]=='#')continue;char* eq=strchr(line,'=');if(!eq)continue;*eq=0;char* val=eq+1;trim(line);trim(val);for(int i=0;i<BIND_COUNT;i++){char kname[48],pname[48];sprintf_s(kname,sizeof(kname),"K_%s",names[i]);sprintf_s(pname,sizeof(pname),"P_%s",names[i]);if(!_stricmp(line,kname))binds[i].vk=keyvk(val);else if(!_stricmp(line,pname))strncpy_s(binds[i].pad,sizeof(binds[i].pad),val,_TRUNCATE);}}
 fclose(f); WIN32_FILE_ATTRIBUTE_DATA fad;if(GetFileAttributesExA(path,GetFileExInfoStandard,&fad))gcfgtime=fad.ftLastWriteTime;logmsg(3,"M8.5 INPUT: user keyboard/controller profile loaded");
}
static void reloadcfg_if_changed(void){ULONGLONG now=GetTickCount64();if(now-glastcfgcheck<250)return;glastcfgcheck=now;if(!gcfgpath[0])return;WIN32_FILE_ATTRIBUTE_DATA fad;if(!GetFileAttributesExA(gcfgpath,GetFileExInfoStandard,&fad))return;if(CompareFileTime(&fad.ftLastWriteTime,&gcfgtime)!=0){loadcfg();logmsg(3,"M8.5 INPUT: live binding profile reloaded");}}
static int keydown(int i){return binds[i].vk && (GetAsyncKeyState(binds[i].vk)&0x8000);}
static int padpressed(const char* t,const XINPUT_GAMEPAD* g){if(!t||!g||!_stricmp(t,"None"))return 0;
 if(!_stricmp(t,"DPadUp"))return !!(g->wButtons&0x0001);if(!_stricmp(t,"DPadDown"))return !!(g->wButtons&0x0002);if(!_stricmp(t,"DPadLeft"))return !!(g->wButtons&0x0004);if(!_stricmp(t,"DPadRight"))return !!(g->wButtons&0x0008);
 if(!_stricmp(t,"Start"))return !!(g->wButtons&0x0010);if(!_stricmp(t,"Back"))return !!(g->wButtons&0x0020);if(!_stricmp(t,"LThumb"))return !!(g->wButtons&0x0040);if(!_stricmp(t,"RThumb"))return !!(g->wButtons&0x0080);if(!_stricmp(t,"LB"))return !!(g->wButtons&0x0100);if(!_stricmp(t,"RB"))return !!(g->wButtons&0x0200);
 if(!_stricmp(t,"A"))return !!(g->wButtons&0x1000);if(!_stricmp(t,"B"))return !!(g->wButtons&0x2000);if(!_stricmp(t,"X"))return !!(g->wButtons&0x4000);if(!_stricmp(t,"Y"))return !!(g->wButtons&0x8000);if(!_stricmp(t,"LT"))return g->bLeftTrigger>40;if(!_stricmp(t,"RT"))return g->bRightTrigger>40;
 const int d=12000;if(!_stricmp(t,"LeftStickUp"))return g->sThumbLY>d;if(!_stricmp(t,"LeftStickDown"))return g->sThumbLY<-d;if(!_stricmp(t,"LeftStickLeft"))return g->sThumbLX<-d;if(!_stricmp(t,"LeftStickRight"))return g->sThumbLX>d;if(!_stricmp(t,"RightStickUp"))return g->sThumbRY>d;if(!_stricmp(t,"RightStickDown"))return g->sThumbRY<-d;if(!_stricmp(t,"RightStickLeft"))return g->sThumbRX<-d;if(!_stricmp(t,"RightStickRight"))return g->sThumbRX>d;return 0;}
static int padamount(const char* t,const XINPUT_GAMEPAD* g){if(!t||!g)return 0;int v=0;if(!_stricmp(t,"LeftStickUp"))v=g->sThumbLY;else if(!_stricmp(t,"LeftStickDown"))v=-g->sThumbLY;else if(!_stricmp(t,"LeftStickLeft"))v=-g->sThumbLX;else if(!_stricmp(t,"LeftStickRight"))v=g->sThumbLX;else if(!_stricmp(t,"RightStickUp"))v=g->sThumbRY;else if(!_stricmp(t,"RightStickDown"))v=-g->sThumbRY;else if(!_stricmp(t,"RightStickLeft"))v=-g->sThumbRX;else if(!_stricmp(t,"RightStickRight"))v=g->sThumbRX;else return padpressed(t,g)?80:0;if(v<0)v=0;if(v<7849)return 0;v=(v*80)/32767;if(v>80)v=80;return v;}


EXPORT int CALL PluginGetVersion(int* type,int* version,int* api,const char** name,int* caps){ static const char n[]="TrackerOfTime M8 Direct XInput Input Plugin"; if(type)*type=M64PLUGIN_INPUT;if(version)*version=0x00010000;if(api)*api=0x00020100;if(name)*name=n;if(caps)*caps=0;return M64ERR_SUCCESS; }
EXPORT int CALL PluginStartup(HMODULE core,void* ctx,debug_cb cb){ (void)core;gctx=ctx;gdebug=cb;loadcfg();const char* dlls[]={"xinput1_4.dll","xinput1_3.dll","xinput9_1_0.dll"};for(int i=0;i<3&&!gxlib;i++){gxlib=LoadLibraryA(dlls[i]);if(gxlib)gxget=(xgetstate_fn)GetProcAddress(gxlib,"XInputGetState");if(!gxget&&gxlib){FreeLibrary(gxlib);gxlib=NULL;}}if(!gxget){logmsg(2,"M8.5 INPUT: XInput unavailable; configured keyboard remains active");return M64ERR_SUCCESS;}findpad();logmsg(3,gconnected?"M8.5 INPUT: configurable XInput + keyboard active":"M8.5 INPUT: keyboard active; XInput hot-plug armed");return M64ERR_SUCCESS; }
EXPORT int CALL PluginShutdown(void){if(gxlib)FreeLibrary(gxlib);gxlib=NULL;gxget=NULL;gconnected=0;return M64ERR_SUCCESS;}
EXPORT void CALL InitiateControllers(CONTROL_INFO info){if(!info.Controls)return;for(int i=0;i<4;i++){info.Controls[i].Present=(i==0);info.Controls[i].RawData=0;info.Controls[i].Plugin=PLUGIN_MEMPAK;info.Controls[i].Type=CONT_TYPE_STANDARD;}logmsg(3,"M8.5 INPUT: N64 Controller #1 initialized");}
EXPORT int CALL RomOpen(void){return 1;} EXPORT void CALL RomClosed(void){}
EXPORT void CALL GetKeys(int c,BUTTONS* k){reloadcfg_if_changed();if(!k)return;k->Value=0;if(c!=0)return;unsigned int m=0;int x=0,y=0;XINPUT_STATE s;ZeroMemory(&s,sizeof(s));XINPUT_GAMEPAD* g=NULL;if(gxget&&(gxget(gindex,&s)==0||(findpad()&&gxget(gindex,&s)==0)))g=&s.Gamepad;
 int active[BIND_COUNT];for(int i=0;i<BIND_COUNT;i++)active[i]=keydown(i)||(g&&padpressed(binds[i].pad,g));
 if(active[DPAD_RIGHT])m|=1u<<0;if(active[DPAD_LEFT])m|=1u<<1;if(active[DPAD_DOWN])m|=1u<<2;if(active[DPAD_UP])m|=1u<<3;if(active[N64_START])m|=1u<<4;if(active[N64_Z])m|=1u<<5;if(active[N64_B])m|=1u<<6;if(active[N64_A])m|=1u<<7;if(active[C_RIGHT])m|=1u<<8;if(active[C_LEFT])m|=1u<<9;if(active[C_DOWN])m|=1u<<10;if(active[C_UP])m|=1u<<11;if(active[N64_R])m|=1u<<12;if(active[N64_L])m|=1u<<13;
 int sl=keydown(STICK_LEFT)?80:(g?padamount(binds[STICK_LEFT].pad,g):0),sr=keydown(STICK_RIGHT)?80:(g?padamount(binds[STICK_RIGHT].pad,g):0),sd=keydown(STICK_DOWN)?80:(g?padamount(binds[STICK_DOWN].pad,g):0),su=keydown(STICK_UP)?80:(g?padamount(binds[STICK_UP].pad,g):0);x=sr-sl;y=su-sd;if(x>80)x=80;if(x<-80)x=-80;if(y>80)y=80;if(y<-80)y=-80;k->buttons=(unsigned short)m;k->X_AXIS=(signed char)x;k->Y_AXIS=(signed char)y;}
EXPORT void CALL ControllerCommand(int c,unsigned char* cmd){(void)c;(void)cmd;} EXPORT void CALL ReadController(int c,unsigned char* cmd){(void)c;(void)cmd;} EXPORT void CALL SDL_KeyDown(int keymod,int keysym){(void)keymod;(void)keysym;} EXPORT void CALL SDL_KeyUp(int keymod,int keysym){(void)keymod;(void)keysym;}
