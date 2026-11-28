using System;
using System.IO;
using System.Linq;
using System.Reflection;
public static class BindingProbe
{
    public static int Run(string candidate,string helper,string game,string tokens)
    {
        var roots=new[]{Path.GetDirectoryName(candidate),Path.Combine(game,"BepInEx/core"),Path.Combine(game,"BepInEx/plugins"),Path.Combine(game,"CharaStudio_Data/Managed")};
        ResolveEventHandler resolver=(s,e)=>{string name=new AssemblyName(e.Name).Name+".dll";foreach(string root in roots){string path=Path.Combine(root,name);if(File.Exists(path))return Assembly.LoadFrom(path);}return null;};
        AppDomain.CurrentDomain.AssemblyResolve+=resolver;
        int checks=0,failures=0;
        try
        {
            var a=Assembly.LoadFrom(candidate);var h=Assembly.LoadFrom(helper);
            foreach(string tokenText in tokens.Split(','))
            {
                checks++;
                try
                {
                    var method=a.ManifestModule.ResolveMethod(Convert.ToInt32(tokenText,16));
                    if(method.DeclaringType.FullName!="Codex.DragDollPlusHistory.InactiveHistory"||method.DeclaringType.Assembly!=h)throw new InvalidOperationException("Resolved to wrong assembly");
                    var args=method.GetParameters().Select(p=>p.ParameterType==typeof(bool)?(object)false:null).ToArray();
                    try{method.Invoke(null,args);if(method.Name=="PreparingCommit"||method.Name=="TryApply")throw new InvalidOperationException("Expected null sentinel rejection");}
                    catch(TargetInvocationException e){if(method.Name=="PreparingCommit"&&e.InnerException is ArgumentNullException||method.Name=="TryApply"&&e.InnerException is NullReferenceException){}else throw;}
                    Console.WriteLine("BINDING_PASS "+method.Name+" externalAssembly="+method.DeclaringType.Assembly.GetName().Name);
                }
                catch(Exception e){failures++;var actual=e is TargetInvocationException?e.InnerException:e;Console.WriteLine("BINDING_FAIL token="+tokenText+" type="+actual.GetType().FullName);}
            }
        }
        finally{AppDomain.CurrentDomain.AssemblyResolve-=resolver;}
        Console.WriteLine("bindingChecks="+checks+" failures="+failures+" runtime="+Environment.Version);
        return failures==0?0:1;
    }
}
