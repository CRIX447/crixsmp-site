// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.
// Derived from Potassium 1.0.0 (MapSanitizerPatch), Copyright (C) 2026 Potassium contributors,
// https://github.com/niko-tar-gz/Potassium (GPL-3.0). Modified by CRIX OPTIMIZER contributors in 2026;
// see CHANGELOG.md for the list of changes.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;
using HarmonyLib;
using UnityEngine;

namespace CRIXOptimizer.CPU
{
    /// <summary>
    /// Speeds up CustomMapLoader.SanitizeObject, which runs for every object of a custom map while it loads.
    ///
    /// SafeLookups (default, new in CRIX): a transpiler replaces only the List&lt;T&gt;.Contains allow-list
    /// lookups inside the game's method with cached hash-set lookups that return identical results. All of the
    /// game's own sanitising logic stays in place, so rules added by future game updates keep working. If the
    /// method contains no such lookups the transpiler changes nothing.
    ///
    /// FullReplacement (Potassium 1.0.0 MapSanitizerPatch): replaces the whole method with Potassium's
    /// re-implementation of the rules as they were at Potassium's release. Faster, but bypasses any rule the
    /// game added later. This is a security-relevant method (custom maps are third-party content), which is
    /// why it is opt-in here.
    /// </summary>
    internal static class MapSanitizerPatch
    {
        public const string SafeId = "Maps.SanitizerSafeLookups";
        public const string FullId = "Maps.SanitizerFullReplacement";

        public static int RewrittenLookups { get; private set; }

        private static FieldInfo approvedLayersField;
        private static FieldInfo componentAllowlistField;
        private static FieldInfo typeNameAllowlistField;
        private static HashSet<int> approvedLayers;
        private static HashSet<Type> approvedComponentTypes;
        private static List<string> approvedTypeNameFragments;
        private static readonly Dictionary<Type, bool> TypeNameApprovalCache = new Dictionary<Type, bool>();
        private static bool fullFaulted;

        private static readonly MethodInfo ContainsType = typeof(FastLookups).GetMethod(nameof(FastLookups.ListContainsType));
        private static readonly MethodInfo ContainsInt = typeof(FastLookups).GetMethod(nameof(FastLookups.ListContainsInt));
        private static readonly MethodInfo ContainsString = typeof(FastLookups).GetMethod(nameof(FastLookups.ListContainsString));

        public static void Apply(PatchSet patches, GameTypes types, SanitizerMode mode)
        {
            Type loader = types.Find("CustomMapLoader");
            MethodInfo sanitize = Accessors.FindMethod(loader, "SanitizeObject", 2);
            if (sanitize != null && sanitize.ReturnType != typeof(bool))
                sanitize = null;

            if (mode == SanitizerMode.SafeLookups)
            {
                patches.TryPatch(SafeId, sanitize, transpiler: Method(nameof(SafeTranspiler)));
                return;
            }

            if (mode == SanitizerMode.FullReplacement)
            {
                BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                approvedLayersField = loader?.GetField("APPROVED_LAYERS", statics);
                componentAllowlistField = loader?.GetField("componentAllowlist", statics);
                typeNameAllowlistField = loader?.GetField("componentTypeStringAllowList", statics);
                if (approvedLayersField == null || componentAllowlistField == null || typeNameAllowlistField == null)
                {
                    patches.MarkUnavailable(FullId, "CustomMapLoader allow-list fields not found; the game's sanitizer has changed, full replacement is unsafe");
                    return;
                }
                ParameterInfo[] ps = sanitize?.GetParameters();
                if (ps == null || ps[0].ParameterType != typeof(GameObject) || ps[1].ParameterType != typeof(GameObject))
                {
                    patches.MarkUnavailable(FullId, "SanitizeObject(GameObject, GameObject) signature not found");
                    return;
                }
                patches.TryPatch(FullId, sanitize, prefix: Method(nameof(FullReplacementPrefix)));
            }
        }

