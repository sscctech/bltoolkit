﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace BLToolkit.Data.Sql
{
	using FJoin = SqlQuery.FromClause.Join;

	[DebuggerDisplay("SQL = {SqlText}")]
	public class SqlQuery : ISqlExpression, ISqlTableSource
	{
		#region Init

		public SqlQuery()
		{
			_sourceID = Interlocked.Increment(ref SourceIDCounter);

			_select  = new SelectClause (this);
			_from    = new FromClause   (this);
			_where   = new WhereClause  (this);
			_groupBy = new GroupByClause(this);
			_having  = new WhereClause  (this);
			_orderBy = new OrderByClause(this);
		}

		internal void Set(
			SelectClause       select,
			FromClause         from,
			WhereClause        where,
			GroupByClause      groupBy,
			WhereClause        having,
			OrderByClause      orderBy,
			List<Union>        unions,
			SqlQuery           parentSql,
			bool               parameterDependent,
			List<SqlParameter> parameters)
		{
			_select             = select;
			_from               = from;
			_where              = where;
			_groupBy            = groupBy;
			_having             = having;
			_orderBy            = orderBy;
			_unions             = unions;
			_parentSql          = parentSql;
			_parameterDependent = parameterDependent;
			_parameters.AddRange(parameters);

			_select. SetSqlQuery(this);
			_from.   SetSqlQuery(this);
			_where.  SetSqlQuery(this);
			_groupBy.SetSqlQuery(this);
			_having. SetSqlQuery(this);
			_orderBy.SetSqlQuery(this);
		}

		readonly List<SqlParameter> _parameters = new List<SqlParameter>();
		public   List<SqlParameter>  Parameters
		{
			get { return _parameters; }
		}

		private bool _parameterDependent;
		public  bool  ParameterDependent
		{
			get { return _parameterDependent;  }
			set { _parameterDependent = value; }
		}

		private SqlQuery _parentSql;
		public  SqlQuery  ParentSql
		{
			get { return _parentSql;  }
			set { _parentSql = value; }
		}

		public bool IsSimple
		{
			get
			{
				return !Select.HasModifier && Where.IsEmpty && GroupBy.IsEmpty && Having.IsEmpty && OrderBy.IsEmpty;
			}
		}

		#endregion

		#region Column

		public class Column : IEquatable<Column>, ISqlExpression, IChild<ISqlTableSource>
		{
			public Column(ISqlTableSource parent, ISqlExpression expression, string alias)
			{
				if (expression == null) throw new ArgumentNullException("expression");

				_parent     = parent;
				_expression = expression;
				_alias      = alias;
			}

			public Column(ISqlTableSource builder, ISqlExpression expression)
				: this(builder, expression, null)
			{
			}

			private ISqlExpression _expression;
			public  ISqlExpression  Expression
			{
				get { return _expression;  }
				set { _expression = value; }
			}

			internal string _alias;
			public   string  Alias
			{
				get
				{
					if (_alias == null)
					{
						if (_expression is SqlField)
						{
							SqlField field = (SqlField)_expression;
							return field.Alias ?? field.PhysicalName;
						}

						if (_expression is Column)
						{
							Column col = (Column)_expression;
							return col.Alias;
						}
					}

					return _alias;
				}
				set { _alias = value; }
			}

			public bool Equals(Column other)
			{
				return _alias == other._alias && _expression.Equals(other._expression);
			}

			public override string ToString()
			{
				if (Expression is SqlQuery)
					return "(\n\t\t" + Expression.ToString().Replace("\n", "\n\t\t") + "\n\t)";

				return Expression.ToString();
			}

			#region ISqlExpression Members

			public int Precedence
			{
				get { return Sql.Precedence.Primary; }
			}

			public Type SystemType
			{
				get { return Expression.SystemType; }
			}

			public bool CanBeNull()
			{
				return Expression.CanBeNull();
			}

			public ICloneableElement Clone(Dictionary<ICloneableElement, ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
			{
				if (!doClone(this))
					return this;

				ICloneableElement clone;

				ISqlTableSource parent = (ISqlTableSource)_parent.Clone(objectTree, doClone);

				if (!objectTree.TryGetValue(this, out clone))
					objectTree.Add(this, clone = new Column(
						parent,
						(ISqlExpression)_expression.Clone(objectTree, doClone),
						_alias));

				return clone;
			}

			#endregion

			#region IEquatable<ISqlExpression> Members

			bool IEquatable<ISqlExpression>.Equals(ISqlExpression other)
			{
				if (this == other)
					return true;

				return other is Column && Equals((Column)other);
			}

			#endregion

			#region ISqlExpressionWalkable Members

			[Obsolete]
			public ISqlExpression Walk(bool skipColumns, WalkingFunc func)
			{
				if (!(skipColumns && _expression is Column))
					_expression = _expression.Walk(skipColumns, func);

				return func(this);
			}

			#endregion

			#region IChild<ISqlTableSource> Members

			string IChild<ISqlTableSource>.Name
			{
				get { return Alias; }
			}

			private ISqlTableSource _parent;
			public  ISqlTableSource  Parent
			{
				get { return _parent;  }
				set { _parent = value; }
			}

			#endregion
	
			#region IQueryElement Members

			public QueryElementType ElementType { get { return QueryElementType.Column; } }

			#endregion
		}

		#endregion

		#region TableSource

		public class TableSource : ISqlTableSource, ISqlExpressionWalkable
		{
			public TableSource(ISqlTableSource source, string alias)
				: this(source, alias, null)
			{
			}

			public TableSource(ISqlTableSource source, string alias, params JoinedTable[] joins)
			{
				if (source == null) throw new ArgumentNullException("source");

				_source = source;
				_alias  = alias;

				if (joins != null)
					_joins.AddRange(joins);
			}

			public TableSource(ISqlTableSource source, string alias, IEnumerable<JoinedTable> joins)
			{
				if (source == null) throw new ArgumentNullException("source");

				_source = source;
				_alias  = alias;

				if (joins != null)
					_joins.AddRange(joins);
			}

			private ISqlTableSource _source;
			public  ISqlTableSource  Source
			{
				get { return _source;  }
				set { _source = value; }
			}

			internal string _alias;
			public   string  Alias
			{
				get
				{
					if (string.IsNullOrEmpty(_alias))
					{
						if (_source is TableSource)
							return (_source as TableSource).Alias;

						if (_source is SqlTable)
							return ((SqlTable)_source).Alias;
					}

					return _alias;
				}
				set { _alias = value; }
			}

			public TableSource this[ISqlTableSource table]
			{
				get { return this[table, null]; }
			}

			public TableSource this[ISqlTableSource table, string alias]
			{
				get
				{
					foreach (JoinedTable tj in Joins)
					{
						TableSource t = CheckTableSource(tj.Table, table, alias);

						if (t != null)
							return t;
					}

					return null;
				}
			}

			readonly List<JoinedTable> _joins = new List<JoinedTable>();
			public   List<JoinedTable>  Joins
			{
				get { return _joins;  }
			}

			public void ForEach(Action<TableSource> action)
			{
				action(this);
				foreach (JoinedTable join in Joins)
					join.ForEach(action);

				if (Source is SqlQuery)
					((SqlQuery)Source).ForEachTable(action);
			}

			public int GetJoinNumber()
			{
				int n = Joins.Count;

				foreach (JoinedTable join in Joins)
					n += join.Table.GetJoinNumber();

				return n;
			}

			public override string ToString()
			{
				StringBuilder sb = new StringBuilder(Source is SqlQuery ?
					"(\n\t" + Source.ToString().Replace("\n", "\n\t") + "\n)" :
					Source.ToString());

				sb.Append(" as t").Append(SourceID);

				foreach (JoinedTable join in Joins)
					sb.AppendLine().Append('\t').Append(join.ToString().Replace("\n", "\n\t"));

				return sb.ToString();
			}

			#region ISqlExpressionWalkable Members

			[Obsolete]
			public ISqlExpression Walk(bool skipColumns, WalkingFunc func)
			{
				if (_source is ISqlExpression)
					_source = (ISqlTableSource)((ISqlExpression)_source).Walk(skipColumns, func);

				for (int i = 0; i < Joins.Count; i++)
					((ISqlExpressionWalkable)Joins[i]).Walk(skipColumns, func);

				return null;
			}

			#endregion

			#region ISqlTableSource Members

			public int       SourceID { get { return Source.SourceID; } }
			public SqlField  All      { get { return Source.All;      } }

			#endregion

			#region ICloneableElement Members

			public ICloneableElement Clone(Dictionary<ICloneableElement, ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
			{
				if (!doClone(this))
					return this;

				ICloneableElement clone;

				if (!objectTree.TryGetValue(this, out clone))
				{
					TableSource ts = new TableSource((ISqlTableSource)_source.Clone(objectTree, doClone), _alias);

					objectTree.Add(this, clone = ts);

					ts._joins.AddRange(_joins.ConvertAll<JoinedTable>(delegate(JoinedTable jt) { return (JoinedTable)jt.Clone(objectTree, doClone); }));
				}

				return clone;
			}

			#endregion

			#region IQueryElement Members

			public QueryElementType ElementType { get { return QueryElementType.TableSource; } }

			#endregion
		}

		#endregion

		#region TableJoin

		public enum JoinType
		{
			Auto,
			Inner,
			Left
		}

		public class JoinedTable : IQueryElement, ISqlExpressionWalkable, ICloneableElement
		{
			public JoinedTable(JoinType joinType, TableSource table, bool isWeak, SearchCondition searchCondition)
			{
				_joinType  = joinType;
				_table     = table;
				_isWeak    = isWeak;
				_condition = searchCondition;
			}

			public JoinedTable(JoinType joinType, TableSource table, bool isWeak)
				: this(joinType, table, isWeak, new SearchCondition())
			{
			}

			public JoinedTable(JoinType joinType, ISqlTableSource table, string alias, bool isWeak)
				: this(joinType, new TableSource(table, alias), isWeak)
			{
			}

			private JoinType _joinType;
			public  JoinType  JoinType
			{
				get { return _joinType;  }
				set { _joinType = value; }
			}

			private TableSource _table;
			public  TableSource  Table
			{
				get { return _table;  }
				set { _table = value; }
			}

			private SearchCondition _condition;
			public  SearchCondition  Condition
			{
				get { return _condition;  }
			}

			private bool _isWeak;
			public  bool  IsWeak
			{
				get { return _isWeak;  }
				set { _isWeak = value; }
			}

			public ICloneableElement Clone(Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
			{
				if (!doClone(this))
					return this;

				ICloneableElement clone;

				if (!objectTree.TryGetValue(this, out clone))
					objectTree.Add(this, clone = new JoinedTable(
						JoinType,
						(TableSource)_table.Clone(objectTree, doClone), 
						_isWeak,
						(SearchCondition)_condition.Clone(objectTree, doClone)));

				return clone;
			}

			public void ForEach(Action<TableSource> action)
			{
				Table.ForEach(action);
			}

			public override string ToString()
			{
				return (JoinType == JoinType.Inner? "INNER" : "LEFT") + " JOIN " + Table + " ON " + Condition;
			}

			#region ISqlExpressionWalkable Members

			[Obsolete]
			public ISqlExpression Walk(bool skipColumns, WalkingFunc action)
			{
				_condition = (SearchCondition)((ISqlExpressionWalkable)_condition).Walk(skipColumns, action);

				_table.Walk(skipColumns, action);

				return null;
			}

			#endregion

			#region IQueryElement Members

			public QueryElementType ElementType
			{
				get { return QueryElementType.JoinedTable; }
			}

			#endregion
		}

		#endregion

		#region Predicate

		public abstract class Predicate : ISqlPredicate
		{
			public enum Operator
			{
				Equal,          // =     Is the operator used to test the equality between two expressions.
				NotEqual,       // <> != Is the operator used to test the condition of two expressions not being equal to each other.
				Greater,        // >     Is the operator used to test the condition of one expression being greater than the other.
				GreaterOrEqual, // >=    Is the operator used to test the condition of one expression being greater than or equal to the other expression.
				NotGreater,     // !>    Is the operator used to test the condition of one expression not being greater than the other expression.
				Less,           // <     Is the operator used to test the condition of one expression being less than the other.
				LessOrEqual,    // <=    Is the operator used to test the condition of one expression being less than or equal to the other expression.
				NotLess         // !<    Is the operator used to test the condition of one expression not being less than the other expression.
			}

			public class Expr : Predicate
			{
				public Expr(ISqlExpression exp1, int precedence)
					: base(precedence)
				{
					_expr1 = exp1;
				}

				public Expr(ISqlExpression exp1)
					: base(exp1.Precedence)
				{
					_expr1 = exp1;
				}

				ISqlExpression _expr1; public ISqlExpression Expr1 { get { return _expr1; } }

				[Obsolete]
				protected override void Walk(bool skipColumns, WalkingFunc func)
				{
					_expr1 = _expr1.Walk(skipColumns, func);
				}

				public override bool CanBeNull()
				{
					return _expr1.CanBeNull();
				}

				protected override ICloneableElement Clone(Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
				{
					if (!doClone(this))
						return this;

					ICloneableElement clone;

					if (!objectTree.TryGetValue(this, out clone))
						objectTree.Add(this, clone = new Expr((ISqlExpression)_expr1.Clone(objectTree, doClone), _precedence));

					return clone;
				}

				public override QueryElementType ElementType
				{
					get { return QueryElementType.ExprPredicate; }
				}
			}

			public class NotExpr : Expr
			{
				public NotExpr(ISqlExpression exp1, bool isNot, int precedence)
					: base(exp1, precedence)
				{
					_isNot = isNot;
				}

				bool _isNot; public bool IsNot { get { return _isNot; } set { _isNot = value; } }

				protected override ICloneableElement Clone(Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
				{
					if (!doClone(this))
						return this;

					ICloneableElement clone;

					if (!objectTree.TryGetValue(this, out clone))
						objectTree.Add(this, clone = new NotExpr((ISqlExpression)Expr1.Clone(objectTree, doClone), _isNot, _precedence));

					return clone;
				}

				public override QueryElementType ElementType
				{
					get { return QueryElementType.NotExprPredicate; }
				}
			}

			// { expression { = | <> | != | > | >= | ! > | < | <= | !< } expression
			//
			public class ExprExpr : Expr
			{
				public ExprExpr(ISqlExpression exp1, Operator op, ISqlExpression exp2)
					: base(exp1, Sql.Precedence.Comparison)
				{
					_op    = op;
					_expr2 = exp2;
				}

				readonly Operator       _op;    public new Operator   Operator { get { return _op;    } }
				private  ISqlExpression _expr2; public ISqlExpression Expr2    { get { return _expr2; } }

				[Obsolete]
				protected override void Walk(bool skipColumns, WalkingFunc func)
				{
					base.Walk(skipColumns, func);
					_expr2 = _expr2.Walk(skipColumns, func);
				}

				public override bool CanBeNull()
				{
					return base.CanBeNull() || _expr2.CanBeNull();
				}

				protected override ICloneableElement Clone(Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
				{
					if (!doClone(this))
						return this;

					ICloneableElement clone;

					if (!objectTree.TryGetValue(this, out clone))
						objectTree.Add(this, clone = new ExprExpr(
							(ISqlExpression)Expr1.Clone(objectTree, doClone), _op, (ISqlExpression)_expr2.Clone(objectTree, doClone)));

					return clone;
				}

				public override string ToString()
				{
					string op;

					switch (_op)
					{
						case Operator.Equal         : op = "=";  break;
						case Operator.NotEqual      : op = "<>"; break;
						case Operator.Greater       : op = ">";  break;
						case Operator.GreaterOrEqual: op = ">="; break;
						case Operator.NotGreater    : op = "!>"; break;
						case Operator.Less          : op = "<";  break;
						case Operator.LessOrEqual   : op = "<="; break;
						case Operator.NotLess       : op = "!<"; break;
						default: throw new InvalidOperationException();
					}

					return Expr1 + " " + op + " " + Expr2;
				}

				public override QueryElementType ElementType
				{
					get { return QueryElementType.ExprExprPredicate; }
				}
			}

			// string_expression [ NOT ] LIKE string_expression [ ESCAPE 'escape_character' ]
			//
			public class Like : NotExpr
			{
				public Like(ISqlExpression exp1, bool isNot, ISqlExpression exp2, ISqlExpression escape)
					: base(exp1, isNot, Sql.Precedence.Comparison)
				{
					_expr2  = exp2;
					_escape = escape;
				}

				ISqlExpression _expr2;  public ISqlExpression Expr2  { get { return _expr2;  } }
				ISqlExpression _escape; public ISqlExpression Escape { get { return _escape; } }

				[Obsolete]
				protected override void Walk(bool skipColumns, WalkingFunc func)
				{
					base.Walk(skipColumns, func);
					_expr2 = _expr2.Walk(skipColumns, func);

					if (_escape != null)
						_escape = _escape.Walk(skipColumns, func);
				}

				protected override ICloneableElement Clone(Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
				{
					if (!doClone(this))
						return this;

					ICloneableElement clone;

					if (!objectTree.TryGetValue(this, out clone))
						objectTree.Add(this, clone = new Like(
							(ISqlExpression)Expr1.Clone(objectTree, doClone), IsNot, (ISqlExpression)_expr2.Clone(objectTree, doClone), _escape));

					return clone;
				}

				public override QueryElementType ElementType
				{
					get { return QueryElementType.LikePredicate; }
				}
			}

			// expression [ NOT ] BETWEEN expression AND expression
			//
			public class Between : NotExpr
			{
				public Between(ISqlExpression exp1, bool isNot, ISqlExpression exp2, ISqlExpression exp3)
					: base(exp1, isNot, Sql.Precedence.Comparison)
				{
					_expr2 = exp2;
					_expr3 = exp3;
				}

				ISqlExpression _expr2; public ISqlExpression Expr2 { get { return _expr2; } }
				ISqlExpression _expr3; public ISqlExpression Expr3 { get { return _expr3; } }

				[Obsolete]
				protected override void Walk(bool skipColumns, WalkingFunc func)
				{
					base.Walk(skipColumns, func);
					_expr2 = _expr2.Walk(skipColumns, func);
					_expr3 = _expr3.Walk(skipColumns, func);
				}

				protected override ICloneableElement Clone(Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
				{
					if (!doClone(this))
						return this;

					ICloneableElement clone;

					if (!objectTree.TryGetValue(this, out clone))
						objectTree.Add(this, clone = new Between(
							(ISqlExpression)Expr1.Clone(objectTree, doClone),
							IsNot,
							(ISqlExpression)_expr2.Clone(objectTree, doClone),
							(ISqlExpression)_expr3.Clone(objectTree, doClone)));

					return clone;
				}

				public override QueryElementType ElementType
				{
					get { return QueryElementType.BetweenPredicate; }
				}
			}

			// expression IS [ NOT ] NULL
			//
			public class IsNull : NotExpr
			{
				public IsNull(ISqlExpression exp1, bool isNot)
					: base(exp1, isNot, Sql.Precedence.Comparison)
				{
				}

				protected override ICloneableElement Clone(Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
				{
					if (!doClone(this))
						return this;

					ICloneableElement clone;

					if (!objectTree.TryGetValue(this, out clone))
						objectTree.Add(this, clone = new IsNull((ISqlExpression)Expr1.Clone(objectTree, doClone), IsNot));

					return clone;
				}

				public override string ToString()
				{
					return Expr1 + " IS " + (IsNot ? "NOT " : "") + "NULL";
				}

				public override QueryElementType ElementType
				{
					get { return QueryElementType.IsNullPredicate; }
				}
			}

			// expression [ NOT ] IN ( subquery | expression [ ,...n ] )
			//
			public class InSubquery : NotExpr
			{
				public InSubquery(ISqlExpression exp1, bool isNot, SqlQuery subQuery)
					: base(exp1, isNot, Sql.Precedence.Comparison)
				{
					_subQuery = subQuery;
				}

				SqlQuery _subQuery; public SqlQuery SubQuery { get { return _subQuery; } }

				[Obsolete]
				protected override void Walk(bool skipColumns, WalkingFunc func)
				{
					base.Walk(skipColumns, func);
					_subQuery = (SqlQuery)((ISqlExpression)_subQuery).Walk(skipColumns, func);
				}

				protected override ICloneableElement Clone(Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
				{
					if (!doClone(this))
						return this;

					ICloneableElement clone;

					if (!objectTree.TryGetValue(this, out clone))
						objectTree.Add(this, clone = new InSubquery(
							(ISqlExpression)Expr1.Clone(objectTree, doClone),
							IsNot,
							(SqlQuery)_subQuery.Clone(objectTree, doClone)));

					return clone;
				}

				public override QueryElementType ElementType
				{
					get { return QueryElementType.InSubqueryPredicate; }
				}
			}

			public class InList : NotExpr
			{
				public InList(ISqlExpression exp1, bool isNot, params ISqlExpression[] values)
					: base(exp1, isNot, Sql.Precedence.Comparison)
				{
					if (values != null && values.Length > 0)
						_values.AddRange(values);
				}

				public InList(ISqlExpression exp1, bool isNot, IEnumerable<ISqlExpression> values)
					: base(exp1, isNot, Sql.Precedence.Comparison)
				{
					if (values != null)
						_values.AddRange(values);
				}

				readonly List<ISqlExpression> _values = new List<ISqlExpression>();
				public   List<ISqlExpression>  Values { get { return _values; } }

				[Obsolete]
				protected override void Walk(bool skipColumns, WalkingFunc action)
				{
					base.Walk(skipColumns, action);
					for (int i = 0; i < _values.Count; i++)
						_values[i] = _values[i].Walk(skipColumns, action);
				}

				protected override ICloneableElement Clone(Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
				{
					if (!doClone(this))
						return this;

					ICloneableElement clone;

					if (!objectTree.TryGetValue(this, out clone))
					{
						objectTree.Add(this, clone = new InList(
							(ISqlExpression)Expr1.Clone(objectTree, doClone),
							IsNot,
							_values.ConvertAll<ISqlExpression>(delegate(ISqlExpression e) { return (ISqlExpression)e.Clone(objectTree, doClone); }).ToArray()));
					}

					return clone;
				}

				public override QueryElementType ElementType
				{
					get { return QueryElementType.InListPredicate; }
				}
			}

			// CONTAINS ( { column | * } , '< contains_search_condition >' )
			// FREETEXT ( { column | * } , 'freetext_string' )
			// expression { = | <> | != | > | >= | !> | < | <= | !< } { ALL | SOME | ANY } ( subquery )
			// EXISTS ( subquery )

			public class FuncLike : Predicate
			{
				public FuncLike(SqlFunction func)
					: base(func.Precedence)
				{
					_func = func;
				}

				SqlFunction _func; public SqlFunction Function { get { return _func; } }
		
				[Obsolete]
				protected override void Walk(bool skipColumns, WalkingFunc func)
				{
					_func = (SqlFunction)((ISqlExpression)_func).Walk(skipColumns, func);
				}

				public override bool CanBeNull()
				{
					return _func.CanBeNull();
				}

				protected override ICloneableElement Clone(Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
				{
					if (!doClone(this))
						return this;

					ICloneableElement clone;

					if (!objectTree.TryGetValue(this, out clone))
						objectTree.Add(this, clone = new FuncLike((SqlFunction)_func.Clone(objectTree, doClone)));

					return clone;
				}

				public override QueryElementType ElementType
				{
					get { return QueryElementType.FuncLikePredicate; }
				}
			}

			protected Predicate(int precedence)
			{
				_precedence = precedence;
			}

			#region IPredicate Members

			readonly int _precedence;
			public   int  Precedence
			{
				get { return _precedence;  }
			}

			public    abstract bool              CanBeNull();
			protected abstract ICloneableElement Clone    (Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone);
			[Obsolete]
			protected abstract void              Walk     (bool skipColumns, WalkingFunc action);

			[Obsolete]
			ISqlExpression ISqlExpressionWalkable.Walk(bool skipColumns, WalkingFunc func)
			{
				Walk(skipColumns, func);
				return null;
			}

			ICloneableElement ICloneableElement.Clone(Dictionary<ICloneableElement, ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
			{
				if (!doClone(this))
					return this;

				return Clone(objectTree, doClone);
			}

			#endregion

			#region IQueryElement Members

			public abstract QueryElementType ElementType { get; }

			#endregion
		}

		#endregion

		#region Condition

		public class Condition : IQueryElement, ICloneableElement
		{
			public Condition(bool isNot, ISqlPredicate predicate)
			{
				_isNot     = isNot;
				_predicate = predicate;
			}

			public Condition(bool isNot, ISqlPredicate predicate, bool isOr)
			{
				_isNot     = isNot;
				_predicate = predicate;
				_isOr      = isOr;
			}

			private bool          _isNot;     public bool          IsNot     { get { return _isNot;     } set { _isNot     = value; } }
			private ISqlPredicate _predicate; public ISqlPredicate Predicate { get { return _predicate; } set { _predicate = value; } }
			private bool          _isOr;      public bool          IsOr      { get { return _isOr;      } set { _isOr      = value; } }

			public int Precedence
			{
				get
				{
					return
						_isNot ? Sql.Precedence.LogicalNegation :
						_isOr  ? Sql.Precedence.LogicalDisjunction :
						         Sql.Precedence.LogicalConjunction;
				}
			}

			public ICloneableElement Clone(Dictionary<ICloneableElement, ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
			{
				if (!doClone(this))
					return this;

				ICloneableElement clone;

				if (!objectTree.TryGetValue(this, out clone))
					objectTree.Add(this, clone = new Condition(_isNot, (ISqlPredicate)_predicate.Clone(objectTree, doClone), _isOr));

				return clone;
			}

			public bool CanBeNull()
			{
				return Predicate.CanBeNull();
			}

			public override string ToString()
			{
				return "(" + (IsNot ? "NOT " : "") + Predicate + ")" + (IsOr ? " OR " : " AND ");
			}

			#region IQueryElement Members

			public QueryElementType ElementType
			{
				get { return QueryElementType.Condition; }
			}

			#endregion
		}

		#endregion

		#region SearchCondition

		public class SearchCondition : ConditionBase<SearchCondition, SearchCondition.Next>, ISqlPredicate, ISqlExpression
		{
			public SearchCondition()
			{
			}

			public SearchCondition(IEnumerable<Condition> list)
			{
				_conditions.AddRange(list);
			}

			public class Next
			{
				internal Next(SearchCondition parent)
				{
					_parent = parent;
				}

				readonly SearchCondition _parent;

				public SearchCondition Or  { get { return _parent.SetOr(true);  } }
				public SearchCondition And { get { return _parent.SetOr(false); } }

				public ISqlExpression  ToExpr() { return _parent; }
			}

			readonly List<Condition> _conditions = new List<Condition>();
			public   List<Condition>  Conditions
			{
				get { return _conditions; }
			}

			protected override SearchCondition Search
			{
				get { return this; }
			}

			protected override Next GetNext()
			{
				return new Next(this);
			}

			#region Overrides

			public override string ToString()
			{
				StringBuilder sb = new StringBuilder();

				foreach (Condition c in Conditions)
					sb.Append(c);

				if (Conditions.Count > 0)
					sb.Remove(sb.Length - 4, 4);

				return sb.ToString();
			}

			#endregion

			#region IPredicate Members

			public int Precedence
			{
				get
				{
					if (_conditions.Count == 0) return Sql.Precedence.Unknown;
					if (_conditions.Count == 1) return _conditions[0].Precedence;

					int precedence = Sql.Precedence.Primary;

					foreach (Condition condition in _conditions)
						precedence = Math.Min(precedence, condition.Precedence);

					return precedence;
				}
			}

			public Type SystemType
			{
				get { return typeof(bool); }
			}

			[Obsolete]
			ISqlExpression ISqlExpressionWalkable.Walk(bool skipColumns, WalkingFunc func)
			{
				foreach (Condition condition in Conditions)
					condition.Predicate.Walk(skipColumns, func);

				return func(this);
			}

			#endregion

			#region IEquatable<ISqlExpression> Members

			bool IEquatable<ISqlExpression>.Equals(ISqlExpression other)
			{
				return this == other;
			}

			#endregion

			#region ISqlExpression Members

			public bool CanBeNull()
			{
				foreach (Condition c in Conditions)
					if (c.CanBeNull())
						return true;

				return false;
			}

			#endregion

			#region ICloneableElement Members

			public ICloneableElement Clone(Dictionary<ICloneableElement, ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
			{
				if (!doClone(this))
					return this;

				ICloneableElement clone;

				if (!objectTree.TryGetValue(this, out clone))
				{
					SearchCondition sc = new SearchCondition();

					objectTree.Add(this, clone = sc);

					sc._conditions.AddRange(_conditions.ConvertAll<Condition>(delegate(Condition c) { return (Condition)c.Clone(objectTree, doClone); }));
				}

				return clone;
			}

			#endregion

			#region IQueryElement Members

			public QueryElementType ElementType { get { return QueryElementType.SearchCondition; } }

			#endregion
		}

		#endregion

		#region ConditionBase

		interface IConditionExpr<T>
		{
			T Expr    (ISqlExpression expr);
			T Field   (SqlField          field);
			T SubQuery(SqlQuery     sqlQuery);
			T Value   (object         value);
		}

		public abstract class ConditionBase<T1,T2> : IConditionExpr<ConditionBase<T1,T2>.Expr_>
			where T1 : ConditionBase<T1,T2>
		{
			public class Expr_
			{
				internal Expr_(ConditionBase<T1,T2> condition, bool isNot, ISqlExpression expr)
				{
					_condition = condition;
					_isNot     = isNot;
					_expr      = expr;
				}

				readonly ConditionBase<T1,T2> _condition;
				readonly bool                 _isNot;
				readonly ISqlExpression       _expr;

				T2 Add(ISqlPredicate predicate)
				{
					_condition.Search.Conditions.Add(new Condition(_isNot, predicate));
					return _condition.GetNext();
				}

				#region Predicate.ExprExpr

				public class Op_ : IConditionExpr<T2>
				{
					internal Op_(Expr_ expr, Predicate.Operator op) 
					{
						_expr = expr;
						_op   = op;
					}

					readonly Expr_              _expr;
					readonly Predicate.Operator _op;

					public T2 Expr    (ISqlExpression expr) { return _expr.Add(new Predicate.ExprExpr(_expr._expr, _op, expr)); }
					public T2 Field   (SqlField      field) { return Expr(field);               }
					public T2 SubQuery(SqlQuery   subQuery) { return Expr(subQuery);            }
					public T2 Value   (object        value) { return Expr(new SqlValue(value)); }

					public T2 All     (SqlQuery   subQuery) { return Expr(SqlFunction.CreateAll (subQuery)); }
					public T2 Some    (SqlQuery   subQuery) { return Expr(SqlFunction.CreateSome(subQuery)); }
					public T2 Any     (SqlQuery   subQuery) { return Expr(SqlFunction.CreateAny (subQuery)); }
				}

				public Op_ Equal          { get { return new Op_(this, Predicate.Operator.Equal);          } }
				public Op_ NotEqual       { get { return new Op_(this, Predicate.Operator.NotEqual);       } }
				public Op_ Greater        { get { return new Op_(this, Predicate.Operator.Greater);        } }
				public Op_ GreaterOrEqual { get { return new Op_(this, Predicate.Operator.GreaterOrEqual); } }
				public Op_ NotGreater     { get { return new Op_(this, Predicate.Operator.NotGreater);     } }
				public Op_ Less           { get { return new Op_(this, Predicate.Operator.Less);           } }
				public Op_ LessOrEqual    { get { return new Op_(this, Predicate.Operator.LessOrEqual);    } }
				public Op_ NotLess        { get { return new Op_(this, Predicate.Operator.NotLess);        } }

				#endregion

				#region Predicate.Like

				public T2 Like(ISqlExpression expression, SqlValue escape) { return Add(new Predicate.Like(_expr, false, expression, escape)); }
				public T2 Like(ISqlExpression expression)                  { return Like(expression, null); }
				public T2 Like(string expression,         SqlValue escape) { return Like(new SqlValue(expression), escape); }
				public T2 Like(string expression)                          { return Like(new SqlValue(expression), null);   }

				#endregion

				#region Predicate.Between

				public T2 Between   (ISqlExpression expr1, ISqlExpression expr2) { return Add(new Predicate.Between(_expr, false, expr1, expr2)); }
				public T2 NotBetween(ISqlExpression expr1, ISqlExpression expr2) { return Add(new Predicate.Between(_expr, true,  expr1, expr2)); }

				#endregion

				#region Predicate.IsNull

				public T2 IsNull    { get { return Add(new Predicate.IsNull(_expr, false)); } }
				public T2 IsNotNull { get { return Add(new Predicate.IsNull(_expr, true));  } }

				#endregion

				#region Predicate.In

				public T2 In   (SqlQuery subQuery) { return Add(new Predicate.InSubquery(_expr, false, subQuery)); }
				public T2 NotIn(SqlQuery subQuery) { return Add(new Predicate.InSubquery(_expr, true,  subQuery)); }

				Predicate.InList CreateInList(bool isNot, object[] exprs)
				{
					Predicate.InList list = new Predicate.InList(_expr, isNot, null);

					if (exprs != null && exprs.Length > 0)
					{
						foreach (object item in exprs)
						{
							if (item == null || item is SqlValue && ((SqlValue)item).Value == null)
								continue;

							if (item is ISqlExpression)
								list.Values.Add((ISqlExpression)item);
							else
								list.Values.Add(new SqlValue(item));
						}
					}

					return list;
				}

				public T2 In   (params object[] exprs) { return Add(CreateInList(false, exprs)); }
				public T2 NotIn(params object[] exprs) { return Add(CreateInList(true,  exprs)); }

				#endregion
			}

			public class Not_ : IConditionExpr<Expr_>
			{
				internal Not_(ConditionBase<T1,T2> condition)
				{
					_condition = condition;
				}

				readonly ConditionBase<T1,T2> _condition;

				public Expr_ Expr    (ISqlExpression expr)     { return new Expr_(_condition, true, expr); }
				public Expr_ Field   (SqlField       field)    { return Expr(field);               }
				public Expr_ SubQuery(SqlQuery     subQuery) { return Expr(subQuery);            }
				public Expr_ Value   (object         value)    { return Expr(new SqlValue(value)); }

				public T2 Exists(SqlQuery subQuery)
				{
					_condition.Search.Conditions.Add(new Condition(true, new Predicate.FuncLike(SqlFunction.CreateExists(subQuery))));
					return _condition.GetNext();
				}
			}

			protected abstract SearchCondition Search { get; }
			protected abstract T2              GetNext();

			protected T1 SetOr(bool value)
			{
				Search.Conditions[Search.Conditions.Count - 1].IsOr = value;
				return (T1)this;
			}

			public Not_  Not { get { return new Not_(this); } }

			public Expr_ Expr    (ISqlExpression expr)   { return new Expr_(this, false, expr); }
			public Expr_ Field   (SqlField       field)  { return Expr(field);                  }
			public Expr_ SubQuery(SqlQuery     subQuery) { return Expr(subQuery);               }
			public Expr_ Value   (object         value)  { return Expr(new SqlValue(value));    }

			public T2 Exists(SqlQuery subQuery)
			{
				Search.Conditions.Add(new Condition(false, new Predicate.FuncLike(SqlFunction.CreateExists(subQuery))));
				return GetNext();
			}
		}

		#endregion

		#region OrderByItem

		public class OrderByItem : IQueryElement, ICloneableElement
		{
			public OrderByItem(ISqlExpression expression, bool isDescending)
			{
				_expression   = expression;
				_isDescending = isDescending;
			}

			private  ISqlExpression _expression;   public ISqlExpression Expression   { get { return _expression;   } }
			readonly bool           _isDescending; public bool           IsDescending { get { return _isDescending; } }

			[Obsolete]
			internal void Walk(bool skipColumns, WalkingFunc func)
			{
				_expression = _expression.Walk(skipColumns, func);
			}

			public ICloneableElement Clone(Dictionary<ICloneableElement, ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
			{
				if (!doClone(this))
					return this;

				ICloneableElement clone;

				if (!objectTree.TryGetValue(this, out clone))
					objectTree.Add(this, clone = new OrderByItem((ISqlExpression)_expression.Clone(objectTree, doClone), _isDescending));

				return clone;
			}

			#region IQueryElement Members

			public QueryElementType ElementType
			{
				get { return QueryElementType.OrderByItem; }
			}

			#endregion
		}

		#endregion

		#region ClauseBase

		public abstract class ClauseBase
		{
			protected ClauseBase(SqlQuery sqlQuery)
			{
				_sqlQuery = sqlQuery;
			}

			public SelectClause  Select  { get { return SqlQuery.Select;  } }
			public FromClause    From    { get { return SqlQuery.From;    } }
			public WhereClause   Where   { get { return SqlQuery.Where;   } }
			public GroupByClause GroupBy { get { return SqlQuery.GroupBy; } }
			public WhereClause   Having  { get { return SqlQuery.Having;  } }
			public OrderByClause OrderBy { get { return SqlQuery.OrderBy; } }
			public SqlQuery      End()   { return SqlQuery; }

			private   SqlQuery _sqlQuery;
			protected SqlQuery  SqlQuery
			{
				get { return _sqlQuery;  }
			}

			internal void SetSqlQuery(SqlQuery sqlQuery)
			{
				_sqlQuery = sqlQuery;
			}
		}

		public abstract class ClauseBase<T1, T2> : ConditionBase<T1, T2>
			where T1 : ClauseBase<T1, T2>
		{
			protected ClauseBase(SqlQuery sqlQuery)
			{
				_sqlQuery = sqlQuery;
			}

			public SelectClause  Select  { get { return SqlQuery.Select;  } }
			public FromClause    From    { get { return SqlQuery.From;    } }
			public GroupByClause GroupBy { get { return SqlQuery.GroupBy; } }
			public WhereClause   Having  { get { return SqlQuery.Having;  } }
			public OrderByClause OrderBy { get { return SqlQuery.OrderBy; } }
			public SqlQuery      End()   { return SqlQuery; }

			private   SqlQuery _sqlQuery;
			protected SqlQuery  SqlQuery
			{
				get { return _sqlQuery; }
			}

			internal void SetSqlQuery(SqlQuery sqlQuery)
			{
				_sqlQuery = sqlQuery;
			}
		}

		#endregion

		#region SelectClause

		public class SelectClause : ClauseBase, IQueryElement, ISqlExpressionWalkable
		{
			#region Init

			internal SelectClause(SqlQuery sqlQuery) : base(sqlQuery)
			{
			}

			internal SelectClause(
				SqlQuery   sqlQuery,
				SelectClause clone,
				Dictionary<ICloneableElement,ICloneableElement> objectTree,
				Predicate<ICloneableElement> doClone)
				: base(sqlQuery)
			{
				_columns.AddRange(clone._columns.ConvertAll<Column>(delegate(Column c) { return (Column)c.Clone(objectTree, doClone); }));
				_isDistinct = clone._isDistinct;
				_takeValue  = clone._takeValue == null ? null : (ISqlExpression)clone._takeValue.Clone(objectTree, doClone);
				_skipValue  = clone._skipValue == null ? null : (ISqlExpression)clone._skipValue.Clone(objectTree, doClone);
			}

			internal SelectClause(bool isDistinct, ISqlExpression takeValue, ISqlExpression skipValue, IEnumerable<Column> columns)
				: base(null)
			{
				_isDistinct = isDistinct;
				_takeValue  = takeValue;
				_skipValue  = skipValue;
				_columns.AddRange(columns);
			}

			#endregion

			#region Columns

			public SelectClause Field(SqlField field)
			{
				AddOrGetColumn(new Column(SqlQuery, field));
				return this;
			}

			public SelectClause Field(SqlField field, string alias)
			{
				AddOrGetColumn(new Column(SqlQuery, field, alias));
				return this;
			}

			public SelectClause SubQuery(SqlQuery subQuery)
			{
				if (subQuery.ParentSql != null && subQuery.ParentSql != SqlQuery)
					throw new ArgumentException("SqlQuery already used as subquery");

				subQuery.ParentSql = SqlQuery;

				AddOrGetColumn(new Column(SqlQuery, subQuery));
				return this;
			}

			public SelectClause SubQuery(SqlQuery sqlQuery, string alias)
			{
				if (sqlQuery.ParentSql != null && sqlQuery.ParentSql != SqlQuery)
					throw new ArgumentException("SqlQuery already used as subquery");

				sqlQuery.ParentSql = SqlQuery;

				AddOrGetColumn(new Column(SqlQuery, sqlQuery, alias));
				return this;
			}

			public SelectClause Expr(ISqlExpression expr)
			{
				AddOrGetColumn(new Column(SqlQuery, expr));
				return this;
			}

			public SelectClause Expr(ISqlExpression expr, string alias)
			{
				AddOrGetColumn(new Column(SqlQuery, expr, alias));
				return this;
			}

			public SelectClause Expr(string expr, params ISqlExpression[] values)
			{
				AddOrGetColumn(new Column(SqlQuery, new SqlExpression(null, expr, values)));
				return this;
			}

			public SelectClause Expr(Type systemType, string expr, params ISqlExpression[] values)
			{
				AddOrGetColumn(new Column(SqlQuery, new SqlExpression(systemType, expr, values)));
				return this;
			}

			public SelectClause Expr(string expr, int priority, params ISqlExpression[] values)
			{
				AddOrGetColumn(new Column(SqlQuery, new SqlExpression(null, expr, priority, values)));
				return this;
			}

			public SelectClause Expr(Type systemType, string expr, int priority, params ISqlExpression[] values)
			{
				AddOrGetColumn(new Column(SqlQuery, new SqlExpression(systemType, expr, priority, values)));
				return this;
			}

			public SelectClause Expr(string alias, string expr, int priority, params ISqlExpression[] values)
			{
				AddOrGetColumn(new Column(SqlQuery, new SqlExpression(null, expr, priority, values)));
				return this;
			}

			public SelectClause Expr(Type systemType, string alias, string expr, int priority, params ISqlExpression[] values)
			{
				AddOrGetColumn(new Column(SqlQuery, new SqlExpression(systemType, expr, priority, values)));
				return this;
			}

			public SelectClause Expr<T>(ISqlExpression expr1, string operation, ISqlExpression expr2)
			{
				AddOrGetColumn(new Column(SqlQuery, new SqlBinaryExpression(typeof(T), expr1, operation, expr2)));
				return this;
			}

			public SelectClause Expr<T>(ISqlExpression expr1, string operation, ISqlExpression expr2, int priority)
			{
				AddOrGetColumn(new Column(SqlQuery, new SqlBinaryExpression(typeof(T), expr1, operation, expr2, priority)));
				return this;
			}

			public SelectClause Expr<T>(string alias, ISqlExpression expr1, string operation, ISqlExpression expr2, int priority)
			{
				AddOrGetColumn(new Column(SqlQuery, new SqlBinaryExpression(typeof(T), expr1, operation, expr2, priority), alias));
				return this;
			}

			public int Add(ISqlExpression expr)
			{
				return Columns.IndexOf(AddOrGetColumn(new Column(SqlQuery, expr)));
			}

			public int Add(ISqlExpression expr, string alias)
			{
				return Columns.IndexOf(AddOrGetColumn(new Column(SqlQuery, expr, alias)));
			}

			Column AddOrGetColumn(Column col)
			{
				foreach (Column c in Columns)
					if (c.Equals(col))
						return col;

				Columns.Add(col);

				return col;
			}

			readonly List<Column> _columns = new List<Column>();
			public   List<Column>  Columns
			{
				get { return _columns; }
			}

			#endregion

			#region HasModifier

			public bool HasModifier
			{
				get { return IsDistinct || SkipValue != null || TakeValue != null; }
			}

			#endregion

			#region Distinct

			public SelectClause Distinct
			{
				get { _isDistinct = true; return this; }
			}

			private bool _isDistinct;
			public  bool  IsDistinct { get { return _isDistinct; } set { _isDistinct = value; } }

			#endregion

			#region Take

			public SelectClause Take(int value)
			{
				_takeValue = new SqlValue(value);
				return this;
			}

			public SelectClause Take(ISqlExpression value)
			{
				_takeValue = value;
				return this;
			}

			private ISqlExpression _takeValue;
			public  ISqlExpression  TakeValue { get { return _takeValue; } set { _takeValue = value; } }

			#endregion

			#region Skip

			public SelectClause Skip(int value)
			{
				_skipValue = new SqlValue(value);
				return this;
			}

			public SelectClause Skip(ISqlExpression value)
			{
				_skipValue = value;
				return this;
			}

			private ISqlExpression _skipValue;
			public  ISqlExpression  SkipValue { get { return _skipValue; } set { _skipValue = value; } }

			#endregion

			#region Overrides

			public override string ToString()
			{
				StringBuilder sb = new StringBuilder("SELECT \n");

				if (Columns.Count == 0)
					sb.Append("\t*, \n");
				else
					foreach (Column c in Columns)
						sb.Append("\t").Append(c.ToString()).Append(", \n");

				return sb.Remove(sb.Length - 3, 3).ToString();
			}

			#endregion

			#region ISqlExpressionWalkable Members

			[Obsolete]
			ISqlExpression ISqlExpressionWalkable.Walk(bool skipColumns, WalkingFunc func)
			{
				for (int i = 0; i < Columns.Count; i++)
				{
					Column         col  = Columns[i];
					ISqlExpression expr = col.Walk(skipColumns, func);

					if (expr is Column)
						Columns[i] = (Column)expr;
					else
						Columns[i] = new Column(col.Parent, expr, col.Alias);
				}

				if (TakeValue != null) TakeValue = TakeValue.Walk(skipColumns, func);
				if (SkipValue != null) SkipValue = SkipValue.Walk(skipColumns, func);

				return null;
			}

			#endregion

			#region IQueryElement Members

			public QueryElementType ElementType
			{
				get { return QueryElementType.SelectClause; }
			}

			#endregion
		}

		private SelectClause _select;
		public  SelectClause  Select
		{
			get { return _select; }
		}

		#endregion

		#region FromClause

		public class FromClause : ClauseBase, IQueryElement, ISqlExpressionWalkable
		{
			#region Join

			public class Join : ConditionBase<Join,Join.Next>
			{
				public class Next
				{
					internal Next(Join parent)
					{
						_parent = parent;
					}

					readonly Join _parent;

					public Join Or  { get { return _parent.SetOr(true);  } }
					public Join And { get { return _parent.SetOr(false); } }

					public static implicit operator Join(Next next)
					{
						return next._parent;
					}
				}

				protected override SearchCondition Search
				{
					get { return _joinedTable.Condition; }
				}

				protected override Next GetNext()
				{
					return new Next(this);
				}

				internal Join(JoinType joinType, ISqlTableSource table, string alias, bool isWeak, ICollection<Join> joins)
				{
					_joinedTable = new JoinedTable(joinType, table, alias, isWeak);

					if (joins != null && joins.Count > 0)
						foreach (Join join in joins)
							_joinedTable.Table.Joins.Add(join._joinedTable);
				}

				readonly JoinedTable _joinedTable;
				internal JoinedTable  JoinedTable
				{
					get { return _joinedTable; }
				}
			}

			#endregion

			internal FromClause(SqlQuery sqlQuery) : base(sqlQuery)
			{
			}

			internal FromClause(
				SqlQuery sqlQuery,
				FromClause clone,
				Dictionary<ICloneableElement,ICloneableElement> objectTree,
				Predicate<ICloneableElement> doClone)
				: base(sqlQuery)
			{
				_tables.AddRange(clone._tables.ConvertAll<TableSource>(delegate(TableSource ts) { return (TableSource)ts.Clone(objectTree, doClone); }));
			}

			internal FromClause(List<TableSource> tables)
				: base(null)
			{
				_tables.AddRange(tables);
			}

			public FromClause Table(ISqlTableSource table, params FJoin[] joins)
			{
				return Table(table, null, joins);
			}

			public FromClause Table(ISqlTableSource table, string alias, params FJoin[] joins)
			{
				TableSource ts = AddOrGetTable(table, alias);

				if (joins != null && joins.Length > 0)
					foreach (Join join in joins)
						ts.Joins.Add(join.JoinedTable);

				return this;
			}

			TableSource GetTable(ISqlTableSource table, string alias)
			{
				foreach (TableSource ts in Tables)
					if (ts.Source == table)
						if (alias == null || ts.Alias == alias)
							return ts;
						else
							throw new ArgumentException("alias");

				return null;
			}

			TableSource AddOrGetTable(ISqlTableSource table, string alias)
			{
				TableSource ts = GetTable(table, alias);

				if (ts != null)
					return ts;

				TableSource t = new TableSource(table, alias);

				Tables.Add(t);

				return t;
			}

			public TableSource this[ISqlTableSource table]
			{
				get { return this[table, null]; }
			}

			public TableSource this[ISqlTableSource table, string alias]
			{
				get
				{
					foreach (TableSource ts in Tables)
					{
						TableSource t = CheckTableSource(ts, table, alias);

						if (t != null)
							return t;
					}

					return null;
				}
			}

			readonly List<TableSource> _tables = new List<TableSource>();
			public   List<TableSource>  Tables
			{
				get { return _tables; }
			}

			#region Overrides

			public override string ToString()
			{
				StringBuilder sb = new StringBuilder(" \nFROM \n");

				if (Tables.Count > 0)
				{
					foreach (TableSource ts in Tables)
						sb.Append('\t').Append(ts.ToString().Replace("\n", "\n\t")).Append(", ");

					sb.Remove(sb.Length - 2, 2);
				}

				return sb.ToString();
			}

			#endregion

			#region ISqlExpressionWalkable Members

			[Obsolete]
			ISqlExpression ISqlExpressionWalkable.Walk(bool skipColumns, WalkingFunc func)
			{
				for (int i = 0; i <	Tables.Count; i++)
					((ISqlExpressionWalkable)Tables[i]).Walk(skipColumns, func);

				return null;
			}

			#endregion

			#region IQueryElement Members

			public QueryElementType ElementType
			{
				get { return QueryElementType.FromClause; }
			}

			#endregion
		}

		public static FJoin InnerJoin    (ISqlTableSource table,               params FJoin[] joins) { return new FJoin(JoinType.Inner, table, null,  false, joins); }
		public static FJoin InnerJoin    (ISqlTableSource table, string alias, params FJoin[] joins) { return new FJoin(JoinType.Inner, table, alias, false, joins); }
		public static FJoin LeftJoin     (ISqlTableSource table,               params FJoin[] joins) { return new FJoin(JoinType.Left,  table, null,  false, joins); }
		public static FJoin LeftJoin     (ISqlTableSource table, string alias, params FJoin[] joins) { return new FJoin(JoinType.Left,  table, alias, false, joins); }
		public static FJoin Join         (ISqlTableSource table,               params FJoin[] joins) { return new FJoin(JoinType.Auto,  table, null,  false, joins); }
		public static FJoin Join         (ISqlTableSource table, string alias, params FJoin[] joins) { return new FJoin(JoinType.Auto,  table, alias, false, joins); }

		public static FJoin WeakInnerJoin(ISqlTableSource table,               params FJoin[] joins) { return new FJoin(JoinType.Inner, table, null,  true,  joins); }
		public static FJoin WeakInnerJoin(ISqlTableSource table, string alias, params FJoin[] joins) { return new FJoin(JoinType.Inner, table, alias, true,  joins); }
		public static FJoin WeakLeftJoin (ISqlTableSource table,               params FJoin[] joins) { return new FJoin(JoinType.Left,  table, null,  true,  joins); }
		public static FJoin WeakLeftJoin (ISqlTableSource table, string alias, params FJoin[] joins) { return new FJoin(JoinType.Left,  table, alias, true,  joins); }
		public static FJoin WeakJoin     (ISqlTableSource table,               params FJoin[] joins) { return new FJoin(JoinType.Auto,  table, null,  true,  joins); }
		public static FJoin WeakJoin     (ISqlTableSource table, string alias, params FJoin[] joins) { return new FJoin(JoinType.Auto,  table, alias, true,  joins); }

		private FromClause _from;
		public  FromClause  From
		{
			get { return _from; }
		}

		#endregion

		#region WhereClause

		public class WhereClause : ClauseBase<WhereClause,WhereClause.Next>, IQueryElement, ISqlExpressionWalkable
		{
			public class Next : ClauseBase
			{
				internal Next(WhereClause parent) : base(parent.SqlQuery)
				{
					_parent = parent;
				}

				readonly WhereClause _parent;

				public WhereClause Or  { get { return _parent.SetOr(true);  } }
				public WhereClause And { get { return _parent.SetOr(false); } }
			}

			internal WhereClause(SqlQuery sqlQuery) : base(sqlQuery)
			{
				_searchCondition = new SearchCondition();
			}

			internal WhereClause(
				SqlQuery sqlQuery,
				WhereClause clone,
				Dictionary<ICloneableElement,ICloneableElement> objectTree,
				Predicate<ICloneableElement> doClone)
				: base(sqlQuery)
			{
				_searchCondition = (SearchCondition)clone._searchCondition.Clone(objectTree, doClone);
			}

			internal WhereClause(SearchCondition searchCondition) : base(null)
			{
				_searchCondition = searchCondition;
			}

			private SearchCondition _searchCondition;
			public  SearchCondition  SearchCondition
			{
				get { return _searchCondition;  }
			}

			public bool IsEmpty
			{
				get { return SearchCondition.Conditions.Count == 0; }
			}

			protected override SearchCondition Search
			{
				get { return _searchCondition; }
			}

			protected override Next GetNext()
			{
				return new Next(this);
			}

			public override string ToString()
			{
				return Search.Conditions.Count == 0 ? "" : "\nWHERE\n\t" + Search;
			}

			#region ISqlExpressionWalkable Members

			[Obsolete]
			ISqlExpression ISqlExpressionWalkable.Walk(bool skipColumns, WalkingFunc action)
			{
				_searchCondition = (SearchCondition)((ISqlExpressionWalkable)_searchCondition).Walk(skipColumns, action);
				return null;
			}

			#endregion

			#region IQueryElement Members

			public QueryElementType ElementType
			{
				get { return QueryElementType.WhereClause; }
			}

			#endregion
		}

		private WhereClause _where;
		public  WhereClause  Where
		{
			get { return _where; }
		}

		#endregion

		#region GroupByClause

		public class GroupByClause : ClauseBase, IQueryElement, ISqlExpressionWalkable
		{
			internal GroupByClause(SqlQuery sqlQuery) : base(sqlQuery)
			{
			}

			internal GroupByClause(
				SqlQuery    sqlQuery,
				GroupByClause clone,
				Dictionary<ICloneableElement,ICloneableElement> objectTree,
				Predicate<ICloneableElement> doClone)
				: base(sqlQuery)
			{
				_items.AddRange(clone._items.ConvertAll<ISqlExpression>(delegate(ISqlExpression e) { return (ISqlExpression)e.Clone(objectTree, doClone); }));
			}

			internal GroupByClause(IEnumerable<ISqlExpression> items) : base(null)
			{
				_items.AddRange(items);
			}

			public GroupByClause Expr(ISqlExpression expr)
			{
				Add(expr);
				return this;
			}

			public GroupByClause Field(SqlField field)
			{
				return Expr(field);
			}

			void Add(ISqlExpression expr)
			{
				foreach (ISqlExpression e in Items)
					if (e.Equals(expr))
						return;

				Items.Add(expr);
			}

			readonly List<ISqlExpression> _items = new List<ISqlExpression>();
			public   List<ISqlExpression>  Items
			{
				get { return _items; }
			}

			public bool IsEmpty
			{
				get { return Items.Count == 0; }
			}

			public override string ToString()
			{
				if (Items.Count == 0)
					return "";

				StringBuilder sb = new StringBuilder(" \nGROUP BY \n");

				foreach (ISqlExpression item in Items)
					sb.Append('\t').Append(item.ToString()).Append(",");

				return sb.Remove(sb.Length - 1, 1).ToString();
			}

			#region ISqlExpressionWalkable Members

			[Obsolete]
			ISqlExpression ISqlExpressionWalkable.Walk(bool skipColumns, WalkingFunc func)
			{
				for (int i = 0; i < Items.Count; i++)
					Items[i] = Items[i].Walk(skipColumns, func);

				return null;
			}

			#endregion

			#region IQueryElement Members

			public QueryElementType ElementType
			{
				get { return QueryElementType.GroupByClause; }
			}

			#endregion
		}

		private GroupByClause _groupBy;
		public  GroupByClause  GroupBy
		{
			get { return _groupBy; }
		}

		#endregion

		#region HavingClause

		private WhereClause _having;
		public  WhereClause  Having
		{
			get { return _having; }
		}

		#endregion

		#region OrderByClause

		public class OrderByClause : ClauseBase, IQueryElement, ISqlExpressionWalkable
		{
			internal OrderByClause(SqlQuery sqlQuery) : base(sqlQuery)
			{
			}

			internal OrderByClause(
				SqlQuery    sqlQuery,
				OrderByClause clone,
				Dictionary<ICloneableElement,ICloneableElement> objectTree,
				Predicate<ICloneableElement> doClone)
				: base(sqlQuery)
			{
				_items.AddRange(clone._items.ConvertAll<OrderByItem>(delegate(OrderByItem item) { return (OrderByItem)item.Clone(objectTree, doClone); }));
			}

			internal OrderByClause(IEnumerable<OrderByItem> items) : base(null)
			{
				_items.AddRange(items);
			}

			public OrderByClause Expr(ISqlExpression expr, bool isDescending)
			{
				Add(expr, isDescending);
				return this;
			}

			public OrderByClause Expr     (ISqlExpression expr)               { return Expr(expr,  false);        }
			public OrderByClause ExprAsc  (ISqlExpression expr)               { return Expr(expr,  false);        }
			public OrderByClause ExprDesc (ISqlExpression expr)               { return Expr(expr,  true);         }
			public OrderByClause Field    (SqlField field, bool isDescending) { return Expr(field, isDescending); }
			public OrderByClause Field    (SqlField field)                    { return Expr(field, false);        }
			public OrderByClause FieldAsc (SqlField field)                    { return Expr(field, false);        }
			public OrderByClause FieldDesc(SqlField field)                    { return Expr(field, true);         }

			void Add(ISqlExpression expr, bool isDescending)
			{
				foreach (OrderByItem item in Items)
					if (item.Expression.Equals(expr))
						return;

				Items.Add(new OrderByItem(expr, isDescending));
			}

			readonly List<OrderByItem> _items = new List<OrderByItem>();
			public   List<OrderByItem>  Items
			{
				get { return _items; }
			}

			public bool IsEmpty
			{
				get { return Items.Count == 0; }
			}

			public override string ToString()
			{
				if (Items.Count == 0)
					return "";

				return base.ToString();
			}

			#region ISqlExpressionWalkable Members

			[Obsolete]
			ISqlExpression ISqlExpressionWalkable.Walk(bool skipColumns, WalkingFunc func)
			{
				for (int i = 0; i < Items.Count; i++)
					Items[i].Walk(skipColumns, func);

				return null;
			}

			#endregion

			#region IQueryElement Members

			public QueryElementType ElementType
			{
				get { return QueryElementType.OrderByClause; }
			}

			#endregion
		}

		private OrderByClause _orderBy;
		public  OrderByClause  OrderBy
		{
			get { return _orderBy; }
		}

		#endregion

		#region Union

		public class Union : IQueryElement
		{
			public Union()
			{
			}

			public Union(SqlQuery sqlQuery, bool isAll)
			{
				_sqlQuery = sqlQuery;
				_isAll    = isAll;
			}

			private SqlQuery _sqlQuery; public SqlQuery SqlQuery { get { return _sqlQuery; } }
			private bool     _isAll;    public bool     IsAll    { get { return _isAll;    } }

			public QueryElementType ElementType
			{
				get { return QueryElementType.Union; }
			}

			public override string ToString()
			{
				return " \nUNION" + (IsAll ? " ALL" : "") + " \n" + SqlQuery;
			}
		}

		private List<Union> _unions;
		public  List<Union>  Unions
		{
			get
			{
				if (_unions == null)
					_unions = new List<Union>();
				return _unions;
			}
		}

		public bool HasUnion { get { return _unions != null && _unions.Count > 0; } }

		public void AddUnion(SqlQuery union, bool isAll)
		{
			Unions.Add(new Union(union, isAll));
		}

		#endregion

		#region FinalizeAndValidate

		public void FinalizeAndValidate()
		{
			OptimizeUnions();
			FinalizeAndValidateInternal();
			SetAliases();
		}

		void OptimizeUnions()
		{
			Dictionary<ISqlExpression,ISqlExpression> exprs = new Dictionary<ISqlExpression,ISqlExpression>();

			new QueryVisitor().Visit(this, delegate(IQueryElement e)
			{
				SqlQuery sql = e as SqlQuery;

				if (sql == null || sql.From.Tables.Count != 1 || !sql.IsSimple)
					return;

				TableSource table = sql.From.Tables[0];

				if (table.Joins.Count != 0 || !(table.Source is SqlQuery))
					return;

				SqlQuery union = (SqlQuery)table.Source;

				if (!union.HasUnion)
					return;

				exprs.Add(sql, union);

				for (int i = 0; i < sql.Select.Columns.Count; i++)
				{
					Column scol = sql.  Select.Columns[i];
					Column ucol = union.Select.Columns[i];

					scol.Expression = ucol.Expression;
					scol._alias     = ucol._alias;

					exprs.Add(ucol, scol);
				}

				sql.From.Tables.Clear();
				sql.From.Tables.AddRange(union.From.Tables);

				sql.Where.  SearchCondition.Conditions.AddRange(union.Where. SearchCondition.Conditions);
				sql.Having. SearchCondition.Conditions.AddRange(union.Having.SearchCondition.Conditions);
				sql.GroupBy.Items.                     AddRange(union.GroupBy.Items);
				sql.OrderBy.Items.                     AddRange(union.OrderBy.Items);
				sql.Unions.InsertRange(0, union.Unions);
			});

			((ISqlExpressionWalkable)this).Walk(false, delegate(ISqlExpression expr)
			{
				ISqlExpression e;
				return exprs.TryGetValue(expr, out e)? e: expr;
			});
		}

		void FinalizeAndValidateInternal()
		{
			OptimizeSearchCondition(Where. SearchCondition);
			OptimizeSearchCondition(Having.SearchCondition);

			ForEachTable(delegate(TableSource table)
			{
				foreach (JoinedTable join in table.Joins)
					OptimizeSearchCondition(join.Condition);
			});

			new QueryVisitor().Visit(this, delegate(IQueryElement e)
			{
				SqlQuery sql = e as SqlQuery;

				if (sql != null && sql != this)
				{
					sql.FinalizeAndValidateInternal();

					if (sql.ParameterDependent)
						ParameterDependent = true;
				}
			});

			ResolveWeakJoins();
			OptimizeSubQueries();

			new QueryVisitor().Visit(this, delegate(IQueryElement e)
			{
				SqlQuery sql = e as SqlQuery;

				if (sql != null && sql != this)
					sql.RemoveOrderBy();
			});
		}

		internal static void OptimizeSearchCondition(SearchCondition searchCondition)
		{
			// This 'if' could be replaced by one simple match:
			//
			// match (searchCondition.Conditions)
			// {
			// | [SearchCondition(true, _) sc] =>
			//     searchCondition.Conditions = sc.Conditions;
			//     OptimizeSearchCondition(searchCodition)
			//
			// | [SearchCondition(false, [SearchCondition(true, [ExprExpr]) sc])] => ...
			//
			// | [Expr(true,  SqlValue(true))]
			// | [Expr(false, SqlValue(false))]
			//     searchCondition.Conditions = []
			// }
			//
			// One day I am going to rewrite all this crap in Nemerle.
			//
			if (searchCondition.Conditions.Count == 1)
			{
				Condition cond = searchCondition.Conditions[0];

				if (cond.Predicate is SearchCondition)
				{
					SearchCondition sc = (SearchCondition)cond.Predicate;

					if (!cond.IsNot)
					{
						searchCondition.Conditions.Clear();
						searchCondition.Conditions.AddRange(sc.Conditions);

						OptimizeSearchCondition(searchCondition);
						return;
					}

					if (sc.Conditions.Count == 1)
					{
						Condition c1 = sc.Conditions[0];

						if (!c1.IsNot && c1.Predicate is Predicate.ExprExpr)
						{
							Predicate.ExprExpr ee = (Predicate.ExprExpr)c1.Predicate;
							Predicate.Operator op;

							switch (ee.Operator)
							{
								case Predicate.Operator.Equal          : op = Predicate.Operator.NotEqual;       break;
								case Predicate.Operator.NotEqual       : op = Predicate.Operator.Equal;          break;
								case Predicate.Operator.Greater        : op = Predicate.Operator.LessOrEqual;    break;
								case Predicate.Operator.NotLess        :
								case Predicate.Operator.GreaterOrEqual : op = Predicate.Operator.Less;           break;
								case Predicate.Operator.Less           : op = Predicate.Operator.GreaterOrEqual; break;
								case Predicate.Operator.NotGreater     :
								case Predicate.Operator.LessOrEqual    : op = Predicate.Operator.Greater;        break;;
								default: throw new InvalidOperationException();
							}

							c1.Predicate = new Predicate.ExprExpr(ee.Expr1, op, ee.Expr2);

							searchCondition.Conditions.Clear();
							searchCondition.Conditions.AddRange(sc.Conditions);

							OptimizeSearchCondition(searchCondition);
							return;
						}
					}
				}

				if (cond.Predicate is Predicate.Expr)
				{
					Predicate.Expr expr = (Predicate.Expr)cond.Predicate;

					if (expr.Expr1 is SqlValue)
					{
						SqlValue value = (SqlValue)expr.Expr1;

						if (value.Value is bool)
							if (cond.IsNot ? !(bool)value.Value : (bool)value.Value)
								searchCondition.Conditions.Clear();
					}
				}
			}

			for (int i = 0; i < searchCondition.Conditions.Count; i++)
			{
				Condition cond = searchCondition.Conditions[i];

				if (cond.Predicate is Predicate.Expr)
				{
					Predicate.Expr expr = (Predicate.Expr)cond.Predicate;

					if (expr.Expr1 is SqlValue)
					{
						SqlValue value = (SqlValue)expr.Expr1;

						if (value.Value is bool)
						{
							if (cond.IsNot ? !(bool)value.Value : (bool)value.Value)
							{
								if (i > 0)
								{
									if (searchCondition.Conditions[i-1].IsOr)
									{
										searchCondition.Conditions.RemoveRange(0, i);
										OptimizeSearchCondition(searchCondition);

										break;
									}
								}
							}
						}
					}
				}
				else if (cond.Predicate is SearchCondition)
				{
					SearchCondition sc = (SearchCondition)cond.Predicate;
					OptimizeSearchCondition(sc);
				}
			}
		}

		void ForEachTable(Action<TableSource> action)
		{
			From.Tables.ForEach(delegate(TableSource tbl) { tbl.ForEach(action); });

			new QueryVisitor().Visit(this, delegate(IQueryElement e)
			{
				if (e is SqlQuery && e != this)
					((SqlQuery)e).ForEachTable(action);
			});
		}

		delegate bool FindTableSource(TableSource table);

		void RemoveOrderBy()
		{
			if (OrderBy.Items.Count > 0 && Select.SkipValue == null && Select.TakeValue == null)
				OrderBy.Items.Clear();
		}

		void ResolveWeakJoins()
		{
			List<ISqlTableSource> tables = null;

			FindTableSource findTable = null; findTable = delegate(TableSource table)
			{
				if (tables.Contains(table.Source))
					return true;

				foreach (JoinedTable join in table.Joins)
				{
					if (findTable(join.Table))
					{
						join.IsWeak = false;
						return true;
					}
				}

				if (table.Source is SqlQuery)
					foreach (TableSource t in ((SqlQuery)table.Source).From.Tables)
						if (findTable(t))
							return true;

				return false;
			};

			ForEachTable(delegate(TableSource table)
			{
				for (int i = 0; i < table.Joins.Count; i++)
				{
					JoinedTable join = table.Joins[i];

					if (join.IsWeak)
					{
						if (tables == null)
						{
							tables = new List<ISqlTableSource>();

							Action<IQueryElement> tableCollector = delegate(IQueryElement expr)
							{
								SqlField field = expr as SqlField;

								if (field != null && !tables.Contains(field.Table))
									tables.Add(field.Table);
							};

							QueryVisitor visitor = new QueryVisitor();

							visitor.VisitAll(Select,  tableCollector);
							visitor.VisitAll(Where,   tableCollector);
							visitor.VisitAll(GroupBy, tableCollector);
							visitor.VisitAll(Having,  tableCollector);
							visitor.VisitAll(OrderBy, tableCollector);
						}

						if (findTable(join.Table))
						{
							join.IsWeak = false;
						}
						else
						{
							table.Joins.RemoveAt(i);
							i--;
							continue;
						}
					}
				}
			});
		}

		TableSource OptimizeSubQuery(TableSource source, bool optimizeWhere)
		{
			for (int i = 0; i < source.Joins.Count; i++)
			{
				JoinedTable jt    = source.Joins[i];
				TableSource table = OptimizeSubQuery(jt.Table, jt.JoinType == JoinType.Inner);

				if (table != jt.Table)
				{
					SqlQuery sql = jt.Table.Source as SqlQuery;

					if (sql != null && sql.OrderBy.Items.Count > 0)
						foreach (OrderByItem item in sql.OrderBy.Items)
							OrderBy.Expr(item.Expression, item.IsDescending);

					jt.Table = table;
				}
			}

			if (source.Source is SqlQuery)
			{
				SqlQuery query = (SqlQuery)source.Source;

				if (query.From.Tables.Count == 1 &&
				    //query.From.Tables[0].Joins.Count == 0 &&
				    (optimizeWhere || query.Where.IsEmpty && query.Having.IsEmpty) &&
				   !query.HasUnion               &&
				    query.GroupBy.IsEmpty        &&
				   !query.Select.HasModifier     &&
				   !query.Select.Columns.Exists(delegate(Column c) { return !(c.Expression is SqlField); }))
				{
					Dictionary<ISqlExpression,SqlField> map = new Dictionary<ISqlExpression,SqlField>(query.Select.Columns.Count);

					foreach (Column c in query.Select.Columns)
						map.Add(c, (SqlField)c.Expression);

					SqlQuery top = this;

					while (top.ParentSql != null)
						top = top.ParentSql;

					((ISqlExpressionWalkable)top).Walk(false, delegate(ISqlExpression expr)
					{
						SqlField fld;
						return map.TryGetValue(expr, out fld)? fld: expr;
					});

					query.From.Tables[0].Joins.AddRange(source.Joins);

					if (query.From.Tables[0].Alias == null)
						query.From.Tables[0].Alias = source.Alias;

					if (!query.Where. IsEmpty) ConcatSearchCondition(Where,  query.Where);
					if (!query.Having.IsEmpty) ConcatSearchCondition(Having, query.Having);

					return query.From.Tables[0];
				}
			}

			return source;
		}

		static void ConcatSearchCondition(WhereClause where1, WhereClause where2)
		{
			if (where1.IsEmpty)
			{
				where1.SearchCondition.Conditions.AddRange(where2.SearchCondition.Conditions);
			}
			else
			{
				if (where1.SearchCondition.Precedence < Sql.Precedence.LogicalConjunction)
				{
					SearchCondition sc1 = new SearchCondition();

					sc1.Conditions.AddRange(where1.SearchCondition.Conditions);

					where1.SearchCondition.Conditions.Clear();
					where1.SearchCondition.Conditions.Add(new Condition(false, sc1));
				}

				if (where2.SearchCondition.Precedence < Sql.Precedence.LogicalConjunction)
				{
					SearchCondition sc2 = new SearchCondition();

					sc2.Conditions.AddRange(where2.SearchCondition.Conditions);

					where1.SearchCondition.Conditions.Add(new Condition(false, sc2));
				}
				else
					where1.SearchCondition.Conditions.AddRange(where2.SearchCondition.Conditions);
			}
		}

		void OptimizeSubQueries()
		{
			for (int i = 0; i < From.Tables.Count; i++)
			{
				TableSource table = OptimizeSubQuery(From.Tables[i], true);

				if (table != From.Tables[i])
				{
					SqlQuery sql = From.Tables[i].Source as SqlQuery;

					if (sql != null && sql.OrderBy.Items.Count > 0)
						foreach (OrderByItem item in sql.OrderBy.Items)
							OrderBy.Expr(item.Expression, item.IsDescending);

					From.Tables[i] = table;
				}
			}
		}

		IDictionary<string,object> _aliases;

		public void RemoveAlias(string alias)
		{
			if (_aliases != null && _aliases.ContainsKey(alias))
				_aliases.Remove(alias);
		}

		public string GetAlias(string desiredAlias, string defaultAlias)
		{
			if (_aliases == null)
				_aliases = new Dictionary<string,object>();

			string alias = desiredAlias;

			if (string.IsNullOrEmpty(desiredAlias))
			{
				desiredAlias = defaultAlias;
				alias        = defaultAlias + "1";
			}

			for (int i = 1; _aliases.ContainsKey(alias); i++)
				alias = desiredAlias + i;

			_aliases.Add(alias, alias);

			return alias;
		}

		public string[] GetTempAliases(int n, string defaultAlias)
		{
			string[] aliases = new string[n];

			for (int i = 0; i < aliases.Length; i++)
				aliases[i] = GetAlias(defaultAlias, defaultAlias);

			for (int i = 0; i < aliases.Length; i++)
				RemoveAlias(aliases[i]);

			return aliases;
		}

		void SetAliases()
		{
			_aliases = null;

			Dictionary<object,object> objs = new Dictionary<object,object>();

			Parameters.Clear();

			new QueryVisitor().VisitAll(this, delegate(IQueryElement expr)
			{
				switch (expr.ElementType)
				{
					case QueryElementType.SqlParameter:
						{
							SqlParameter p = (SqlParameter)expr;

							if (p.IsQueryParameter)
							{
								if (!objs.ContainsKey(expr))
								{
									objs.Add(expr, expr);
									p.Name = GetAlias(p.Name, "p");
								}

								Parameters.Add(p);
							}
							else
								ParameterDependent = true;
						}

						break;

					case QueryElementType.Column:
						{
							if (!objs.ContainsKey(expr))
							{
								objs.Add(expr, expr);

								Column c = (Column)expr;

								if (c.Alias != "*")
									c.Alias = GetAlias(c.Alias, "c");
							}
						}

						break;

					case QueryElementType.TableSource:
						{
							TableSource table = (TableSource)expr;

							if (!objs.ContainsKey(table))
							{
								objs.Add(table, table);
								table.Alias = GetAlias(table.Alias, "t");
							}
						}

						break;

					case QueryElementType.SqlQuery:
						{
							SqlQuery sql = (SqlQuery)expr;

							if (sql.HasUnion)
							{
								for (int i = 0; i < sql.Select.Columns.Count; i++)
								{
									Column col = sql.Select.Columns[i];

									for (int j = 0; j < sql.Unions.Count; j++)
									{
										SelectClause union = sql.Unions[j].SqlQuery.Select;

										objs.Remove(union.Columns[i].Alias);

										union.Columns[i].Alias = null;

										if (union.Columns[i].Alias != col.Alias)
											union.Columns[i].Alias = col.Alias;
									}
								}
							}
						}

						break;
				}
			});
		}

		#endregion

		#region Clone

		SqlQuery(SqlQuery clone, Dictionary<ICloneableElement,ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
		{
			objectTree.Add(clone, this);

			_sourceID = Interlocked.Increment(ref SourceIDCounter);

			_select  = new SelectClause (this, clone._select,  objectTree, doClone);
			_from    = new FromClause   (this, clone._from,    objectTree, doClone);
			_where   = new WhereClause  (this, clone._where,   objectTree, doClone);
			_groupBy = new GroupByClause(this, clone._groupBy, objectTree, doClone);
			_having  = new WhereClause  (this, clone._having,  objectTree, doClone);
			_orderBy = new OrderByClause(this, clone._orderBy, objectTree, doClone);

			_parameters.AddRange(clone._parameters.ConvertAll<SqlParameter>(delegate(SqlParameter p) { return (SqlParameter)p.Clone(objectTree, doClone); }));
			_parameterDependent = clone.ParameterDependent;

			new QueryVisitor().Visit(this, delegate(IQueryElement expr)
			{
				SqlQuery sb = expr as SqlQuery;

				if (sb != null && sb.ParentSql == clone)
					sb.ParentSql = this;
			});
		}

		public SqlQuery Clone()
		{
			return (SqlQuery)Clone(new Dictionary<ICloneableElement,ICloneableElement>(), delegate(ICloneableElement o) { return true; });
		}

		public SqlQuery Clone(Predicate<ICloneableElement> doClone)
		{
			return (SqlQuery)Clone(new Dictionary<ICloneableElement,ICloneableElement>(), doClone);
		}

		#endregion

		#region Helpers

		public TableSource GetTableSource(ISqlTableSource table)
		{
			TableSource ts = From[table];
			return ts == null && ParentSql != null? ParentSql.GetTableSource(table) : ts;
		}

		static TableSource CheckTableSource(TableSource ts, ISqlTableSource table, string alias)
		{
			if (ts.Source == table && (alias == null || ts.Alias == alias))
				return ts;

			TableSource jt = ts[table, alias];

			if (jt != null)
				return jt;

			if (ts.Source is SqlQuery)
			{
				TableSource s = ((SqlQuery)ts.Source).From[table, alias];

				if (s != null)
					return s;
			}

			return null;
		}

		#endregion

		#region Overrides

		public string SqlText { get { return ToString(); } }

		public override string ToString()
		{
			//return "";

			StringBuilder sb = new StringBuilder();

			sb
				.Append(Select)
				.Append(From)
				.Append(Where)
				.Append(GroupBy)
				.Append(Having)
				.Append(OrderBy);

			if (HasUnion)
				foreach (Union u in Unions)
					sb.Append(u);

			return sb.ToString();
		}

		#endregion

		#region ISqlExpression Members

		public bool CanBeNull()
		{
			return true;
		}

		public int Precedence
		{
			get { return Sql.Precedence.Unknown; }
		}

		public Type SystemType
		{
			get
			{
				if (Select.Columns.Count == 1)
					return Select.Columns[0].SystemType;
				return null;
			}
		}

		#endregion

		#region ICloneableElement Members

		public ICloneableElement Clone(Dictionary<ICloneableElement, ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
		{
			if (!doClone(this))
				return this;

			ICloneableElement clone;

			if (!objectTree.TryGetValue(this, out clone))
				clone = new SqlQuery(this, objectTree, doClone);

			return clone;
		}

		#endregion

		#region ISqlExpressionWalkable Members

		[Obsolete]
		ISqlExpression ISqlExpressionWalkable.Walk(bool skipColumns, WalkingFunc func)
		{
			((ISqlExpressionWalkable)Select) .Walk(skipColumns, func);
			((ISqlExpressionWalkable)From)   .Walk(skipColumns, func);
			((ISqlExpressionWalkable)Where)  .Walk(skipColumns, func);
			((ISqlExpressionWalkable)GroupBy).Walk(skipColumns, func);
			((ISqlExpressionWalkable)Having) .Walk(skipColumns, func);
			((ISqlExpressionWalkable)OrderBy).Walk(skipColumns, func);

			return func(this);
		}

		#endregion

		#region IEquatable<ISqlExpression> Members

		bool IEquatable<ISqlExpression>.Equals(ISqlExpression other)
		{
			return this == other;
		}

		#endregion

		#region ISqlTableSource Members

		public static int SourceIDCounter;

		readonly int _sourceID;
		public   int  SourceID { get { return _sourceID; } }

		private SqlField _all;
		public  SqlField  All
		{
			get
			{
				if (_all == null)
				{
					_all = new SqlField(null, "*", "*", true, -1);
					((IChild<ISqlTableSource>)_all).Parent = this;
				}

				return _all;
			}
		}

		#endregion

		#region IQueryElement Members

		public QueryElementType ElementType { get { return QueryElementType.SqlQuery; } }

		#endregion
	}
}
