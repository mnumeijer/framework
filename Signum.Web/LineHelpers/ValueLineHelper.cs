﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using System.Linq.Expressions;
using Signum.Utilities;
using System.Web.Mvc.Html;
using Signum.Entities.Reflection;
using Signum.Entities;
using System.Globalization;
using Signum.Entities.Basics;


namespace Signum.Web
{
    public static class ValueLineHelper
    {
        public static ValueLineConfigurator Configurator = new ValueLineConfigurator();

        private static MvcHtmlString InternalValueLine(this HtmlHelper helper, ValueLine valueLine)
        {
            if (!valueLine.Visible || (valueLine.HideIfNull && valueLine.UntypedValue == null))
                return MvcHtmlString.Empty;

            if (valueLine.PlaceholderLabels && !valueLine.ValueHtmlProps.ContainsKey("placeholder"))
                valueLine.ValueHtmlProps["placeholder"] = valueLine.LabelText;

            var value = InternalValue(helper, valueLine);

            if (valueLine.InlineCheckbox)
                return new HtmlTag("label").InnerHtml("{0} {1}".FormatHtml(value, valueLine.LabelText)).ToHtml();

            return helper.FormGroup(valueLine, valueLine.Prefix, valueLine.LabelText, value);
        }

        private static MvcHtmlString InternalValue(HtmlHelper helper, ValueLine valueLine)
        {
            HtmlStringBuilder sb = new HtmlStringBuilder();
            ValueLineType vltype = valueLine.ValueLineType ?? Configurator.GetDefaultValueLineType(valueLine.Type);

            using (valueLine.UnitText == null ? null : sb.SurroundLine(new HtmlTag("div").Class("input-group")))
            {
                sb.AddLine(Configurator.Constructor[vltype](helper, valueLine));

                if (valueLine.UnitText.HasText())
                    sb.AddLine(helper.Span(valueLine.Compose("unit"), valueLine.UnitText, "input-group-addon"));
            }

            return sb.ToHtml();
        }

        public static MvcHtmlString EnumComboBox(this HtmlHelper helper, ValueLine valueLine)
        {
            var uType = valueLine.Type.UnNullify();
            Enum value = valueLine.UntypedValue as Enum;

            if (valueLine.ReadOnly)
            {
                MvcHtmlString result = MvcHtmlString.Empty;
                if (valueLine.WriteHiddenOnReadonly)
                    result = result.Concat(helper.Hidden(valueLine.Prefix, valueLine.UntypedValue.ToString()));

                string str = value == null ? null :
                    LocalizedAssembly.GetDescriptionOptions(uType).IsSet(DescriptionOptions.Members) ? value.NiceToString() : value.ToString();

                return result.Concat(helper.FormControlStatic(null, str, valueLine.ValueHtmlProps));
            }

            StringBuilder sb = new StringBuilder();
            List<SelectListItem> items = valueLine.EnumComboItems ?? valueLine.CreateComboItems();

            if (value != null)
                items.Where(e => e.Value == value.ToString())
                    .SingleOrDefaultEx()
                    .TryDo(s => s.Selected = true);

            valueLine.ValueHtmlProps.AddCssClass("form-control");
            return helper.DropDownList(valueLine.Prefix, items, valueLine.ValueHtmlProps);
        }

        public static MvcHtmlString DateTimePicker(this HtmlHelper helper, ValueLine valueLine)
        {
            DateTime? value = (DateTime?)valueLine.UntypedValue;

            if (value.HasValue)
                value = value.Value.ToUserInterface();

            if (valueLine.ReadOnly)
            {
                MvcHtmlString result = MvcHtmlString.Empty;
                if (valueLine.WriteHiddenOnReadonly)
                    result = result.Concat(helper.Hidden(valueLine.Prefix, value.TryToString(valueLine.Format)));
                return result.Concat(helper.FormControlStatic(null, value.TryToString(valueLine.Format), valueLine.ValueHtmlProps));
            }

            valueLine.ValueHtmlProps.AddCssClass("form-control");
            return helper.DateTimePicker(valueLine.Prefix, true, value, valueLine.Format, CultureInfo.CurrentCulture, valueLine.ValueHtmlProps);
        }

        public static MvcHtmlString TimeSpanPicker(this HtmlHelper helper, ValueLine valueLine)
        {
            TimeSpan? value = (TimeSpan?)valueLine.UntypedValue;

            if (valueLine.ReadOnly)
            {
                MvcHtmlString result = MvcHtmlString.Empty;
                if (valueLine.WriteHiddenOnReadonly)
                    result = result.Concat(helper.Hidden(valueLine.Prefix, value.TryToString(valueLine.Format)));
                return result.Concat(helper.FormControlStatic(null, value.TryToString(valueLine.Format), valueLine.ValueHtmlProps));
            }

            var dateFormatAttr = valueLine.PropertyRoute.PropertyInfo.SingleAttribute<TimeSpanDateFormatAttribute>();
            if (dateFormatAttr != null)
                return helper.TimePicker(valueLine.Prefix, true, value, dateFormatAttr.Format, CultureInfo.CurrentCulture, valueLine.ValueHtmlProps);
            else
            {
                valueLine.ValueHtmlProps.AddCssClass("form-control");
                return helper.TextBox(valueLine.Prefix, value == null ? "" : value.Value.ToString(valueLine.Format, CultureInfo.CurrentCulture), valueLine.ValueHtmlProps);
            }
        }