        private static MethodInfo Method(string name) =>
            typeof(MapSanitizerPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>Public for tests: rewrites List&lt;Type|int|string&gt;.Contains calls into FastLookups calls.</summary>
        internal static IEnumerable<CodeInstruction> SafeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            int rewritten = 0;
            var output = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in instructions)
            {
                MethodInfo replacement = ReplacementFor(instruction);
                if (replacement != null)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    rewritten++;
                }
                output.Add(instruction);
            }
            RewrittenLookups += rewritten;
            return output;
        }

        private static MethodInfo ReplacementFor(CodeInstruction instruction)
        {
            if ((instruction.opcode != OpCodes.Callvirt && instruction.opcode != OpCodes.Call) || !(instruction.operand is MethodInfo method))
                return null;
            if (method.Name != "Contains" || method.IsStatic || method.GetParameters().Length != 1)
                return null;
            Type declaring = method.DeclaringType;
            if (declaring == null || !declaring.IsGenericType || declaring.GetGenericTypeDefinition() != typeof(List<>))
                return null;
            Type element = declaring.GetGenericArguments()[0];
            if (element == typeof(Type))
                return ContainsType;
            if (element == typeof(int))
                return ContainsInt;
            if (element == typeof(string))
                return ContainsString;
            return null;
        }

        // ----------------------------------------------------------------- Potassium's full replacement

        private static bool FullReplacementPrefix(GameObject __0, GameObject __1, ref bool __result)
        {
            if (Live.MapSanitizer != SanitizerMode.FullReplacement || Live.Suspended || fullFaulted)
                return true;
            try
            {
                GameObject gameObject = __0;
                GameObject mapRoot = __1;
                EnsureCaches();

                if (gameObject == null)
                {
                    Debug.LogError("CustomMapLoader::SanitizeObject gameobject null");
                    __result = false;
                    return false;
                }

                if (!approvedLayers.Contains(gameObject.layer))
                    gameObject.layer = 0;

                Component[] components = gameObject.GetComponents<Component>();
                foreach (Component component in components)
                {
                    if (component == null)
                    {
                        Debug.Log("CustomMapLoader::SanitizeObject null component has caused " + gameObject.name + " to be DELETED");
                        UnityEngine.Object.Destroy(gameObject);
                        __result = false;
                        return false;
                    }

                    Type componentType = component.GetType();
                    bool isAllowed = approvedComponentTypes.Contains(componentType);
                    if (isAllowed && componentType == typeof(Camera))
                    {
                        Camera camera = (Camera)component;
                        if (camera != null && camera.targetTexture == null)
                            isAllowed = false;
                    }

                    if (!isAllowed)
                        isAllowed = IsTypeNameAllowed(componentType);

                    if (!isAllowed)
                    {
                        Debug.Log($"CustomMapLoader::SanitizeObject component type {componentType} has caused {gameObject.name} to be DELETED");
                        UnityEngine.Object.Destroy(gameObject);
                        __result = false;
                        return false;
                    }
                }

                if (gameObject.transform.parent == null && mapRoot != null && gameObject.transform != mapRoot.transform)
                    gameObject.transform.SetParent(mapRoot.transform);

                __result = true;
                return false;
            }
            catch (Exception e)
            {
                fullFaulted = true;
                PatchGuard.Fault(FullId, e);
                return true;
            }
        }

        private static void EnsureCaches()
        {
            if (approvedLayers != null)
                return;
            approvedLayers = new HashSet<int>((List<int>)approvedLayersField.GetValue(null));
            approvedComponentTypes = new HashSet<Type>((List<Type>)componentAllowlistField.GetValue(null));
            approvedTypeNameFragments = (List<string>)typeNameAllowlistField.GetValue(null);
        }

        private static bool IsTypeNameAllowed(Type componentType)
        {
            if (TypeNameApprovalCache.TryGetValue(componentType, out bool isAllowed))
                return isAllowed;

            string typeName = componentType.ToString();
            for (int i = 0; i < approvedTypeNameFragments.Count; i++)
            {
                if (typeName.Contains(approvedTypeNameFragments[i]))
                {
                    TypeNameApprovalCache[componentType] = true;
                    return true;
                }
            }

            TypeNameApprovalCache[componentType] = false;
            return false;
        }
    }
}
