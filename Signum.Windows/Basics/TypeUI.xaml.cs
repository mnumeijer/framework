﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Signum.Windows;
using Signum.Entities;
using System.Reflection;
using Signum.Entities.Basics;
using Signum.Utilities;

namespace Signum.Windows.Basics
{
    /// <summary>
    /// Interaction logic for Exception.xaml
    /// </summary>
    public partial class TypeUI : UserControl
    {
        public TypeUI()
        {
            InitializeComponent();
        }
    }

    public static class TypeClient
    {
        public static void Start()
        {
            if (Navigator.Manager.NotDefined(MethodInfo.GetCurrentMethod()))
            {
                Navigator.AddSetting(new EntitySettings<TypeEntity>() { View = e => new TypeUI()});
            }
        }

        public static IEnumerable<TypeEntity> ViewableServerTypes()
        {
            return from t in Navigator.Manager.EntitySettings.Keys
                   let tdn = Server.ServerTypes.TryGetC(t)
                   where tdn != null && Navigator.IsViewable(t)
                   select tdn;
        }
    }
}
