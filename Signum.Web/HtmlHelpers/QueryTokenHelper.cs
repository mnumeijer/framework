﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json.Linq;
using Signum.Entities.DynamicQuery;
using Signum.Utilities;

namespace Signum.Web
{
    public class QueryTokenBuilderSettings
    {
        public QueryTokenBuilderSettings(QueryDescription descripton, SubTokensOptions options)
        {
            this.QueryDescription = descripton;
            this.Options = options;
        }

        public readonly QueryDescription QueryDescription;
        public readonly SubTokensOptions Options;

        public Action<QueryToken, HtmlTag> Decorators;

        public JObject RequestExtraJSonData;
        public string ControllerUrl;
    }
  
    public static class QueryTokenBuilderHelper
    {
        public static Func<QueryToken, bool> AllowSubTokens = null;

        public static MvcHtmlString QueryTokenBuilder(this HtmlHelper helper, QueryToken queryToken, Context context, QueryTokenBuilderSettings settings)
        {
            HtmlStringBuilder sb = new HtmlStringBuilder();
            using (sb.SurroundLine(new HtmlTag("span").Id(context.Prefix).Class("token-builder")))
            {
                sb.Add(QueryTokenBuilderOptions(helper, queryToken, context, settings));
            }

            if (settings.ControllerUrl.HasText())
            {
                sb.Add(MvcHtmlString.Create("<script>" + JsModule.Finder["QueryTokenBuilder.init"](context.Prefix,
                    Finder.ResolveWebQueryName(settings.QueryDescription.QueryName), settings.ControllerUrl, (int)settings.Options, settings.RequestExtraJSonData).ToString()
                    + "</script>"));
            }
        
            return sb.ToHtml();     
        }

        public static MvcHtmlString QueryTokenBuilderOptions(this HtmlHelper helper, QueryToken queryToken, Context context, QueryTokenBuilderSettings settings)
        {
            var tokenPath = queryToken.Follow(qt => qt.Parent).Reverse().NotNull().ToList();

            HtmlStringBuilder sb = new HtmlStringBuilder();

            for (int i = 0; i < tokenPath.Count; i++)
            {
                sb.AddLine(helper.QueryTokenCombo(i == 0 ? null : tokenPath[i - 1], tokenPath[i], i, context, settings));
            }

            sb.AddLine(helper.QueryTokenCombo(queryToken, null, tokenPath.Count, context, settings));

            return sb.ToHtml();
        }

        static MvcHtmlString QueryTokenCombo(this HtmlHelper helper, QueryToken previous, QueryToken selected, int index, Context context, QueryTokenBuilderSettings settings)
        {
            if (previous != null && AllowSubTokens != null && !AllowSubTokens(previous))
                return MvcHtmlString.Create("");

            var queryTokens = previous.SubTokens(settings.QueryDescription, settings.Options);

            if (queryTokens.IsEmpty())
                return new HtmlTag("input")
                .Attr("type", "hidden")
                .IdName(context.Compose("ddlTokensEnd_" + index))
                .Attr("disabled", "disabled")
                .Attr("data-parenttoken", previous == null ? "" : previous.FullKey());

            var options = new HtmlStringBuilder();
            options.AddLine(new HtmlTag("option").Attr("value", "").SetInnerText("-").ToHtml());
            foreach (var qt in queryTokens)
            {
                var option = new HtmlTag("option")
                    .Attr("value", previous == null ? qt.FullKey() : qt.Key)
                    .SetInnerText((previous == null && qt.Parent != null ? " - " : "") + qt.ToString());

                if (selected != null && qt.Key == selected.Key)
                    option.Attr("selected", "selected");

                option.Attr("title", qt.NiceTypeName);
                option.Attr("style", "color:" + qt.TypeColor);

                if (settings.Decorators != null)
                    settings.Decorators(qt, option); 

                options.AddLine(option.ToHtml());
            }

            HtmlTag dropdown = new HtmlTag("select")
                .Class("form-control")
                .IdName(context.Compose("ddlTokens_" + index))
                .InnerHtml(options.ToHtml()) 
                .Attr("data-parenttoken", previous == null ? "" : previous.FullKey());

            if (selected != null)
            {
                dropdown.Attr("title", selected.NiceTypeName);
                dropdown.Attr("style", "color:" + selected.TypeColor);
            }

            return dropdown.ToHtml();
        }


    
      
    }
}