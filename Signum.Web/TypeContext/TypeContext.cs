﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Linq.Expressions;
using Signum.Utilities;
using System.Web.Mvc;
using System.Web.Mvc.Html;
using Signum.Entities;
using Signum.Utilities.DataStructures;
using Signum.Entities.Reflection;
using Signum.Utilities.Reflection;
using Signum.Engine;
using Signum.Utilities.ExpressionTrees;
using System.IO;
using System.Web.WebPages;
using System.Text.RegularExpressions;

namespace Signum.Web
{
    public class Context : IDisposable
    {
        public const string Separator = "_";

        public string Prefix { get; set; }

        public Context Parent { get; private set; } 

        public Context(Context parent, string prefix)
        {
            if(parent == null)
            {
                this.Parent = Default;
                this.Prefix = prefix ?? ""; 
            }
            else
            {
                this.Parent = parent;
                this.Prefix = parent.Compose(prefix) ?? ""; 
            }
        }

        public string Compose(string nameToAppend)
        {
            return Prefix.Add(Separator, nameToAppend);
        }

        public string Compose(params string[] namesToAppend)
        {
            return Compose((IEnumerable<string>)namesToAppend);
        }

        public string Compose(IEnumerable<string> namesToAppend)
        {
            return this.Compose(namesToAppend.ToString(Separator));
        }

        public static readonly Context Default = new Context(null, null)
        {
            FormGroupStyle = FormGroupStyle.LabelColumns,
            FormGroupSize = FormGroupSize.Small,
            LabelColumns = new BsColumn(2),
            ReadOnly = false,
            PlaceholderLabels = false,
        };

        FormGroupStyle? formGroupStyle;
        public FormGroupStyle FormGroupStyle
        {
            get { return formGroupStyle ?? Parent.FormGroupStyle; }
            set { formGroupStyle = value; }
        }

        FormGroupSize? formGroupSize;
        public FormGroupSize FormGroupSize
        {
            get { return formGroupSize ?? Parent.FormGroupSize; }
            set { formGroupSize = value; }
        }

        public string FormGroupSizeCss 
        {
            get 
            {
                return FormGroupSize == FormGroupSize.Normal ? "form-md" :
                    FormGroupSize == FormGroupSize.Small ? "form-sm" :
                    "form-xs";
            }
        }

        bool? placeholderLabels;
        public bool PlaceholderLabels
        {
            get { return placeholderLabels ?? Parent.PlaceholderLabels; ; }
            set { placeholderLabels = value; }
        }

        BsColumn labelColummns;
        public BsColumn LabelColumns
        {
            get { return labelColummns ?? Parent.LabelColumns; }
            set 
            { 
                labelColummns = value;
                ValueColumns = value == null ? null : value.Inverse();
            }
        }

        BsColumn valueColumns;
        public BsColumn ValueColumns
        {
            get { return valueColumns ?? Parent.ValueColumns; }
            set { valueColumns = value; }
        }

        bool? readOnly; 
        public bool ReadOnly
        {
            get { return readOnly ?? Parent.ReadOnly; }
            set
            {
                readOnly = value;
                if (value) 
                    SetReadOnly();
            }
        }

        protected virtual void SetReadOnly() { }

        public override string ToString()
        {
            return Prefix; 
        }

        public void Dispose()
        {
        }
    }


    public class BsColumn
    {
        public readonly short? xs;
        public readonly short? sm;
        public readonly short? md;
        public readonly short? lg;

        readonly string catchedString; 

        public BsColumn(short sm)
        {
            this.xs = null;
            this.sm = sm;
            this.md = null;
            this.lg = null;
            this.catchedString = "col-sm-" + sm;
        }

        public BsColumn(short? xs, short? sm, short? md, short? lg)
        {
            this.xs = xs;
            this.sm = sm;
            this.md = md;
            this.lg = lg;
            this.catchedString =  " ".CombineIfNotEmpty(
                xs == null? null: "col-xs-" + xs,
                sm == null ? null : "col-sm-" + sm,
                md == null ? null : "col-md-" + md,
                lg == null ? null : "col-lg-" + lg);
        }

        public BsColumn Inverse()
        {
            return new BsColumn(
                (short?)(12 - xs),
                (short?)(12 - sm),
                (short?)(12 - md),
                (short?)(12 - lg));
        }

