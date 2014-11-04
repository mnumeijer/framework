﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signum.Engine;
using System.Data;
using Signum.Engine.Maps;
using Signum.Utilities;
using Signum.Entities;
using Signum.Engine.SchemaInfoTables;
using System.Text.RegularExpressions;
using Signum.Engine.Linq;
using Signum.Entities.Reflection;

namespace Signum.Engine
{
    public static class SchemaSynchronizer
    {
        public static SqlPreCommand SynchronizeSchemasScript(Replacements replacements)
        {
            HashSet<SchemaName> model = Schema.Current.GetDatabaseTables().Select(a => a.Name.Schema).ToHashSet();
            HashSet<SchemaName> database = new HashSet<SchemaName>();
            foreach (var db in model.Select(a => a.Database).Distinct())
	        {
                using (Administrator.OverrideDatabaseInViews(db))
                {
                    database.AddRange(
                     from s in Database.View<SysSchemas>()
                     select new SchemaName(db, s.name));
                }
	        }

            return Synchronizer.SynchronizeScript(
                model.ToDictionary(a => a),
                database.ToDictionary(a => a),
                (_, newSN) => SqlBuilder.CreateSchema(newSN),
                null,
                null, Spacing.Simple);
        }

        public static SqlPreCommand SynchronizeSystemDefaultConstraints(Replacements replacements)
        {
            var allConstraints = Schema.Current.DatabaseNames().Select(db =>
            {
                using (Administrator.OverrideDatabaseInViews(db))
                {
                    var constaints = (from t in Database.View<SysTables>()
                                      join s in Database.View<SysSchemas>() on t.schema_id equals s.schema_id
                                      join c in Database.View<SysColumns>() on t.object_id equals c.object_id
                                      join ctr in Database.View<SysDefaultConstraints>() on c.default_object_id equals ctr.object_id
                                      where ctr.is_system_named
                                      select new
                                      {
                                          table = new ObjectName(new SchemaName(db, s.name), t.name),
                                          column = c.name,
                                          constraint = ctr.name,
                                          definition = ctr.definition,
                                      }).ToList();

                    if (!constaints.Any())
                        return null;

                    return new SqlPreCommandSimple(
                        constaints.ToString(a => "-- because default constraint " + a.constraint + " in " + a.table.ToString() + "." + a.column, "\r\n") + @"
declare @sql nvarchar(max)
set @sql = ''
select @sql = @sql + 'ALTER TABLE [' + t.name + '] DROP CONSTRAINT [' + dc.name  + '];' 
from {0}sys.default_constraints dc
join {0}sys.tables t on dc.parent_object_id = t.object_id
exec {0}dbo.sp_executesql @sql".Formato(db == null ? null : (db.ToString() + ".")));
                }
            }).ToList();

            return allConstraints.Combine(Spacing.Simple);
        } 

        public static SqlPreCommand SynchronizeTablesScript(Replacements replacements)
        {
            //Temproal HACK
            if (Database.View<SysIndexes>().Any(a => a.name.StartsWith("FIX")) && SafeConsole.Ask("Old index naming convention...rename first?"))
            {
                return Schema.Current.DatabaseNames().Select(db =>
                {
                    using (Administrator.OverrideDatabaseInViews(db))
                    {
                        var indexes =
                            (from s in Database.View<SysSchemas>()
                             from t in s.Tables()
                             from ix in t.Indices()
                             where !ix.is_primary_key
                             select new { schemaName = s.name, tableName = t.name, ix.is_unique, indexName = ix.name }).ToList();

                        return (from ix in indexes
                                let newName = ix.is_unique ? Regex.Replace(ix.indexName, @"^IX_\w+?_", "UIX_") : Regex.Replace(ix.indexName, @"^F?IX_\w+?_", "IX_")
                                where ix.indexName != newName
                                select new SqlPreCommandSimple("EXEC SP_RENAME '{0}.{1}' , '{2}', 'INDEX' ".Formato(
                                    new ObjectName(new SchemaName(db, ix.schemaName), ix.tableName), ix.indexName, newName))).Combine(Spacing.Simple);
                    }
                }).Combine(Spacing.Double);
            }

            Dictionary<string, ITable> model = Schema.Current.GetDatabaseTables().ToDictionary(a => a.Name.ToString(), "schema tables");

            Dictionary<string, DiffTable> database = DefaultGetDatabaseDescription(Schema.Current.DatabaseNames());

            replacements.AskForReplacements(database.Keys.ToHashSet(), model.Keys.ToHashSet(), Replacements.KeyTables);

            database = replacements.ApplyReplacementsToOld(database, Replacements.KeyTables);

            Dictionary<ITable, Dictionary<string, Index>> modelIndices = model.Values
                .ToDictionary(t => t, t => t.GeneratAllIndexes().ToDictionary(a => a.IndexName, "Indexes for {0}".Formato(t.Name)));

            model.JoinDictionaryForeach(database, (tn, tab, diff) =>
            {
                var key = Replacements.KeyColumnsForTable(tn);

                replacements.AskForReplacements(diff.Colums.Keys.ToHashSet(), tab.Columns.Keys.ToHashSet(), key);

                diff.Colums = replacements.ApplyReplacementsToOld(diff.Colums,  key);

                diff.Indices = ApplyIndexAutoReplacements(diff, tab, modelIndices[tab]);
            });

            Func<ObjectName, ObjectName> ChangeName = (ObjectName objectName) =>
            {
                string name = replacements.Apply(Replacements.KeyTables, objectName.ToString());

                return model.TryGetC(name).Try(a => a.Name) ?? objectName;
            };

            Func<DiffTable, DiffIndex, Index, bool> columnsChanged = (dif, dix, mix) =>
            {
                if (dix.Columns.Count != mix.Columns.Length)
                    return true;

                var dixColumns = dif.Colums.Where(kvp => dix.Columns.Contains(kvp.Value.Name));

                return !dixColumns.All(kvp => dif.Colums.GetOrThrow(kvp.Key).ColumnEquals(mix.Columns.SingleEx(c => c.Name == kvp.Key)));
            };

            //use database without replacements to just remove indexes
            SqlPreCommand dropStatistics =
                Synchronizer.SynchronizeScript(model, database,
                 null,
                (tn, dif) => SqlBuilder.DropStatistics(tn, dif.Stats),
                (tn, tab, dif) =>
                {
                    var removedColums = dif.Colums.Keys.Except(tab.Columns.Keys).ToHashSet();

                    return SqlBuilder.DropStatistics(tn, dif.Stats.Where(a => a.Columns.Any(removedColums.Contains)).ToList());
                },
                 Spacing.Double);


            SqlPreCommand dropIndices =
                Synchronizer.SynchronizeScript(model, database,
                 null,
                (tn, dif) => dif.Indices.Values.Select(ix => SqlBuilder.DropIndex(dif.Name, ix)).Combine(Spacing.Simple),
                (tn, tab, dif) =>
                {
                    Dictionary<string, Index> modelIxs = modelIndices[tab];

                    var removedColums = dif.Colums.Keys.Except(tab.Columns.Keys).ToHashSet();

                    var changes = Synchronizer.SynchronizeScript(modelIxs, dif.Indices,
                        null,
                        (i, dix) => dix.IsControlledIndex || dix.Columns.Any(removedColums.Contains)  ? SqlBuilder.DropIndex(dif.Name, dix) : null,
                        (i, mix, dix) => (mix as UniqueIndex).Try(u => u.ViewName) != dix.ViewName || columnsChanged(dif, dix, mix) ? SqlBuilder.DropIndex(dif.Name, dix) : null,
                        Spacing.Simple);

                    return changes;
                },
                 Spacing.Double);

            SqlPreCommand dropForeignKeys = Synchronizer.SynchronizeScript(
                 model,
                 database,
                 null,
                 (tn, dif) => dif.Colums.Values.Select(c => c.ForeingKey != null ? SqlBuilder.AlterTableDropConstraint(dif.Name, c.ForeingKey.Name) : null).Combine(Spacing.Simple),
                 (tn, tab, dif) => Synchronizer.SynchronizeScript(
                     tab.Columns,
                     dif.Colums,
                     null,
                     (cn, colDb) => colDb.ForeingKey != null ? SqlBuilder.AlterTableDropConstraint(dif.Name, colDb.ForeingKey.Name) : null,
                     (cn, colModel, colDb) => colDb.ForeingKey == null ? null :
                         colModel.ReferenceTable == null || !colModel.ReferenceTable.Name.Equals(ChangeName(colDb.ForeingKey.TargetTable)) ? 
                         SqlBuilder.AlterTableDropConstraint(dif.Name, colDb.ForeingKey.Name) :
                         null, Spacing.Simple),
                        Spacing.Double);

            SqlPreCommand tables =
                Synchronizer.SynchronizeScript(
                model,
                database,
                (tn, tab) => SqlBuilder.CreateTableSql(tab),
                (tn, dif) => SqlBuilder.DropTable(dif.Name),
                (tn, tab, dif) =>
                    SqlPreCommand.Combine(Spacing.Simple,
                    !object.Equals(dif.Name, tab.Name) ? SqlBuilder.RenameOrMove(dif, tab) : null,
                    Synchronizer.SynchronizeScript(
                        tab.Columns,
                        dif.Colums,
                        (cn, tabCol) => SqlBuilder.AlterTableAddColumn(tab, tabCol),
                        (cn, difCol) => SqlBuilder.AlterTableDropColumn(tab, cn),
                        (cn, tabCol, difCol) =>SqlPreCommand.Combine(Spacing.Simple,
                            difCol.Name == tabCol.Name ? null : SqlBuilder.RenameColumn(tab, difCol.Name, tabCol.Name),
                            difCol.ColumnEquals(tabCol) ? null : SqlBuilder.AlterTableAlterColumn(tab, tabCol),
                            UpdateByFkChange(tn, difCol, tabCol, ChangeName)),
                        Spacing.Simple)),
                 Spacing.Double);

            var tableReplacements = replacements.TryGetC(Replacements.KeyTables);
            if (tableReplacements != null)
                replacements[Replacements.KeyTablesInverse] = tableReplacements.Inverse();

            SqlPreCommand syncEnums = SynchronizeEnumsScript(replacements);

            SqlPreCommand addForeingKeys = Synchronizer.SynchronizeScript(
                 model,
                 database,
                 (tn, tab) => SqlBuilder.AlterTableForeignKeys(tab),
                 null,
                 (tn, tab, dif) => Synchronizer.SynchronizeScript(
                     tab.Columns,
                     dif.Colums,
                     (cn, colModel) => colModel.ReferenceTable == null ? null:
                         SqlBuilder.AlterTableAddConstraintForeignKey(tab, colModel.Name, colModel.ReferenceTable),
                     null,
                     (cn, colModel, coldb) =>
                     {
                         if(colModel.ReferenceTable == null)
                            return null;

                         if(coldb.ForeingKey == null || !colModel.ReferenceTable.Name.Equals(ChangeName(coldb.ForeingKey.TargetTable)))
                            return SqlBuilder.AlterTableAddConstraintForeignKey(tab, colModel.Name, colModel.ReferenceTable);
                                                          
                         var name = SqlBuilder.ForeignKeyName(tab.Name.Name, colModel.Name);
                         return SqlPreCommand.Combine(Spacing.Simple,
                            name != coldb.ForeingKey.Name? SqlBuilder.RenameForeignKey(tab.Name.Schema, coldb.ForeingKey.Name, name) : null, 
                            (coldb.ForeingKey.IsDisabled || coldb.ForeingKey.IsNotTrusted) && !ExecutionMode.IsSynchronizeSchemaOnly ? SqlBuilder.EnableForeignKey(tab.Name,  name) : null);
                     },
                     Spacing.Simple),
                 Spacing.Double);

            bool? createMissingFreeIndexes = null;

            SqlPreCommand addIndices =
                Synchronizer.SynchronizeScript(model, database,
                 (tn, tab) => modelIndices[tab].Values.Select(SqlBuilder.CreateIndex).Combine(Spacing.Simple),
                 null,
                (tn, tab, dif) =>
                {
                    var columnReplacements = replacements.TryGetC(Replacements.KeyColumnsForTable(tn));

                    Func<IColumn, bool> isNew = c => !dif.Colums.ContainsKey(columnReplacements.TryGetC(c.Name) ?? c.Name);

                    Dictionary<string, Index> modelIxs = modelIndices[tab];

                    var controlledIndexes = Synchronizer.SynchronizeScript(modelIxs, dif.Indices,
                        (i, mix) => mix is UniqueIndex || mix.Columns.Any(isNew) || SafeConsole.Ask(ref createMissingFreeIndexes, "Create missing non-unique index too?") ? SqlBuilder.CreateIndex(mix) : null,
                        null,
                        (i, mix, dix) => (mix as UniqueIndex).Try(u => u.ViewName) != dix.ViewName || columnsChanged(dif, dix, mix) ? SqlBuilder.CreateIndex(mix) :
                            mix.IndexName != dix.IndexName ? SqlBuilder.RenameIndex(tab, dix.IndexName, mix.IndexName) : null,
                        Spacing.Simple);

                    return SqlPreCommand.Combine(Spacing.Simple, controlledIndexes);
                }, Spacing.Double);

            return SqlPreCommand.Combine(Spacing.Triple, dropStatistics, dropIndices, dropForeignKeys, tables, syncEnums, addForeingKeys, addIndices);
        }



        private static Dictionary<string, DiffIndex> ApplyIndexAutoReplacements(DiffTable diff, ITable tab, Dictionary<string, Index> dictionary)
        {
            List<string> oldOnly = diff.Indices.Keys.Where(n => !dictionary.ContainsKey(n)).ToList();
            List<string> newOnly = dictionary.Keys.Where(n => !diff.Indices.ContainsKey(n)).ToList();

            if (oldOnly.Count == 0 || newOnly.Count == 0)
                return diff.Indices;

            Dictionary<string, string> replacements = new Dictionary<string,string>(); 
            foreach (var o in oldOnly)
	        {
                var oldIx = diff.Indices[o];

                var nIx = newOnly.FirstOrDefault(n =>
                {
                    var newIx = dictionary[n];

                    if (oldIx.IsUnique != (newIx is UniqueIndex))
                        return false;

                    if (oldIx.ViewName != null || (newIx is UniqueIndex) && ((UniqueIndex)newIx).ViewName != null)
                        return false;

                    var news = newIx.Columns.Select(c => diff.Colums.TryGetC(c.Name).Try(d => d.Name)).NotNull().ToHashSet();

                    if (!news.SetEquals(oldIx.Columns))
                        return false;

                    var uix = newIx as UniqueIndex;
                    if (uix != null && uix.Where != null && !oldIx.IndexName.EndsWith(StringHashEncoder.Codify(uix.Where)))
                        return false;

                    return true;
                });

                if (nIx != null)
                {
                    replacements.Add(o, nIx);
                    newOnly.Remove(nIx); 
                }
	        }

            if (replacements.IsEmpty())
                return diff.Indices;

            return diff.Indices.SelectDictionary(on => replacements.TryGetC(on) ?? on, dif => dif);
        }

        private static SqlPreCommandSimple UpdateByFkChange(string tn, DiffColumn difCol, IColumn tabCol, Func<ObjectName, ObjectName> changeName)
        {
            if (difCol.ForeingKey == null || tabCol.ReferenceTable == null)
                return null;

            ObjectName oldFk = changeName(difCol.ForeingKey.TargetTable);

            if (oldFk.Equals(tabCol.ReferenceTable.Name))
                return null;

            AliasGenerator ag = new AliasGenerator();

            return new SqlPreCommandSimple(
@"UPDATE {2}
SET {0} = GetId{5}({4}.Id)
FROM {1} {2}
JOIN {3} {4} ON {2}.{0} = {4}.Id".Formato(tabCol.Name,
                tn, ag.NextTableAlias(tn),
                oldFk, ag.NextTableAlias(oldFk.Name),
                tabCol.ReferenceTable.Name.Name));
        }

