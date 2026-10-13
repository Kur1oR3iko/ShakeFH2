#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>

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
    UINT selection=0xffffffff; DWORD last_check=0;
    LONG last_sequence=0; ULONGLONG last_sample=GetTickCount64(); int was_active=0;
    for (;;) {
        ULONGLONG now=GetTickCount64();
        if (!pad || selection!=(UINT)s->selected || GetTickCount()-last_check>1000) {
            void *vector=0,*candidate=0,*gamepad=0; UINT count=0;
            hr=METHOD(factory,10,GetVector)(factory,&vector);
            if (SUCCEEDED(hr)) hr=METHOD(vector,7,GetSize)(vector,&count);
            if (SUCCEEDED(hr) && (UINT)s->selected<count) hr=METHOD(vector,6,GetAt)(vector,(UINT)s->selected,&candidate);
            else if (SUCCEEDED(hr)) hr=(HRESULT)0x8007048f;
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
        } else if (was_active) {
            Vibration zero={0}; METHOD(pad,7,Put)(pad,zero);
            InterlockedExchange(&s->lease,0); was_active=0;
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
