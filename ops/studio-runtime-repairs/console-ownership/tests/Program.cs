using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

public class StubConsole { public int Aborts; public void Abort() { Aborts++; } }
public class Probe
{
    public static StubConsole ConsoleInstance;
    public static bool Owns;
    public static bool AllocationResult;
    static int frees;
    public static bool AllocateStub() => AllocationResult;
    public static void ControlCapture() => throw new Exception("Unpopulated CIL fixture");
    public static void CandidateCapture() => throw new Exception("Unpopulated CIL fixture");
    public static void ControlClose() => throw new Exception("Unpopulated CIL fixture");
    public static void CandidateClose() => throw new Exception("Unpopulated CIL fixture");
    public static bool FreeConsoleRecorded() { frees++; return FreeConsole(); }
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint create, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(IntPtr handle, byte[] data, int count, out int written, IntPtr over);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcess(string app, StringBuilder command, IntPtr procAttr, IntPtr threadAttr, bool inherit, uint flags, IntPtr environment, string cwd, ref Startup startup, out ProcessInfo info);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Startup { public uint cb; public string reserved, desktop, title; public uint x,y,cx,cy,xchars,ychars,fill,flags; public ushort show,reserved2; public IntPtr reservedPtr,input,output,error; }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr process,thread; public uint processId,threadId; }

    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch(Exception e) { if(args.Length>1 && args[0]!="--launch") File.WriteAllText(args[1],JsonSerializer.Serialize(new {passed=false,error=e.ToString()})); else System.Console.WriteLine(e); return 2; }
    }
    static int Run(string[] args)
    {
        if (args[0] == "--launch") return Launch(args[1]);
        string mode=args[0], output=args[1];
        var initial=GetConsoleWindow();
        if(initial==IntPtr.Zero) throw new Exception("Hidden process has no console.");
        var bep=CreateFile("CONOUT$",0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
        var uc=CreateFile("CONOUT$",0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(bep.ToInt64()==-1 || uc.ToInt64()==-1) throw new Exception("CONOUT$ open failed.");
        var writer=new StreamWriter(new FileStream(new SafeFileHandle(bep,false),FileAccess.Write),new UTF8Encoding(false)){AutoFlush=true};
        writer.Write("before\n");
        ConsoleInstance=mode.EndsWith("-null")?null:new StubConsole();
        Owns=mode.StartsWith("owned");
        bool original=mode=="control";
        if(original) ControlClose(); else CandidateClose();
        bool bepOk=Write(bep,out int bepError),ucOk=Write(uc,out int ucError);
        string exception=null;
        try {writer.Write("after\n");} catch(IOException e) {exception=e.GetType().Name;}
        int countAfterFirst=frees;
        if(!original) CandidateClose();
        bool shouldFree=original||mode.StartsWith("owned");
        bool ownsAfter=Owns;
        AllocationResult=true; Owns=false; ControlCapture();bool originalDiscards=!Owns;
        CandidateCapture();bool capturesSuccess=Owns;
        AllocationResult=false;CandidateCapture();bool capturesFailure=!Owns;
        int aborts = ConsoleInstance?.Aborts??0;
        int expectedAborts = mode.EndsWith("-null")?0:original?1:2;
        bool passed=(shouldFree ? !bepOk && bepError==6 && !ucOk && ucError==6 && exception!=null && countAfterFirst==1 && frees==1 && !ownsAfter : bepOk&&ucOk&&exception==null&&frees==0&&GetConsoleWindow()==initial)&&originalDiscards&&capturesSuccess&&capturesFailure&&aborts==expectedAborts;
        var receipt=new {mode,passed,initialWindowPresent=initial!=IntPtr.Zero,bepOk,bepError,ucOk,ucError,streamWriterException=exception,freeCalls=frees,aborts=ConsoleInstance?.Aborts??0,ownsAfter,windowPresentAfter=GetConsoleWindow()!=IntPtr.Zero,originalDiscards,capturesSuccess,capturesFailure,source="Close CIL extracted from exact original/candidate; Abort substituted with counting stub; Win32 console APIs real. Allocation result-consumer CIL extracted with bool-return stub."};
        File.WriteAllText(output,JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));
        return passed?0:1;
    }
    static bool Write(IntPtr h,out int error) {byte[] data=Encoding.UTF8.GetBytes("sentinel\n");bool ok=WriteFile(h,data,data.Length,out int count,IntPtr.Zero);error=ok?0:Marshal.GetLastWin32Error();return ok&&count==data.Length;}
    static int Launch(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach(string mode in new[]{"control","borrowed","borrowed-null","owned","owned-null"})
        {
            string file=Path.Combine(Path.GetFullPath(directory),mode+".json");
            if(File.Exists(file))throw new IOException("Result exists.");
            var start=new Startup{cb=(uint)Marshal.SizeOf<Startup>(),flags=1,show=0};
            string dotnet=Environment.ProcessPath,dll=Assembly.GetExecutingAssembly().Location;
            var command=new StringBuilder("\""+dotnet+"\" \""+dll+"\" "+mode+" \""+file+"\"");
            if(!CreateProcess(dotnet,command,IntPtr.Zero,IntPtr.Zero,false,0x10,IntPtr.Zero,Environment.CurrentDirectory,ref start,out var child))throw new Exception("CreateProcess "+Marshal.GetLastWin32Error());
            uint wait=WaitForSingleObject(child.process,15000);
            CloseHandle(child.thread);CloseHandle(child.process);
            if(wait!=0)throw new Exception("Child timeout "+mode);
            var json=File.ReadAllText(file);System.Console.WriteLine(json);
            if(!JsonDocument.Parse(json).RootElement.GetProperty("passed").GetBoolean())return 1;
        }
        return 0;
    }
}
