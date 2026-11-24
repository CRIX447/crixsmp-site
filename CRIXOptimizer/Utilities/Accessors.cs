// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace CRIXOptimizer.Utilities
{
    /// <summary>
    /// Builds fast delegates for game members found by name (field or property, any visibility).
    /// Delegates are compiled once with expression trees, so hot patch code does no reflection and no boxing.
    /// If compilation is unavailable, a reflection-based delegate is returned instead (slower but correct).
    /// All factory methods return null when the member does not exist.
    /// </summary>
    internal static class Accessors
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static MemberInfo FindFieldOrProperty(Type type, string name, bool isStatic)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, Declared);
                if (field != null && field.IsStatic == isStatic)
                    return field;
                PropertyInfo property = t.GetProperty(name, Declared);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    MethodInfo accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
                    if (accessor != null && accessor.IsStatic == isStatic)
                        return property;
                }
            }
            return null;
        }

        public static MethodInfo FindMethod(Type type, string name, int parameterCount = -1)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                MethodInfo match = t.GetMethods(Declared)
                    .FirstOrDefault(m => m.Name == name && !m.IsGenericMethodDefinition && (parameterCount < 0 || m.GetParameters().Length == parameterCount));
                if (match != null)
                    return match;
            }
            return null;
        }

        public static Type MemberType(MemberInfo member) =>
            member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;

        // ------------------------------------------------------------------ instance members

        public static Func<object, T> Getter<T>(Type type, string name)
        {
            if (type == null)
                return null;
            MemberInfo member = FindFieldOrProperty(type, name, false);
            if (member == null || !CanRead(member) || !IsConvertible(MemberType(member), typeof(T)))
                return null;

            try
            {
                ParameterExpression obj = Expression.Parameter(typeof(object), "obj");
                Expression instance = Expression.Convert(obj, member.DeclaringType);
                Expression access = Access(instance, member);
                return Expression.Lambda<Func<object, T>>(Expression.Convert(access, typeof(T)), obj).Compile();
            }
            catch
            {
                return o => (T)ReadValue(member, o);
            }
        }

        public static Action<object, T> Setter<T>(Type type, string name)
        {
            if (type == null)
                return null;
            MemberInfo member = FindFieldOrProperty(type, name, false);
            if (member == null || !CanWrite(member) || !IsConvertible(typeof(T), MemberType(member)))
                return null;

            try
            {
                ParameterExpression obj = Expression.Parameter(typeof(object), "obj");
                ParameterExpression value = Expression.Parameter(typeof(T), "value");
                Expression instance = Expression.Convert(obj, member.DeclaringType);
                Expression target = Access(instance, member);
                Expression body = Expression.Assign(target, Expression.Convert(value, MemberType(member)));
                return Expression.Lambda<Action<object, T>>(body, obj, value).Compile();
            }
            catch
            {
                return (o, v) => WriteValue(member, o, v);
            }
        }

        // ------------------------------------------------------------------ static members

        public static Func<T> StaticGetter<T>(Type type, string name)
        {
            if (type == null)
                return null;
            MemberInfo member = FindFieldOrProperty(type, name, true);
            if (member == null || !CanRead(member) || !IsConvertible(MemberType(member), typeof(T)))
                return null;

            try
            {
                Expression access = Access(null, member);
                return Expression.Lambda<Func<T>>(Expression.Convert(access, typeof(T))).Compile();
            }
            catch
            {
                return () => (T)ReadValue(member, null);
            }
        }

        public static Action<T> StaticSetter<T>(Type type, string name)
        {
            if (type == null)
                return null;
            MemberInfo member = FindFieldOrProperty(type, name, true);
            if (member == null || !CanWrite(member) || !IsConvertible(typeof(T), MemberType(member)))
                return null;

            try
            {
                ParameterExpression value = Expression.Parameter(typeof(T), "value");
                Expression body = Expression.Assign(Access(null, member), Expression.Convert(value, MemberType(member)));
                return Expression.Lambda<Action<T>>(body, value).Compile();
            }
            catch
            {
                return v => WriteValue(member, null, v);
            }
        }

        /// <summary>Getter bound to a specific target object (e.g. a render-pipeline asset).</summary>
        public static Func<T> BoundGetter<T>(object target, string name)
        {
            Func<object, T> getter = target == null ? null : Getter<T>(target.GetType(), name);
            return getter == null ? null : () => getter(target);
        }

        public static Action<T> BoundSetter<T>(object target, string name)
        {
            Action<object, T> setter = target == null ? null : Setter<T>(target.GetType(), name);
            return setter == null ? null : v => setter(target, v);
        }

        // ------------------------------------------------------------------ methods

        /// <summary>Instance method with no parameters, result ignored.</summary>
        public static Action<object> Action0(Type type, string name)
        {
            MethodInfo method = type == null ? null : FindMethod(type, name, 0);
            if (method == null || method.IsStatic)
                return null;

            try
            {
                ParameterExpression obj = Expression.Parameter(typeof(object), "obj");
                Expression call = Expression.Call(Expression.Convert(obj, method.DeclaringType), method);
                return Expression.Lambda<Action<object>>(call, obj).Compile();
            }
            catch
            {
                return o => method.Invoke(o, null);
            }
        }

        /// <summary>Instance method with one reference-type parameter, result ignored.</summary>
        public static Action<object, object> Action1(Type type, string name)
        {
            MethodInfo method = type == null ? null : FindMethod(type, name, 1);
            if (method == null || method.IsStatic)
                return null;
            Type parameterType = method.GetParameters()[0].ParameterType;
            if (parameterType.IsByRef)
                return null;

            try
            {
                ParameterExpression obj = Expression.Parameter(typeof(object), "obj");
                ParameterExpression arg = Expression.Parameter(typeof(object), "arg");
                Expression call = Expression.Call(Expression.Convert(obj, method.DeclaringType), method, Expression.Convert(arg, parameterType));
                return Expression.Lambda<Action<object, object>>(call, obj, arg).Compile();
            }
            catch
            {
                return (o, a) => method.Invoke(o, new[] { a });
            }
        }

        /// <summary>Instance method returning bool, called with one constant argument (e.g. an enum value).</summary>
        public static Func<object, bool> PredicateWithConstant(Type type, string name, object constantArgument)
        {
            MethodInfo method = type == null ? null : FindMethod(type, name, 1);
            if (method == null || method.IsStatic || method.ReturnType != typeof(bool))
                return null;
            Type parameterType = method.GetParameters()[0].ParameterType;
            if (constantArgument == null || !parameterType.IsInstanceOfType(constantArgument))
                return null;

            try
            {
                ParameterExpression obj = Expression.Parameter(typeof(object), "obj");
                Expression call = Expression.Call(Expression.Convert(obj, method.DeclaringType), method, Expression.Constant(constantArgument, parameterType));
                return Expression.Lambda<Func<object, bool>>(call, obj).Compile();
            }
            catch
            {
                object[] args = { constantArgument };
                return o => (bool)method.Invoke(o, args);
            }
        }

        /// <summary>Instance method with no parameters returning a reference type.</summary>
        public static Func<object, object> Func0(Type type, string name)
        {
            MethodInfo method = type == null ? null : FindMethod(type, name, 0);
            if (method == null || method.IsStatic || method.ReturnType == typeof(void) || method.ReturnType.IsValueType)
                return null;

            try
            {
                ParameterExpression obj = Expression.Parameter(typeof(object), "obj");
                Expression call = Expression.Call(Expression.Convert(obj, method.DeclaringType), method);
                return Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), obj).Compile();
            }
            catch
            {
                return o => method.Invoke(o, null);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Expression Access(Expression instance, MemberInfo member) =>
            member is FieldInfo field ? Expression.Field(instance, field) : (Expression)Expression.Property(instance, (PropertyInfo)member);

        private static bool CanRead(MemberInfo member) => member is FieldInfo || ((PropertyInfo)member).GetGetMethod(true) != null;

        private static bool CanWrite(MemberInfo member) =>
            member is FieldInfo field ? !field.IsInitOnly && !field.IsLiteral : ((PropertyInfo)member).GetSetMethod(true) != null;

        private static bool IsConvertible(Type from, Type to)
        {
            if (to.IsAssignableFrom(from) || from.IsAssignableFrom(to))
                return true;
            // Numeric conversions (e.g. an int field read as float) are allowed explicitly.
            return IsNumeric(from) && IsNumeric(to);
        }

        private static bool IsNumeric(Type t) =>
            t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint);

        private static object ReadValue(MemberInfo member, object instance) =>
            member is FieldInfo field ? field.GetValue(instance) : ((PropertyInfo)member).GetValue(instance, null);

        private static void WriteValue(MemberInfo member, object instance, object value)
        {
            Type target = MemberType(member);
            if (value != null && !target.IsInstanceOfType(value))
                value = Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
            if (member is FieldInfo field)
                field.SetValue(instance, value);
            else
                ((PropertyInfo)member).SetValue(instance, value, null);
        }
    }
}
