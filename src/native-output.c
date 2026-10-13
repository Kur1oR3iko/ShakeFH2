#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <xinput.h>
#include <string.h>

/* Public Windows.Gaming.Input COM ABI. The output lives inside Azimuth so
   Windows associates it with the foreground game rather than the companion. */
typedef struct { double lm, rm, lt, rt; } Vibration;
typedef HRESULT (WINAPI *RoInit)(UINT);
typedef HRESULT (WINAPI *CreateString)(PCWSTR, UINT, void **);
typedef HRESULT (WINAPI *DeleteString)(void *);
typedef HRESULT (WINAPI *GetFactory)(void *, const GUID *, void **);
typedef HRESULT (WINAPI *Query)(void *, const GUID *, void **);
typedef ULONG (WINAPI *Release)(void *);
typedef HRESULT (WINAPI *GetVector)(void *, void **);
typedef HRESULT (WINAPI *GetSize)(void *, UINT *);
typedef HRESULT (WINAPI *GetAt)(void *, UINT, void **);
typedef HRESULT (WINAPI *Put)(void *, Vibration);
#define METHOD(obj, slot, type) ((type)((*(void ***)(obj))[slot]))

/* Shared layout is versioned by the host: floats[4], sequence, call lease,
   spin lock, padding, game index, strength, host PID, state HRESULT,
   GetTickCount64 heartbeat, selected pad, output sequence, native put HRESULT. */
typedef struct {
    float value[4]; volatile LONG sequence, lease, lock, reserved;
    UINT game_id; volatile LONG strength, host_pid, state;
    volatile ULONGLONG heartbeat;
    volatile LONG selected, output_sequence, put_result, reserved2;
} Shared;
static const GUID statics_id = {0x8bbce529,0xd49c,0x39e9,{0x95,0x60,0xe4,0x7d,0xde,0x96,0xb7,0xc8}};
static const GUID gamepad_id = {0xbc7bb43c,0x0a69,0x3903,{0x9e,0x9d,0xa5,0x0f,0x86,0xa4,0x5d,0xe5}};
static volatile LONG started;
static const GUID controller_id={0x1baf6522,0x5f64,0x42c5,{0x82,0x67,0xb9,0xfe,0x22,0x15,0xbf,0xbd}};
static const GUID raw_statics_id={0xeb8d0792,0xe95a,0x4b19,{0xaf,0xc7,0x0a,0x59,0xf8,0xbf,0x75,0x9e}};
typedef HRESULT (WINAPI *FromController)(void *,void *,void **);
typedef HRESULT (WINAPI *GetUShort)(void *,USHORT *);
static int virtual_pad(void *raw_factory,void *pad) {
    void *controller=0,*raw=0; USHORT vendor=0,product=0;
    if(!raw_factory) return 0;
    HRESULT hr=METHOD(pad,0,Query)(pad,&controller_id,&controller);
    if(SUCCEEDED(hr)) hr=METHOD(raw_factory,11,FromController)(raw_factory,controller,&raw);
    if(SUCCEEDED(hr)&&raw) { METHOD(raw,9,GetUShort)(raw,&product); METHOD(raw,10,GetUShort)(raw,&vendor); }
    if(raw) METHOD(raw,2,Release)(raw); if(controller) METHOD(controller,2,Release)(controller);
    return vendor==0x045e&&product==0x028e;
}
static double clamp(float f) { return !(f >= 0.0f) ? 0.0 : f > 1.0f ? 1.0 : f; }
static void release(void **obj) { if (*obj) { METHOD(*obj,2,Release)(*obj); *obj=0; } }

