﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Signum.Entities;
using Signum.Engine;
using Signum.Utilities;
using Signum.Entities.Reflection;
using Signum.Engine.Exceptions;
using System.Linq.Expressions;
using System.Reflection;
using Signum.Utilities.Reflection;
using System.Data;
using Signum.Utilities.ExpressionTrees;
using System.Threading;
using System.Text;
using Signum.Utilities.DataStructures;
using System.Data.Common;
using System.Collections.Concurrent;
using Signum.Engine.Basics;

namespace Signum.Engine.Maps
{
    public struct Forbidden
    {
        public Forbidden(HashSet<IdentifiableEntity> set)
        {
            this.set = set;
        }

        public Forbidden(DirectedGraph<IdentifiableEntity> graph, IdentifiableEntity entity)
        {
            this.set = graph == null ? null : graph.TryRelatedTo(entity);
        }

        readonly HashSet<IdentifiableEntity> set;

        public bool IsEmpty
        {
            get { return set == null || set.Count == 0; }
        }

        public bool Contains(IdentifiableEntity entity)
        {
            return set != null && set.Contains(entity);
        }
    }

    public struct EntityForbidden
    {
        public readonly Entity Entity;
        public readonly Forbidden Forbidden;

        public EntityForbidden(IdentifiableEntity entity, Forbidden forbidden)
        {
            this.Entity = (Entity)entity;
            this.Forbidden = forbidden;
        }

        public EntityForbidden(IdentifiableEntity entity, DirectedGraph<IdentifiableEntity> graph)
        {
            this.Entity = (Entity)entity;
            this.Forbidden = new Forbidden(graph, entity);
        }
    }

    public partial class Table
    {
        ResetLazy<InsertCacheIdentity> inserterIdentity;
        ResetLazy<InsertCacheDisableIdentity> inserterDisableIdentity;

        internal void InsertMany(List<IdentifiableEntity> list, DirectedGraph<IdentifiableEntity> backEdges)
        {
            using (HeavyProfiler.LogNoStackTrace("InsertMany", () => this.Type.TypeName()))
            {
                if (Identity)
                {
                    InsertCacheIdentity ic = inserterIdentity.Value;
                    list.SplitStatements(ls => ic.GetInserter(ls.Count)(ls, backEdges));
                }
                else
                {
                    InsertCacheDisableIdentity ic = inserterDisableIdentity.Value;
                    list.SplitStatements(ls => ic.GetInserter(ls.Count)(ls, backEdges));
                }
            }
        }

        internal object[] BulkInsertDataRow(IdentifiableEntity ident)
        {
            var parameters = Identity ?
                inserterIdentity.Value.InsertParameters(ident, new Forbidden(), "") :
                inserterDisableIdentity.Value.InsertParameters(ident, new Forbidden(), "");

            return parameters.Select(a => a.Value).ToArray();
        }

        class InsertCacheDisableIdentity
        {
            internal Table table;

            public Func<string, string> SqlInsertPattern;
            public Func<IdentifiableEntity, Forbidden, string, List<DbParameter>> InsertParameters;

            ConcurrentDictionary<int, Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>>> insertDisableIdentityCache = 
                new ConcurrentDictionary<int, Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>>>();

           
            internal Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>> GetInserter(int numElements)
            {
                return insertDisableIdentityCache.GetOrAdd(numElements, (int num) => num == 1 ? GetInsertDisableIdentity() : GetInsertMultiDisableIdentity(num));
            }

       
            Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>> GetInsertDisableIdentity()
            {
                string sqlSingle = SqlInsertPattern("");

                return (list, graph) =>
                {
                    IdentifiableEntity ident = list.Single();

                    AssertHasId(ident);

                    Entity entity = ident as Entity;
                    if (entity != null)
                        entity.Ticks = TimeZoneManager.Now.Ticks;

                    table.SetToStrField(ident);

                    var forbidden = new Forbidden(graph, ident);

                    new SqlPreCommandSimple(sqlSingle, InsertParameters(ident, forbidden, "")).ExecuteNonQuery();

                    ident.IsNew = false;
                    if (table.saveCollections.Value != null)
                        table.saveCollections.Value.InsertCollections(new List<EntityForbidden> { new EntityForbidden(ident, forbidden) });
                };
            }



            Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>> GetInsertMultiDisableIdentity(int num)
            {
                string sqlMulti = Enumerable.Range(0, num).ToString(i => SqlInsertPattern(i.ToString()), ";\r\n");

                return (idents, graph) =>
                {
                    for (int i = 0; i < num; i++)
                    {
                        var ident = idents[i];
                        AssertHasId(ident);

                        Entity entity = ident as Entity;
                        if (entity != null)
                            entity.Ticks = TimeZoneManager.Now.Ticks;

                        table.SetToStrField(ident);
                    }

                    List<DbParameter> result = new List<DbParameter>();
                    for (int i = 0; i < idents.Count; i++)
                        result.AddRange(InsertParameters(idents[i], new Forbidden(graph, idents[i]), i.ToString()));

                    new SqlPreCommandSimple(sqlMulti, result).ExecuteNonQuery();
                    for (int i = 0; i < num; i++)
                    {
                        IdentifiableEntity ident = idents[i];

                        ident.IsNew = false;
                    }

                    if (table.saveCollections.Value != null)
                        table.saveCollections.Value.InsertCollections(idents.Select(e => new EntityForbidden(e, graph)).ToList());
                };
            }

            internal static InsertCacheDisableIdentity InitializeInsertDisableIdentity(Table table)
            {
                using (HeavyProfiler.LogNoStackTrace("InitializeInsertDisableIdentity", () => table.Type.TypeName()))
                {
                    InsertCacheDisableIdentity result = new InsertCacheDisableIdentity { table = table };

                    var trios = new List<Table.Trio>();
                    var assigments = new List<Expression>();
                    var paramIdent = Expression.Parameter(typeof(IdentifiableEntity), "ident");
                    var paramForbidden = Expression.Parameter(typeof(Forbidden), "forbidden");
                    var paramSuffix = Expression.Parameter(typeof(string), "suffix");

                    var cast = Expression.Parameter(table.Type, "casted");
                    assigments.Add(Expression.Assign(cast, Expression.Convert(paramIdent, table.Type)));

                    foreach (var item in table.Fields.Values)
                        item.Field.CreateParameter(trios, assigments, Expression.Field(cast, item.FieldInfo), paramForbidden, paramSuffix);
                    
                    if(table.Mixins != null)
                        foreach (var item in table.Mixins.Values)
                            item.CreateParameter(trios, assigments, cast, paramForbidden, paramSuffix);

                    result.SqlInsertPattern = (suffix) =>
                        "INSERT {0} ({1})\r\n VALUES ({2})".Formato(table.Name,
                        trios.ToString(p => p.SourceColumn.SqlEscape(), ", "),
                        trios.ToString(p => p.ParameterName + suffix, ", "));

                    var expr = Expression.Lambda<Func<IdentifiableEntity, Forbidden, string, List<DbParameter>>>(
                        CreateBlock(trios.Select(a => a.ParameterBuilder), assigments), paramIdent, paramForbidden, paramSuffix);

                    result.InsertParameters = expr.Compile();

                    return result;
                }
            }
        }

