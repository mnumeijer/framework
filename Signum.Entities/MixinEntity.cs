﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Signum.Entities.Reflection;
using Signum.Utilities;
using Signum.Utilities.Reflection;
using Signum.Utilities.ExpressionTrees;

namespace Signum.Entities
{
    [Serializable, DescriptionOptions(DescriptionOptions.Members)]
    public abstract class MixinEntity : ModifiableEntity
    {
        protected MixinEntity(IdentifiableEntity mainEntity, MixinEntity next)
        {
            this.mainEntity = mainEntity;
            this.next = next;
        }

        [Ignore]
        readonly MixinEntity next;
        [HiddenProperty]
        public MixinEntity Next
        {
            get { return next; }
        }

        [Ignore]
        readonly IdentifiableEntity mainEntity;
        [HiddenProperty]
        public IdentifiableEntity MainEntity
        {
            get { return mainEntity; }
        }

        protected internal virtual void CopyFrom(MixinEntity mixin, object[] args)
        {

        }
    }

    public static class MixinDeclarations
    {
        public static readonly MethodInfo miMixin = ReflectionTools.GetMethodInfo((IdentifiableEntity i) => i.Mixin<CorruptMixin>()).GetGenericMethodDefinition();

        public static Dictionary<Type, HashSet<Type>> Declarations = new Dictionary<Type, HashSet<Type>>();

        public static Dictionary<Type, Func<IdentifiableEntity, MixinEntity, MixinEntity>> Constructors =
            new Dictionary<Type, Func<IdentifiableEntity, MixinEntity, MixinEntity>>();

        public static Func<Type, string> CanAddMixins;

        public static void Register<T, M>()
            where T : IdentifiableEntity
            where M : MixinEntity
        {
            Register(typeof(T), typeof(M));
        }

        public static void Register(Type mainEntity, Type mixinEntity)
        {
            if (!typeof(IdentifiableEntity).IsAssignableFrom(mainEntity))
                throw new InvalidOperationException("{0} is not a {1}".Formato(mainEntity.Name, typeof(IdentifiableEntity).Name));

            if (mainEntity.IsAbstract)
                throw new InvalidOperationException("{0} is abstract".Formato(mainEntity.Name));

            if (!typeof(MixinEntity).IsAssignableFrom(mixinEntity))
                throw new InvalidOperationException("{0} is not a {1}".Formato(mixinEntity.Name, typeof(MixinEntity).Name));

            string error = CanAddMixins == null ? null : CanAddMixins(mainEntity); 
            if (error != null)
                throw new InvalidOperationException(error);

            GetMixinDeclarations(mainEntity).Add(mixinEntity);

            AddConstructor(mixinEntity);
        }

        public static void Import(Dictionary<Type, HashSet<Type>> declarations)
        {
            Declarations = declarations;

            foreach (var t in declarations.SelectMany(d => d.Value).Distinct())
                AddConstructor(t);
        }

        static void AddConstructor(Type mixinEntity)
        {
            Constructors.GetOrCreate(mixinEntity, () =>
            {
                var constructors = mixinEntity.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

                if (constructors.Length != 1)
                    throw new InvalidOperationException("{0} should have just one non-public construtor with parameters (IdentifiableEntity mainEntity, MixinEntity next)"
                        .Formato(mixinEntity.Name));

                var ci = constructors.Single();

                var pi = ci.GetParameters();

                if (ci.IsPublic || pi.Length != 2 || pi[0].ParameterType != typeof(IdentifiableEntity) || pi[1].ParameterType != typeof(MixinEntity))
                    throw new InvalidOperationException("{0} does not have a non-public construtor with parameters (IdentifiableEntity mainEntity, MixinEntity next)");

                return (Func<IdentifiableEntity, MixinEntity, MixinEntity>)Expression.Lambda(Expression.New(ci, pMainEntity, pNext), pMainEntity, pNext).Compile();
            });
        }

        static readonly ParameterExpression pMainEntity = ParameterExpression.Parameter(typeof(IdentifiableEntity));
        static readonly ParameterExpression pNext = ParameterExpression.Parameter(typeof(MixinEntity));

        public static HashSet<Type> GetMixinDeclarations(Type mainEntity)
        {
            return Declarations.GetOrCreate(mainEntity,
                () =>
                {
                    var hs = mainEntity.GetCustomAttributes(typeof(MixinAttribute), inherit: false)
                        .Cast<MixinAttribute>()
                        .Select(t => t.MixinType)
                        .ToHashSet();

                    foreach (var t in hs)
                        AddConstructor(t);

                    return hs; 
                });
        }

        public static bool IsDeclared(Type mainEntity, Type mixinType)
        {
            return GetMixinDeclarations(mainEntity).Contains(mixinType);
        }

        public static void AssertDeclared(Type mainEntity, Type mixinType)
        {
            if (!IsDeclared(mainEntity, mixinType))
                throw new InvalidOperationException("Mixin {0} is not registered for {1}. Consider writing MixinDeclarations.Register<{1}, {0}>() at the beginning of Starter.Start".Formato(mixinType.TypeName(), mainEntity.TypeName())); 
        }

        internal static MixinEntity CreateMixins(IdentifiableEntity mainEntity)
        {
            var types = GetMixinDeclarations(mainEntity.GetType());

            MixinEntity result = null;
            foreach (var t in types)
                result = Constructors[t](mainEntity, result);

            return result;
        }

        public static T SetMixin<T, M, V>(this T entity, Expression<Func<M, V>> mixinProperty, V value)
            where T : IIdentifiable
            where M : MixinEntity
        {
            M mixin = ((IdentifiableEntity)(IIdentifiable)entity).Mixin<M>();

            var pi = ReflectionTools.BasePropertyInfo(mixinProperty);

            var setter = MixinSetterCache<M>.Setter<V>(pi);

            setter(mixin, value);

            return entity;
        }

        static class MixinSetterCache<T> where T : MixinEntity
        {
            static ConcurrentDictionary<string, Delegate> cache = new ConcurrentDictionary<string, Delegate>();

            internal static Action<T, V> Setter<V>(PropertyInfo pi)
            {
                return (Action<T, V>)cache.GetOrAdd(pi.Name, s => ReflectionTools.CreateSetter<T, V>(Reflector.FindFieldInfo(typeof(T), pi)));
            }
        }

        public static T CopyMixinsFrom<T>(this T newEntity, IIdentifiable original, params object[] args)
            where T: IIdentifiable
        {
            var list = (from nm in ((IdentifiableEntity)(IIdentifiable)newEntity).Mixins
                        join om in ((IdentifiableEntity)(IIdentifiable)original).Mixins
                        on nm.GetType() equals om.GetType()
                        select new { nm, om });

            foreach (var pair in list)
            {
                pair.nm.CopyFrom(pair.om, args);
            }

            return newEntity;
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class MixinAttribute : Attribute
    {
        readonly Type mixinType;
        public MixinAttribute(Type mixinType)
        {
            this.mixinType = mixinType;
        }

        public Type MixinType
        {
            get { return this.mixinType; }
        }
    }
}
