﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CodeArt.DomainDriven;
using CodeArt.Util;

namespace CodeArt.DomainDriven.DataAccess
{
    /// <summary>
    /// 基于表达式的查询,可以指定对象属性等表达式
    /// select子语句系统内部使用，外部请不要调用
    /// </summary>
    public abstract class QueryExpression : QueryBuilder
    {
        /// <summary>
        /// 表达式针对的目标表
        /// </summary>
        public DataTable Target
        {
            get;
            private set;
        }


        public string Expression
        {
            get;
            private set;
        }

        public SqlDefinition Definition
        {
            get;
            private set;
        }


        /// <summary>
        /// 查询的锁定级别
        /// </summary>
        public QueryLevel Level
        {
            get;
            private set;
        }

        protected override string GetName()
        {
            return this.Definition.Key;
        }


        public QueryExpression(DataTable target, string expression, QueryLevel level)
        {
            this.Target = target;
            this.Expression = expression;
            this.Definition = SqlDefinition.Create(this.Expression);
            this.Level = level;
        }

        protected override string Process(DynamicData param)
        {
            var commandText = GetCommandText(param);
            return this.Definition.Process(commandText, param);
        }

        /// <summary>
        /// 获取命令文本
        /// </summary>
        /// <param name="target"></param>
        /// <param name="param"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        protected abstract string GetCommandText(DynamicData param);

        protected string GetObjectSql()
        {
            var table = this.Target;

            StringBuilder sql = new StringBuilder();
            sql.Append("select ");
            sql.AppendLine(GetSelectFieldsSql(table, this.Definition));
            sql.AppendLine(" from ");
            sql.AppendLine(GetFromSql(table, this.Level, this.Definition));
            sql.Append(GetJoinSql(table, this.Definition));

            return GetFinallyObjectSql(sql.ToString(), table);
        }

        #region 得到select语句

        /// <summary>
        /// 获取表<paramref name="chainRoot"/>需要查询的select字段
        /// </summary>
        /// <param name="chainRoot"></param>
        /// <param name="exp"></param>
        /// <param name="chain">可以为输出的字段前置对象链</param>
        /// <returns></returns>
        private static string GetSelectFieldsSql(DataTable chainRoot, SqlDefinition exp)
        {
            StringBuilder sql = new StringBuilder();
            sql.Append(GetChainRootSelectFieldsSql(chainRoot, exp).Trim());

            using (var temp = TempIndex.Borrow())
            {
                var index = temp.Item;
                sql.Append(GetSlaveSelectFieldsSql(chainRoot, chainRoot, exp, index).Trim());
            }
            sql.Length--;//移除最后一个逗号
            return sql.ToString();
        }

        /// <summary>
        /// 填充查询链中根表的select的字段
        /// </summary>
        /// <param name="chainRoot"></param>
        /// <param name="exp"></param>
        /// <param name="sql"></param>
        private static string GetChainRootSelectFieldsSql(DataTable chainRoot, SqlDefinition exp)
        {
            StringBuilder sql = new StringBuilder();
            if (chainRoot.IsDerived)
            {
                FillChainRootSelectFieldsSql(chainRoot.InheritedRoot, TableType.InheritedRoot, exp, sql);

                foreach (var derived in chainRoot.Deriveds)
                {
                    FillChainRootSelectFieldsSql(derived, TableType.Derived, exp, sql);
                }
            }
            else
            {
                FillChainRootSelectFieldsSql(chainRoot, TableType.Common, exp, sql);
            }
            return sql.ToString();
        }

        private static void FillChainRootSelectFieldsSql(DataTable current, TableType tableType, SqlDefinition exp, StringBuilder sql)
        {
            sql.AppendLine();

            foreach (var field in current.Fields)
            {
                if (field.Tip.Lazy) continue;

                if (tableType == TableType.Derived)
                {
                    //派生表不输出主键信息
                    if (field.Name == EntityObject.IdPropertyName)
                        continue;

                    if (current.Type != DataTableType.AggregateRoot)
                    {
                        if (field.Name == current.Root.TableIdName)
                            continue;
                    }
                }

                if (!ContainsField(field.Name, exp)) continue;

                sql.AppendFormat("{0}.{1} as {1},", SqlStatement.Qualifier(current.Name),
                                                    SqlStatement.Qualifier(field.Name));
            }
        }

