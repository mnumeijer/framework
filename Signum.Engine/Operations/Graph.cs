﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signum.Engine.Basics;
using Signum.Engine.Operations.Internal;
using Signum.Entities;
using Signum.Entities.Basics;
using Signum.Utilities;

namespace Signum.Engine.Operations
{
    public delegate F Overrider<F>(F baseFunc); 

    public class Graph<T>
         where T : class, IIdentifiable
    {
        public class Construct : _Construct<T>, IConstructOperation
        {
            protected readonly ConstructSymbol<T>.Simple Symbol;
            OperationSymbol IOperation.OperationSymbol { get { return Symbol.Symbol; } }
            Type IOperation.OverridenType { get { return typeof(T); } }
            OperationType IOperation.OperationType { get { return OperationType.Constructor; } }
            bool IOperation.Returns { get { return true; } }
            Type IOperation.ReturnType { get { return typeof(T); } }
            public bool LogAlsoIfNotSaved { get; set; }

            //public Func<object[], T> Construct { get; set; } (inherited)
            public bool Lite { get { return false; } }


            public Construct(ConstructSymbol<T>.Simple symbol)
            {
                this.Symbol = symbol;
            }

            public void OverrideConstruct(Overrider<Func<object[], T>> overrider)
            {
                this.Construct = overrider(this.Construct);
            }

            IIdentifiable IConstructOperation.Construct(params object[] args)
            {
                using (HeavyProfiler.Log("Construct", () => Symbol.Symbol.Key))
                {
                    OperationLogic.AssertOperationAllowed(Symbol.Symbol, inUserInterface: false);

                    OperationLogDN log = new OperationLogDN
                    {
                        Operation = Symbol.Symbol,
                        Start = TimeZoneManager.Now,
                        User = UserHolder.Current.ToLite()
                    };

                    try
                    {
                        using (Transaction tr = new Transaction())
                        { 
                            T result;
                            using (OperationLogic.AllowSave<T>())
                            using (OperationLogic.OnSuroundOperation(this, null, log, args))
                            {
                                result = Construct(args);

                                AssertEntity(result);

                                if ((result != null && !result.IsNew) || LogAlsoIfNotSaved)
                                {
                                    log.SetTarget(result);
                                    log.End = TimeZoneManager.Now;
                                }
                                else
                                    log = null;
                            }

                            if (log != null)
                                using (ExecutionMode.Global())
                                    log.Save();

                            return tr.Commit(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        OperationLogic.SetExceptionData(ex, null, args);

                        if (LogAlsoIfNotSaved)
                        {
                            if (Transaction.InTestTransaction)
                                throw;

                            var exLog = ex.LogException();

                            using (Transaction tr2 = Transaction.ForceNew())
                            {
                                log.Exception = exLog.ToLite();

                                using (ExecutionMode.Global())
                                    log.Save();

                                tr2.Commit();
                            }
                        }

                        throw;
                    }
                }
            }

            protected virtual void AssertEntity(T entity)
            {
            }

            public virtual void AssertIsValid()
            {
                if (Construct == null)
                    throw new InvalidOperationException("Operation {0} does not have Constructor initialized".Formato(Symbol.Symbol));
            }

            public override string ToString()
            {
                return "{0} Construct {1}".Formato(Symbol.Symbol, typeof(T));
            }
        }

        public class ConstructFrom<F> : IConstructorFromOperation
            where F : class, IIdentifiable
        {
            protected readonly ConstructSymbol<T>.From<F> Symbol;
            OperationSymbol IOperation.OperationSymbol { get { return Symbol.Symbol; } }
            Type IOperation.OverridenType { get { return typeof(F); } }
            OperationType IOperation.OperationType { get { return OperationType.ConstructorFrom; } }

            public bool Lite { get; set; }
            public bool LogAlsoIfNotSaved { get; set; }

            bool IOperation.Returns { get { return true; } }
            Type IOperation.ReturnType { get { return typeof(T); } }

            Type IEntityOperation.BaseType { get { return Symbol.BaseType; } }
            bool IEntityOperation.HasCanExecute { get { return CanConstruct != null; } }

            public bool AllowsNew { get; set; }

            public Func<F, string> CanConstruct { get; set; }

            public ConstructFrom<F> OverrideCanConstruct(Overrider<Func<F, string>> overrider)
            {
                this.CanConstruct = overrider(this.CanConstruct ?? (f => null));
                return this;
            }

            public Func<F, object[], T> Construct { get; set; }

            public void OverrideConstruct(Overrider<Func<F, object[], T>> overrider)
            {
                this.Construct = overrider(this.Construct);
            }

            public ConstructFrom(ConstructSymbol<T>.From<F> symbol)
            {
                this.Symbol = symbol;
                this.Lite = true;
            }

            string IEntityOperation.CanExecute(IIdentifiable entity)
            {
                return OnCanConstruct(entity);
            }

            string OnCanConstruct(IIdentifiable entity)
            {
                if (entity.IsNew && !AllowsNew)
                    return EngineMessage.TheEntity0IsNew.NiceToString().Formato(entity);

                if (CanConstruct != null)
                    return CanConstruct((F)entity);

                return null;
            }

            IIdentifiable IConstructorFromOperation.Construct(IIdentifiable origin, params object[] args)
            {
                using (HeavyProfiler.Log("ConstructFrom", () => Symbol.Symbol.Key))
                {
                    OperationLogic.AssertOperationAllowed(Symbol.Symbol, inUserInterface: false);

                    string error = OnCanConstruct(origin);
                    if (error != null)
                        throw new ApplicationException(error);

                    OperationLogDN log = new OperationLogDN
                    {
                        Operation = Symbol.Symbol,
                        Start = TimeZoneManager.Now,
                        User = UserHolder.Current.ToLite(),
                        Origin = origin.ToLiteFat(),
                    };

                    try
                    {
                        using (Transaction tr = new Transaction())
                        {
                            T result;
                            using (OperationLogic.AllowSave(origin.GetType()))
                            using (OperationLogic.AllowSave<T>())
                            using (OperationLogic.OnSuroundOperation(this, log, origin, args))
                            {
                                result = Construct((F)origin, args);

                                AssertEntity(result);

                                if ((result != null && !result.IsNew) || LogAlsoIfNotSaved)
                                {
                                    log.End = TimeZoneManager.Now;
                                    log.SetTarget(result);
                                }
                                else
                                {
                                    log = null;
                                }
                            }

                            if (log != null)
                                using (ExecutionMode.Global())
                                    log.Save();

                            return tr.Commit(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        OperationLogic.SetExceptionData(ex, (IdentifiableEntity)origin, args);

                        if (LogAlsoIfNotSaved)
                        {
                            if (Transaction.InTestTransaction)
                                throw;

                            var exLog = ex.LogException();

                            using (Transaction tr2 = Transaction.ForceNew())
                            {
                                log.Exception = exLog.ToLite();

                                using (ExecutionMode.Global())
                                    log.Save();

                                tr2.Commit();
                            }
                        }

                        throw;
                    }

                }
            }

            protected virtual void AssertEntity(T entity)
            {
            }

            public virtual void AssertIsValid()
            {
                if (Construct == null)
                    throw new InvalidOperationException("Operation {0} does not hace Construct initialized".Formato(Symbol.Symbol));
            }

            public override string ToString()
            {
                return "{0} ConstructFrom {1} -> {2}".Formato(Symbol.Symbol, typeof(F), typeof(T));
            }

        }

        public class ConstructFromMany<F> : IConstructorFromManyOperation
            where F : class, IIdentifiable
        {
            protected readonly ConstructSymbol<T>.FromMany<F> Symbol;
            OperationSymbol IOperation.OperationSymbol { get { return Symbol.Symbol; } }
            Type IOperation.OverridenType { get { return typeof(F); } }
            OperationType IOperation.OperationType { get { return OperationType.ConstructorFromMany; } }

            bool IOperation.Returns { get { return true; } }
            Type IOperation.ReturnType { get { return typeof(T); } }

            Type IConstructorFromManyOperation.BaseType { get { return Symbol.BaseType; } }

            public bool LogAlsoIfNotSaved { get; set; }

            public Func<List<Lite<F>>, object[], T> Construct { get; set; }

            public void OverrideConstruct(Overrider<Func<List<Lite<F>>, object[], T>> overrider)
            {
                this.Construct = overrider(this.Construct);
            }

            public ConstructFromMany(ConstructSymbol<T>.FromMany<F> symbol)
            {
                this.Symbol = symbol;
            }

            IIdentifiable IConstructorFromManyOperation.Construct(IEnumerable<Lite<IIdentifiable>> lites, params object[] args)
            {
                using (HeavyProfiler.Log("ConstructFromMany", () => Symbol.Symbol.Key))
                {
                    OperationLogic.AssertOperationAllowed(Symbol.Symbol, inUserInterface: false);

                    OperationLogDN log = new OperationLogDN
                    {
                        Operation = Symbol.Symbol,
                        Start = TimeZoneManager.Now,
                        User = UserHolder.Current.ToLite()
                    };

                    try
                    {
                        using (Transaction tr = new Transaction())
                        {
                            T result;

                            using (OperationLogic.AllowSave<F>())
                            using (OperationLogic.AllowSave<T>())
                            using (OperationLogic.OnSuroundOperation(this, log, null, args))
                            {
                                result = OnConstruct(lites.Cast<Lite<F>>().ToList(), args);

                                AssertEntity(result);

                                if ((result != null && !result.IsNew) || LogAlsoIfNotSaved)
                                {
                                    log.End = TimeZoneManager.Now;
                                    log.SetTarget(result);
                                }
                                else
                                {
                                    log = null;
                                }
                            }

                            if (log != null)
                                using (ExecutionMode.Global())
                                    log.Save();

                            return tr.Commit(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        OperationLogic.SetExceptionData(ex, null, args);

                        if (LogAlsoIfNotSaved)
                        {
                            if (Transaction.InTestTransaction)
                                throw;

                            var exLog = ex.LogException();

                            using (Transaction tr2 = Transaction.ForceNew())
                            {
                                log.Exception = exLog.ToLite();

                                using (ExecutionMode.Global())
                                    log.Save();

                                tr2.Commit();
                            }
                        }

                        throw;
                    }
                }
            }

            protected virtual T OnConstruct(List<Lite<F>> lites, object[] args)
            {
                return Construct(lites, args);
            }

            protected virtual void AssertEntity(T entity)
            {
            }

            public virtual void AssertIsValid()
            {
                if (Construct == null)
                    throw new InvalidOperationException("Operation {0} Constructor initialized".Formato(Symbol));
            }

            public override string ToString()
            {
                return "{0} ConstructFromMany {1} -> {2}".Formato(Symbol, typeof(F), typeof(T));
            }
        }

        public class Execute : _Execute<T>, IExecuteOperation
        {
            protected readonly ExecuteSymbol<T> Symbol;
            OperationSymbol IOperation.OperationSymbol { get { return Symbol.Symbol; } }
            Type IOperation.OverridenType { get { return typeof(T); } }
            OperationType IOperation.OperationType { get { return OperationType.Execute; } }
            public bool Lite { get; set; }
            bool IOperation.Returns { get { return true; } }
            Type IOperation.ReturnType { get { return null; } }

            Type IEntityOperation.BaseType { get { return Symbol.BaseType; } }
            bool IEntityOperation.HasCanExecute { get { return CanExecute != null; } }

            public bool AllowsNew { get; set; }

            //public Action<T, object[]> Execute { get; set; } (inherited)
            public Func<T, string> CanExecute { get; set; }

            public Execute OverrideCanExecute(Overrider<Func<T, string>> overrider)
            {
                this.CanExecute = overrider(this.CanExecute ?? (t => null));
                return this;
            }

            public void OverrideExecute(Overrider<Action<T, object[]>> overrider)
            {
                this.Execute = overrider(this.Execute);
            }

            public Execute(ExecuteSymbol<T> symbol)
            {
                this.Symbol = symbol;
                this.Lite = true;
            }

            string IEntityOperation.CanExecute(IIdentifiable entity)
            {
                return OnCanExecute((T)entity);
            }

            protected virtual string OnCanExecute(T entity)
            {
                if (entity.IsNew && !AllowsNew)
                    return EngineMessage.TheEntity0IsNew.NiceToString().Formato(entity);

                if (CanExecute != null)
                    return CanExecute(entity);

                return null;
            }

            void IExecuteOperation.Execute(IIdentifiable entity, params object[] args)
            {
                using (HeavyProfiler.Log("Execute", () => Symbol.Symbol.Key))
                {
                    OperationLogic.AssertOperationAllowed(Symbol.Symbol, inUserInterface: false);

                    string error = OnCanExecute((T)entity);
                    if (error != null)
                        throw new ApplicationException(error);

                    OperationLogDN log = new OperationLogDN
                    {
                        Operation = Symbol.Symbol,
                        Start = TimeZoneManager.Now,
                        User = UserHolder.Current.ToLite()
                    };

                    try
                    {
                        using (Transaction tr = new Transaction())
                        {
                            using (OperationLogic.AllowSave(entity.GetType()))
                            using (OperationLogic.OnSuroundOperation(this, log, entity, args))
                            {
                                Execute((T)entity, args);

                                AssertEntity((T)entity);

                                entity.Save(); //Nothing happens if already saved

                                log.SetTarget(entity);
                                log.End = TimeZoneManager.Now;
                            }

                            using (ExecutionMode.Global())
                                log.Save();

                            tr.Commit();
                        }
                    }
                    catch (Exception ex)
                    {
                        OperationLogic.SetExceptionData(ex, (IdentifiableEntity)entity, args);

                        if (Transaction.InTestTransaction)
                            throw;

                        var exLog = ex.LogException();

                        using (Transaction tr2 = Transaction.ForceNew())
                        {
                            log.Target = entity.IsNew ? null : entity.ToLite();
                            log.Exception = exLog.ToLite();

                            using (ExecutionMode.Global())
                                log.Save();

                            tr2.Commit();
                        }

                        throw;
                    }
                }
            }

            protected virtual void AssertEntity(T entity)
            {
            }

            public virtual void AssertIsValid()
            {
                if (Execute == null)
                    throw new InvalidOperationException("Operation {0} does not have Execute initialized".Formato(Symbol));
            }

            public override string ToString()
            {
                return "{0} Execute on {1}".Formato(Symbol, typeof(T));
            }
        }

        public class Delete : _Delete<T>, IDeleteOperation
        {
            protected readonly DeleteSymbol<T> Symbol;
            OperationSymbol IOperation.OperationSymbol { get { return Symbol.Symbol; } }
            Type IOperation.OverridenType { get { return typeof(T); } }
            OperationType IOperation.OperationType { get { return OperationType.Delete; } }
            public bool Lite { get; set; }
            bool IOperation.Returns { get { return false; } }
            Type IOperation.ReturnType { get { return null; } }

            public bool AllowsNew { get { return false; } }

            Type IEntityOperation.BaseType { get { return Symbol.BaseType; } }
            bool IEntityOperation.HasCanExecute { get { return CanDelete != null; } }

            //public Action<T, object[]> Delete { get; set; } (inherited)
            public Func<T, string> CanDelete { get; set; }

            public Delete OverrideCanDelete(Overrider<Func<T, string>> overrider)
            {
                this.CanDelete = overrider(this.CanDelete ?? (t => null));
                return this;
            }

            public void OverrideDelete(Overrider<Action<T, object[]>> overrider)
            {
                this.Delete = overrider(this.Delete);
            }

            public Delete(DeleteSymbol<T> symbol)
            {
                this.Symbol = symbol;
                this.Lite = true;
            }

            string IEntityOperation.CanExecute(IIdentifiable entity)
            {
                return OnCanDelete((T)entity);
            }

            protected virtual string OnCanDelete(T entity)
            {
                if (entity.IsNew)
                    return EngineMessage.TheEntity0IsNew.NiceToString().Formato(entity);

                if (CanDelete != null)
                    return CanDelete(entity);

                return null;
            }

            void IDeleteOperation.Delete(IIdentifiable entity, params object[] args)
            {
                using (HeavyProfiler.Log("Delete", () => Symbol.Symbol.Key))
                {
                    OperationLogic.AssertOperationAllowed(Symbol.Symbol, inUserInterface: false);

                    string error = OnCanDelete((T)entity);
                    if (error != null)
                        throw new ApplicationException(error);

                    OperationLogDN log = new OperationLogDN
                    {
                        Operation = Symbol.Symbol,
                        Start = TimeZoneManager.Now,
                        User = UserHolder.Current.ToLite()
                    };

                    using (OperationLogic.AllowSave(entity.GetType()))
                    using (OperationLogic.OnSuroundOperation(this, log, entity, args))
                    {
                        try
                        {
                            using (Transaction tr = new Transaction())
                            {
                                OnDelete((T)entity, args);

                                log.SetTarget(entity);
                                log.End = TimeZoneManager.Now;

                                using (ExecutionMode.Global())
                                    log.Save();

                                tr.Commit();
                            }
                        }
                        catch (Exception ex)
                        {
                            OperationLogic.SetExceptionData(ex, (IdentifiableEntity)entity, args);

                            if (Transaction.InTestTransaction)
                                throw;

                            var exLog = ex.LogException();

                            using (Transaction tr2 = Transaction.ForceNew())
                            {
                                log.Target = entity.ToLite();
                                log.Exception = exLog.ToLite();

                                using (ExecutionMode.Global())
                                    log.Save();

                                tr2.Commit();
                            }

                            throw;
                        }
                    }
                }
            }

            protected virtual void OnDelete(T entity, object[] args)
            {
                Delete(entity, args);
            }

            public virtual void AssertIsValid()
            {
                if (Delete == null)
                    throw new InvalidOperationException("Operation {0} does not have Delete initialized".Formato(Symbol.Symbol));
            }

            public override string ToString()
            {
                return "{0} Delete {1}".Formato(Symbol.Symbol, typeof(T));
            }
        }
    }
}
