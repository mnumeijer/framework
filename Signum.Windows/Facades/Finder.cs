﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Signum.Entities;
using Signum.Entities.DynamicQuery;
using Signum.Services;
using Signum.Utilities;

namespace Signum.Windows
{
    public static class Finder
    {
        public static FinderManager Manager { get; private set; }

        public static void Start(FinderManager manager)
        {
            Manager = manager;
        }

        public static void Explore(ExploreOptions options)
        {
            Manager.Explore(options);
        }

        public static Lite<T> Find<T>()
            where T : IdentifiableEntity
        {
            return (Lite<T>)Manager.Find(new FindOptions(typeof(T)));
        }

        public static Lite<T> Find<T>(FindOptions options)
            where T : IdentifiableEntity
        {
            if (options.QueryName == null)
                options.QueryName = typeof(T);

            return (Lite<T>)Manager.Find(options);
        }

        public static Lite<IdentifiableEntity> Find(FindOptions options)
        {
            return Manager.Find(options);
        }


        public static List<Lite<IdentifiableEntity>> FindMany(FindManyOptions options)
        {
            return Manager.FindMany(options);
        }

        public static List<Lite<T>> FindMany<T>()
         where T : IdentifiableEntity
        {
            List<Lite<IdentifiableEntity>> result = Manager.FindMany(new FindManyOptions(typeof(T)));
            if (result == null)
                return null;

            return result.Cast<Lite<T>>().ToList();
        }

        public static List<Lite<T>> FindMany<T>(FindManyOptions options)
            where T : IdentifiableEntity
        {
            if (options.QueryName == null)
                options.QueryName = typeof(T);

            List<Lite<IdentifiableEntity>> result = Manager.FindMany(options);
            if (result == null)
                return null;

            return result.Cast<Lite<T>>().ToList();
        }

        public static QuerySettings GetQuerySettings(object queryName)
        {
            return Manager.GetQuerySettings(queryName);
        }

        public static bool IsFindable(object queryName)
        {
            return Manager.OnIsFindable(queryName);
        }

        public static void AddQuerySettings(List<QuerySettings> settings)
        {
            Manager.QuerySettings.AddRange(settings, s => s.QueryName, s => s, "QuerySettings");
        }

        public static void AddQuerySetting(QuerySettings setting)
        {
            Manager.QuerySettings.AddOrThrow(setting.QueryName, setting, "QuerySettings {0} repeated");
        }
    }

    public class FinderManager
    {
        public Dictionary<object, QuerySettings> QuerySettings { get; set; }

        public event Action<SearchWindow, object> TaskSearchWindow;

        public FinderManager()
        {
            QuerySettings = new Dictionary<object, QuerySettings>();
        }

        public ImageSource DefaultFindIcon = ImageLoader.GetImageSortName("find.png");

        public event Action Initializing;
        bool initialized;
        internal void Initialize()
        {
            if (!initialized)
            {
                if (!Server.OfflineMode)
                {
                    QueryToken.EntityExtensions = DynamicQueryServer.GetExtensionToken;
                }

                CompleteQuerySettings();

                TaskSearchWindow += TaskSetIconSearchWindow;

                if (Initializing != null)
                    Initializing();

                initialized = true;
            }
        }

        void CompleteQuerySettings()
        {
            var dic = Server.Return((IDynamicQueryServer s) => s.GetQueryNames()).ToDictionary(a => a, a => new QuerySettings(a));
            foreach (var kvp in dic)
            {
                if (!QuerySettings.ContainsKey(kvp.Key))
                    QuerySettings.Add(kvp.Key, kvp.Value);
            }
        }

        public QuerySettings GetQuerySettings(object queryName)
        {
            return QuerySettings.TryGetC(queryName);
        }

        void TaskSetIconSearchWindow(SearchWindow sw, object qn)
        {
            sw.Icon = GetFindIcon(qn, true);
        }

        public virtual ImageSource GetFindIcon(object queryName, bool useDefault)
        {
            var qs = QuerySettings.TryGetC(queryName);
            if (qs != null && qs.Icon != null)
                return qs.Icon;

            if (queryName is Type)
            {
                EntitySettings es = Navigator.Manager.EntitySettings.TryGetC((Type)queryName);
                if (es != null && es.Icon != null)
                    return es.Icon;
            }

            return useDefault ? DefaultFindIcon : null;
        }

