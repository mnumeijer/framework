﻿#pragma warning disable 1591
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.18033
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Signum.Web.Views
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Web;
    using System.Web.Helpers;
    using System.Web.Mvc;
    using System.Web.Mvc.Ajax;
    using System.Web.Mvc.Html;
    using System.Web.Routing;
    using System.Web.Security;
    using System.Web.UI;
    using System.Web.WebPages;
    using Signum.Entities;
    
    #line 1 "..\..\Signum\Views\PopupOkControl.cshtml"
    using Signum.Entities.Reflection;
    
    #line default
    #line hidden
    using Signum.Utilities;
    using Signum.Web;
    
    [System.CodeDom.Compiler.GeneratedCodeAttribute("RazorGenerator", "1.5.0.0")]
    [System.Web.WebPages.PageVirtualPathAttribute("~/Signum/Views/PopupOkControl.cshtml")]
    public class PopupOkControl : System.Web.Mvc.WebViewPage<TypeContext>
    {
        public PopupOkControl()
        {
        }
        public override void Execute()
        {



            
            #line 3 "..\..\Signum\Views\PopupOkControl.cshtml"
   ModifiableEntity modifiable = Model.UntypedValue as ModifiableEntity; 

            
            #line default
            #line hidden
WriteLiteral("<div id=\"");


            
            #line 4 "..\..\Signum\Views\PopupOkControl.cshtml"
    Write(Model.Compose("panelPopup"));

            
            #line default
            #line hidden
WriteLiteral("\" class=\"sf-popup-control\" data-prefix=\"");


            
            #line 4 "..\..\Signum\Views\PopupOkControl.cshtml"
                                                                        Write(Model.ControlID);

            
            #line default
            #line hidden
WriteLiteral("\" data-title=\"");


            
            #line 4 "..\..\Signum\Views\PopupOkControl.cshtml"
                                                                                                      Write(Navigator.Manager.GetTypeTitle(modifiable));

            
            #line default
            #line hidden
WriteLiteral("\">\r\n    <h2><span class=\"sf-entity-title\">");


            
            #line 5 "..\..\Signum\Views\PopupOkControl.cshtml"
                                  Write(ViewBag.Title ?? Model.UntypedValue.TryToString());

            
            #line default
            #line hidden
WriteLiteral("</span></h2>\r\n    <div class=\"sf-button-bar\">\r\n        <button id=\"");


            
            #line 7 "..\..\Signum\Views\PopupOkControl.cshtml"
               Write(Model.Compose("btnOk"));

            
            #line default
            #line hidden
WriteLiteral("\" class=\"sf-entity-button sf-ok-button\" ");


            
            #line 7 "..\..\Signum\Views\PopupOkControl.cshtml"
                                                                               Write(ViewData[ViewDataKeys.OnOk] != null ? Html.Raw("onclick=\"" + ViewData[ViewDataKeys.OnOk] + "\"") : null);

            
            #line default
            #line hidden
WriteLiteral(">\r\n                OK</button>                \r\n    </div>\r\n    ");


            
            #line 10 "..\..\Signum\Views\PopupOkControl.cshtml"
Write(Html.ValidationSummaryAjax(Model));

            
            #line default
            #line hidden
WriteLiteral("\r\n    <div id=\"");


            
            #line 11 "..\..\Signum\Views\PopupOkControl.cshtml"
        Write(Model.Compose("divMainControl"));

            
            #line default
            #line hidden
WriteLiteral("\" class=\"sf-main-control");


            
            #line 11 "..\..\Signum\Views\PopupOkControl.cshtml"
                                                                 Write(modifiable != null && modifiable.IsGraphModified ? " sf-changed" : "");

            
            #line default
            #line hidden
WriteLiteral("\">\r\n");


            
            #line 12 "..\..\Signum\Views\PopupOkControl.cshtml"
           Html.RenderPartial(ViewData[ViewDataKeys.PartialViewName].ToString(), Model);

            
            #line default
            #line hidden
WriteLiteral("    </div>\r\n</div>\r\n");


        }
    }
}
#pragma warning restore 1591