        class InsertCacheIdentity
        {
            internal Table table;

            public Func<string, bool, string> SqlInsertPattern;
            public Func<IdentifiableEntity, Forbidden, string, List<DbParameter>> InsertParameters;

            ConcurrentDictionary<int, Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>>> insertIdentityCache =
               new ConcurrentDictionary<int, Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>>>();

            internal Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>> GetInserter(int numElements)
            {
                return insertIdentityCache.GetOrAdd(numElements, (int num) => num == 1 ? GetInsertIdentity() : GetInsertMultiIdentity(num));
            }

            Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>> GetInsertIdentity()
            {
                string sqlSingle = SqlInsertPattern("", false) + ";SELECT CONVERT(Int,@@Identity) AS [newID]";

                return (list, graph) =>
                {
                    IdentifiableEntity ident = list.Single();

                    AssertNoId(ident);

                    Entity entity = ident as Entity;
                    if (entity != null)
                        entity.Ticks = TimeZoneManager.Now.Ticks;

                    table.SetToStrField(ident);

                    var forbidden = new Forbidden(graph, ident);

                    ident.id = (int)new SqlPreCommandSimple(sqlSingle, InsertParameters(ident, forbidden, "")).ExecuteScalar();

                    ident.IsNew = false;

                    if (table.saveCollections.Value != null)
                        table.saveCollections.Value.InsertCollections(new List<EntityForbidden> { new EntityForbidden(ident, forbidden) });
                };
            }


            Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>> GetInsertMultiIdentity(int num)
            {
                string sqlMulti = new StringBuilder()
                    .AppendLine("DECLARE @MyTable TABLE (Id INT);")
                    .AppendLines(Enumerable.Range(0, num).Select(i => SqlInsertPattern(i.ToString(), true)))
                    .AppendLine("SELECT Id from @MyTable").ToString();

                return (idents, graph) =>
                {
                    for (int i = 0; i < num; i++)
                    {
                        var ident = idents[i];
                        AssertNoId(ident);

                        Entity entity = ident as Entity;
                        if (entity != null)
                            entity.Ticks = TimeZoneManager.Now.Ticks;

                        table.SetToStrField(ident);
                    }

                    List<DbParameter> result = new List<DbParameter>();
                    for (int i = 0; i < idents.Count; i++)
                        result.AddRange(InsertParameters(idents[i], new Forbidden(graph, idents[i]), i.ToString()));

                    DataTable dt = new SqlPreCommandSimple(sqlMulti, result).ExecuteDataTable();

                    for (int i = 0; i < num; i++)
                    {
                        IdentifiableEntity ident = idents[i];

                        ident.id = (int)dt.Rows[i][0];
                        ident.IsNew = false;
                    }

                    if (table.saveCollections.Value != null)
                        table.saveCollections.Value.InsertCollections(idents.Select(e => new EntityForbidden(e, graph)).ToList());
                };

            }

            internal static InsertCacheIdentity InitializeInsertIdentity(Table table)
            {
                using (HeavyProfiler.LogNoStackTrace("InitializeInsertIdentity", () => table.Type.TypeName()))
                {
                    InsertCacheIdentity result = new InsertCacheIdentity { table = table };

                    var trios = new List<Table.Trio>();
                    var assigments = new List<Expression>();
                    var paramIdent = Expression.Parameter(typeof(IdentifiableEntity), "ident");
                    var paramForbidden = Expression.Parameter(typeof(Forbidden), "forbidden");
                    var paramSuffix = Expression.Parameter(typeof(string), "suffix");

                    var cast = Expression.Parameter(table.Type, "casted");
                    assigments.Add(Expression.Assign(cast, Expression.Convert(paramIdent, table.Type)));

                    foreach (var item in table.Fields.Values.Where(a => !(a.Field is FieldPrimaryKey)))
                        item.Field.CreateParameter(trios, assigments, Expression.Field(cast, item.FieldInfo), paramForbidden, paramSuffix);

                    if (table.Mixins != null)
                        foreach (var item in table.Mixins.Values)
                            item.CreateParameter(trios, assigments, cast, paramForbidden, paramSuffix);

                    result.SqlInsertPattern = (suffix, output) =>
                        "INSERT {0} ({1})\r\n{2} VALUES ({3})".Formato(table.Name,
                        trios.ToString(p => p.SourceColumn.SqlEscape(), ", "),
                        output ? "OUTPUT INSERTED.Id into @MyTable \r\n" : null,
                        trios.ToString(p => p.ParameterName + suffix, ", "));


                    var expr = Expression.Lambda<Func<IdentifiableEntity, Forbidden, string, List<DbParameter>>>(
                        CreateBlock(trios.Select(a => a.ParameterBuilder), assigments), paramIdent, paramForbidden, paramSuffix);

                    result.InsertParameters = expr.Compile();

                    return result;
                }
            }
        }


        static void AssertHasId(IdentifiableEntity ident)
        {
            if (ident.IdOrNull == null)
                throw new InvalidOperationException("{0} should have an Id, since the table has no Identity".Formato(ident, ident.IdOrNull));
        }

        static void AssertNoId(IdentifiableEntity ident)
        {
            if (ident.IdOrNull != null)
                throw new InvalidOperationException("{0} is new, but has Id {1}".Formato(ident, ident.IdOrNull));
        }


        public IColumn ToStrColumn
        {
            get
            {
                EntityField entity;

                if (Fields.TryGetValue("toStr", out entity))
                    return (IColumn)entity.Field;

                return null;
            }
        }

        internal bool SetToStrField(IdentifiableEntity entity)
        {
            var toStrColumn = ToStrColumn;
            if (toStrColumn != null)
            {
                string newStr;
                using (CultureInfoUtils.ChangeCultureUI(Schema.Current.ForceCultureInfo))
                    newStr = entity.ToString();

                if (newStr.HasText() && toStrColumn.Size.HasValue && newStr.Length > toStrColumn.Size)
                    newStr = newStr.Substring(0, toStrColumn.Size.Value);

                if (entity.toStr != newStr)
                {
                    entity.toStr = newStr;
                    return true;
                }

            }

            return false;
        }


        internal static FieldInfo fiId = ReflectionTools.GetFieldInfo((IdentifiableEntity i) => i.id);

