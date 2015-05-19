﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Signum.Engine.Maps;
using Signum.Entities;
using Signum.Entities.Reflection;
using Signum.Utilities;
using Signum.Utilities.DataStructures;
using Signum.Utilities.ExpressionTrees;
using Signum.Utilities.Reflection;

namespace Signum.Engine.CodeGeneration
{
    public class EntityCodeGenerator
    {
        public string SolutionName;
        public string SolutionFolder;

        public Dictionary<ObjectName, DiffTable> Tables;
        public DirectedGraph<DiffTable> Graph;

        public Schema CurrentSchema; 

        public virtual void GenerateEntitiesFromDatabaseTables()
        {
            CurrentSchema = Schema.Current;

            var tables = SchemaSynchronizer.DefaultGetDatabaseDescription(Schema.Current.DatabaseNames()).Values.ToList();

            CleanDiffTables(tables);

            this.Tables = tables.ToDictionary(a=>a.Name);

            Graph = DirectedGraph<DiffTable>.Generate(tables, t =>
                t.Columns.Values.Select(a => a.ForeignKey).NotNull().Select(a => a.TargetTable).Distinct().Select(on => this.Tables.GetOrThrow(on)));

            GetSolutionInfo(out SolutionFolder, out SolutionName);

            string projectFolder = GetProjectFolder();

            if (!Directory.Exists(projectFolder))
                throw new InvalidOperationException("{0} not found. Override GetProjectFolder".FormatWith(projectFolder));

            bool? overwriteFiles = null;

            foreach (var gr in tables.GroupBy(t => GetFileName(t)))
            {
                string str = WriteFile(gr.Key, gr);

                if (str != null)
                {
                    string fileName = Path.Combine(projectFolder, gr.Key);

                    FileTools.CreateParentDirectory(fileName);

                    if (!File.Exists(fileName) || SafeConsole.Ask(ref overwriteFiles, "Overwrite {0}?".FormatWith(fileName)))
                    {
                        File.WriteAllText(fileName, str);
                    }
                }
            }
        }

      

        protected virtual string GetProjectFolder()
        {
            return Path.Combine(SolutionFolder, SolutionName + ".Entities");
        }

        protected virtual void CleanDiffTables(List<DiffTable> tables)
        {
            
        }
        
        protected virtual void GetSolutionInfo(out string solutionFolder, out string solutionName)
        {
            CodeGenerator.GetSolutionInfo(out solutionFolder, out solutionName);
        }

        protected virtual string GetFileName(DiffTable t)
        {
            string name = t.Name.ToString().Replace('.', '\\');

            name = Regex.Replace(name, "[" + Regex.Escape(new string(Path.GetInvalidPathChars())) + "]", "");

            return name + ".cs";
        }

        protected virtual string WriteFile(string fileName, IEnumerable<DiffTable> tables)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var item in GetUsingNamespaces(fileName, tables))
                sb.AppendLine("using {0};".FormatWith(item));

            sb.AppendLine();
            sb.AppendLine("namespace " + GetNamespace(fileName));
            sb.AppendLine("{");
            int length = sb.Length;
            foreach (var t in tables.OrderByDescending(a => a.Columns.Count))
            {
                var entity = WriteTableEntity(fileName, t);
                if (entity != null)
                {
                    sb.Append(entity.Indent(4));
                    sb.AppendLine();
                    sb.AppendLine();
                }
            }

            if (sb.Length == length)
                return null;

            sb.AppendLine("}");

