﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Signum.Entities.Reflection;
using Signum.Utilities;

namespace Signum.Entities.DynamicQuery
{
    [Serializable]
    public class AggregateToken : QueryToken
    {
        public AggregateFunction AggregateFunction { get; private set; }

        object queryName; 
        public override object QueryName
        {
            get { return AggregateFunction == AggregateFunction.Count ? queryName : base.QueryName; }
        }

        public AggregateToken(AggregateFunction function, object queryName)
            : base(null)
        {
            if (function != AggregateFunction.Count)
                throw new ArgumentException("function should be Count for this overload");

            if (queryName == null)
                throw new ArgumentNullException("queryName");

            this.queryName = queryName;
            this.AggregateFunction = function;
        }


        public AggregateToken(AggregateFunction function, QueryToken parent)
            : base(parent)
        {
            if (function == AggregateFunction.Count)
                throw new ArgumentException("function should not different than Count for this overload");

            if (parent == null)
                throw new ArgumentNullException("parent");

            this.AggregateFunction = function;
        }

        public override string ToString()
        {
            return AggregateFunction.NiceToString();
        }

        public override string NiceName()
        {
            if (AggregateFunction == AggregateFunction.Count)
                return AggregateFunction.NiceToString();

            return "{0} of {1}".Formato(AggregateFunction.NiceToString(), Parent.ToString());
        }

        public override string Format
        {
            get
            {
                if (AggregateFunction == AggregateFunction.Count || AggregateFunction == AggregateFunction.Average)
                    return null;
                return Parent.Format;
            }
        }

        public override string Unit
        {
            get
            {
                if (AggregateFunction == AggregateFunction.Count)
                    return null;
                return Parent.Unit;
            }
        }

        public override Type Type
        {
            get
            {
                if (AggregateFunction == AggregateFunction.Count)
                    return typeof(int);

                var pu = Parent.Type.UnNullify();

                if (AggregateFunction == AggregateFunction.Average && (pu != typeof(float) || pu != typeof(double) || pu == typeof(decimal)))
                    return Parent.Type.IsNullable() ? typeof(double?) : typeof(double);

                if (pu == typeof(bool) ||
                    pu == typeof(byte) || pu == typeof(sbyte) ||
                    pu == typeof(short) || pu == typeof(ushort) ||
                    pu == typeof(uint) ||
                    pu == typeof(ulong))
                    return Parent.Type.IsNullable() ? typeof(int?) : typeof(int);

                return Parent.Type;
            }
        }

        public override string Key
        {
            get { return AggregateFunction.ToString(); }
        }

        protected override List<QueryToken> SubTokensOverride(SubTokensOptions options)
        {
            return new List<QueryToken>();
        }

        protected override Expression BuildExpressionInternal(BuildExpressionContext context)
        {
            throw new InvalidOperationException("AggregateToken should have a replacement at this stage");
        }

        public override PropertyRoute GetPropertyRoute()
        {
            if (AggregateFunction == AggregateFunction.Count)
                return null;

            return Parent.GetPropertyRoute();
        }

        public override Implementations? GetImplementations()
        {
            return null;
        }

        public override string IsAllowed()
        {
            if (AggregateFunction == AggregateFunction.Count)
                return null;

            return Parent.IsAllowed();
        }

        public override QueryToken Clone()
        {
            if (AggregateFunction == AggregateFunction.Count)
                return new AggregateToken(AggregateFunction.Count, this.queryName);
            else
                return new AggregateToken(AggregateFunction, Parent.Clone());
        }

      

        public override string TypeColor
        {
            get { return "#0000FF"; }
        }
    }

    [DescriptionOptions(DescriptionOptions.Members)]
    public enum AggregateFunction
    {
        Count,
        Average,
        Sum,
        Min,
        Max,
    }
}
