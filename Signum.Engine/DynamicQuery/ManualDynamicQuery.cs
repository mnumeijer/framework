﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signum.Entities.DynamicQuery;
using Signum.Utilities.Reflection;
using Signum.Entities;
using System.Linq.Expressions;
using System.Reflection;
using Signum.Utilities;
using Signum.Entities.Reflection;

namespace Signum.Engine.DynamicQuery
{
    public class ManualDynamicQueryCore<T> : DynamicQueryCore<T>
    {
        public Func<QueryRequest, QueryDescription, DEnumerableCount<T>> Execute { get; private set; }

        public ManualDynamicQueryCore(Func<QueryRequest, QueryDescription, DEnumerableCount<T>> execute)
        {
            if (execute == null)
                throw new ArgumentNullException("execute");

            this.Execute = execute;

            this.StaticColumns = MemberEntryFactory.GenerateList<T>(MemberOptions.Properties | MemberOptions.Fields)
              .Select((e, i) => new ColumnDescriptionFactory(i, e.MemberInfo, null)).ToArray();
        }


        public override ResultTable ExecuteQuery(QueryRequest request)
        {
            request.Columns.Insert(0, new _EntityColumn(EntityColumnFactory().BuildColumnDescription(), QueryName));

            DEnumerableCount<T> manualResult = Execute(request, GetQueryDescription());

            return manualResult.ToResultTable(request); 
        }

        public override int ExecuteQueryCount(QueryCountRequest request)
        {
            var req = new QueryRequest
            {
                QueryName = request.QueryName,
                Filters = request.Filters,
                Columns = new List<Column>() { new Column(this.EntityColumnFactory().BuildColumnDescription(), QueryName) },
                Orders = new List<Order>(),
                Pagination = new Pagination.All(),
            };

            return Execute(req, GetQueryDescription()).Collection.Count();
        }

        public override Lite<Entity> ExecuteUniqueEntity(UniqueEntityRequest request)
        {
            var req = new QueryRequest
            {
                QueryName = request.QueryName,
                Filters = request.Filters,
                Orders = request.Orders,
                Columns = new List<Column> { new Column(this.EntityColumnFactory().BuildColumnDescription(), QueryName) },
                Pagination = new Pagination.Firsts(2),
            };

            DEnumerable<T> mr = Execute(req, GetQueryDescription());

            return (Lite<Entity>)mr.Collection.Select(entitySelector.Value).Unique(request.UniqueType);
        }

        static readonly Lazy<Func<object, Lite<IEntity>>> entitySelector = new Lazy<Func<object, Lite<IEntity>>>(() =>
        {
            ParameterExpression pe = Expression.Parameter(typeof(object), "p");
            return  Expression.Lambda<Func<object, Lite<IEntity>>>(TupleReflection.TupleChainProperty(pe, 0), pe).Compile();
        }, true);

        public override ResultTable ExecuteQueryGroup(QueryGroupRequest request)
        {
            var simpleFilters = request.Filters.Where(f => !(f.Token is AggregateToken)).ToList();
            var aggregateFilters = request.Filters.Where(f => f.Token is AggregateToken).ToList();

            var keys = request.Columns.Select(t => t.Token).Where(t => !(t is AggregateToken)).ToHashSet();

            var allAggregates = request.AllTokens().OfType<AggregateToken>().ToHashSet();

            DEnumerableCount<T> plainCollection = Execute(new QueryRequest
            {   
                Columns = keys.Concat(allAggregates.Select(at => at.Parent).NotNull()).Distinct().Select(t => new Column(t, t.NiceName())).ToList(),
                Orders = new List<Order>(),
                Filters = simpleFilters,
                QueryName = request.QueryName,
                Pagination = new Pagination.All(),
            }, GetQueryDescription());

            var groupCollection = plainCollection
                     .GroupBy(keys, allAggregates)
                     .Where(aggregateFilters)
                     .OrderBy(request.Orders);

            var cols = request.Columns
                .Select(c => Tuple.Create(c, Expression.Lambda(c.Token.BuildExpression(groupCollection.Context), groupCollection.Context.Parameter))).ToList();

            var values = groupCollection.Collection.ToArray();

            return values.ToResultTable(cols, values.Length, new Pagination.All());
        }
    }
}