        public static Dictionary<string, DiffTable> DefaultGetDatabaseDescription(List<DatabaseName> databases)
        {
            var udttypes = Schema.Current.Settings.UdtSqlName.Values.ToHashSet(StringComparer.InvariantCultureIgnoreCase);

            List<DiffTable> allTables = new List<DiffTable>();

            foreach (var db in databases)
            {
                using (Administrator.OverrideDatabaseInViews(db))
                {
                    var tables =
                        (from s in Database.View<SysSchemas>()
                         from t in s.Tables()
                         where !t.ExtendedProperties().Any(a => a.name == "microsoft_database_tools_support")
                         select new DiffTable
                         {
                             Name = new ObjectName(new SchemaName(db, s.name), t.name),
                             Colums = (from c in t.Columns()
                                       join type in Database.View<SysTypes>() on c.user_type_id equals type.user_type_id
                                       join ctr in Database.View<SysObjects>().DefaultIfEmpty() on c.default_object_id equals ctr.object_id
                                       select new DiffColumn
                                       {
                                           Name = c.name,
                                           SqlDbType = udttypes.Contains(type.name) ? SqlDbType.Udt : ToSqlDbType(type.name),
                                           UdtTypeName = udttypes.Contains(type.name) ? type.name : null,
                                           Nullable = c.is_nullable,
                                           Length = c.max_length,
                                           Precission = c.precision,
                                           Scale = c.scale,
                                           Identity = c.is_identity,
                                           DefaultConstraintName = ctr.name,
                                           PrimaryKey = t.Indices().Any(i => i.is_primary_key && i.IndexColumns().Any(ic => ic.column_id == c.column_id)),
                                           ForeingKey = (from fk in t.ForeignKeys()
                                                         where fk.ForeignKeyColumns().Any(fkc => fkc.parent_column_id == c.column_id)
                                                         join rt in Database.View<SysTables>() on fk.referenced_object_id equals rt.object_id
                                                         join rs in Database.View<SysSchemas>() on rt.schema_id equals rs.schema_id
                                                         select fk.name == null ? null : new DiffForeignKey
                                                         {
                                                             Name = fk.name,
                                                             IsDisabled = fk.is_disabled,
                                                             TargetTable = new ObjectName(new SchemaName(db, rs.name), rt.name),
                                                         }).FirstOrDefault(),
                                       }).ToDictionary(a => a.Name, "columns"),

                             SimpleIndices = (from i in t.Indices()
                                              where !i.is_primary_key //&& !(i.is_unique && i.name.StartsWith("IX_"))
                                              select new DiffIndex
                                              {
                                                  IsUnique = i.is_unique,
                                                  IndexName = i.name,
                                                  Columns = (from ic in i.IndexColumns()
                                                             join c in t.Columns() on ic.column_id equals c.column_id
                                                             select c.name).ToList()
                                              }).ToList(),

                             ViewIndices = (from v in Database.View<SysViews>()
                                            where v.name.StartsWith("VIX_" + t.name + "_")
                                            from i in v.Indices()
                                            select new DiffIndex
                                            {
                                                IsUnique = i.is_unique,
                                                ViewName = v.name,
                                                IndexName = i.name,
                                                Columns = (from ic in i.IndexColumns()
                                                           join c in v.Columns() on ic.column_id equals c.column_id
                                                           select c.name).ToList()

                                            }).ToList(),

                             Stats = (from st in t.Stats()
                                      where st.user_created
                                      select new DiffStats
                                      {
                                          StatsName = st.name,
                                          Columns = (from ic in st.StatsColumns()
                                                     join c in t.Columns() on ic.column_id equals c.column_id
                                                     select c.name).ToList()
                                      }).ToList(),

                         }).ToList();

                    allTables.AddRange(tables);
                }
            }

            var database = allTables.ToDictionary(t => t.Name.ToString());

            return database;
        }


