using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace CRIXOptimizer.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    /// <summary>Marks tests that need Harmony patching; they run after the pure unit tests.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class IntegrationAttribute : Attribute { }

    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void True(bool condition, string message = "expected true")
        {
            if (!condition)
                throw new AssertionException(message);
        }

        public static void False(bool condition, string message = "expected false") => True(!condition, message);

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertionException($"{message ?? "values differ"}: expected <{expected}>, actual <{actual}>");
        }

        public static void Near(double expected, double actual, double tolerance, string message = null)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                throw new AssertionException($"{message ?? "not within tolerance"}: expected {expected} ± {tolerance}, actual {actual}");
        }

        public static void InRange(double actual, double min, double max, string message = null)
        {
            if (double.IsNaN(actual) || actual < min || actual > max)
                throw new AssertionException($"{message ?? "out of range"}: expected [{min}, {max}], actual {actual}");
        }

        public static void NotNull(object value, string message = "expected non-null")
        {
            if (value == null)
                throw new AssertionException(message);
        }

        public static void Null(object value, string message = "expected null")
        {
            if (value != null)
                throw new AssertionException(message + $" (was {value})");
        }

        public static T Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T e)
            {
                return e;
            }
            catch (Exception e)
            {
                throw new AssertionException($"expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}");
            }
            throw new AssertionException($"expected {typeof(T).Name}, nothing thrown");
        }
    }

    /// <summary>Minimal TAP-style runner (no dependencies, so it runs on plain Mono).</summary>
    public static class TestRunner
    {
        public static int Run(string[] args)
        {
            if (args != null && args.Length == 2 && args[0] == "--dump-docs")
                return DocDump.Run(args[1]);
            string filter = args != null && args.Length > 0 ? args[0] : null;
            Assembly assembly = typeof(TestRunner).Assembly;
            var classes = assembly.GetTypes()
                .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Any(m => m.GetCustomAttribute<TestAttribute>() != null))
                .OrderBy(t => t.GetCustomAttribute<IntegrationAttribute>() != null ? 1 : 0)
                .ThenBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

            int passed = 0, failed = 0, number = 0;
            var failures = new List<string>();
            var total = Stopwatch.StartNew();
            Console.WriteLine($"# runtime: {RuntimeDescription()}");
            Console.WriteLine($"# BepInEx assembly: {SafeVersion("BepInEx")}, Harmony assembly: {SafeVersion("0Harmony")}");

            foreach (Type type in classes)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                    .OrderBy(m => m.MetadataToken)
                    .ToList();
                foreach (MethodInfo method in methods)
                {
                    string name = type.Name + "." + method.Name;
                    if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    number++;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        object instance = method.IsStatic ? null : Activator.CreateInstance(type);
                        method.Invoke(instance, null);
                        passed++;
                        Console.WriteLine($"ok {number} - {name} ({sw.ElapsedMilliseconds} ms)");
                    }
                    catch (TargetInvocationException e)
                    {
                        failed++;
                        Exception inner = e.InnerException ?? e;
                        failures.Add($"{name}: {inner.GetType().Name}: {inner.Message}");
                        Console.WriteLine($"not ok {number} - {name}");
                        Console.WriteLine($"  # {inner.GetType().Name}: {inner.Message}");
                        if (!(inner is AssertionException))
                        {
                            for (Exception cause = inner.InnerException; cause != null; cause = cause.InnerException)
                                Console.WriteLine($"  # caused by {cause.GetType().Name}: {cause.Message}");
                            Console.WriteLine("  # " + inner.StackTrace?.Replace("\n", "\n  # "));
                        }
                    }
                }
            }

            Console.WriteLine($"1..{number}");
            Console.WriteLine($"# passed {passed}, failed {failed}, {total.ElapsedMilliseconds} ms");
            foreach (string f in failures)
                Console.WriteLine("# FAILED " + f);
            return failed == 0 ? 0 : 1;
        }

        private static string RuntimeDescription()
        {
            Type mono = Type.GetType("Mono.Runtime");
            if (mono != null)
            {
                MethodInfo display = mono.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
                return "Mono " + (display?.Invoke(null, null) ?? "?");
            }
            return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        }

        private static string SafeVersion(string assemblyName)
        {
            try
            {
                return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName)?.GetName().Version?.ToString()
                       ?? Assembly.Load(assemblyName).GetName().Version.ToString();
            }
            catch
            {
                return "not loaded";
            }
        }
    }
}
