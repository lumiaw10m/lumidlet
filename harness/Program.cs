using System;
using System.IO;
using Lumidlet.Vm;

namespace Lumidlet.Harness
{
    static partial class Program
    {
        static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "main") return RunMain(args[1], args[2]);
            return RunMidlet(args);
        }

        static int RunMain(string jarPath, string cls)
        {
            var jar = new JarFile(File.ReadAllBytes(jarPath));
            var vm = new JVm(jar);
            vm.Log = s => Console.WriteLine(s);
            Lib.Library.Register(vm);
            vm.Boot();
            var t = new JThread(vm, "main");
            JThread.Current = t;
            var c = vm.LoadClass(cls.Replace('.', '/'));
            var m = c.Declared("main([Ljava/lang/String;)V");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { t.Call(m, new ObjArray(vm.LoadClass("[Ljava/lang/String;"), 0)); }
            catch (JavaException e) { Console.WriteLine("UNCAUGHT " + ((Lib.ThrowableObj)e.Obj).Describe()); return 1; }
            Console.WriteLine("[done in " + sw.ElapsedMilliseconds + " ms]");
            return 0;
        }
    }
}
