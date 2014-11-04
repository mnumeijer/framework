﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signum.Entities;
using Signum.Engine.Maps;
using Signum.Engine;
using System.IO;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Signum.Entities.Basics;
using Signum.Engine.DynamicQuery;
using Signum.Utilities.ExpressionTrees;
using Signum.Utilities;
using Microsoft.SqlServer.Types;
using Signum.Engine.Operations;
using Signum.Engine.Basics;
using Signum.Test.Properties;

namespace Signum.Test.Environment
{
    public static class MusicStarter
    {
        static bool startedAndLoaded = false;
        public static void StartAndLoad()
        {
            if (!startedAndLoaded)
            {
                Start(UserConnections.Replace(Settings.Default.SignumTest));

                Administrator.TotalGeneration();

                Schema.Current.Initialize();

                MusicLoader.Load();

                startedAndLoaded = true;
            }
        }

        public static void Start(string connectionString)
        {
            SchemaBuilder sb = new SchemaBuilder();
            DynamicQueryManager dqm = new DynamicQueryManager();
           
            //Connector.Default = new SqlCeConnector(@"Data Source=C:\BaseDatos.sdf", sb.Schema, dqm);
            
            Connector.Default = new SqlConnector(connectionString, sb.Schema, dqm, SqlServerVersion.SqlServer2008);


            sb.Schema.Version = typeof(MusicStarter).Assembly.GetName().Version;

            sb.Schema.Settings.OverrideAttributes((OperationLogDN ol) => ol.User, new ImplementedByAttribute());
            sb.Schema.Settings.OverrideAttributes((ExceptionDN e) => e.User, new ImplementedByAttribute());

            Validator.PropertyValidator((OperationLogDN e) => e.User).Validators.Clear();
            
            TypeLogic.Start(sb, dqm);

            OperationLogic.Start(sb, dqm);
            ExceptionLogic.Start(sb, dqm);

            MusicLogic.Start(sb, dqm);
        }
    }
}