        public virtual string SearchTitle(object queryName)
        {
            return SearchMessage.FinderOf0.NiceToString().Formato(QueryUtils.GetNiceName(queryName));
        }

        public virtual Lite<IdentifiableEntity> Find(FindOptions options)
        {
            AssertFindable(options.QueryName);

            if (options.ReturnIfOne)
            {
                Lite<IdentifiableEntity> lite = DynamicQueryServer.QueryUnique(new UniqueOptions(options.QueryName)
                {
                    FilterOptions = options.FilterOptions,
                    UniqueType = UniqueType.SingleOrMany
                });

                if (lite != null)
                {
                    return lite;
                }
            }

            SearchWindow sw = CreateSearchWindow(options);

            sw.MultiSelection = false;

            if (sw.ShowDialog() == true)
            {
                return sw.SelectedItem;
            }
            return null;
        }


        public virtual List<Lite<IdentifiableEntity>> FindMany(FindManyOptions options)
        {
            AssertFindable(options.QueryName);

            SearchWindow sw = CreateSearchWindow(options);
            if (sw.ShowDialog() == true)
            {
                return sw.SelectedItems;
            }
            return null;
        }

        public virtual void Explore(ExploreOptions options)
        {
            AssertFindable(options.QueryName);

            if (options.NavigateIfOne)
            {
                Lite<IdentifiableEntity> lite = DynamicQueryServer.QueryUnique(new UniqueOptions(options.QueryName)
                {
                    FilterOptions = options.FilterOptions,
                    UniqueType = UniqueType.Only
                });

                if (lite != null)
                {
                    Navigator.Navigate(lite, new NavigateOptions { Closed = options.Closed });
                    return;
                }
            }

            Navigator.OpenIndependentWindow(() => CreateSearchWindow(options),
                afterShown: null,
                closed: options.Closed);
        }


        protected virtual SearchWindow CreateSearchWindow(FindOptionsBase options)
        {
            SearchWindow sw = new SearchWindow(options.GetSearchMode(), options.SearchOnLoad)
            {
                QueryName = options.QueryName,
                FilterOptions = new FreezableCollection<FilterOption>(options.FilterOptions.Select(c => c.CloneIfNecessary())),
                OrderOptions = new ObservableCollection<OrderOption>(options.OrderOptions.Select(c => c.CloneIfNecessary())),
                ColumnOptions = new ObservableCollection<ColumnOption>(options.ColumnOptions.Select(c => c.CloneIfNecessary())),
                ColumnOptionsMode = options.ColumnOptionsMode,
                Pagination = options.Pagination ?? GetQuerySettings(options.QueryName).Pagination ?? FindOptions.DefaultPagination,
                ShowFilters = options.ShowFilters,
                ShowFilterButton = options.ShowFilterButton,
                ShowFooter = options.ShowFooter,
                ShowHeader = options.ShowHeader,
                Title = options.WindowTitle ?? SearchTitle(options.QueryName)
            };

            if (options.InitializeSearchControl != null)
                options.InitializeSearchControl(sw.SearchControl);

            if (TaskSearchWindow != null)
                TaskSearchWindow(sw, options.QueryName);

            return sw;
        }

        public event Func<object, bool> IsFindable;

        internal protected virtual bool OnIsFindable(object queryName)
        {
            QuerySettings qs = QuerySettings.TryGetC(queryName);
            if (qs == null || !qs.IsFindable)
                return false;

            foreach (var isFindable in IsFindable.GetInvocationListTyped())
            {
                if (!isFindable(queryName))
                    return false;
            }

            return true;
        }

        internal protected virtual void AssertFindable(object queryName)
        {
            QuerySettings qs = QuerySettings.TryGetC(queryName);
            if (qs == null)
                throw new InvalidOperationException(SearchMessage.Query0NotRegistered.NiceToString().Formato(queryName));

            if (!OnIsFindable(queryName))
                throw new UnauthorizedAccessException(SearchMessage.Query0NotAllowed.NiceToString().Formato(queryName));
        }

    }
}