        public static SqlDbType ToSqlDbType(string str)
        {
            if(str == "numeric")
                return SqlDbType.Decimal;

            return str.ToEnum<SqlDbType>(true);
        }

  
        static SqlPreCommand SynchronizeEnumsScript(Replacements replacements)
        {
            Schema schema = Schema.Current;
            
            List<SqlPreCommand> commands = new List<SqlPreCommand>();

            foreach (var table in schema.Tables.Values)
            {
                Type enumType = EnumEntity.Extract(table.Type);
                if (enumType != null)
                {
                    IEnumerable<IdentifiableEntity> should = EnumEntity.GetEntities(enumType);
                    Dictionary<string, IdentifiableEntity> shouldByName = should.ToDictionary(a => a.ToString());

                    List<IdentifiableEntity> current = Administrator.TryRetrieveAll(table.Type, replacements);
                    Dictionary<string, IdentifiableEntity> currentByName = current.ToDictionary(a => a.toStr, table.Name.Name);

                    string key = Replacements.KeyEnumsForTable(table.Name.Name);

                    replacements.AskForReplacements(currentByName.Keys.ToHashSet(), shouldByName.Keys.ToHashSet(), key);

                    currentByName = replacements.ApplyReplacementsToOld(currentByName, key);

                    var mix = shouldByName.JoinDictionary(currentByName, (n, s, c) => new { s, c }).Where(a =>a.Value.s.id != a.Value.c.id).ToDictionary();

                    HashSet<int> usedIds = current.Select(a => a.Id).ToHashSet();

                    Dictionary<string, IdentifiableEntity> middleByName = mix.Where(kvp => usedIds.Contains(kvp.Value.s.Id)).ToDictionary(kvp => kvp.Key, kvp => Clone(kvp.Value.c));

                    if (middleByName.Any())
                    {
                        var moveToAux = SyncEnums(schema, table,  currentByName.Where(a => middleByName.ContainsKey(a.Key)).ToDictionary(), middleByName);
                        if (moveToAux != null)
                            commands.Add(moveToAux);
                    }

                    var currentMiddleByName = currentByName.ToDictionary();

                    currentMiddleByName.SetRange(middleByName);

                    var com = SyncEnums(schema, table, currentMiddleByName, shouldByName);
                    if (com != null)
                        commands.Add(com);
                }
            }

            return SqlPreCommand.Combine(Spacing.Double, commands.ToArray());
        }

