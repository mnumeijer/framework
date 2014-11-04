﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signum.Utilities;
using System.Web.Mvc;
using System.Web;

namespace Signum.Web
{
    public class ToolBarDropDown : ToolBarButton
    {
        public List<IMenuItem> Items { get; set; }

        public ToolBarDropDown(string prefix, string idToAppend)
            : base(prefix, idToAppend)
        {
        }

        public override MvcHtmlString ToHtml(HtmlHelper helper)
        {
            HtmlStringBuilder sb = new HtmlStringBuilder();
            using (sb.SurroundLine(new HtmlTag("div").Class("btn-group")))
            {
                var a = new HtmlTag("a")
                    .Id(Id)
                    .Class("btn")
                    .Class("btn-" + Style.ToString().ToLower())
                    .Class(CssClass)
                    .Class("dropdown-toggle")
                    .Attr("data-toggle", "dropdown")
                    .Attr("alt", Tooltip)
                    .Attrs(HtmlProps);

                if (!Enabled)
                    a.Attr("disabled", "disabled");

                using (sb.SurroundLine(a))
                {
                    sb.AddLine(new MvcHtmlString(Text));
                    sb.AddLine(new HtmlTag("span").Class("caret"));
                }


                using (sb.SurroundLine(new HtmlTag("ul").Class("dropdown-menu")))
                {
                    if (Items != null)
                        foreach (var ci in Items)
                            sb.Add(ci.ToHtml());
                }
            }

            return sb.ToHtml();
        }
    }

    public class MenuItemSeparator : IMenuItem
    {
        public MvcHtmlString ToHtml()
        {
            return new HtmlTag("li").Class("divider").ToHtml();
        }
    }

    public class MenuItemHeader : IMenuItem
    {
        public string Text { get; set; }
        public MenuItemHeader(string text)
        {
            this.Text = text;
        }

        public MvcHtmlString ToHtml()
        {
            return new HtmlTag("li").Class("dropdown-header").SetInnerText(this.Text).ToHtml();
        }
    }
}