        /// <summary>
        /// 填充查询链中从表的select的字段
        /// </summary>
        /// <param name="chainRoot"></param>
        /// <param name="master"></param>
        /// <param name="exp"></param>
        /// <param name="sql"></param>
        private static string GetSlaveSelectFieldsSql(DataTable chainRoot, DataTable master, SqlDefinition exp, TempIndex index)
        {
            StringBuilder sql = new StringBuilder();
            if (master.IsDerived)
            {
                FillChildSelectFieldsSql(chainRoot, master.InheritedRoot, exp, sql, index);

                foreach (var derived in master.Deriveds)
                {
                    FillChildSelectFieldsSql(chainRoot, derived, exp, sql, index);
                }
            }
            else
            {
                FillChildSelectFieldsSql(chainRoot, master, exp, sql, index);
            }
            return sql.ToString();
        }

        private static void FillChildSelectFieldsSql(DataTable chainRoot, DataTable master, SqlDefinition exp, StringBuilder sql, TempIndex index)
        {
            foreach (var child in master.BuildtimeChilds)
            {
                if (!index.TryAdd(child)) continue; //防止由于循环引用导致的死循环

                if (child.IsDerived)
                {
                    FillFieldsSql(chainRoot, master, child.InheritedRoot, TableType.InheritedRoot, exp, sql, index);

                    foreach (var derived in child.Deriveds)
                    {
                        FillFieldsSql(chainRoot, master, derived, TableType.Derived, exp, sql, index);
                    }
                }
                else
                {
                    FillFieldsSql(chainRoot, master, child, TableType.Common, exp, sql, index);
                }
            }
        }

        private static void FillFieldsSql(DataTable chainRoot, DataTable master, DataTable current, TableType tableType, SqlDefinition exp, StringBuilder sql, TempIndex index)
        {
            if (!ContainsTable(chainRoot, exp, current)) return;

            var chain = current.GetChainCode(chainRoot);

            sql.AppendLine();

            foreach (var field in current.Fields)
            {
                if (field.Tip.Lazy) continue;

                if (tableType == TableType.Derived)
                {
                    if (field.Name == EntityObject.IdPropertyName || field.Name == current.Root.TableIdName)
                        continue;
                }

                var fieldName = string.Format("{0}_{1}", chain, field.Name);

                if (!ContainsField(fieldName, exp)) continue;

                sql.AppendFormat("{0}.{1} as {2},", SqlStatement.Qualifier(chain),
                                                    SqlStatement.Qualifier(field.Name),
                                                    SqlStatement.Qualifier(fieldName));
            }
            FillChildSelectFieldsSql(chainRoot, current, exp, sql, index);
        }

        #endregion

        #region 获取from语句

        private static string GetFromSql(DataTable chainRoot, QueryLevel level, SqlDefinition exp)
        {
            if (chainRoot.IsDerived)
            {
                return GetFromSqlByDerived(chainRoot, level, exp);
                //return string.Format(" ({0}) as {1}", GetDerivedTableSql(chainRoot, level, string.Empty), chainRoot.Name);
            }
            else
            {
                return string.Format(" {0}{1}", SqlStatement.Qualifier(chainRoot.Name), GetLockCode(level));
            }
        }

        private static string GetFromSqlByDerived(DataTable table, QueryLevel level, SqlDefinition exp)
        {
            var inheritedRoot = table.InheritedRoot;

            StringBuilder sql = new StringBuilder();
            sql.AppendFormat(" {0}{1}", SqlStatement.Qualifier(inheritedRoot.Name), GetLockCode(level)); //inheritedRoot记录了条目信息，所以一定会参与查询
            foreach (var derived in table.Deriveds)
            {
                if (!exp.ContainsExceptId(derived)) continue;


                if (table.Type == DataTableType.AggregateRoot)
                {
                    sql.AppendFormat(" inner join {0} on {1}.Id={0}.Id",
                        SqlStatement.Qualifier(derived.Name), SqlStatement.Qualifier(inheritedRoot.Name));
                }
                else
                {
                    sql.AppendFormat(" inner join {0} on {1}.Id={0}.Id and {1}.{2}={0}.{2}",
                        SqlStatement.Qualifier(derived.Name)
                        , SqlStatement.Qualifier(inheritedRoot.Name)
                        , SqlStatement.Qualifier(table.Root.TableIdName));
                }
            }
            return sql.ToString();
        }

