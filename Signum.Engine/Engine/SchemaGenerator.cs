﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signum.Engine.Maps;
using Signum.Utilities;
using Signum.Entities;
using Signum.Engine.SchemaInfoTables;

namespace Signum.Engine
{
    public static class SchemaGenerator
    {
        public static SqlPreCommand CreateSchemasScript()
        {
            return Schema.Current.GetDatabaseTables()
                .Select(a => a.Name.Schema)
                .Where(s => s.Name != "dbo")
                .Distinct()
                .Select(SqlBuilder.CreateSchema)
                .Combine(Spacing.Simple);
        }

        public static SqlPreCommand CreateTablesScript()
        {
            List<ITable> tables = Schema.Current.GetDatabaseTables().ToList();

            SqlPreCommand createTables = tables.Select(SqlBuilder.CreateTableSql).Combine(Spacing.Double).ToSimple();

            SqlPreCommand foreignKeys = tables.Select(SqlBuilder.AlterTableForeignKeys).Combine(Spacing.Double).ToSimple();

            SqlPreCommand indices = tables.Select(SqlBuilder.CreateAllIndices).NotNull().Combine(Spacing.Double).ToSimple().SplitGOs().Combine(Spacing.Double);

            return SqlPreCommand.Combine(Spacing.Triple, createTables, foreignKeys, indices);
        }

        public static SqlPreCommand InsertEnumValuesScript()
        {
            return (from t in Schema.Current.Tables.Values
                    let enumType = EnumEntity.Extract(t.Type)
                    where enumType != null
                    select EnumEntity.GetEntities(enumType).Select((e, i) => t.InsertSqlSync(e, suffix: t.Name.Name + i)).Combine(Spacing.Simple)
                    ).Combine(Spacing.Double).ToSimple();
        }

      
        public static SqlPreCommand SnapshotIsolation()
        {
            if (!Connector.Current.AllowsSetSnapshotIsolation)
                return null;

            var list = Schema.Current.DatabaseNames().Select(a => a.TryToString()).ToList();

            if (list.Contains(null))
            {
                list.Remove(null);
                list.Add(Connector.Current.DatabaseName());
            }

            var cmd = list.Select(a =>
                SqlPreCommand.Combine(Spacing.Simple,
                //DisconnectUsers(a.name, "SPID" + i) : null,
                SqlBuilder.SetSnapshotIsolation(a, true),
                SqlBuilder.MakeSnapshotIsolationDefault(a, true))).Combine(Spacing.Double);

            return cmd;
        }
        
    }
}