        internal void UpdateMany(List<IdentifiableEntity> list, DirectedGraph<IdentifiableEntity> backEdges)
        {
            using (HeavyProfiler.LogNoStackTrace("UpdateMany", () => this.Type.TypeName()))
            {
                var uc = updater.Value;
                list.SplitStatements(ls => uc.GetUpdater(ls.Count)(ls, backEdges));
            }
        }

        class UpdateCache
        {
            internal Table table; 

            public Func<string, bool, string> SqlUpdatePattern;
            public Func<IdentifiableEntity, long, Forbidden, string, List<DbParameter>> UpdateParameters;

            ConcurrentDictionary<int, Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>>> updateCache = 
                new ConcurrentDictionary<int, Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>>>();


            public Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>> GetUpdater(int numElements)
            {
                return updateCache.GetOrAdd(numElements, num => num == 1 ? GenerateUpdate() : GetUpdateMultiple(num)); 
            }

            Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>> GenerateUpdate()
            {
                string sqlUpdate = SqlUpdatePattern("", false);

                if (typeof(Entity).IsAssignableFrom(table.Type))
                {
                    return (uniList, graph) =>
                    {
                        IdentifiableEntity ident = uniList.Single();
                        Entity entity = (Entity)ident;

                        long oldTicks = entity.Ticks;
                        entity.Ticks = TimeZoneManager.Now.Ticks;

                        table.SetToStrField(ident);

                        var forbidden = new Forbidden(graph, ident);

                        int num = (int)new SqlPreCommandSimple(sqlUpdate, UpdateParameters(ident, oldTicks, forbidden, "")).ExecuteNonQuery();
                        if (num != 1)
                            throw new ConcurrencyException(ident.GetType(), ident.Id);

                       if (table.saveCollections.Value != null)
                            table.saveCollections.Value.UpdateCollections(new List<EntityForbidden> { new EntityForbidden(ident, forbidden) });
                    };
                }
                else
                {
                    return (uniList, graph) =>
                    {
                        IdentifiableEntity ident = uniList.Single();

                        table.SetToStrField(ident);

                        var forbidden = new Forbidden(graph, ident);

                        int num = (int)new SqlPreCommandSimple(sqlUpdate, UpdateParameters(ident, -1, forbidden, "")).ExecuteNonQuery();
                        if (num != 1)
                            throw new EntityNotFoundException(ident.GetType(), ident.Id);
                    };
                }
            }

            Action<List<IdentifiableEntity>, DirectedGraph<IdentifiableEntity>> GetUpdateMultiple(int num)
            {
                string sqlMulti = new StringBuilder()
                      .AppendLine("DECLARE @NotFound TABLE (Id INT);")
                      .AppendLines(Enumerable.Range(0, num).Select(i => SqlUpdatePattern(i.ToString(), true)))
                      .AppendLine("SELECT Id from @NotFound").ToString();

                if (typeof(Entity).IsAssignableFrom(table.Type))
                {
                    return (idents, graph) =>
                    {
                        List<DbParameter> parameters = new List<DbParameter>();
                        for (int i = 0; i < num; i++)
                        {
                            Entity entity = (Entity)idents[i];

                            long oldTicks = entity.Ticks;
                            entity.Ticks = TimeZoneManager.Now.Ticks;

                            parameters.AddRange(UpdateParameters(entity, oldTicks, new Forbidden(graph, entity), i.ToString()));
                        }

                        DataTable dt = new SqlPreCommandSimple(sqlMulti, parameters).ExecuteDataTable();

                        if (dt.Rows.Count > 0)
                            throw new ConcurrencyException(table.Type, dt.Rows.Cast<DataRow>().Select(r => (int)r[0]).ToArray());

                        if (table.saveCollections.Value != null)
                            table.saveCollections.Value.UpdateCollections(idents.Select(e => new EntityForbidden(e, new Forbidden(graph, e))).ToList());
                    };
                }
                else
                {
                    return (idents, graph) =>
                    {
                        List<DbParameter> parameters = new List<DbParameter>();
                        for (int i = 0; i < num; i++)
                        {
                            var ident = idents[i];
                            parameters.AddRange(UpdateParameters(ident, -1, new Forbidden(graph, ident), i.ToString()));
                        }

                        DataTable dt = new SqlPreCommandSimple(sqlMulti, parameters).ExecuteDataTable();

                        if (dt.Rows.Count > 0)
                            throw new EntityNotFoundException(table.Type, dt.Rows.Cast<DataRow>().Select(r => (int)r[0]).ToArray());

                        for (int i = 0; i < num; i++)
                        {
                            IdentifiableEntity ident = idents[i];
                        }

                        if (table.saveCollections.Value != null)
                            table.saveCollections.Value.UpdateCollections(idents.Select(e => new EntityForbidden(e, new Forbidden(graph, e))).ToList());
                    };
                }
            }

            internal static UpdateCache InitializeUpdate(Table table)
            {
                using (HeavyProfiler.LogNoStackTrace("InitializeUpdate", () => table.Type.TypeName()))
                {
                    UpdateCache result = new UpdateCache { table = table };

                    var trios = new List<Trio>();
                    var assigments = new List<Expression>();
                    var paramIdent = Expression.Parameter(typeof(IdentifiableEntity), "ident");
                    var paramForbidden = Expression.Parameter(typeof(Forbidden), "forbidden");
                    var paramOldTicks = Expression.Parameter(typeof(long), "oldTicks");
                    var paramSuffix = Expression.Parameter(typeof(string), "suffix");

                    var cast = Expression.Parameter(table.Type);
                    assigments.Add(Expression.Assign(cast, Expression.Convert(paramIdent, table.Type)));

                    foreach (var item in table.Fields.Values.Where(a => !(a.Field is FieldPrimaryKey)))
                        item.Field.CreateParameter(trios, assigments, Expression.Field(cast, item.FieldInfo), paramForbidden, paramSuffix);

                    if (table.Mixins != null)
                        foreach (var item in table.Mixins.Values)
                            item.CreateParameter(trios, assigments, cast, paramForbidden, paramSuffix);

                    var pb = Connector.Current.ParameterBuilder;

                    string idParamName = ParameterBuilder.GetParameterName("id");

                    string oldTicksParamName = ParameterBuilder.GetParameterName("old_ticks");

                    result.SqlUpdatePattern = (suffix, output) =>
                    {
                        string update = "UPDATE {0} SET \r\n{1}\r\n WHERE id = {2}".Formato(
                            table.Name,
                            trios.ToString(p => "{0} = {1}".Formato(p.SourceColumn.SqlEscape(), p.ParameterName + suffix).Indent(2), ",\r\n"),
                            idParamName + suffix);

                        if (typeof(Entity).IsAssignableFrom(table.Type))
                            update += " AND ticks = {0}".Formato(oldTicksParamName + suffix);

                        if (!output)
                            return update;
                        else
                            return update + "\r\nIF @@ROWCOUNT = 0 INSERT INTO @NotFound (id) VALUES ({0})".Formato(idParamName + suffix);
                    };

                    List<Expression> parameters = trios.Select(a => (Expression)a.ParameterBuilder).ToList();

                    parameters.Add(pb.ParameterFactory(Trio.Concat(idParamName, paramSuffix), SqlBuilder.PrimaryKeyType, null, false, Expression.Field(paramIdent, fiId)));

                    if (typeof(Entity).IsAssignableFrom(table.Type))
                        parameters.Add(pb.ParameterFactory(Trio.Concat(oldTicksParamName, paramSuffix), SqlDbType.BigInt, null, false, paramOldTicks));

                    var expr = Expression.Lambda<Func<IdentifiableEntity, long, Forbidden, string, List<DbParameter>>>(
                        CreateBlock(parameters, assigments), paramIdent, paramOldTicks, paramForbidden, paramSuffix);

                    result.UpdateParameters = expr.Compile();

                    return result;
                }
            }

        }