        private static SqlPreCommand SyncEnums(Schema schema, Table table, Dictionary<string, IdentifiableEntity> current, Dictionary<string, IdentifiableEntity> should)
        {
            return Synchronizer.SynchronizeScript(
                       should,
                       current,
                       (str, s) => table.InsertSqlSync(s),
                       (str, c) => table.DeleteSqlSync(c, comment: c.toStr),
                       (str, s, c) =>
                       {
                           if (s.id == c.id)
                               return table.UpdateSqlSync(c, comment: c.toStr);

                           var insert = table.InsertSqlSync(s);

                           var move = (from t in schema.GetDatabaseTables()
                                       from col in t.Columns.Values
                                       where col.ReferenceTable == table
                                       select new SqlPreCommandSimple("UPDATE {0} SET {1} = {2} WHERE {1} = {3} -- {4} re-indexed"
                                           .Formato(t.Name, col.Name, s.Id, c.Id, c.toStr)))
                                        .Combine(Spacing.Simple);

                           var delete = table.DeleteSqlSync(c, comment: c.toStr);

                           return SqlPreCommand.Combine(Spacing.Simple, insert, move, delete);
                       }, Spacing.Double);
        }

        private static IdentifiableEntity Clone(IdentifiableEntity current)
        {
            var instance = (IdentifiableEntity)Activator.CreateInstance(current.GetType());
            instance.toStr = current.toStr;
            instance.id = current.id.Value + 1000000;
            return instance;
        }