        public static MvcHtmlString TextboxInLine(this HtmlHelper helper, ValueLine valueLine)
        {
            string value = (valueLine.UntypedValue as IFormattable).TryToString(valueLine.Format) ??
                           valueLine.UntypedValue.TryToString() ?? "";

            if (valueLine.ReadOnly)
            {
                MvcHtmlString result = MvcHtmlString.Empty;
                if (valueLine.WriteHiddenOnReadonly)
                    result = result.Concat(helper.Hidden(valueLine.Prefix, value));

                if (valueLine.UnitText.HasText())
                    return new HtmlTag("p").Id(valueLine.Prefix).SetInnerText(value).Class("form-control").Attrs(valueLine.ValueHtmlProps).ToHtml();
                else
                    return result.Concat(helper.FormControlStatic(valueLine.Prefix, value, valueLine.ValueHtmlProps));
            }

            if (!valueLine.ValueHtmlProps.ContainsKey("autocomplete"))
                valueLine.ValueHtmlProps.Add("autocomplete", "off");
            else
                valueLine.ValueHtmlProps.Remove("autocomplete");

            valueLine.ValueHtmlProps["onblur"] = "this.setAttribute('value', this.value); " + valueLine.ValueHtmlProps.TryGetC("onblur");

            if (!valueLine.ValueHtmlProps.ContainsKey("type"))
                valueLine.ValueHtmlProps["type"] = "text";

            valueLine.ValueHtmlProps.AddCssClass("form-control");
            return helper.TextBox(valueLine.Prefix, value, valueLine.ValueHtmlProps);
        }

        public static MvcHtmlString NumericTextbox(this HtmlHelper helper, ValueLine valueLine)
        {
            if (!valueLine.ReadOnly)
                valueLine.ValueHtmlProps.Add("onkeydown", Reflector.IsDecimalNumber(valueLine.Type) ? 
                    "return SF.InputValidator.isDecimal(event);" : 
                    "return SF.InputValidator.isNumber(event);");    
            
            return helper.TextboxInLine(valueLine);
        }