        ResetLazy<UpdateCache> updater;

   
        class CollectionsCache
        {
            public Func<Entity, SqlPreCommand> InsertCollectionsSync;

            public Action<List<EntityForbidden>> InsertCollections;
            public Action<List<EntityForbidden>> UpdateCollections;

            internal static CollectionsCache InitializeCollections(Table table)
            {
                using (HeavyProfiler.LogNoStackTrace("InitializeCollections", () => table.Type.TypeName()))
                {
                    var caches =
                        (from rt in table.TablesMList()
                         select rt.cache.Value).ToList();

                    if (caches.IsEmpty())
                        return null;
                    else
                    {
                        return new CollectionsCache
                        {
                            InsertCollections = (entities) =>
                            {
                                foreach (var rc in caches)
                                    rc.RelationalInserts(entities);
                            },

                            UpdateCollections = (entities) =>
                            {
                                foreach (var rc in caches)
                                    rc.RelationalUpdates(entities);
                            },

                            InsertCollectionsSync = ident =>
                                caches.Select(rc => rc.RelationalUpdateSync(ident)).Combine(Spacing.Double)
                        };
                    }
                }
            }
        }

        ResetLazy<CollectionsCache> saveCollections;


        public SqlPreCommand InsertSqlSync(IdentifiableEntity ident, bool includeCollections = true, string comment = null, string suffix = "")
        {
            PrepareEntitySync(ident);
            SetToStrField(ident);

            SqlPreCommandSimple insert = Identity ?
                new SqlPreCommandSimple(
                    inserterIdentity.Value.SqlInsertPattern(suffix, false),
                    inserterIdentity.Value.InsertParameters(ident, new Forbidden(), suffix)).AddComment(comment) :
                new SqlPreCommandSimple(
                    inserterDisableIdentity.Value.SqlInsertPattern(suffix),
                    inserterDisableIdentity.Value.InsertParameters(ident, new Forbidden(), suffix)).AddComment(comment);

            if (!includeCollections)
                return insert;

            var cc = saveCollections.Value;
            if (cc == null)
                return insert;

            SqlPreCommand collections = cc.InsertCollectionsSync((Entity)ident);

            if (collections == null)
                return insert;

            SqlPreCommand setParent = new SqlPreCommandSimple("SET @idParent = @@Identity");

            return SqlPreCommand.Combine(Spacing.Simple, insert, setParent, collections);
        }

        public SqlPreCommand UpdateSqlSync(IdentifiableEntity ident, bool includeCollections = true, string comment = null)
        {
            PrepareEntitySync(ident);
            
            if (SetToStrField(ident))
                ident.SetSelfModified();

            if (ident.Modified == ModifiedState.Clean || ident.Modified == ModifiedState.Sealed)
                return null;

            var uc = updater.Value;
            SqlPreCommandSimple update = new SqlPreCommandSimple(uc.SqlUpdatePattern("", false),
                uc.UpdateParameters(ident, (ident as Entity).Try(a => a.Ticks) ?? -1, new Forbidden(), "")).AddComment(comment);

            if (!includeCollections)
                return update;

            var cc = saveCollections.Value;
            if (cc == null)
                return update;

            SqlPreCommand collections = cc.InsertCollectionsSync((Entity)ident);

            return SqlPreCommand.Combine(Spacing.Simple, update, collections);
        }

        void PrepareEntitySync(IdentifiableEntity entity)
        {
            DirectedGraph<Modifiable> modifiables = GraphExplorer.PreSaving(() => GraphExplorer.FromRoot(entity), (Modifiable m, ref bool graphModified) =>
            {
                m.PreSaving(ref graphModified);

                IdentifiableEntity ident = m as IdentifiableEntity;

                if (ident != null)
                    Schema.Current.OnPreSaving(ident, ref graphModified);
            });

            string error = GraphExplorer.FullIntegrityCheck(modifiables, withIndependentEmbeddedEntities: false);
            if (error.HasText())
                throw new ApplicationException(error);

            GraphExplorer.PropagateModifications(modifiables.Inverse());
        }

        public class Trio
        {
            public Trio(IColumn column, Expression value, Expression suffix)
            {
                this.SourceColumn = column.Name;
                this.ParameterName = Engine.ParameterBuilder.GetParameterName(column.Name);
                this.ParameterBuilder = Connector.Current.ParameterBuilder.ParameterFactory(Concat(this.ParameterName, suffix), column.SqlDbType, column.UdtTypeName, column.Nullable, value);
            }

            public string SourceColumn;
            public string ParameterName;
            public MemberInitExpression ParameterBuilder; //Expression<DbParameter>

            public override string ToString()
            {
                return "{0} {1} {2}".Formato(SourceColumn, ParameterName, ParameterBuilder.NiceToString());
            }

            static MethodInfo miConcat = ReflectionTools.GetMethodInfo(() => string.Concat("", ""));

            internal static Expression Concat(string baseName, Expression suffix)
            {
                return Expression.Call(null, miConcat, Expression.Constant(baseName), suffix);
            }
        }

        static ConstructorInfo ciNewList = ReflectionTools.GetConstuctorInfo(() => new List<DbParameter>(1));

        public static Expression CreateBlock(IEnumerable<Expression> parameters, IEnumerable<Expression> assigments)
        {
            return Expression.Block(assigments.OfType<BinaryExpression>().Select(a => (ParameterExpression)a.Left),
                assigments.And(
                Expression.ListInit(Expression.New(ciNewList, Expression.Constant(parameters.Count())),
                parameters)));
        }
    }