        public override string ToString()
        {
            return catchedString;
        }

        public static BsColumn operator +(BsColumn a, BsColumn b)
        {
            return new BsColumn(
                (short?)(a.xs + b.xs),
                (short?)(a.sm + b.sm),
                (short?)(a.md + b.md),
                (short?)(a.lg + b.lg)
                ); 
        }

        public static BsColumn operator -(BsColumn a, BsColumn b)
        {
            return new BsColumn(
                (short?)(a.xs - b.xs),
                (short?)(a.sm - b.sm),
                (short?)(a.md - b.md),
                (short?)(a.lg - b.lg)
                );
        }
    }

    /// <summary>
    /// Nomenclature from http://getbootstrap.com/css/#forms
    /// </summary>
    public enum FormGroupStyle
    {
        /// <summary>
        /// Unaffected by FormGroupSize
        /// </summary>
        None,

        /// <summary>
        /// Requires form-vertical container
        /// </summary>
        Basic,

        /// <summary>
        /// Requires form-vertical container
        /// </summary>
        BasicDown,

        /// <summary>
        /// Requires form-vertical / form-inline container
        /// </summary>
        SrOnly,

        /// <summary>
        /// Requires form-horizontal (default),  affected by LabelColumns / ValueColumns
        /// </summary>
        LabelColumns,
    }

    public enum FormGroupSize
    { 
        Normal,
        Small,
        ExtraSmall
    }



    #region TypeContext
    public abstract class TypeContext : Context
    {
        IViewOverrides viewOverrides;
        public IViewOverrides ViewOverrides
        {
            get
            {
                if (viewOverrides != null)
                    return viewOverrides;

                TypeContext parent = Parent as TypeContext;

                if (parent != null)
                    return parent.ViewOverrides;

                return null;
            }
            set { viewOverrides = value; }
        }

        public abstract object UntypedValue { get; }

        public abstract Type Type { get; }

        public PropertyRoute PropertyRoute { get; private set; }

        protected TypeContext(Context parent, string prefix, PropertyRoute propertyRoute)
            :base(parent, prefix)
        {
            this.PropertyRoute = propertyRoute;
        }

        public RuntimeInfo RuntimeInfo()
        {
            if (this.UntypedValue == null)
                return null;

            Type type = this.UntypedValue.GetType();
            if (type.IsLite())
                return new RuntimeInfo((Lite<IIdentifiable>)this.UntypedValue);

            if (type.IsEmbeddedEntity())
                return new RuntimeInfo((EmbeddedEntity)this.UntypedValue);

            if (type.IsIdentifiableEntity())
                return new RuntimeInfo((IdentifiableEntity)this.UntypedValue);

            throw new ArgumentException("Invalid type {0} for RuntimeInfo. It must be Lite, IdentifiableEntity or EmbeddedEntity".Formato(type));
        }

        internal abstract TypeContext Clone(object newValue);

        internal static void AssertId(string id)
        {
            if (!Regex.IsMatch(id, @"^[A-Za-z][A-Za-z0-9-_]*$"))
                throw new InvalidOperationException("'{0}' is not a valid HTML id".Formato(id));
        }
    }
    #endregion

    #region TypeContext<T>
    public class TypeContext<T> : TypeContext
    {
        public T Value { get; set; }

        public override object UntypedValue
        {
            get { return Value; }
        }

        public TypeContext(T value, string prefix)
            : base(null, prefix, PropertyRoute.Root(value.GetType()))
        {
            Value = value;
        }

        public TypeContext(T value, TypeContext parent, string prefix, PropertyRoute propertyRoute)
            : base(parent, prefix, propertyRoute ?? PropertyRoute.Root(value.GetType()))
        {
            Value = value;
        }

        public override Type Type
        {
            get { return typeof(T); }
        }


        public TypeContext<T> SubContext()
        {
            return new TypeContext<T>(this.Value, this, null, PropertyRoute);
        }

        public TypeContext<S> SubContext<S>(Expression<Func<T, S>> property)
        {
            return Common.WalkExpression(this, property);
        }

