using System;
using System.IO;
using System.Reflection;

internal static class Program
{
    private static int Main(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        Assembly tests = Assembly.LoadFrom(Path.Combine(dir, "CRIXOptimizer.Tests.dll"));
        MethodInfo run = tests.GetType("CRIXOptimizer.Tests.TestRunner", true).GetMethod("Run");
        return (int)run.Invoke(null, new object[] { args });
    }
}