    public partial class TableMList
    {
        internal interface IMListCache
        {
            SqlPreCommand RelationalUpdateSync(Entity parent);
            void RelationalInserts(List<EntityForbidden> entities);
            void RelationalUpdates(List<EntityForbidden> entities);

            object[] BulkInsertDataRow(Entity entity, object value, int order);
        }

        internal class TableMListCache<T> : IMListCache
        {
            internal Func<string, string> sqlDelete;
            public Func<Entity, string, DbParameter> DeleteParameter;
            public ConcurrentDictionary<int, Action<List<Entity>>> deleteCache = new ConcurrentDictionary<int, Action<List<Entity>>>();

            Action<List<Entity>> GetDelete(int numEntities)
            {
                return deleteCache.GetOrAdd(numEntities, num =>
                {
                    string sql = Enumerable.Range(0, num).ToString(i => sqlDelete(i.ToString()), ";\r\n");

                    return list =>
                    {
                        List<DbParameter> parameters = new List<DbParameter>();
                        for (int i = 0; i < num; i++)
                        {
                            parameters.Add(DeleteParameter(list[i], i.ToString()));
                        }
                        new SqlPreCommandSimple(sql, parameters).ExecuteNonQuery();
                    };
                }); 
            }

            internal Func<int, string> sqlDeleteExcept;
            public Func<MListDelete, List<DbParameter>> DeleteExceptParameter;
            public ConcurrentDictionary<int, Action<MListDelete>> deleteExceptCache = new ConcurrentDictionary<int, Action<MListDelete>>();

            Action<MListDelete> GetDeleteExcept(int numExceptions)
            {
                return deleteExceptCache.GetOrAdd(numExceptions, num =>
                {
                    string sql = sqlDeleteExcept(numExceptions); Enumerable.Range(0, num).ToString(i => sqlDelete(i.ToString()), ";\r\n");

                    return delete =>
                    {
                        new SqlPreCommandSimple(sql,  DeleteExceptParameter(delete)).ExecuteNonQuery();
                    };
                });
            }

            public struct MListDelete
            {
                public readonly Entity Entity;
                public readonly int[] ExceptRowIds;

                public MListDelete(Entity ident, int[] exceptRowIds)
                {
                    this.Entity = ident;
                    this.ExceptRowIds = exceptRowIds;
                }
            }

            internal bool hasOrder = false;
            internal bool isEmbeddedEntity = false;
            internal Func<string, string> sqlUpdate;
            public Func<Entity, int, T, int, Forbidden, string, List<DbParameter>> UpdateParameters;
            public ConcurrentDictionary<int, Action<List<MListUpdate>>> updateCache =
                new ConcurrentDictionary<int, Action<List<MListUpdate>>>();

            Action<List<MListUpdate>> GetUpdate(int numElements)
            {
                return updateCache.GetOrAdd(numElements, num =>
                {
                    string sql = Enumerable.Range(0, num).ToString(i => sqlUpdate(i.ToString()), ";\r\n");

                    return (List<MListUpdate> list) =>
                    {
                        List<DbParameter> parameters = new List<DbParameter>();
                        for (int i = 0; i < num; i++)
                        {
                            var pair = list[i];

                            var row = pair.MList.InnerList[pair.Index]; 

                            parameters.AddRange(UpdateParameters(pair.Entity, row.RowId.Value, row.Value, pair.Index, pair.Forbidden, i.ToString()));
                        }
                        new SqlPreCommandSimple(sql, parameters).ExecuteNonQuery();
                    };
                });
            }

            public struct MListUpdate
            {
                public readonly Entity Entity;
                public readonly Forbidden Forbidden;
                public readonly IMListPrivate<T> MList;
                public readonly int Index;

                public MListUpdate(EntityForbidden ef, MList<T> mlist, int index)
                {
                    this.Entity = ef.Entity;
                    this.Forbidden = ef.Forbidden;
                    this.MList = mlist;
                    this.Index = index;
                }
            }

            internal Func<string, bool, string> sqlInsert;
            public Func<Entity, T, int, Forbidden, string, List<DbParameter>> InsertParameters;
            public ConcurrentDictionary<int, Action<List<MListInsert>>> insertCache =
                new ConcurrentDictionary<int, Action<List<MListInsert>>>();

            Action<List<MListInsert>> GetInsert(int numElements)
            {
                return insertCache.GetOrAdd(numElements, num =>
                {
                    if (num == 1)
                        return (List<MListInsert> list) =>
                        {
                            var pair = list[0];

                            string sql = sqlInsert("", false) + ";SELECT CONVERT(Int,@@Identity) AS [newID]";

                            var parameters = InsertParameters(pair.Entity, pair.MList.InnerList[pair.Index].Value, pair.Index, pair.Forbidden, "");

                            pair.MList.SetRowId(pair.Index, (int)new SqlPreCommandSimple(sql, parameters).ExecuteScalar());
                            if (this.hasOrder)
                                pair.MList.SetOldIndex(pair.Index);
                        };
                    else
                    {
                        string sqlMulti = new StringBuilder()
                              .AppendLine("DECLARE @MyTable TABLE (Id INT);")
                              .AppendLines(Enumerable.Range(0, num).Select(i => sqlInsert(i.ToString(), true)))
                              .AppendLine("SELECT Id from @MyTable").ToString();

                        return (List<MListInsert> list) =>
                        {
                            List<DbParameter> result = new List<DbParameter>();
                            for (int i = 0; i < num; i++)
                            {
                                var pair = list[i];
                                result.AddRange(InsertParameters(pair.Entity, pair.MList.InnerList[pair.Index].Value, pair.Index, pair.Forbidden, i.ToString()));
                            }

                            DataTable dt = new SqlPreCommandSimple(sqlMulti, result).ExecuteDataTable();

                            for (int i = 0; i < num; i++)
                            {
                                var pair = list[i];

                                pair.MList.SetRowId(pair.Index,(int)dt.Rows[i][0]);

                                if (this.hasOrder)
                                    pair.MList.SetOldIndex(pair.Index);
                            }
                        };
                    }
                });
            }

            public struct MListInsert
            {
                public readonly Entity Entity;
                public readonly Forbidden Forbidden; 
                public readonly IMListPrivate<T> MList;
                public readonly int Index;

                public MListInsert(EntityForbidden ef, MList<T> mlist, int index)
                {
                    this.Entity = ef.Entity;
                    this.Forbidden = ef.Forbidden;
                    this.MList = mlist;
                    this.Index = index;
                }
            }

            public object[] BulkInsertDataRow(Entity entity, object value, int order)
            {
                return InsertParameters(entity, (T)value, order, new Forbidden(null), "").Select(a => a.Value).ToArray(); 
            }