        public IEnumerable<TypeElementContext<S>> TypeElementContext<S>(Expression<Func<T, MList<S>>> property)
        {
            return TypeContextUtilities.TypeElementContext(Common.WalkExpression(this, property));
        }

        internal override TypeContext Clone(object newValue)
        {
            return new TypeContext<T>((T)newValue, (TypeContext)Parent, Prefix, PropertyRoute);
        }
    }
    #endregion

    #region TypeSubContext<T>
    public class TypeSubContext<T> : TypeContext<T>, IDisposable
    {
        PropertyInfo[] properties;

        public TypeSubContext(T value, TypeContext parent, PropertyInfo[] properties, PropertyRoute propertyRoute)
            : base(value, parent.ThrowIfNull(""), properties.ToString(a => a.Name, Separator), propertyRoute)
        {
            this.properties = properties;
        }

        public PropertyInfo[] Properties
        {
            get { return properties; }
        }

        internal override TypeContext Clone(object newValue)
        {
            return new TypeSubContext<T>((T)newValue, (TypeContext)Parent, Properties, PropertyRoute);
        }
    }
    #endregion

    #region TypeElementContext<T>
    public class TypeElementContext<T> : TypeContext<T>
    {
        public int Index { get; private set; }
        public int? RowId { get; private set; }

        public TypeElementContext(T value, TypeContext parent, int index, int? rowId)
            : base(value, parent, index.ToString(), parent.PropertyRoute.Add("Item"))
        {
            this.Index = index;
            this.RowId = rowId;
        }

        internal override TypeContext Clone(object newValue)
        {
            return new TypeElementContext<T>((T)newValue, (TypeContext)Parent, Index, RowId);
        }
    }
    #endregion

    public interface IViewOverrides
    {
        List<Tab> ExpandTabs(List<Tab> tabs, string containerId, HtmlHelper helper, TypeContext context);
        MvcHtmlString OnSurroundLine(PropertyRoute propertyRoute, HtmlHelper helper, TypeContext tc, MvcHtmlString result);
        bool IsVisible(PropertyRoute propertyRoute);
    }

    public class ViewOverrides<T> : IViewOverrides where T : IRootEntity
    {
        public Dictionary<string, Func<HtmlHelper, TypeContext, Tab>> BeforeTabDictionary;
        public ViewOverrides<T> BeforeTab(string id, Func<HtmlHelper, TypeContext<T>, Tab> constructor) 
        {
            if (BeforeTabDictionary == null)
                BeforeTabDictionary = new Dictionary<string, Func<HtmlHelper, TypeContext, Tab>>();

            BeforeTabDictionary[id] = BeforeTabDictionary.TryGetC(id) + new Func<HtmlHelper, TypeContext, Tab>((html, tc) => constructor(html, (TypeContext<T>)tc));

            return this;
        }

        public Dictionary<string, Func<HtmlHelper, TypeContext, Tab>> AfterTabDictionary;
        public ViewOverrides<T> AfterTab(string id, Func<HtmlHelper, TypeContext, Tab> constructor)
        {
            if (AfterTabDictionary == null)
                AfterTabDictionary = new Dictionary<string, Func<HtmlHelper, TypeContext, Tab>>();

            AfterTabDictionary[id] = AfterTabDictionary.TryGetC(id) + new Func<HtmlHelper, TypeContext, Tab>((html, tc) => constructor(html, (TypeContext<T>)tc));

            return this;
        }

        HashSet<string> hiddenTabs;

        public ViewOverrides<T> HideTab(string id)
        {
            if (hiddenTabs == null)
                hiddenTabs = new HashSet<string>();
            hiddenTabs.Add(id);

            return this;
        }

        List<Tab> IViewOverrides.ExpandTabs(List<Tab> tabs, string containerId, HtmlHelper helper, TypeContext context)
        {
            if (hiddenTabs != null && hiddenTabs.Contains(containerId))
                return null;

            List<Tab> newTabs = new List<Tab>();

            var before = BeforeTabDictionary.TryGetC(containerId);
            if (before != null)
                foreach (var b in before.GetInvocationListTyped())
                {
                    var newTab = b(helper, context);
                    if (newTab != null)
                        ExpandTab(newTab, helper, context, newTabs);
                }

            foreach (var item in tabs)
                ExpandTab(item, helper, context, newTabs);

            var after = AfterTabDictionary.TryGetC(containerId);
            if (after != null)
                foreach (var a in after.GetInvocationListTyped())
                {
                    var newTab = a(helper, context);
                    if (newTab != null)
                        ExpandTab(newTab, helper, context, newTabs);
                }

            return newTabs;
        }