        #endregion

        #region 获取join语句

        private static string GetJoinSql(DataTable chainRoot, SqlDefinition exp)
        {
            StringBuilder sql = new StringBuilder();
            using (var temp = TempIndex.Borrow())
            {
                var index = temp.Item;
                FillJoinSql(chainRoot, chainRoot, exp, sql, index);
            }

            return sql.ToString();
        }

        private static void FillJoinSql(DataTable chainRoot, DataTable master, SqlDefinition exp, StringBuilder sql,TempIndex index)
        {
            if(master.IsDerived)
            {
                var inheritedRoot = master.InheritedRoot;
                FillChildJoinSql(chainRoot, inheritedRoot, exp, sql, index);
                foreach (var derived in master.Deriveds)
                {
                    FillChildJoinSql(chainRoot, derived, exp, sql, index);
                }
            }
            else
            {
                FillChildJoinSql(chainRoot, chainRoot, exp, sql, index);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="chainRoot">是查询的根表</param>
        /// <param name="master"></param>
        /// <param name="exp"></param>
        /// <param name="masterProxyName"></param>
        /// <param name="sql"></param>
        private static void FillChildJoinSql(DataTable chainRoot, DataTable master, SqlDefinition exp, StringBuilder sql, TempIndex index)
        {
            var masterChain = master.GetChainCode(chainRoot);

            foreach (var child in master.BuildtimeChilds)
            {
                if (!index.TryAdd(child)) continue; //防止由于循环引用导致的死循环

                if (child.IsDerived)
                {
                    FillJoinSqlByDerived(chainRoot, master, child, masterChain, exp, sql, index);
                }
                else
                {
                    FillJoinSqlByNoDerived(chainRoot, master, child, masterChain, exp, sql, index);
                }
            }
        }

        private static void FillJoinSqlByDerived(DataTable chainRoot, DataTable master, DataTable current, string masterChain, SqlDefinition exp, StringBuilder sql, TempIndex index)
        {
            if (!ContainsTable(chainRoot, exp, current)) return;

            var tip = current.MemberPropertyTip;
            var chain = current.GetChainCode(chainRoot);

            var childSql = GetDerivedTableSql(current, QueryLevel.None);

            sql.AppendLine();

            string masterTableName = string.IsNullOrEmpty(masterChain) ? master.Name : masterChain;

            sql.AppendFormat(" left join ({0}) as {1} on {2}.{3}Id={1}.Id",
                                childSql, 
                                SqlStatement.Qualifier(chain), 
                                SqlStatement.Qualifier(masterTableName)
                                , tip.PropertyName);

            FillJoinSql(chainRoot, current, exp, sql, index);
        }


        private static void FillJoinSqlByNoDerived(DataTable chainRoot, DataTable master, DataTable current, string masterChain, SqlDefinition exp, StringBuilder sql, TempIndex index)
        {
            if (!ContainsTable(chainRoot, exp, current)) return;
            var tip = current.MemberPropertyTip;

            var chain = current.GetChainCode(chainRoot);

            sql.AppendLine();

            string masterTableName = string.IsNullOrEmpty(masterChain) ? master.Name : masterChain;

            sql.AppendFormat(" left join {0} as {1} on {2}.{3}Id={1}.Id",
                                SqlStatement.Qualifier(current.Name),
                                SqlStatement.Qualifier(chain),
                                SqlStatement.Qualifier(masterTableName),
                                tip.PropertyName);

            FillChildJoinSql(chainRoot, current, exp, sql, index);
        }

        #endregion

        #region 其他辅助方法

        private enum TableType
        {
            InheritedRoot,
            Derived,
            Common
        }

        private static string GetLockCode(QueryLevel level)
        {
            var agent = SqlContext.GetAgent();
            if (agent.Database == DatabaseType.SQLServer)
            {
                return SQLServer.SqlStatement.GetLockCode(level);
            }
            throw new NotSupportDatabaseException("GetLockCode", agent.Database);
        }


        private static string GetDerivedTableSql(DataTable table, QueryLevel level)
        {
            var inheritedRoot = table.InheritedRoot;

            StringBuilder sql = new StringBuilder();
            sql.Append("select ");
            sql.Append(GetChainRootSelectFieldsSql(table, SqlDefinition.All));
            sql.Length--;
            sql.AppendLine();
            sql.AppendFormat(" from {0}{1}", SqlStatement.Qualifier(inheritedRoot.Name), GetLockCode(level));
            foreach (var derived in table.Deriveds)
            {
                if (table.Type == DataTableType.AggregateRoot)
                {
                    sql.AppendFormat(" inner join {0} on {1}.Id={0}.Id",
                        SqlStatement.Qualifier(derived.Name), SqlStatement.Qualifier(inheritedRoot.Name));
                }
                else
                {
                    sql.AppendFormat(" inner join {0} on {1}.Id={0}.Id and {1}.{2}={0}.{2}",
                        SqlStatement.Qualifier(derived.Name),
                        SqlStatement.Qualifier(inheritedRoot.Name),
                        SqlStatement.Qualifier(table.Root.TableIdName));
                }
            }
            return sql.ToString();
        }


        /// <summary>
        /// 获取派生类table的完整代码，该代码可获取整个派生类的信息
        /// </summary>
        /// <param name="table"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        //private static string GetDerivedTableSql(DataTable table, QueryLevel level)
        //{
        //    var inheritedRoot = table.InheritedRoot;

        //    StringBuilder sql = new StringBuilder();
        //    sql.Append("select ");
        //    sql.AppendLine(GetSelectFieldsSql(table, SqlDefinition.All));
        //    sql.AppendFormat(" from {0}{1}", inheritedRoot.Name, GetLockCode(level));
        //    FillJoinSql(inheritedRoot, inheritedRoot, SqlDefinition.All, sql);
        //    foreach (var derived in table.Deriveds)
        //    {
        //        if (table.Type == DataTableType.AggregateRoot)
        //        {
        //            sql.AppendFormat(" inner join {0} on {1}.Id={0}.Id",
        //                derived.Name, inheritedRoot.Name);
        //        }
        //        else
        //        {
        //            sql.AppendFormat(" inner join {0} on {1}.Id={0}.Id and {1}.{2}={0}.{2}",
        //                derived.Name, inheritedRoot.Name, table.Root.TableIdName);
        //        }

        //        FillJoinSql(derived, derived, SqlDefinition.All, sql);
        //    }
        //    return sql.ToString();
        //}

        private static bool ContainsField(string fieldName, SqlDefinition exp)
        {
            if (exp.SpecifiedField)
            {
                return exp.ContainsField(fieldName);
            }
            return true;
        }

        private static bool ContainsTable(DataTable root, SqlDefinition exp, DataTable target)
        {
            if (target.IsMultiple) return false; //多表关联的，不连带查询
            var tip = target.MemberPropertyTip;

            if (exp.SpecifiedField)
            {
                //指定了加载字段，那么就看表是否提供了相关的字段
                var path = target.GetChainCode(root);
                return exp.ContainsChain(path);
            }
            else
            {
                if (target.Type == DataTableType.AggregateRoot || tip.Lazy)
                {
                    var path = target.GetChainCode(root);
                    if (!exp.ContainsChain(path))
                    {
                        return false; //默认情况下外部的内聚根、懒惰加载不连带查询
                    }
                }
                return true;
            }
        }


        //获取最终的输出代码
        private string GetFinallyObjectSql(string tableSql, DataTable table)
        {
            string sql = null;
            if (this.Definition.Condition.IsEmpty())
            {
                sql = string.Format("select {2} from ({0}) as {1}", tableSql, SqlStatement.Qualifier(table.Name), this.Definition.GetFieldsSql());
            }
            else
            {
                sql = string.Format("select {3} from ({0}) as {1} where {2}", tableSql, SqlStatement.Qualifier(table.Name), this.Definition.Condition, this.Definition.GetFieldsSql());
            }

            return string.Format("({0}) as {1}", sql, SqlStatement.Qualifier(table.Name));
        }

        #endregion
    }
}