        public static SqlPreCommand SnapshotIsolation(Replacements replacements)
        {
            if (ExecutionMode.IsSynchronizeSchemaOnly)
                return null;

            var list = Schema.Current.DatabaseNames().Select(a => a.TryToString()).ToList();

            if (list.Contains(null))
            {
                list.Remove(null);
                list.Add(Connector.Current.DatabaseName());
            }

            var results = Database.View<SysDatabases>()
                .Where(d => list.Contains(d.name))
                .Select(d => new { d.name, d.snapshot_isolation_state, d.is_read_committed_snapshot_on }).ToList();

            var cmd = results.Select((a, i) =>
                SqlPreCommand.Combine(Spacing.Simple,
                !a.snapshot_isolation_state || !a.is_read_committed_snapshot_on ? DisconnectUsers(a.name, "SPID" + i) : null,
                !a.snapshot_isolation_state ? SqlBuilder.SetSnapshotIsolation(a.name, true) : null,
                !a.is_read_committed_snapshot_on ? SqlBuilder.MakeSnapshotIsolationDefault(a.name, true) : null)).Combine(Spacing.Double);

            if (cmd == null)
                return null;

            return SqlPreCommand.Combine(Spacing.Double,
                new SqlPreCommandSimple("use master -- Start Snapshot"),
                cmd,
                new SqlPreCommandSimple("use {0} -- Stop Snapshot".Formato(Connector.Current.DatabaseName())));
        }