        void ExpandTab(Tab item, HtmlHelper helper, TypeContext context, List<Tab> newTabs)
        {
            var before = BeforeTabDictionary.TryGetC(item.Id);
            if (before != null)
                foreach (var b in before.GetInvocationListTyped())
                {
                    var newTab = b(helper, context);
                    if (newTab != null)
                        ExpandTab(newTab, helper, context, newTabs);
                }

            if (hiddenTabs == null || !hiddenTabs.Contains(item.Id))
                newTabs.Add(item);

            var after = AfterTabDictionary.TryGetC(item.Id);
            if (after != null)
                foreach (var a in after.GetInvocationListTyped())
                {
                    var newTab = a(helper, context);
                    if (newTab != null)
                        ExpandTab(newTab, helper, context, newTabs);
                }
        }

        Dictionary<PropertyRoute, Func<HtmlHelper, TypeContext, MvcHtmlString>> beforeLine;
        public ViewOverrides<T> BeforeLine<S>(Expression<Func<T, S>> propertyRoute, Func<HtmlHelper, TypeContext<T>, MvcHtmlString> constructor)
        {
            return BeforeLine(PropertyRoute.Construct(propertyRoute), (helper, tc) => constructor(helper, (TypeContext<T>)tc));
        }

        public ViewOverrides<T> BeforeLine(PropertyRoute propertyRoute, Func<HtmlHelper, TypeContext, MvcHtmlString> constructor)
        {
            if (beforeLine == null)
                beforeLine = new Dictionary<PropertyRoute, Func<HtmlHelper, TypeContext, MvcHtmlString>>();

            beforeLine[propertyRoute] = beforeLine.TryGetC(propertyRoute) + constructor;

            return this; 
        }


        Dictionary<PropertyRoute, Func<HtmlHelper, TypeContext, MvcHtmlString>> afterLine;
        public ViewOverrides<T> AfterLine< S>(Expression<Func<T, S>> propertyRoute, Func<HtmlHelper, TypeContext<T>, MvcHtmlString> constructor)
        {
            return AfterLine(PropertyRoute.Construct(propertyRoute), (helper, tc) => constructor(helper, (TypeContext<T>)tc));
        }

        public ViewOverrides<T> AfterLine(PropertyRoute propertyRoute, Func<HtmlHelper, TypeContext, MvcHtmlString> constructor)
        {
            if (afterLine == null)
                afterLine = new Dictionary<PropertyRoute, Func<HtmlHelper, TypeContext, MvcHtmlString>>();

            afterLine[propertyRoute] = afterLine.TryGetC(propertyRoute) + constructor;

            return this;
        }


        MvcHtmlString IViewOverrides.OnSurroundLine(PropertyRoute propertyRoute, HtmlHelper helper, TypeContext tc, MvcHtmlString result)
        {
            var before = beforeLine.TryGetC(propertyRoute);
            if (before != null)
                foreach (var b in before.GetInvocationListTyped())
                    result = b(helper, tc).Concat(result);

            var after = afterLine.TryGetC(propertyRoute);
            if (after != null)
                foreach (var a in after.GetInvocationListTyped())
                    result = result.Concat(a(helper, tc));

            return result;
        }

        public ViewOverrides<T> HideLine<S>(Expression<Func<T, S>> propertyRoute) 
        {
            return HideLine(PropertyRoute.Construct(propertyRoute));
        }

        HashSet<PropertyRoute> hiddenLines;
        public ViewOverrides<T> HideLine(PropertyRoute propertyRoute)
        {
            if (hiddenLines == null)
                hiddenLines = new HashSet<PropertyRoute>();

            hiddenLines.Add(propertyRoute);

            return this;
        }

        bool IViewOverrides.IsVisible(PropertyRoute propertyRoute)
        {
            return hiddenLines == null || !hiddenLines.Contains(propertyRoute);
        }
    }
}
