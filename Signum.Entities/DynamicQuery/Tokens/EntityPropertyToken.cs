using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signum.Entities.Reflection;
using System.Linq.Expressions;
using Signum.Utilities.Reflection;
using System.Reflection;
using Signum.Utilities;
using Signum.Utilities.ExpressionTrees;

namespace Signum.Entities.DynamicQuery
{
    [Serializable]
    public class EntityPropertyToken : QueryToken
    {
        public PropertyInfo PropertyInfo { get; private set; }

        public PropertyRoute PropertyRoute { get; private set; }

        static readonly PropertyInfo piId = ReflectionTools.GetPropertyInfo((Entity e) => e.Id); 

        public static QueryToken IdProperty(QueryToken parent)
        {
            return new EntityPropertyToken(parent, piId, PropertyRoute.Root(parent.Type.CleanType()).Add(piId)) { Priority = 10 };
        }

        internal EntityPropertyToken(QueryToken parent, PropertyInfo pi, PropertyRoute pr)
            : base(parent)
        {
            if (pi == null)
                throw new ArgumentNullException("pi");

            this.PropertyInfo = pi;
            this.PropertyRoute = pr;
        }

        public override Type Type
        {
            get { return PropertyInfo.PropertyType.BuildLiteNulifyUnwrapPrimaryKey(new[] { this.GetPropertyRoute() }); }
        }

        public override string ToString()
        {
            return PropertyInfo.NiceName();
        }

        public override string Key
        {
            get { return PropertyInfo.Name; }
        }

        protected override Expression BuildExpressionInternal(BuildExpressionContext context)
        {
            var baseExpression = Parent.BuildExpression(context);

            if (PropertyInfo.Is((Entity ident) => ident.Id) ||
                PropertyInfo.Is((Entity ident) => ident.ToStringProperty))
            {
                var entityExpression = baseExpression.ExtractEntity(true);

                return Expression.Property(entityExpression, PropertyInfo.Name).BuildLiteNulifyUnwrapPrimaryKey(new[] { this.PropertyRoute }); // Late binding over Lite or Identifiable
            }
            else
            {
                var entityExpression = baseExpression.ExtractEntity(false);

                if (PropertyRoute != null && PropertyRoute.Parent != null && PropertyRoute.Parent.PropertyRouteType == PropertyRouteType.Mixin)
                    entityExpression = Expression.Call(entityExpression, MixinDeclarations.miMixin.MakeGenericMethod(PropertyRoute.Parent.Type));

                Expression result = Expression.Property(entityExpression, PropertyInfo);

                return result.BuildLiteNulifyUnwrapPrimaryKey(new[] { this.PropertyRoute });
            }
        }

        protected override List<QueryToken> SubTokensOverride(SubTokensOptions options)
        {
            if (PropertyInfo.PropertyType.UnNullify() == typeof(DateTime))
            {
                PropertyRoute route = this.GetPropertyRoute();

                if (route != null)
                {
                    var att = Validator.TryGetPropertyValidator(route.Parent.Type, route.PropertyInfo.Name).Try(pp =>
                        pp.Validators.OfType<DateTimePrecissionValidatorAttribute>().SingleOrDefaultEx());
                    if (att != null)
                    {
                        return DateTimeProperties(this, att.Precision);
                    }
                }
            }

            if (PropertyInfo.PropertyType.UnNullify() == typeof(double) ||
                PropertyInfo.PropertyType.UnNullify() == typeof(float) ||
                PropertyInfo.PropertyType.UnNullify() == typeof(decimal))
            {
                PropertyRoute route = this.GetPropertyRoute();

                if (route != null)
                {
                    var att = Validator.TryGetPropertyValidator(route.Parent.Type, route.PropertyInfo.Name).Try(pp =>
                        pp.Validators.OfType<DecimalsValidatorAttribute>().SingleOrDefaultEx());
                    if (att != null)
                    {
                        return StepTokens(this, att.DecimalPlaces);
                    }

                    var format = Reflector.FormatString(route);
                    if (format != null)
                        return StepTokens(this, Reflector.NumDecimals(format));
                }
            }

            return SubTokensBase(PropertyInfo.PropertyType, options, GetImplementations());
        }

        public override Implementations? GetImplementations()
        {
            return GetPropertyRoute().TryGetImplementations();
        }

        public override string Format
        {
            get { return Reflector.FormatString(this.GetPropertyRoute()); }
        }

        public override string Unit
        {
            get { return PropertyInfo.GetCustomAttribute<UnitAttribute>().Try(u => u.UnitName); }
        }

        public override string IsAllowed()
        {
            PropertyRoute pr = GetPropertyRoute();

            string parent = Parent.IsAllowed();

            string route = pr == null ? null : pr.IsAllowed();

            if (parent.HasText() && route.HasText())
                return QueryTokenMessage.And.NiceToString().Combine(parent, route);

            return parent ?? route;
        }

        public override PropertyRoute GetPropertyRoute()
        {
            return PropertyRoute;
        }

        public override string NiceName()
        {
            return PropertyInfo.NiceName();
        }

        public override QueryToken Clone()
        {
            return new EntityPropertyToken(Parent.Clone(), PropertyInfo, PropertyRoute);
        }
    }
}
