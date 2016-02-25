namespace FakeItEasy.Core
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// Handles comparisons of instances of <see cref="MethodInfo"/>.
    /// </summary>
    internal class MethodInfoManager
    {
        private static readonly ConcurrentDictionary<TypeMethodInfoPair, MethodInfo> MethodCache = new ConcurrentDictionary<TypeMethodInfoPair, MethodInfo>();

        /// <summary>
        /// Gets a value indicating whether the two instances of <see cref="MethodInfo"/> would invoke the same method
        /// if invoked on an instance of the target type.
        /// </summary>
        /// <param name="target">The type of target for invocation.</param>
        /// <param name="first">The first <see cref="MethodInfo"/>.</param>
        /// <param name="second">The second <see cref="MethodInfo"/>.</param>
        /// <returns>True if the same method would be invoked.</returns>
        public virtual bool WillInvokeSameMethodOnTarget(Type target, MethodInfo first, MethodInfo second)
        {
            if (first == second)
            {
                return true;
            }

            var methodInvokedByFirst = this.GetMethodOnTypeThatWillBeInvokedByMethodInfo(target, first);
            var methodInvokedBySecond = this.GetMethodOnTypeThatWillBeInvokedByMethodInfo(target, second);

            return methodInvokedByFirst != null && methodInvokedBySecond != null && methodInvokedByFirst.Equals(methodInvokedBySecond);
        }

        public virtual MethodInfo GetMethodOnTypeThatWillBeInvokedByMethodInfo(Type type, MethodInfo method)
        {
            var key = new TypeMethodInfoPair(type, method);

            return MethodCache.GetOrAdd(key, k => FindMethodOnTypeThatWillBeInvokedByMethodInfo(k.Type, k.MethodInfo));
        }

        private static bool HasSameBaseMethod(MethodInfo first, MethodInfo second)
        {
            var baseOfFirst = GetBaseDefinition(first);
            var baseOfSecond = GetBaseDefinition(second);

            return IsSameMethod(baseOfFirst, baseOfSecond);
        }

        private static MethodInfo GetBaseDefinition(MethodInfo method)
        {
            if (method.IsGenericMethod && !method.IsGenericMethodDefinition)
            {
                method = method.GetGenericMethodDefinition();
            }

            return method.GetBaseDefinition();
        }

        private static bool IsSameMethod(MethodInfo first, MethodInfo second)
        {
            return first.DeclaringType == second.DeclaringType
#if FEATURE_REFLECTION_METADATATOKEN
                   && first.MetadataToken == second.MetadataToken
#else
                   && first.Name == second.Name
                   && first.GetParameters().Select(p => p.ParameterType).SequenceEqual(second.GetParameters().Select(p => p.ParameterType))
#endif
                   && first.Module == second.Module
                   && first.GetGenericArguments().SequenceEqual(second.GetGenericArguments());
        }

        private static MethodInfo FindMethodOnTypeThatWillBeInvokedByMethodInfo(Type type, MethodInfo method)
        {
            var result =
                (from typeMethod in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                 where HasSameBaseMethod(typeMethod, method)
                 select MakeGeneric(typeMethod, method)).FirstOrDefault();

            if (result != null)
            {
                return result;
            }

            result = GetMethodOnTypeThatImplementsInterfaceMethod(type, method);

            if (result != null)
            {
                return result;
            }

            return GetMethodOnInterfaceTypeImplementedByMethod(type, method);
        }

        private static MethodInfo GetMethodOnInterfaceTypeImplementedByMethod(Type type, MethodInfo method)
        {
            var reflectedType = method.ReflectedType;

            if (reflectedType.IsInterface)
            {
                return null;
            }

            var allInterfaces =
                from i in type.GetInterfaces()
                where TypeImplementsInterface(reflectedType, i)
                select i;

            foreach (var interfaceType in allInterfaces)
            {
                var interfaceMap = reflectedType.GetInterfaceMap(interfaceType);

                var foundMethod =
                    (from methodTargetPair in interfaceMap.InterfaceMethods
                         .Zip(interfaceMap.TargetMethods, (interfaceMethod, targetMethod) => new { InterfaceMethod = interfaceMethod, TargetMethod = targetMethod })
                     where HasSameBaseMethod(EnsureNonGeneric(method), EnsureNonGeneric(methodTargetPair.TargetMethod))
                     select MakeGeneric(methodTargetPair.InterfaceMethod, method)).FirstOrDefault();

                if (foundMethod != null)
                {
                    return GetMethodOnTypeThatImplementsInterfaceMethod(type, foundMethod);
                }
            }

            return null;
        }

        private static MethodInfo GetMethodOnTypeThatImplementsInterfaceMethod(Type type, MethodInfo method)
        {
            var baseDefinition = method.GetBaseDefinition();

            if (!baseDefinition.DeclaringType.GetTypeInfo().IsInterface || !TypeImplementsInterface(type, baseDefinition.DeclaringType))
            {
                return null;
            }

            var interfaceMap = type.GetTypeInfo().GetRuntimeInterfaceMap(baseDefinition.DeclaringType);

            return
                (from methodTargetPair in interfaceMap.InterfaceMethods
                     .Zip(interfaceMap.TargetMethods, (interfaceMethod, targetMethod) => new { InterfaceMethod = interfaceMethod, TargetMethod = targetMethod })
                 where HasSameBaseMethod(EnsureNonGeneric(methodTargetPair.InterfaceMethod), EnsureNonGeneric(method))
                 select MakeGeneric(methodTargetPair.TargetMethod, method)).First();
        }

        private static MethodInfo EnsureNonGeneric(MethodInfo methodInfo)
        {
            return methodInfo.IsGenericMethod ? methodInfo.GetGenericMethodDefinition() : methodInfo;
        }

        private static MethodInfo MakeGeneric(MethodInfo methodToMakeGeneric, MethodInfo originalMethod)
        {
            if (!methodToMakeGeneric.IsGenericMethodDefinition)
            {
                return methodToMakeGeneric;
            }

            return methodToMakeGeneric.MakeGenericMethod(originalMethod.GetGenericArguments());
        }

        private static bool TypeImplementsInterface(Type type, Type interfaceType)
        {
            return type.GetInterfaces().Any(x => x.Equals(interfaceType));
        }

        private struct TypeMethodInfoPair
        {
            public TypeMethodInfoPair(Type type, MethodInfo methodInfo)
                : this()
            {
                Type = type;
                MethodInfo = methodInfo;
            }

            public MethodInfo MethodInfo { get; private set; }

            public Type Type { get; private set; }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Type.GetHashCode() * 23) + MethodInfo.GetHashCode();
                }
            }

            [SuppressMessage("Microsoft.Usage", "CA2231:OverloadOperatorEqualsOnOverridingValueTypeEquals", Justification = "The type is used privately only.")]
            public override bool Equals(object obj)
            {
                var other = (TypeMethodInfoPair)obj;

                return this.Type == other.Type && this.MethodInfo == other.MethodInfo;
            }
        }
    }
}