        public static MvcHtmlString ColorTextbox(this HtmlHelper helper, ValueLine valueLine)
        {
            HtmlStringBuilder sb = new HtmlStringBuilder();

            using (sb.SurroundLine(new HtmlTag("div").Class("input-group")))
            {
                valueLine.ValueHtmlProps.AddCssClass("form-control");

                ColorDN color = (ColorDN)valueLine.UntypedValue;

                sb.AddLine(helper.TextBox(valueLine.Prefix, color == null ? "" : color.RGBHex(), valueLine.ValueHtmlProps));

                sb.AddLine(new HtmlTag("span").Class("input-group-addon").InnerHtml(new HtmlTag("i")));
            }

            sb.AddLine(new HtmlTag("script").InnerHtml(MvcHtmlString.Create(
@" $(function(){
        $('#" + valueLine.Prefix + @"').parent().colorpicker()" + (valueLine.ReadOnly ? ".colorpicker('disable')" : null) + @";
   });")));

            return sb.ToHtml();
        }

        public static MvcHtmlString TextAreaInLine(this HtmlHelper helper, ValueLine valueLine)
        {
            if (valueLine.ReadOnly)
            {
                MvcHtmlString result = MvcHtmlString.Empty;
                if (valueLine.WriteHiddenOnReadonly)
                    result = result.Concat(helper.Hidden(valueLine.Prefix, (string)valueLine.UntypedValue));
                return result.Concat(helper.FormControlStatic("", (string)valueLine.UntypedValue, valueLine.ValueHtmlProps));
            }

            valueLine.ValueHtmlProps.Add("autocomplete", "off");
            valueLine.ValueHtmlProps["onblur"] = "this.innerHTML = this.value; " + valueLine.ValueHtmlProps.TryGetC("onblur");
            valueLine.ValueHtmlProps.AddCssClass("form-control");
            return helper.TextArea(valueLine.Prefix, (string)valueLine.UntypedValue, valueLine.ValueHtmlProps);
        }

        public static MvcHtmlString CheckBox(this HtmlHelper helper, ValueLine valueLine)
        {
            bool? value = (bool?)valueLine.UntypedValue;
            if (!valueLine.InlineCheckbox)
                valueLine.ValueHtmlProps.AddCssClass("form-control");
            return helper.CheckBox(valueLine.Prefix, value ?? false, !valueLine.ReadOnly, valueLine.ValueHtmlProps);
        }

        public static MvcHtmlString ValueLine(this HtmlHelper helper, ValueLine valueLine)
        {
            return helper.InternalValueLine(valueLine);
        }

        public static MvcHtmlString ValueLine<T, S>(this HtmlHelper helper, TypeContext<T> tc, Expression<Func<T, S>> property)
        {
            return helper.ValueLine(tc, property, null);
        }

        public static MvcHtmlString ValueLine<T, S>(this HtmlHelper helper, TypeContext<T> tc, Expression<Func<T, S>> property, Action<ValueLine> settingsModifier)
        {
            TypeContext<S> context = Common.WalkExpression(tc, property);

            var vo = tc.ViewOverrides;

            if (vo != null && !vo.IsVisible(context.PropertyRoute))
                return vo.OnSurroundLine(context.PropertyRoute, helper, tc, null);

            ValueLine vl = new ValueLine(typeof(S), context.Value, context, null, context.PropertyRoute);

            Common.FireCommonTasks(vl);

            if (settingsModifier != null)
                settingsModifier(vl);

            var result = helper.InternalValueLine(vl);

            if (vo == null)
                return result;

            return vo.OnSurroundLine(vl.PropertyRoute, helper, tc, result);
        }

        public static MvcHtmlString HiddenLine<T, S>(this HtmlHelper helper, TypeContext<T> tc, Expression<Func<T, S>> property)
        {
            return helper.HiddenLine(tc, property, null);
        }

        public static MvcHtmlString HiddenLine<T, S>(this HtmlHelper helper, TypeContext<T> tc, Expression<Func<T, S>> property, Action<HiddenLine> settingsModifier)
        {
            TypeContext<S> context = Common.WalkExpression(tc, property);

            HiddenLine hl = new HiddenLine(typeof(S), context.Value, context, null, context.PropertyRoute);

            Common.FireCommonTasks(hl);

            if (settingsModifier != null)
                settingsModifier(hl);

            return Hidden(helper, hl);
        }

        public static MvcHtmlString Hidden(this HtmlHelper helper, HiddenLine hiddenLine)
        {
            if (hiddenLine.ReadOnly)
                return helper.Span(hiddenLine.Prefix, hiddenLine.UntypedValue.TryToString() ?? "", "form-control");

            return helper.Hidden(hiddenLine.Prefix, hiddenLine.UntypedValue.TryToString() ?? "", hiddenLine.ValueHtmlProps);
        }
    }

    public class ValueLineConfigurator
    {
        public int? MaxValueLineSize = 100; 

        public virtual ValueLineType GetDefaultValueLineType(Type type)
        {
            type = type.UnNullify();

            if (type.IsEnum)
                return ValueLineType.Combo;
            else if (type == typeof(ColorDN))
                return ValueLineType.Color;
            else if (type == typeof(TimeSpan))
                return ValueLineType.TimeSpan;
            else
            {
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.DateTime:
                        return ValueLineType.DateTime;
                    case TypeCode.Boolean:
                        return ValueLineType.Boolean;
                    case TypeCode.Double:
                    case TypeCode.Decimal:
                    case TypeCode.Single:
                    case TypeCode.Byte:
                    case TypeCode.SByte:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                        return ValueLineType.Number;
                    case TypeCode.Empty:
                    case TypeCode.Object:
                    case TypeCode.Char:
                    case TypeCode.String:
                    default:
                        return ValueLineType.TextBox;
                }
            }

        }

        public Dictionary<ValueLineType, Func<HtmlHelper, ValueLine, MvcHtmlString>> Constructor = new Dictionary<ValueLineType, Func<HtmlHelper, ValueLine, MvcHtmlString>>()
        {
            {ValueLineType.TextBox, (helper, valueLine) => helper.TextboxInLine(valueLine)},
            {ValueLineType.TextArea, (helper, valueLine) => helper.TextAreaInLine(valueLine)},
            {ValueLineType.Boolean, (helper, valueLine) => helper.CheckBox(valueLine)},
            {ValueLineType.Combo, (helper, valueLine) => helper.EnumComboBox(valueLine)},
            {ValueLineType.DateTime, (helper, valueLine) => helper.DateTimePicker(valueLine)},
            {ValueLineType.TimeSpan, (helper, valueLine) => helper.TimeSpanPicker(valueLine)},
            {ValueLineType.Number, (helper, valueLine) => helper.NumericTextbox(valueLine)},
            {ValueLineType.Color, (helper, valueLine) => helper.ColorTextbox(valueLine)}
        };
    }

    public enum ValueLineType
    {
        Boolean,
        Combo,
        DateTime,
        TimeSpan,
        TextBox,
        TextArea,
        Number,
        Color
    };

}