static DWORD WINAPI run(void *parameter) {
    Shared *s=(Shared *)parameter;
    HMODULE combase=LoadLibraryW(L"combase.dll");
    if (!combase) { s->state=(LONG)0x80004005; return 1; }
    RoInit init=(RoInit)GetProcAddress(combase,"RoInitialize");
    CreateString create=(CreateString)GetProcAddress(combase,"WindowsCreateString");
    DeleteString destroy=(DeleteString)GetProcAddress(combase,"WindowsDeleteString");
    GetFactory activate=(GetFactory)GetProcAddress(combase,"RoGetActivationFactory");
    if (!init || !create || !destroy || !activate) { s->state=(LONG)0x80004001; return 2; }
    HRESULT hr=init(1); if (FAILED(hr)) { s->state=hr; return 3; }
    void *name=0,*factory=0,*pad=0;
    const WCHAR class_name[]=L"Windows.Gaming.Input.Gamepad";
    hr=create(class_name,(UINT)(sizeof(class_name)/sizeof(WCHAR)-1),&name);
    if (SUCCEEDED(hr)) hr=activate(name,&statics_id,&factory);
    if (name) destroy(name);
    if (FAILED(hr)) { s->state=hr; return 4; }
    void *raw_factory=0; const WCHAR raw_name[]=L"Windows.Gaming.Input.RawGameController";
    if(SUCCEEDED(create(raw_name,(UINT)(sizeof(raw_name)/sizeof(WCHAR)-1),&name))) { activate(name,&raw_statics_id,&raw_factory); destroy(name); }
    UINT selection=0xffffffff; DWORD last_check=0;
    LONG last_sequence=0; ULONGLONG last_sample=GetTickCount64(),clear_until=0; int was_active=0,clear_pending=0;
    for (;;) {
        ULONGLONG now=GetTickCount64();
        if (!pad || selection!=(UINT)s->selected || GetTickCount()-last_check>1000) {
            void *vector=0,*candidate=0,*gamepad=0; UINT count=0;
            hr=METHOD(factory,10,GetVector)(factory,&vector);
            if (SUCCEEDED(hr)) hr=METHOD(vector,7,GetSize)(vector,&count);
            if(SUCCEEDED(hr)) {
                UINT physical=0; hr=(HRESULT)0x8007048f;
                for(UINT i=0;i<count;i++) {
                    void *item=0; HRESULT next=METHOD(vector,6,GetAt)(vector,i,&item);
                    if(SUCCEEDED(next)&&!virtual_pad(raw_factory,item)) {
                        if(physical++==(UINT)s->selected) { candidate=item; hr=S_OK; break; }
                    }
                    if(item) METHOD(item,2,Release)(item);
                }
            }
            if (SUCCEEDED(hr)) hr=METHOD(candidate,0,Query)(candidate,&gamepad_id,&gamepad);
            release(&candidate); release(&vector); last_check=GetTickCount();
            if (FAILED(hr)) {
                InterlockedExchange(&s->lease,0); s->state=hr;
                if (pad) { Vibration zero={0}; METHOD(pad,7,Put)(pad,zero); release(&pad); }
                Sleep(250); continue;
            }
            if (pad!=gamepad) { if (pad) { Vibration zero={0}; METHOD(pad,7,Put)(pad,zero); } release(&pad); pad=gamepad; }
            else release(&gamepad);
            selection=(UINT)s->selected; s->state=1;
        }
        int active=s->host_pid!=0 && now-s->heartbeat<1000 && s->lease>0;
        if (active) {
            s->reserved2=0; clear_pending=0;
            LONG seq=s->sequence; float value[4];
            if (seq&1) { Sleep(1); continue; }
            MemoryBarrier(); for (int i=0;i<4;i++) value[i]=s->value[i]; MemoryBarrier();
            if (seq!=s->sequence) { Sleep(1); continue; }
            if (seq!=last_sequence) { last_sequence=seq; last_sample=now; }
            double strength=s->strength/100.0; if (strength<0) strength=0; if(strength>1) strength=1;
            Vibration output={0};
            if (seq && now-last_sample<750) { output.lm=clamp(value[0]); output.rm=clamp(value[1]); output.lt=clamp(value[2])*strength; output.rt=clamp(value[3])*strength; }
            hr=METHOD(pad,7,Put)(pad,output); s->put_result=hr; s->output_sequence=seq;
            if (FAILED(hr)) { InterlockedExchange(&s->lease,0); s->state=hr; release(&pad); }
            else s->state=1;
            was_active=1;
        } else {
            if(was_active||s->reserved2==0) { clear_pending=1; clear_until=now+200; was_active=0; }
            if(clear_pending) {
                Vibration zero={0}; hr=METHOD(pad,7,Put)(pad,zero);
                if(SUCCEEDED(hr)) s->reserved2=1;
                InterlockedExchange(&s->lease,0);
                DWORD foreground_pid=0; GetWindowThreadProcessId(GetForegroundWindow(),&foreground_pid);
                if(foreground_pid==GetCurrentProcessId()&&now>=clear_until) clear_pending=0;
            }
        }
        Sleep(8);
    }
}

__declspec(dllexport) DWORD WINAPI StartOutput(void *parameter) {
    if (InterlockedCompareExchange(&started,1,0)!=0) return 0;
    HANDLE thread=CreateThread(0,0,run,parameter,0,0);
    if (!thread) { started=0; ((Shared *)parameter)->state=(LONG)HRESULT_FROM_WIN32(GetLastError()); return 1; }
    CloseHandle(thread); return 0;
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) { (void)reserved; if (reason==DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance); return TRUE; }

/* Azimuth-specific input table proxy. Preserve physical input, overlay keyboard
   state, and hide only this app's virtual pad from the physical-device scan. */