        public static SqlPreCommandSimple DisconnectUsers(string databaseName, string variableName)
        {
            return new SqlPreCommandSimple(@"DECLARE @{1} VARCHAR(7000)
SELECT @{1} = COALESCE(@{1},'')+'KILL '+CAST(SPID AS VARCHAR)+'; 'FROM master..SysProcesses WHERE DB_NAME(DBId) = '{0}'
EXEC(@{1})".Formato(databaseName, variableName));
        }
    }

    public class DiffTable
    {
        public ObjectName Name;

        public Dictionary<string, DiffColumn> Colums;

        public List<DiffIndex> SimpleIndices
        {
            get { return Indices.Values.ToList(); }
            set { Indices.AddRange(value, a => a.IndexName, a => a); }
        }

        public List<DiffIndex> ViewIndices
        {
            get { return Indices.Values.ToList(); }
            set { Indices.AddRange(value, a => a.IndexName, a => a); }
        }

        public Dictionary<string, DiffIndex> Indices = new Dictionary<string, DiffIndex>();

        public List<DiffStats> Stats = new List<DiffStats>();
    }

    public class DiffStats
    {
        public string StatsName;

        public List<string> Columns;
    }

    public class DiffIndex
    {
        public bool IsUnique;
        public string IndexName;
        public string ViewName;