            return sb.ToString();
        }

        protected virtual List<string> GetUsingNamespaces(string fileName, IEnumerable<DiffTable> tables)
        {
            var result = new List<string> 
            {
                "System",
                "System.Collections.Generic",
                "System.Data",
                "System.Linq",
                "System.Linq.Expressions",
                "System.Text",
                "System.ComponentModel",
                "Signum.Entities",
                "Signum.Utilities",
            };

            var currentNamespace = GetNamespace(fileName);

            var fkNamespaces = 
                (from t in tables
                 from c in t.Columns.Values
                 where c.ForeignKey != null
                 let targetTable = Tables.GetOrThrow(c.ForeignKey.TargetTable)
                 select GetNamespace(GetFileName(targetTable)));

            var mListNamespaces = 
                (from t in tables
                 from kvp in GetMListFields(t)
                 let tec = kvp.Value.TrivialElementColumn
                 let targetTable = tec != null  && tec.ForeignKey != null ? Tables.GetOrThrow(tec.ForeignKey.TargetTable) : kvp.Key
                 select GetNamespace(GetFileName(targetTable)));

            result.AddRange(fkNamespaces.Concat(mListNamespaces).Where(ns => ns != currentNamespace).Distinct());

            return result;
        }

        protected virtual string GetNamespace(string fileName)
        {
            var result = SolutionName + ".Entities";

            string folder = fileName.TryBeforeLast('\\');

            if (folder != null)
                result += "." + folder.Replace('\\', '.');

            return result;
        }

        protected virtual void WriteAttributeTag(StringBuilder sb, IEnumerable<string> attributes)
        {
            foreach (var gr in attributes.GroupsOf(a => a.Length, 100))
            {
                sb.AppendLine("[" + gr.ToString(", ") + "]");
            }
        }

        protected virtual string WriteTableEntity(string fileName, DiffTable table)
        {
            var mListInfo = GetMListInfo(table);

            if (mListInfo != null)
            {
                if(mListInfo.TrivialElementColumn != null)
                    return null;

                var primaryKey = GetPrimaryKeyColumn(table);

                var cols = table.Columns.Values.Where(col=>col != primaryKey && col != mListInfo.BackReferenceColumn).ToList();

                return WriteEmbeddedEntity(fileName, table, GetEntityName(table.Name), cols);
            }

            if (IsEnum(table.Name))
                return WriteEnum(table);

            return WriteEntity(fileName, table);
        }

        protected virtual string WriteEntity(string fileName, DiffTable table)
        {
            var name = GetEntityName(table.Name);

            StringBuilder sb = new StringBuilder();
            WriteAttributeTag(sb, GetEntityAttributes(table));
            sb.AppendLine("public class {0} : {1}".FormatWith(name, GetEntityBaseClass(table.Name)));
            sb.AppendLine("{");

            string multiColumnIndexComment = WriteMultiColumnIndexComment(table, name, table.Columns.Values);
            if (multiColumnIndexComment != null)
            {
                sb.Append(multiColumnIndexComment.Indent(4));
                sb.AppendLine();
            }

            var primaryKey = GetPrimaryKeyColumn(table);

            var columnGroups = (from col in table.Columns.Values
                                where col != primaryKey
                                group col by GetEmbeddedField(table, col) into g
                                select g).ToList();

            foreach (var col in columnGroups.SingleOrDefaultEx(g => g.Key == null).EmptyIfNull())
            {
                string field = WriteField(fileName, table, col);

                if (field != null)
                {
                    sb.Append(field.Indent(4));
                    sb.AppendLine();
                }
            }

            foreach (var gr in columnGroups.Where(g => g.Key != null))
            {
                string embeddedField = WriteEmbeddedField(table, gr.Key);

                if (embeddedField != null)
                {
                    sb.AppendLine(embeddedField.Indent(4));
                    sb.AppendLine();
                }
            }

            foreach (KeyValuePair<DiffTable, MListInfo> kvp in GetMListFields(table))
            {
                string field = WriteFieldMList(fileName, table, kvp.Value, kvp.Key);

                if (field != null)
                {
                    sb.AppendLine(field.Indent(4));
                    sb.AppendLine();
                }
            }

            string toString = WriteToString(table);
            if (toString != null)
            {
                sb.Append(toString.Indent(4));
                sb.AppendLine();
            }

            sb.AppendLine("}");
            sb.AppendLine();

            foreach (var gr in columnGroups.Where(g => g.Key != null))
            {
                string embeddedEntity = WriteEmbeddedEntity(fileName, table, GetEmbeddedTypeName(gr.Key), gr.ToList());
                if (embeddedEntity != null)
                {
                    sb.AppendLine(embeddedEntity);
                    sb.AppendLine();
                }
            }

            string operations = WriteOperations(table);
            if (operations != null)
            {
                sb.Append(operations);
            }

            return sb.ToString();
        }

        protected virtual string GetEmbeddedField(DiffTable table, DiffColumn col)
        {
            return null;
        }

        protected virtual string WriteEmbeddedEntity(string fileName, DiffTable table, string name, List<DiffColumn> columns)
        {
            StringBuilder sb = new StringBuilder();
            WriteAttributeTag(sb, new[] { "Serializable" });
            sb.AppendLine("public class {0} : {1}".FormatWith(name, typeof(EmbeddedEntity).Name));
            sb.AppendLine("{");

            string multiColumnIndexComment = WriteMultiColumnIndexComment(table, name, table.Columns.Values);
            if (multiColumnIndexComment != null)
            {
                sb.Append(multiColumnIndexComment.Indent(4));
                sb.AppendLine();
            }

            foreach (var col in columns)
            {
                string field = WriteField(fileName, table, col);

                if (field != null)
                {
                    sb.Append(field.Indent(4));
                    sb.AppendLine();
                }
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        protected virtual IEnumerable<KeyValuePair<DiffTable, MListInfo>> GetMListFields(DiffTable table)
        {
            return from relatedTable in Graph.InverseRelatedTo(table)
                   let mListInfo2 = GetMListInfo(relatedTable)
                   where mListInfo2 != null && mListInfo2.BackReferenceColumn.ForeignKey.TargetTable.Equals(table.Name)
                   select KVP.Create(relatedTable, mListInfo2);
        }

        protected virtual string WriteEnum(DiffTable table)
        {
            StringBuilder sb = new StringBuilder();

            WriteAttributeTag(sb, GetEnumAttributes(table));
            sb.AppendLine("public enum {0}".FormatWith(GetEntityName(table.Name)));
            sb.AppendLine("{");

            var dataTable = Executor.ExecuteDataTable("select * from " + table.Name);

            var rowsById = dataTable.Rows.Cast<DataRow>().ToDictionary(row=>GetEnumId(table, row));

            int lastId = -1;
            foreach (var kvp in rowsById.OrderBy(a => a.Key))
            {
                string description = GetEnumDescription(table, kvp.Value);

                string value = GetEnumValue(table, kvp.Value);

                string explicitId = kvp.Key == lastId + 1 ? "" : " = " + kvp.Key;

                sb.AppendLine("    " + (description != null ? @"[Description(""" + description + @""")]" : null) + value + explicitId + ",");

                lastId = kvp.Key;
            }

            sb.AppendLine("}");

            return sb.ToString();
        }



        protected virtual List<string> GetEnumAttributes(DiffTable table)
        {
            List<string> atts = new List<string>();

            string tableNameAttribute = GetTableNameAttribute(table.Name, null);

            if (tableNameAttribute != null)
                atts.Add(tableNameAttribute);

            string primaryKeyAttribute = GetPrimaryKeyAttribute(table);

            if (primaryKeyAttribute != null)
                atts.Add(primaryKeyAttribute);

            return atts;
        }

        protected virtual int GetEnumId(DiffTable table, DataRow row)
        {
            throw new NotImplementedException("Override GetEnumId");
        }

        protected virtual string GetEnumValue(DiffTable table, DataRow item)
        {
            throw new NotImplementedException("Override GetEnumValue");
        }

        protected virtual string GetEnumDescription(DiffTable table, DataRow item)
        {
            throw new NotImplementedException("Override GetEnumDescription");
        }

        protected virtual bool IsEnum(ObjectName objectName)
        {
            return false;
        }

        protected virtual string WriteMultiColumnIndexComment(DiffTable table, string name, IEnumerable<DiffColumn> columns)
        {
            var columnNames = columns.Select(c=>c.Name).ToHashSet();
            StringBuilder sb = new StringBuilder();
            foreach (var ix in table.Indices.Values.Where(a => a.Columns.Count > 1 || a.FilterDefinition.HasText())
                .Where(ix => ix.Columns.Intersect(columnNames).Any()))
            {
                sb.AppendLine("//Add to Logic class");
                sb.AppendLine("//sb.AddUniqueIndex<{0}>(e => new {{ {1} }}{2});".FormatWith(name,
                    ix.Columns.ToString(c => "e." + GetFieldName(table, table.Columns.GetOrThrow(c)).FirstUpper(), ", "),
                    ix.FilterDefinition == null ? null : ", " + ix.FilterDefinition));
            }

            return sb.ToString().DefaultText(null);
        }

        protected virtual string WriteOperations(DiffTable table)
        {
            var kind = GetEntityKind(table);
            if (!(kind == EntityKind.Main || kind == EntityKind.Shared || kind == EntityKind.String))
                return null;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("public static class {0}".FormatWith(GetOperationName(table.Name)));
            sb.AppendLine("{");
            sb.AppendLine("    public static readonly ExecuteSymbol<{0}> Save = OperationSymbol.Execute<{0}>();".FormatWith(GetEntityName(table.Name)));
            sb.AppendLine("}");
            return sb.ToString();
        }

        protected virtual string GetOperationName(ObjectName objectName)
        {
            return GetEntityName(objectName).RemoveSuffix("Entity") + "Operation";
        }

        protected virtual MListInfo GetMListInfo(DiffTable table)
        {
            return null;
        }

        protected virtual IEnumerable<string> GetEntityAttributes(DiffTable table)
        {
            List<string> atts = new List<string> { "Serializable" };

            atts.Add("EntityKind(EntityKind." + GetEntityKind(table) + ", EntityData." + GetEntityData(table) + ")");

            string tableNameAttribute = GetTableNameAttribute(table.Name, null);
            if (tableNameAttribute != null)
                atts.Add(tableNameAttribute);

            string primaryKeyAttribute = GetPrimaryKeyAttribute(table);
            if (primaryKeyAttribute != null)
                atts.Add(primaryKeyAttribute);

            string ticksColumnAttribute = GetTicksColumnAttribute(table);
            if (ticksColumnAttribute != null)
                atts.Add(ticksColumnAttribute);

            return atts;
        }

        protected virtual string GetTicksColumnAttribute(DiffTable table)
        {
            return "TicksColumn(Default = \"0\")";
        }


        protected virtual string GetPrimaryKeyAttribute(DiffTable table)
        {
            DiffColumn primaryKey = GetPrimaryKeyColumn(table);

            if (primaryKey == null)
                return null;

            var def = CurrentSchema.Settings.DefaultPrimaryKeyAttribute;
            
            Type type = GetValueType(primaryKey);

            List<string> parts = new List<string>();
          
            if (primaryKey.Name != def.Name)
                parts.Add("Name = \"" + primaryKey.Name + "\"");

            if (primaryKey.Identity != def.Identity)
            {
                parts.Add("Identity = " + primaryKey.Identity.ToString().ToLower());
                parts.Add("IdentityBehaviour = " + primaryKey.Identity.ToString().ToLower());
            }

            parts.AddRange(GetSqlDbTypeParts(primaryKey, type));

            if (type != def.Type || parts.Any())
                parts.Insert(0, "typeof(" + type.TypeName() + ")");

            if(parts.Any())
                return "PrimaryKey(" + parts.ToString(", ") + ")";

            return null;
        }

        protected virtual DiffColumn GetPrimaryKeyColumn(DiffTable table)
        {
            return table.Columns.Values.SingleOrDefaultEx(a => a.PrimaryKey);
        }

        protected virtual string GetTableNameAttribute(ObjectName objectName, MListInfo mListInfo)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("TableName(\"" + objectName.Name + "\"");
            if (objectName.Schema != SchemaName.Default)
                sb.Append(", SchemaName = \"" + objectName.Schema.Name + "\"");

            if (objectName.Schema.Database != null)
            {
                sb.Append(", DatabaseName = \"" + objectName.Schema.Database.Name + "\"");

                if (objectName.Schema.Database != null)
                {
                    sb.Append(", ServerName = \"" + objectName.Schema.Database.Server.Name + "\"");
                }
            }

            sb.Append(")");
            return sb.ToString();
        }

        protected virtual EntityData GetEntityData(DiffTable table)
        {
            return EntityData.Transactional;
        }

        protected virtual EntityKind GetEntityKind(DiffTable table)
        {
            return EntityKind.Main;
        } 

        protected virtual string GetEntityName(ObjectName objectName)
        {
            return objectName.Name + (IsEnum(objectName) ? "" : "Entity");
        }

        protected virtual string GetEntityBaseClass(ObjectName objectName)
        {
            return typeof(Entity).Name;
        }

        protected virtual string WriteField(string fileName, DiffTable table, DiffColumn col)
        {
            string relatedEntity = GetRelatedEntity(table, col);

            string type = GetFieldType(table, col, relatedEntity);

            string fieldName = GetFieldName(table, col);

            StringBuilder sb = new StringBuilder();

            WriteAttributeTag(sb, GetFieldAttributes(table, col, relatedEntity));
            sb.AppendLine("{0} {1};".FormatWith(type, CSharpRenderer.Escape(fieldName)));
            WriteAttributeTag(sb, GetPropertyAttributes(table, col, relatedEntity));

            sb.AppendLine("public {0} {1}".FormatWith(type, fieldName.FirstUpper()));
            sb.AppendLine("{");
            sb.AppendLine("    get { return " + CSharpRenderer.Escape(fieldName) + "; }");
            if (!IsReadonly(table, col))
                sb.AppendLine("    set { Set(ref " + CSharpRenderer.Escape(fieldName) + ", value); }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        protected virtual string GetRelatedEntity(DiffTable table, DiffColumn col)
        {
            if (col.ForeignKey == null)
                return null;

            return GetEntityName(col.ForeignKey.TargetTable);
        }

        protected virtual bool IsReadonly(DiffTable table, DiffColumn col)
        {
            return false;
        }

        protected virtual IEnumerable<string> GetPropertyAttributes(DiffTable table, DiffColumn col, string relatedEntity)
        {
            List<string> attributes = new List<string>();

            if (HasNotNullValidator(col, relatedEntity))
                attributes.Add("NotNullValidator");

            return attributes;
        }

        protected virtual bool HasNotNullValidator(DiffColumn col, string relatedEntity)
        {
            return HasNotNullableAttribute(col, relatedEntity);
        }

        protected virtual bool HasNotNullableAttribute(DiffColumn col, string relatedEntity)
        {
            return !col.Nullable && (relatedEntity != null || GetValueType(col).IsClass);
        }

        protected virtual string GetFieldName(DiffTable table, DiffColumn col)
        {
            string name = col.Name.Contains(' ') ? col.Name.ToPascal(false) : col.Name;

            if (this.GetRelatedEntity(table, col) != null)
            {
                if (name.Length > 2 && name.EndsWith("Id", StringComparison.InvariantCultureIgnoreCase))
                    name = name.RemoveEnd("Id".Length);

                if (name.Length > 2 && name.StartsWith("Id", StringComparison.InvariantCultureIgnoreCase))
                    name = name.RemoveStart("Id".Length);
            }

            return name.FirstLower();
        }

        protected virtual IEnumerable<string> GetFieldAttributes(DiffTable table, DiffColumn col, string relatedEntity)
        {
            List<string> attributes = new List<string>();

            if (HasNotNullableAttribute(col, relatedEntity))
                attributes.Add("NotNullable");

            if (col.ForeignKey == null)
            {
                string sqlDbType = GetSqlTypeAttribute(table, col);

                if (sqlDbType != null)
                    attributes.Add(sqlDbType);
            }

            if (GetEmbeddedField(table, col) != null || col.Name != DefaultColumnName(table, col))
                attributes.Add("ColumnName(\"" + col.Name + "\")");

            if (HasUniqueIndex(table, col))
                attributes.Add("UniqueIndex");

            return attributes;
        }

        protected virtual bool HasUniqueIndex(DiffTable table, DiffColumn col)
        {
            return table.Indices.Values.Any(a => a.FilterDefinition == null && a.Columns.Only() == col.Name && a.IsUnique && a.Type == DiffIndexType.NonClustered);
        }

        protected virtual string DefaultColumnName(DiffTable table, DiffColumn col)
        {
            string fieldName = GetFieldName(table, col).FirstUpper();

            if (col.ForeignKey == null)
                return fieldName;

            return "id" + fieldName;
        }

        protected virtual string GetSqlTypeAttribute(DiffTable table, DiffColumn col)
        {
            Type type = GetValueType(col);
            List<string> parts = GetSqlDbTypeParts(col, type);

            if (parts.Any())
                return "SqlDbType(" + parts.ToString(", ") + ")";

            return null;
        }

        protected virtual List<string> GetSqlDbTypeParts(DiffColumn col, Type type)
        {
            List<string> parts = new List<string>();
            var pair = CurrentSchema.Settings.GetSqlDbTypePair(type);
            if (pair.SqlDbType != col.SqlDbType)
                parts.Add("SqlDbType = SqlDbType." + col.SqlDbType);

            var defaultSize = CurrentSchema.Settings.GetSqlSize(null, pair.SqlDbType);
            if (defaultSize != null)
            {
                if (!(defaultSize == col.Precission || defaultSize == col.Length / DiffColumn.BytesPerChar(col.SqlDbType) || defaultSize == int.MaxValue && col.Length == -1))
                    parts.Add("Size = " + (col.Length == -1 ? "int.MaxValue" :
                                        col.Length != 0 ? (col.Length / DiffColumn.BytesPerChar(col.SqlDbType)).ToString() :
                                        col.Precission != 0 ? col.Precission.ToString() : "0"));
            }

            var defaultScale = CurrentSchema.Settings.GetSqlScale(null, col.SqlDbType);
            if (defaultScale != null)
            {
                if (!(col.Scale == defaultScale))
                    parts.Add("Scale = " + col.Scale);
            }

            if (col.Default != null)
                parts.Add("Default = \"" + CleanDefault(col.Default) + "\"");

            return parts;
        }

        protected virtual string CleanDefault(string def)
        {
            if (def.StartsWith("(") && def.EndsWith(")"))
                return def.Substring(1, def.Length - 2);

            return def;
        }

        protected virtual string GetFieldType(DiffTable table, DiffColumn col, string relatedEntity)
        {
            if (relatedEntity != null)
            {
                if (IsEnum(col.ForeignKey.TargetTable))
                    return col.Nullable ? relatedEntity + "?" : relatedEntity;

                return IsLite(table, col) ? "Lite<" + relatedEntity + ">" : relatedEntity;
            }

            var valueType = GetValueType(col);

            if (col.Nullable)
                return valueType.Nullify().TypeName();

            return valueType.TypeName();
        }

        protected virtual bool IsLite(DiffTable table, DiffColumn col)
        {
            return true;
        }

        protected virtual Type GetValueType(DiffColumn col)
        {
            switch (col.SqlDbType)
            {
                case SqlDbType.BigInt: return typeof(long);
                case SqlDbType.Binary: return typeof(byte[]);
                case SqlDbType.Bit: return typeof(bool);
                case SqlDbType.Char: return typeof(char);
                case SqlDbType.Date: return typeof(DateTime);
                case SqlDbType.DateTime: return typeof(DateTime);
                case SqlDbType.DateTime2: return typeof(DateTime);
                case SqlDbType.DateTimeOffset: return typeof(DateTimeOffset);
                case SqlDbType.Decimal: return typeof(Decimal);
                case SqlDbType.Float: return typeof(double);
                case SqlDbType.Image: return typeof(byte[]);
                case SqlDbType.Int: return typeof(int);
                case SqlDbType.Money: return typeof(decimal);
                case SqlDbType.NChar: return typeof(string);
                case SqlDbType.NText: return typeof(string);
                case SqlDbType.NVarChar: return typeof(string);
                case SqlDbType.Real: return typeof(float);
                case SqlDbType.SmallDateTime: return typeof(DateTime);
                case SqlDbType.SmallInt: return typeof(short);
                case SqlDbType.SmallMoney: return typeof(decimal);
                case SqlDbType.Text: return typeof(string);
                case SqlDbType.Time: return typeof(TimeSpan);
                case SqlDbType.Timestamp: return typeof(TimeSpan);
                case SqlDbType.TinyInt: return typeof(byte);
                case SqlDbType.UniqueIdentifier: return typeof(Guid);
                case SqlDbType.VarBinary: return typeof(byte[]);
                case SqlDbType.VarChar: return typeof(string);
                case SqlDbType.Xml: return typeof(string);
                case SqlDbType.Udt: return Schema.Current.Settings.UdtSqlName
                    .SingleOrDefaultEx(kvp => StringComparer.InvariantCultureIgnoreCase.Equals(kvp.Value, col.UserTypeName))
                    .Key;
                default: throw new NotImplementedException("Unknown translation for " + col.SqlDbType);
            }
        }

        protected virtual string WriteEmbeddedField(DiffTable table, string fieldName)
        {
            StringBuilder sb = new StringBuilder();

            fieldName = fieldName.FirstLower();
            string propertyName = fieldName.FirstUpper();
            string typeName = GetEmbeddedTypeName(fieldName);

            sb.AppendLine("[NotNullable]");
            sb.AppendLine("{0} {1};".FormatWith(typeName, fieldName));
            sb.AppendLine("[NotNullValidator]");
            sb.AppendLine("public {0} {1}".FormatWith(typeName, propertyName));
            sb.AppendLine("{");
            sb.AppendLine("    get { return " + fieldName + "; }");
            sb.AppendLine("    set { Set(ref " + fieldName + ", value); }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        protected virtual string GetEmbeddedTypeName(string fieldName)
        {
            return fieldName.FirstUpper() + "Embedded";
        }

        protected virtual string WriteFieldMList(string fileName, DiffTable table, MListInfo mListInfo, DiffTable relatedTable)
        {
            string type;
            List<string> fieldAttributes;
            if(mListInfo.TrivialElementColumn == null )
            {
                type = GetEntityName(relatedTable.Name);
                fieldAttributes = new List<string> { "NotNullable" };
            }
            else
            {
                string relatedEntity = GetRelatedEntity(relatedTable, mListInfo.TrivialElementColumn);
                type = GetFieldType(relatedTable, mListInfo.TrivialElementColumn, relatedEntity);

                fieldAttributes = GetFieldAttributes(relatedTable, mListInfo.TrivialElementColumn, relatedEntity).ToList(); 
            }

            var preserveOrder = GetPreserveOrderAttribute(mListInfo);
            if (preserveOrder != null)
                fieldAttributes.Add(preserveOrder);
       
            string primaryKey = GetPrimaryKeyAttribute(relatedTable);
            if (primaryKey != null)
                fieldAttributes.Add(primaryKey);

            string tableName = GetTableNameAttribute(relatedTable.Name, mListInfo);
            if (tableName != null)
                fieldAttributes.Add(tableName);

            string backColumn = GetBackColumnNameAttribute(mListInfo.BackReferenceColumn);
            if (backColumn != null)
                fieldAttributes.AddRange(backColumn);

            StringBuilder sb = new StringBuilder();

            string fieldName = GetFieldMListName(table, relatedTable, mListInfo);
            WriteAttributeTag(sb, fieldAttributes);

            sb.AppendLine("MList<{0}> {1} = new MList<{0}>();".FormatWith(type, CSharpRenderer.Escape(fieldName)));
            sb.AppendLine("[NotNullValidator, NoRepeatValidator]");
            sb.AppendLine("public MList<{0}> {1}".FormatWith(type, fieldName.FirstUpper()));
            sb.AppendLine("{");
            sb.AppendLine("    get { return " + CSharpRenderer.Escape(fieldName) + "; }");
            sb.AppendLine("    set { Set(ref " + CSharpRenderer.Escape(fieldName) + ", value); }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        protected virtual string GetPreserveOrderAttribute(MListInfo mListInfo)
        {
            if(mListInfo.PreserveOrderColumn == null)
                return null;

            var parts = new List<string>();

            parts.Add("\"" + mListInfo.PreserveOrderColumn.Name  +"\"");

             Type type = GetValueType(mListInfo.PreserveOrderColumn);

            parts.AddRange(GetSqlDbTypeParts(mListInfo.PreserveOrderColumn, type));

            return @"PreserveOrder({0})".FormatWith(parts.ToString(", "));
        }

        protected virtual string GetBackColumnNameAttribute(DiffColumn backReference)
        {
            if (backReference.Name == "idParent")
                return null;

            return "BackReferenceColumnName(\"{0}\")".FormatWith(backReference.Name);
        }

        protected virtual string GetFieldMListName(DiffTable table, DiffTable relatedTable, MListInfo mListInfo)
        {
            ObjectName name = mListInfo.TrivialElementColumn.Try(te => te.ForeignKey.TargetTable) ?? relatedTable.Name;

            return NaturalLanguageTools.Pluralize(GetEntityName(name).RemoveSuffix("Entity")).FirstLower();
        }

        protected virtual string WriteToString(DiffTable table)
        {
            var toStringColumn = GetToStringColumn(table);
            if (toStringColumn == null)
                return null;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("static Expression<Func<{0}, string>> ToStringExpression = e => e.{1}{2};".FormatWith(GetEntityName(table.Name),
                toStringColumn.PrimaryKey ? "Id" : GetFieldName(table, toStringColumn).FirstUpper(),
                GetFieldType(table, toStringColumn, null) == "string" ? "" : ".TryToString()"));
            sb.AppendLine("public override string ToString()");
            sb.AppendLine("{");
            sb.AppendLine("    return ToStringExpression.Evaluate(this);");
            sb.AppendLine("}");
            return sb.ToString();
        }

        protected virtual DiffColumn GetToStringColumn(DiffTable table)
        {
            return table.Columns.TryGetC("Name") ?? table.Columns.Values.FirstOrDefault(a => a.PrimaryKey);
        }
    }

    public class MListInfo
    {
        public MListInfo(DiffColumn backReferenceColumn)
        {
            this.BackReferenceColumn = backReferenceColumn;
        }

        public readonly DiffColumn BackReferenceColumn;
        public DiffColumn TrivialElementColumn;
        public DiffColumn PreserveOrderColumn;
    }
}