            public Func<Entity, MList<T>> Getter;

            public void RelationalInserts(List<EntityForbidden> entities)
            {
                List<MListInsert> toInsert = new List<MListInsert>();

                foreach (var ef in entities)
                {
                    if (!ef.Forbidden.IsEmpty)
                        continue; //Will be called again

                    MList<T> collection = Getter(ef.Entity);

                    if (collection == null)
                        continue;

                    if (collection.Modified == ModifiedState.Clean)
                        continue;

                    for (int i = 0; i < collection.Count; i++)
                    {
                        toInsert.Add(new MListInsert(ef, collection, i));
                    }
                }

                toInsert.SplitStatements(list => GetInsert(list.Count)(list));
            }

            public void RelationalUpdates(List<EntityForbidden> idents)
            {
                List<Entity> toDelete = new List<Entity>();
                List<MListDelete> toDeleteExcept = new List<MListDelete>();
                List<MListInsert> toInsert = new List<MListInsert>();
                List<MListUpdate> toUpdate = new List<MListUpdate>();

                foreach (var ef in idents)
                {
                    if (!ef.Forbidden.IsEmpty)
                        continue; //Will be called again

                    MList<T> collection = Getter(ef.Entity);

                    if (collection == null)
                        toDelete.Add(ef.Entity);
                    else
                    {
                        if (collection.Modified == ModifiedState.Clean)
                            continue;

                        var innerList = ((IMListPrivate<T>)collection).InnerList;

                        var exceptions = innerList.Select(a => a.RowId).NotNull().ToArray();

                        if (exceptions.IsEmpty())
                            toDelete.Add(ef.Entity);
                        else
                            toDeleteExcept.Add(new MListDelete(ef.Entity, exceptions));

                        if (isEmbeddedEntity || hasOrder)
                        {
                            for (int i = 0; i < innerList.Count; i++)
                            {
                                var row = innerList[i];

                                if(row.RowId.HasValue)
                                {
                                    if(hasOrder  && row.OldIndex != i ||
                                       isEmbeddedEntity && ((ModifiableEntity)(object)row.Value).IsGraphModified)
                                    {
                                        toUpdate.Add(new MListUpdate(ef, collection, i));
                                    }
                                }
                            }
                        }

                        for (int i = 0; i < innerList.Count; i++)
                        {
                            if (innerList[i].RowId == null)
                                toInsert.Add(new MListInsert(ef, collection, i));
                        }
                    }
                }

                toDelete.SplitStatements(list => GetDelete(list.Count)(list));

                toDeleteExcept.ForEach(e => GetDeleteExcept(e.ExceptRowIds.Length)(e)); 
                toUpdate.SplitStatements(listPairs => GetUpdate(listPairs.Count)(listPairs));
                toInsert.SplitStatements(listPairs => GetInsert(listPairs.Count)(listPairs));
            }

            public SqlPreCommand RelationalUpdateSync(Entity parent)
            {
                MList<T> collection = Getter(parent);

                if (collection == null)
                {
                    if (parent.IsNew)
                        return null;

                    return new SqlPreCommandSimple(sqlDelete(""), new List<DbParameter> { DeleteParameter(parent, "") });
                }

                if (collection.Modified == ModifiedState.Clean)
                    return null;

                var sqlIns = sqlInsert("", false);

                if (parent.IsNew)
                {
                    return collection.Select((e, i) =>
                    {
                        var parameters = InsertParameters(parent, e, i, new Forbidden(new HashSet<IdentifiableEntity> { parent }), "");
                        parameters.RemoveAt(0); // wont be replaced, generating @idParent
                        return new SqlPreCommandSimple(sqlIns, parameters).AddComment(e.ToString());
                    }).Combine(Spacing.Simple);
                }
                else
                {
                    return SqlPreCommand.Combine(Spacing.Simple,
                        new SqlPreCommandSimple(sqlDelete(""), new List<DbParameter> { DeleteParameter(parent, "") }),
                        collection.Select((e, i) => new SqlPreCommandSimple(sqlIns, InsertParameters(parent, e, i, new Forbidden(), "")).AddComment(e.ToString())).Combine(Spacing.Simple));
                }
            }




           
        }

        static GenericInvoker<Func<TableMList, IMListCache>> giCreateCache =
            new GenericInvoker<Func<TableMList, IMListCache>>((TableMList rt) => rt.CreateCache<int>());


        internal Lazy<IMListCache> cache;

