using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TrackerOfTime.V2.M8_5.Desktop.Interop;

public sealed class EmbeddedGameHost : HwndHost
{
    const int GWL_STYLE=-16, GWL_EXSTYLE=-20, WM_SIZE=0x0005, WM_ERASEBKGND=0x0014, WM_EXITSIZEMOVE=0x0232;
    const uint EVENT_OBJECT_SHOW=0x8002, WINEVENT_OUTOFCONTEXT=0x0000; const int OBJID_WINDOW=0;
    const long WS_CHILD=0x40000000L, WS_VISIBLE=0x10000000L, WS_POPUP=0x80000000L, SS_BLACKRECT=0x00000004L,
        WS_CAPTION=0x00C00000L, WS_THICKFRAME=0x00040000L, WS_BORDER=0x00800000L,
        WS_DLGFRAME=0x00400000L, WS_SYSMENU=0x00080000L, WS_MINIMIZEBOX=0x00020000L,
        WS_MAXIMIZEBOX=0x00010000L;
    const long WS_EX_APPWINDOW=0x00040000L, WS_EX_WINDOWEDGE=0x00000100L, WS_EX_CLIENTEDGE=0x00000200L, WS_EX_TOOLWINDOW=0x00000080L;
    const uint SWP_NOZORDER=0x0004, SWP_NOACTIVATE=0x0010, SWP_FRAMECHANGED=0x0020, SWP_SHOWWINDOW=0x0040;
    const int SW_HIDE=0, SW_SHOWNA=8;
    IntPtr _holder,_game,_oldParent,_showHook,_blackBrush;
    WinEventDelegate? _showHookProc;
    long _oldStyle,_oldExStyle;
    int _pid;
    public string Renderer { get; set; } = "Rice";
    readonly DispatcherTimer _guard;

    bool IsGlide => Renderer.Equals("Glide64mk2",StringComparison.OrdinalIgnoreCase);

