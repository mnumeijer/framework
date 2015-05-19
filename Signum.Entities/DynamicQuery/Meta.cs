﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.ObjectModel;
using Signum.Utilities;
using System.Collections;
using Signum.Entities.Reflection;
using Signum.Utilities.Reflection;

namespace Signum.Entities.DynamicQuery
{
    [Serializable]
    public abstract class Meta
    {
        public readonly Implementations? Implementations;

        public abstract string IsAllowed();

        protected Meta(Implementations? implementations)
        {
            this.Implementations = implementations;
        }
    }

    [Serializable]
    public class CleanMeta : Meta
    {
        public readonly PropertyRoute[] PropertyRoutes;

        public CleanMeta(Implementations? implementations, params PropertyRoute[] propertyRoutes)
            : base(implementations)
        {
            this.PropertyRoutes = propertyRoutes;
        }

        public override string IsAllowed()
        {
            var result = PropertyRoutes.Select(a => a.IsAllowed()).NotNull().CommaAnd();
            if (string.IsNullOrEmpty(result))
                return null;

            return result;
        }

        public override string ToString()
        {
            return "CleanMeta({0})".FormatWith(PropertyRoutes.ToString(", "));
        }

    }

    [Serializable]
    public class DirtyMeta : Meta
    {
        public readonly ReadOnlyCollection<CleanMeta> CleanMetas;

        public DirtyMeta(Implementations? implementations, Meta[] properties)
            : base(implementations)
        {
            CleanMetas = properties.OfType<CleanMeta>().Concat(
                properties.OfType<DirtyMeta>().SelectMany(d => d.CleanMetas))
                .ToReadOnly();
        }

        public override string IsAllowed()
        {
            var result = CleanMetas.Select(a => a.IsAllowed()).NotNull().CommaAnd();
            if (string.IsNullOrEmpty(result))
                return null;

            return result;
        }

        public override string ToString()
        {
            return "DirtyMeta({0})".FormatWith(CleanMetas.ToString(", "));
        }
    }
}
