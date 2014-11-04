﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Signum.Engine.Linq
{
    /// <summary>
    /// A visitor that replaces references to one specific instance of a node with another
    /// </summary>
    internal class Replacer : DbExpressionVisitor
    {
        Expression searchFor;
        Expression replaceWith;

        private Replacer() { }

        static internal Expression Replace(Expression expression, Expression searchFor, Expression replaceWith)
        {
            return new Replacer
            {
                searchFor = searchFor,
                replaceWith = replaceWith
            }.Visit(expression);
        }

        public override Expression Visit(Expression exp)
        {
            if (exp != null && (exp == this.searchFor || exp.Equals(this.searchFor)))
            {
                return this.replaceWith;
            }
            return base.Visit(exp);
        }
    }
}