        TableMListCache<T> CreateCache<T>()
        {
            var pb = Connector.Current.ParameterBuilder;

            TableMListCache<T> result = new TableMListCache<T>();

            result.Getter = ident => (MList<T>)FullGetter(ident);

            result.sqlDelete = suffix => "DELETE {0} WHERE {1} = {2}".Formato(Name, BackReference.Name.SqlEscape(), ParameterBuilder.GetParameterName(BackReference.Name + suffix));
            result.DeleteParameter = (ident, suffix) => pb.CreateReferenceParameter(ParameterBuilder.GetParameterName(BackReference.Name + suffix), false, ident.Id);

            result.sqlDeleteExcept = num =>
            {
                var sql = "DELETE {0} WHERE {1} = {2}"
                    .Formato(Name, BackReference.Name.SqlEscape(), ParameterBuilder.GetParameterName(BackReference.Name));

                sql += " AND {0} NOT IN ({1})"
                    .Formato(PrimaryKey.Name.SqlEscape(), 0.To(num).Select(i => ParameterBuilder.GetParameterName("e" + i)).ToString(", "));

                return sql;
            };

            result.DeleteExceptParameter = delete =>
            {
                var list = new List<DbParameter>
                { 
                    pb.CreateReferenceParameter(ParameterBuilder.GetParameterName(BackReference.Name), false, delete.Entity.Id)
                };

                list.AddRange(delete.ExceptRowIds.Select((e, i) => pb.CreateReferenceParameter(ParameterBuilder.GetParameterName("e" + i), false, e)));

                return list;
            };

            var paramIdent = Expression.Parameter(typeof(IdentifiableEntity), "ident");
            var paramItem = Expression.Parameter(typeof(T), "item");
            var paramOrder = Expression.Parameter(typeof(int), "order");
            var paramForbidden = Expression.Parameter(typeof(Forbidden), "forbidden");
            var paramSuffix = Expression.Parameter(typeof(string), "suffix");
            
            
            {
                var trios = new List<Table.Trio>();
                var assigments = new List<Expression>();

                BackReference.CreateParameter(trios, assigments, paramIdent, paramForbidden, paramSuffix);
                if (this.Order != null)
                    Order.CreateParameter(trios, assigments, paramOrder, paramForbidden, paramSuffix);
                Field.CreateParameter(trios, assigments, paramItem, paramForbidden, paramSuffix);

                result.sqlInsert = (suffix, output) => "INSERT {0} ({1})\r\n{2} VALUES ({3})".Formato(Name,
                    trios.ToString(p => p.SourceColumn.SqlEscape(), ", "),
                    output ? "OUTPUT INSERTED.Id into @MyTable \r\n" : null,
                    trios.ToString(p => p.ParameterName + suffix, ", "));

                var expr = Expression.Lambda<Func<IdentifiableEntity, T, int, Forbidden, string, List<DbParameter>>>(
                    Table.CreateBlock(trios.Select(a => a.ParameterBuilder), assigments), paramIdent, paramItem, paramOrder, paramForbidden, paramSuffix);

                result.InsertParameters = expr.Compile();
            }

            result.hasOrder = this.Order != null; 
            result.isEmbeddedEntity = typeof(EmbeddedEntity).IsAssignableFrom(this.Field.FieldType);

            if (result.isEmbeddedEntity || result.hasOrder)
            {
                var trios = new List<Table.Trio>();
                var assigments = new List<Expression>();

                var paramRowId = Expression.Parameter(typeof(int), "rowId");

                string idParent = "idParent";
                string rowId = "rowId";

                //BackReference.CreateParameter(trios, assigments, paramIdent, paramForbidden, paramSuffix);
                if (this.Order != null)
                    Order.CreateParameter(trios, assigments, paramOrder, paramForbidden, paramSuffix);
                Field.CreateParameter(trios, assigments, paramItem, paramForbidden, paramSuffix);

                result.sqlUpdate = suffix => "UPDATE {0} SET \r\n{1}\r\n WHERE {2} = {3} AND {4} = {5}".Formato(Name,
                    trios.ToString(p => "{0} = {1}".Formato(p.SourceColumn.SqlEscape(), p.ParameterName + suffix).Indent(2), ",\r\n"),
                    this.BackReference.Name.SqlEscape(), ParameterBuilder.GetParameterName(idParent + suffix),
                    this.PrimaryKey.Name.SqlEscape(), ParameterBuilder.GetParameterName(rowId + suffix));

                var parameters = trios.Select(a => a.ParameterBuilder).ToList();

                parameters.Add(pb.ParameterFactory(Table.Trio.Concat(idParent, paramSuffix), SqlBuilder.PrimaryKeyType, null, false, Expression.Field(paramIdent, Table.fiId)));
                parameters.Add(pb.ParameterFactory(Table.Trio.Concat(rowId, paramSuffix), SqlBuilder.PrimaryKeyType, null, false, paramRowId));

                var expr = Expression.Lambda<Func<IdentifiableEntity, int, T, int, Forbidden, string, List<DbParameter>>>(
                    Table.CreateBlock(parameters, assigments), paramIdent, paramRowId, paramItem, paramOrder, paramForbidden, paramSuffix);
                result.UpdateParameters = expr.Compile();
            }

            return result;
        }
    }

    internal static class SaveUtils
    {
        public static void SplitStatements<T>(this IList<T> original, Action<List<T>> action)
        {
            if (!Connector.Current.AllowsMultipleQueries)
            {
                List<T> part = new List<T>(1);
                for (int i = 0; i < original.Count; i++)
                {
                    part[0] = original[i];
                    action(part);
                }
            }
            else
            {
                int max = Schema.Current.Settings.MaxNumberOfStatementsInSaveQueries;

                List<T> part = new List<T>(max);
                int i = 0;
                for (; i <= original.Count - max; i += max)
                {
                    Fill(part, original, i, max);
                    action(part);
                }

                int remaining = original.Count - i;
                if (remaining > 0)
                {
                    Fill(part, original, i, remaining);
                    action(part);
                }
            }
        }

        static List<T> Fill<T>(List<T> part, IList<T> original, int pos, int count)
        {
            part.Clear();
            int max = pos + count;
            for (int i = pos; i < max; i++)
                part.Add(original[i]);
            return part;
        }
    }


    public abstract partial class Field
    {
        protected internal virtual void CreateParameter(List<Table.Trio> trios, List<Expression> assigments, Expression value, Expression forbidden, Expression suffix) { }
    }

    public partial class FieldPrimaryKey
    {
        protected internal override void CreateParameter(List<Table.Trio> trios, List<Expression> assigments, Expression value, Expression forbidden, Expression suffix)
        {
            trios.Add(new Table.Trio(this, value, suffix));
        }
    }

    public partial class FieldValue
    {
        protected internal override void CreateParameter(List<Table.Trio> trios, List<Expression> assigments, Expression value, Expression forbidden, Expression suffix)
        {
            trios.Add(new Table.Trio(this, value, suffix));
        }
    }

    public static partial class FieldReferenceExtensions
    {
        static MethodInfo miGetIdForLite = ReflectionTools.GetMethodInfo(() => GetIdForLite(null, new Forbidden()));
        static MethodInfo miGetIdForEntity = ReflectionTools.GetMethodInfo(() => GetIdForEntity(null, new Forbidden()));
        static MethodInfo miGetIdForLiteCleanEntity = ReflectionTools.GetMethodInfo(() => GetIdForLiteCleanEntity(null, new Forbidden()));

        public static void AssertIsLite(this IFieldReference fr)
        {
            if (!fr.IsLite)
                throw new InvalidOperationException("The field is not a lite");
        }

        public static Expression GetIdFactory(this IFieldReference fr, Expression value, Expression forbidden)
        {
            var mi = !fr.IsLite ? miGetIdForEntity :
                fr.ClearEntityOnSaving ? miGetIdForLiteCleanEntity :
                miGetIdForLite;

            return Expression.Call(mi, value, forbidden);
        }

        static int? GetIdForLite(Lite<IIdentifiable> lite, Forbidden forbidden)
        {
            if (lite == null)
                return null;

            if (lite.UntypedEntityOrNull == null)
                return lite.Id;

            if (forbidden.Contains(lite.UntypedEntityOrNull))
                return null;

            lite.RefreshId();

            return lite.Id;
        }

        static int? GetIdForLiteCleanEntity(Lite<IIdentifiable> lite, Forbidden forbidden)
        {
            if (lite == null)
                return null;

            if (lite.UntypedEntityOrNull == null)
                return lite.Id;

            if (forbidden.Contains(lite.UntypedEntityOrNull))
                return null;

            lite.RefreshId();
            lite.ClearEntity();

            return lite.Id;
        }

        static int? GetIdForEntity(IIdentifiable value, Forbidden forbidden)
        {
            if (value == null)
                return null;

            IdentifiableEntity ie = (IdentifiableEntity)value;
            return forbidden.Contains(ie) ? (int?)null : ie.Id;
        }