typedef struct {
    volatile DWORD host,enabled,virtual_index; volatile LONG sequence;
    volatile ULONGLONG heartbeat;
    XINPUT_GAMEPAD keyboard;
    volatile LONG status,physical_index,calls;
} InputShared;
typedef DWORD (WINAPI *GetStateFn)(DWORD,XINPUT_STATE *);
typedef DWORD (WINAPI *SetStateFn)(DWORD,XINPUT_VIBRATION *);
static InputShared *volatile input_shared;
static GetStateFn real_get;
static SetStateFn real_set;
static void **get_slot;
static volatile LONG input_started;
static SRWLOCK report_lock=SRWLOCK_INIT;
static XINPUT_GAMEPAD last_keyboard,last_combined;
static DWORD packet;
static int input_active(InputShared *s) { return s&&s->enabled&&s->host&&GetTickCount64()-s->heartbeat<1000; }
__declspec(dllexport) DWORD WINAPI TestInputMix(const XINPUT_GAMEPAD *physical,const XINPUT_GAMEPAD *keyboard,XINPUT_GAMEPAD *output) {
    *output=*physical;
    output->wButtons|=keyboard->wButtons;
    if(keyboard->bLeftTrigger>output->bLeftTrigger) output->bLeftTrigger=keyboard->bLeftTrigger;
    if(keyboard->bRightTrigger>output->bRightTrigger) output->bRightTrigger=keyboard->bRightTrigger;
    if(keyboard->sThumbLX) output->sThumbLX=keyboard->sThumbLX;
    if(keyboard->sThumbLY) output->sThumbLY=keyboard->sThumbLY;
    if(keyboard->sThumbRX) output->sThumbRX=keyboard->sThumbRX;
    if(keyboard->sThumbRY) output->sThumbRY=keyboard->sThumbRY;
    return 0;
}
static DWORD WINAPI merged_get(DWORD user,XINPUT_STATE *state) {
    InputShared *s=input_shared;
    if(!input_active(s)) return real_get(user,state);
    if(user!=0) return ERROR_DEVICE_NOT_CONNECTED;
    if(!state) return ERROR_BAD_ARGUMENTS;
    XINPUT_STATE physical={0}; LONG selected=-1;
    for(DWORD i=0;i<4;i++) if(i!=s->virtual_index&&real_get(i,&physical)==ERROR_SUCCESS) { selected=(LONG)i; break; }
    if(selected<0) ZeroMemory(&physical,sizeof(physical)); s->physical_index=selected;
    AcquireSRWLockExclusive(&report_lock);
    for(int retry=0;retry<3;retry++) {
        LONG before=s->sequence; if(before&1) continue;
        MemoryBarrier(); XINPUT_GAMEPAD copy=s->keyboard; MemoryBarrier();
        if(before==s->sequence) { last_keyboard=copy; break; }
    }
    XINPUT_GAMEPAD combined; TestInputMix(&physical.Gamepad,&last_keyboard,&combined);
    if(memcmp(&last_combined,&combined,sizeof(combined))!=0) { last_combined=combined; packet++; }
    state->Gamepad=combined; state->dwPacketNumber=packet;
    ReleaseSRWLockExclusive(&report_lock); InterlockedIncrement(&s->calls); return ERROR_SUCCESS;
}
static DWORD WINAPI merged_set(DWORD user,XINPUT_VIBRATION *vibration) {
    InputShared *s=input_shared;
    if(!input_active(s)) return real_set(user,vibration);
    return s->physical_index>=0?real_set((DWORD)s->physical_index,vibration):ERROR_SUCCESS;
}
static void **find_table(HMODULE module) {
    BYTE *base=(BYTE *)module; IMAGE_DOS_HEADER *dos=(IMAGE_DOS_HEADER *)base;
    IMAGE_NT_HEADERS64 *nt=(IMAGE_NT_HEADERS64 *)(base+dos->e_lfanew);
    IMAGE_SECTION_HEADER *section=IMAGE_FIRST_SECTION(nt);
    for(UINT i=0;i<nt->FileHeader.NumberOfSections;i++) if(section[i].Characteristics&IMAGE_SCN_MEM_WRITE) {
        BYTE *start=base+section[i].VirtualAddress; DWORD size=section[i].Misc.VirtualSize;
        for(DWORD offset=0;offset+2*sizeof(void *)<=size;offset+=sizeof(void *)) {
            void **p=(void **)(start+offset);
            if(p[0]==(void *)real_get&&p[1]==(void *)real_set) return p;
        }
    }
    return 0;
}
static DWORD WINAPI input_loop(void *unused) {
    (void)unused; HMODULE xinput=LoadLibraryW(L"xinput1_4.dll");
    real_get=(GetStateFn)GetProcAddress(xinput,"XInputGetState"); real_set=(SetStateFn)GetProcAddress(xinput,"XInputSetState");
    for(;;) {
        InputShared *s=input_shared;
        if(input_active(s)) {
            if(!get_slot) {
                get_slot=find_table(GetModuleHandleW(0));
                if(get_slot) { InterlockedExchangePointer(get_slot+1,(void *)merged_set); InterlockedExchangePointer(get_slot,(void *)merged_get); }
            }
            s->status=get_slot?1:0;
        } else if(get_slot) {
            InterlockedExchangePointer(get_slot,(void *)real_get); InterlockedExchangePointer(get_slot+1,(void *)real_set); get_slot=0;
            if(s) s->status=0;
        }
        Sleep(10);
    }
}
__declspec(dllexport) DWORD WINAPI StartInputRoute(void *parameter) {
    InterlockedExchangePointer((void *volatile *)&input_shared,parameter);
    if(InterlockedCompareExchange(&input_started,1,0)!=0) return 0;
    HANDLE thread=CreateThread(0,0,input_loop,0,0,0);
    if(!thread) { input_started=0; return 1; } CloseHandle(thread); return 0;
}