    public EmbeddedGameHost()
    {
        _guard=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(50)};
        _guard.Tick+=(_,_)=>GuardHostedWindow();
    }

    public bool IsAttached=>_game!=IntPtr.Zero;
    public int ProcessId=>_pid;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _holder=CreateWindowEx(0,"STATIC","",WS_CHILD|WS_VISIBLE|SS_BLACKRECT,0,0,100,100,hwndParent.Handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        _blackBrush=CreateSolidBrush(0x00000000);
        _guard.Start();
        return new HandleRef(this,_holder);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _guard.Stop(); Detach();
        if(_holder!=IntPtr.Zero){DestroyWindow(_holder);_holder=IntPtr.Zero;}
        if(_blackBrush!=IntPtr.Zero){DeleteObject(_blackBrush);_blackBrush=IntPtr.Zero;}
    }

    protected override IntPtr WndProc(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(msg==WM_ERASEBKGND && _blackBrush!=IntPtr.Zero && GetClientRect(hwnd,out var bg)){FillRect(wParam,ref bg,_blackBrush);handled=true;return new IntPtr(1);}
        if(msg==WM_SIZE)SyncBounds();
        return base.WndProc(hwnd,msg,wParam,lParam,ref handled);
    }

    public async Task<bool> AttachProcessAsync(int pid,TimeSpan timeout,CancellationToken ct=default)
    {
        _pid=pid; InstallShowGuard(pid);
        var until=DateTime.UtcNow+timeout;
        while(DateTime.UtcNow<until)
        {
            ct.ThrowIfCancellationRequested();
            var w=FindBestTopLevelWindow(pid, includeHidden:true);
            if(w!=IntPtr.Zero && AttachWindow(w))
            {
                await Task.Delay(350,ct); GuardHostedWindow(); SyncBounds(); return true;
            }
            await Task.Delay(50,ct);
        }
        RemoveShowGuard(); return IsAttached;
    }

    void InstallShowGuard(int pid)
    {
        RemoveShowGuard();
        _showHookProc=(hook,eventType,hwnd,idObject,idChild,eventThread,eventTime)=>
        {
            if(hwnd==IntPtr.Zero||idObject!=OBJID_WINDOW||GetParent(hwnd)!=IntPtr.Zero)return;
            GetWindowThreadProcessId(hwnd,out var windowPid); if(windowPid!=(uint)_pid)return;
            // Hide the native renderer until it has been re-parented into the WPF host.
            // This prevents the temporary standalone renderer-window flash.
            ShowWindow(hwnd,SW_HIDE);
            Dispatcher.BeginInvoke(new Action(()=>{if(_holder!=IntPtr.Zero&&_pid>0)AttachWindow(hwnd);}),DispatcherPriority.Send);
        };
        _showHook=SetWinEventHook(EVENT_OBJECT_SHOW,EVENT_OBJECT_SHOW,IntPtr.Zero,_showHookProc,(uint)pid,0,WINEVENT_OUTOFCONTEXT);
    }
    void RemoveShowGuard(){if(_showHook!=IntPtr.Zero){UnhookWinEvent(_showHook);_showHook=IntPtr.Zero;}_showHookProc=null;}

    bool AttachWindow(IntPtr hwnd)
    {
        if(_holder==IntPtr.Zero||hwnd==IntPtr.Zero)return false;
        if(hwnd==_game){SyncBounds();return true;}
        if(_game!=IntPtr.Zero&&_game!=hwnd)ShowWindow(_game,SW_HIDE);

        ShowWindow(hwnd,SW_HIDE);
        _game=hwnd;
        _oldStyle=GetWindowLongPtr(hwnd,GWL_STYLE).ToInt64();
        _oldExStyle=GetWindowLongPtr(hwnd,GWL_EXSTYLE).ToInt64();
        _oldParent=GetParent(hwnd);

        ShowWindow(_holder,SW_SHOWNA);
        var hostedStyle=(_oldStyle|WS_CHILD|WS_VISIBLE)&~(WS_POPUP|WS_CAPTION|WS_THICKFRAME|WS_BORDER|WS_DLGFRAME|WS_SYSMENU|WS_MINIMIZEBOX|WS_MAXIMIZEBOX);
        var hostedExStyle=_oldExStyle&~(WS_EX_APPWINDOW|WS_EX_WINDOWEDGE|WS_EX_CLIENTEDGE);
        SetWindowLongPtr(hwnd,GWL_STYLE,new IntPtr(hostedStyle));
        SetWindowLongPtr(hwnd,GWL_EXSTYLE,new IntPtr(hostedExStyle));
        SetParent(hwnd,_holder);
        if(GetParent(hwnd)!=_holder){_game=IntPtr.Zero;return false;}

        SyncBounds();
        ShowWindow(hwnd,SW_SHOWNA);
        SyncBounds();
        RemoveShowGuard();
        return true;
    }

    void GuardHostedWindow()
    {
        if(_pid<=0||_holder==IntPtr.Zero)return;
        var top=FindBestTopLevelWindow(_pid,includeHidden:false);
        if(top!=IntPtr.Zero&&top!=_game)AttachWindow(top);else SyncBounds();
    }

    public void SyncBounds()
    {
        if(_game==IntPtr.Zero||_holder==IntPtr.Zero||!IsWindow(_game)||!GetClientRect(_holder,out var r))return;
        var w=Math.Max(1,r.Right-r.Left);
        var h=Math.Max(1,r.Bottom-r.Top);
        if(IsGlide)
        {
            // functional ResizeVideoOutput. Preserve that real render surface and center it
            // in the available host instead of resizing the HWND/GL viewport.
            const int nativeW=640, nativeH=480;
            var x=Math.Max(0,(w-nativeW)/2);
            var y=Math.Max(0,(h-nativeH)/2);
            SetWindowPos(_game,IntPtr.Zero,x,y,nativeW,nativeH,SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED|SWP_SHOWWINDOW);
            MoveWindow(_game,x,y,nativeW,nativeH,true);
            return;
        }

        SetWindowPos(_game,IntPtr.Zero,0,0,w,h,SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED|SWP_SHOWWINDOW);
        MoveWindow(_game,0,0,w,h,true); ResizeRendererChildren(_game,w,h); NotifyRendererResize(_game,w,h);
    }

    static void ResizeRendererChildren(IntPtr parent,int width,int height)
    {
        EnumChildWindows(parent,(child,_)=>{if(IsWindow(child)){SetWindowPos(child,IntPtr.Zero,0,0,width,height,SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED|SWP_SHOWWINDOW);NotifyRendererResize(child,width,height);}return true;},IntPtr.Zero);
    }
    static void NotifyRendererResize(IntPtr hwnd,int width,int height){var packed=new IntPtr(((height&0xFFFF)<<16)|(width&0xFFFF));SendMessage(hwnd,WM_SIZE,IntPtr.Zero,packed);SendMessage(hwnd,WM_EXITSIZEMOVE,IntPtr.Zero,IntPtr.Zero);RedrawWindow(hwnd,IntPtr.Zero,IntPtr.Zero,0x0001|0x0100|0x0400);}

    public void FocusGame()
    {
        // Direct-XInput provider now polls keyboard state globally and merges it with XInput, so keyboard
        // operation no longer depends on moving Win32 focus away from WPF. Keep this only for pure Input-SDL.
        if(_game==IntPtr.Zero||!IsWindow(_game))return;
        var focusTarget=_game; long bestArea=0;
        EnumChildWindows(_game,(child,_)=>{if(!IsWindowVisible(child)||!GetWindowRect(child,out var rr))return true;var area=(long)Math.Max(0,rr.Right-rr.Left)*Math.Max(0,rr.Bottom-rr.Top);if(area>bestArea){bestArea=area;focusTarget=child;}return true;},IntPtr.Zero);
        var targetThread=GetWindowThreadProcessId(focusTarget,out _);var currentThread=GetCurrentThreadId();var attached=targetThread!=0&&targetThread!=currentThread&&AttachThreadInput(currentThread,targetThread,true);
        try{SetFocus(focusTarget);}finally{if(attached)AttachThreadInput(currentThread,targetThread,false);}
    }

    public void Detach()
    {
        if(_holder!=IntPtr.Zero)ShowWindow(_holder,SW_SHOWNA);
        _pid=0;RemoveShowGuard();if(_game==IntPtr.Zero)return;
        var game=_game;_game=IntPtr.Zero;if(IsWindow(game))RestoreWindow(game);
        _oldParent=IntPtr.Zero;
    }
    void RestoreCurrentWindow(){var g=_game;_game=IntPtr.Zero;if(g!=IntPtr.Zero&&IsWindow(g))RestoreWindow(g);}
    void RestoreWindow(IntPtr game){ShowWindow(game,SW_HIDE);SetParent(game,_oldParent);SetWindowLongPtr(game,GWL_STYLE,new IntPtr(_oldStyle));SetWindowLongPtr(game,GWL_EXSTYLE,new IntPtr(_oldExStyle));SetWindowPos(game,IntPtr.Zero,0,0,800,600,SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED);}

    protected override void OnWindowPositionChanged(System.Windows.Rect rc){base.OnWindowPositionChanged(rc);SyncBounds();}

    static IntPtr FindBestTopLevelWindow(int pid,bool includeHidden)
    {
        IntPtr best=IntPtr.Zero;long bestScore=0;
        EnumWindows((h,_)=>{GetWindowThreadProcessId(h,out var p);if(p!=(uint)pid||(!includeHidden&&!IsWindowVisible(h))||GetParent(h)!=IntPtr.Zero||!GetWindowRect(h,out var r))return true;var w=Math.Max(0,r.Right-r.Left);var hh=Math.Max(0,r.Bottom-r.Top);var score=(long)w*hh;if(score>bestScore){bestScore=score;best=h;}return true;},IntPtr.Zero);return best;
    }

    [StructLayout(LayoutKind.Sequential)]struct RECT{public int Left,Top,Right,Bottom;}
    delegate bool EnumWindowsProc(IntPtr h,IntPtr l);delegate bool EnumChildProc(IntPtr h,IntPtr l);delegate void WinEventDelegate(IntPtr hook,uint evt,IntPtr hwnd,int idObject,int idChild,uint thread,uint time);
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr CreateWindowEx(int ex,string cls,string name,long style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr inst,IntPtr param);
    [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr h);[DllImport("user32.dll")]static extern bool EnumWindows(EnumWindowsProc cb,IntPtr l);[DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr p,EnumChildProc cb,IntPtr l);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);[DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);[DllImport("user32.dll")]static extern bool IsWindow(IntPtr h);[DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out RECT r);[DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out RECT r);
    [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr h);[DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetParent(IntPtr c,IntPtr n);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr h,int i);[DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr h,int i,IntPtr v);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f);[DllImport("user32.dll")]static extern bool MoveWindow(IntPtr h,int x,int y,int w,int hgt,bool repaint);[DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int cmd);
    [DllImport("user32.dll")]static extern int FillRect(IntPtr hdc,ref RECT rect,IntPtr brush);[DllImport("gdi32.dll")]static extern IntPtr CreateSolidBrush(uint color);[DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);[DllImport("user32.dll")]static extern bool RedrawWindow(IntPtr h,IntPtr r,IntPtr region,uint flags);[DllImport("user32.dll")]static extern IntPtr SetFocus(IntPtr h);[DllImport("user32.dll")]static extern bool AttachThreadInput(uint a,uint b,bool attach);[DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr mod,WinEventDelegate cb,uint pid,uint tid,uint flags);[DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
}