        static MethodInfo miGetTypeForLite = ReflectionTools.GetMethodInfo(() => GetTypeForLite(null, new Forbidden()));
        static MethodInfo miGetTypeForEntity = ReflectionTools.GetMethodInfo(() => GetTypeForEntity(null, new Forbidden()));

        public static Expression GetTypeFactory(this IFieldReference fr, Expression value, Expression forbidden)
        {
            return Expression.Call(fr.IsLite ? miGetTypeForLite : miGetTypeForEntity, value, forbidden);
        }

        static Type GetTypeForLite(Lite<IIdentifiable> value, Forbidden forbidden)
        {
            if (value == null)
                return null;

            Lite<IIdentifiable> l = (Lite<IIdentifiable>)value;
            return l.UntypedEntityOrNull == null ? l.EntityType :
                 forbidden.Contains(l.UntypedEntityOrNull) ? null :
                 l.EntityType;
        }

        static Type GetTypeForEntity(IIdentifiable value, Forbidden forbidden)
        {
            if (value == null)
                return null;

            IdentifiableEntity ie = (IdentifiableEntity)value;
            return forbidden.Contains(ie) ? null : ie.GetType();
        }
    }

    public partial class FieldReference
    {
        protected internal override void CreateParameter(List<Table.Trio> trios, List<Expression> assigments, Expression value, Expression forbidden, Expression suffix)
        {
            trios.Add(new Table.Trio(this, this.GetIdFactory(value, forbidden), suffix));
        }
    }

    public partial class FieldEnum
    {
        protected internal override void CreateParameter(List<Table.Trio> trios, List<Expression> assigments, Expression value, Expression forbidden, Expression suffix)
        {
            trios.Add(new Table.Trio(this, Expression.Convert(value, Nullable ? typeof(int?) : typeof(int)), suffix));
        }
    }

    public partial class FieldMList
    {
    }

    public partial class FieldEmbedded
    {
        protected internal override void CreateParameter(List<Table.Trio> trios, List<Expression> assigments, Expression value, Expression forbidden, Expression suffix)
        {
            ParameterExpression embedded = Expression.Parameter(this.FieldType, "embedded");

            if (HasValue != null)
            {
                trios.Add(new Table.Trio(HasValue, Expression.NotEqual(value, Expression.Constant(null, FieldType)), suffix));

                assigments.Add(Expression.Assign(embedded, Expression.Convert(value, this.FieldType)));

                foreach (var ef in EmbeddedFields.Values)
                {
                    ef.Field.CreateParameter(trios, assigments,
                        Expression.Condition(
                            Expression.Equal(embedded, Expression.Constant(null, this.FieldType)),
                            Expression.Constant(null, ef.FieldInfo.FieldType.Nullify()),
                            Expression.Field(embedded, ef.FieldInfo).Nullify()), forbidden, suffix);
                }
            }
            else
            {

                assigments.Add(Expression.Assign(embedded, Expression.Convert(value.NodeType == ExpressionType.Conditional ? value : Expression.Call(Expression.Constant(this), miCheckNull, value), this.FieldType)));
                foreach (var ef in EmbeddedFields.Values)
                {
                    ef.Field.CreateParameter(trios, assigments,
                        Expression.Field(embedded, ef.FieldInfo), forbidden, suffix);
                }
            }
        }

        static MethodInfo miCheckNull = ReflectionTools.GetMethodInfo((FieldEmbedded fe) => fe.CheckNull(null));
        object CheckNull(object obj)
        {
            if (obj == null)
                throw new InvalidOperationException("Impossible to save 'null' on the not-nullable embedded field of type '{0}'".Formato(this.FieldType.Name));

            return obj;
        }
    }

    public partial class FieldMixin
    {
        protected internal override void CreateParameter(List<Table.Trio> trios, List<Expression> assigments, Expression value, Expression forbidden, Expression suffix)
        {
            ParameterExpression mixin = Expression.Parameter(this.FieldType, "mixin");

            assigments.Add(Expression.Assign(mixin, Expression.Call(value, MixinDeclarations.miMixin.MakeGenericMethod(this.FieldType))));
            foreach (var ef in Fields.Values)
            {
                ef.Field.CreateParameter(trios, assigments,
                    Expression.Field(mixin, ef.FieldInfo), forbidden, suffix);
            }
        }
    }

    public partial class FieldImplementedBy
    {
        protected internal override void CreateParameter(List<Table.Trio> trios, List<Expression> assigments, Expression value, Expression forbidden, Expression suffix)
        {
            ParameterExpression ibType = Expression.Parameter(typeof(Type), "ibType");
            ParameterExpression ibId = Expression.Parameter(typeof(int?), "ibId");

            assigments.Add(Expression.Assign(ibType, Expression.Call(Expression.Constant(this), miCheckType, this.GetTypeFactory(value, forbidden))));
            assigments.Add(Expression.Assign(ibId, this.GetIdFactory(value, forbidden)));

            var nullId = Expression.Constant(null, typeof(int?));

            foreach (var imp in ImplementationColumns)
            {
                trios.Add(new Table.Trio(imp.Value,
                    Expression.Condition(Expression.Equal(ibType, Expression.Constant(imp.Key)), ibId, Expression.Constant(null, typeof(int?))), suffix
                    ));
            }
        }

        static MethodInfo miCheckType = ReflectionTools.GetMethodInfo((FieldImplementedBy fe) => fe.CheckType(null));

        Type CheckType(Type type)
        {
            if (type != null && !ImplementationColumns.ContainsKey(type))
                throw new InvalidOperationException("Type {0} is not in the list of ImplementedBy:\r\n{1}".Formato(type.Name, ImplementationColumns.ToString(kvp => "{0} -> {1}".Formato(kvp.Key.Name, kvp.Value.Name), "\r\n")));

            return type;
        }
    }

    public partial class ImplementationColumn
    {

    }

    public partial class FieldImplementedByAll
    {
        protected internal override void CreateParameter(List<Table.Trio> trios, List<Expression> assigments, Expression value, Expression forbidden, Expression suffix)
        {
            trios.Add(new Table.Trio(Column, this.GetIdFactory(value, forbidden), suffix));
            trios.Add(new Table.Trio(ColumnType, Expression.Call(Expression.Constant(this), miConvertType, this.GetTypeFactory(value, forbidden)), suffix));
        }

        static MethodInfo miConvertType = ReflectionTools.GetMethodInfo((FieldImplementedByAll fe) => fe.ConvertType(null));

        int? ConvertType(Type type)
        {
            if (type == null)
                return null;

            return TypeLogic.TypeToId.GetOrThrow(type, "{0} not registered in the schema");
        }
    }


}