        public List<string> Columns;

        public override string ToString()
        {
            return "{0} ({1})".Formato(IndexName, Columns.ToString(", "));
        }

        public bool IsControlledIndex
        {
            get { return IndexName.StartsWith("IX_") || IndexName.StartsWith("UIX_"); }
        }
    }

    public class DiffColumn
    {
        public string Name;
        public SqlDbType SqlDbType;
        public string UdtTypeName; 
        public bool Nullable;
        public int Length; 
        public int Precission;
        public int Scale;
        public bool Identity;
        public bool PrimaryKey;

        public DiffForeignKey ForeingKey; 

        public string DefaultConstraintName;

        public bool ColumnEquals(IColumn other)
        {
            var result =
                   SqlDbType == other.SqlDbType
                && StringComparer.InvariantCultureIgnoreCase.Equals(UdtTypeName, other.UdtTypeName)
                && Nullable == other.Nullable
                && (other.Size == null || other.Size.Value == Precission || other.Size.Value == Length / 2 || other.Size.Value == int.MaxValue && Length == -1)
                && (other.Scale == null || other.Scale.Value == Scale)
                && Identity == other.Identity
                && PrimaryKey == other.PrimaryKey;

            return result;
        }
    }

    public class DiffForeignKey
    {
        public string Name;
        public ObjectName TargetTable;
        public bool IsDisabled; 
        public bool IsNotTrusted;
    }
}
