﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Dynamic;

using CodeArt.Runtime;
using CodeArt.Util;
using CodeArt.IO;
using CodeArt.AppSetting;
using System.Linq.Expressions;

namespace CodeArt.DTO
{
    [DebuggerDisplay("{GetCode()}")]
    public class DTObject : DynamicObject
    {
        #region 根

        internal DTEObject _root;

        internal DTObject()
        {
        }

        internal DTEObject GetRoot()
        {
            return _root;
        }

        /// <summary>
        /// 清理数据，提供池使用
        /// </summary>
        internal void Clear()
        {
            _root = null;
            this.IsReadOnly = false;
        }


        //internal DTObject(DTEObject root, bool isReadOnly)
        //{
        //    _root = root;
        //    this.IsReadOnly = isReadOnly;
        //}

        internal DTEntity Parent
        {
            get
            {
                return _root.Parent;
            }
            set
            {
                _root.Parent = value;
            }
        }

        #endregion

        #region 只读控制

        public bool IsReadOnly
        {
            get;
            internal set;
        }

        private void ValidateReadOnly()
        {
            if (this.IsReadOnly)
                throw new DTOException(Strings.DTOReadOnly);
        }

        #endregion

        #region 值

        public object this[string findExp]
        {
            get
            {
                return GetValue(findExp);
            }
            set
            {
                SetValue(findExp, value);
            }
        }

        private DTEntity CreateEntity(string name, object value)
        {
            if(IsList(value))
            {
                var list = value as IEnumerable;
                if (list != null) return CreateListEntity(name, list);
            }
            
            var dto = value as DTObject;
            if (dto != null)
            {
                var root = dto.GetRoot();
                root.Name = name;
                return root;
            }
            else
            {
                return DTOPool.CreateDTEValue(name, value, this.IsPinned);
            }
        }

        private DTEList CreateListEntity(string name, IEnumerable list)
        {
            var dte = DTOPool.CreateDTEList(this.IsPinned);
            dte.Name = name;

            foreach (var item in list)
            {
                dte.CreateAndPush((dto) =>
                {
                    dto.SetValue(item);
                });
            }
            return dte;
        }

        public void SetValue(string findExp, object value)
        {
            ValidateReadOnly();

            var dtoValue = value as DTObject;
            if (dtoValue != null)
            {
                SetObject(findExp, dtoValue);
                return;
            }

            var eitities = FindEntities(findExp, false);
            if (eitities.Length == 0)
            {
                var query = QueryExpression.Create(findExp);
                _root.SetEntity(query, (name) =>
                {
                    return CreateEntity(name, value);
                });
            }
            else
            {
                var isPureValue = IsPureValue(value);
                foreach (var e in eitities)
                {
                    if(e.Type == DTEntityType.Value && isPureValue)
                    {
                        var ev = e as DTEValue;
                        ev.Value = value;
                        continue;
                    }

                    var parent = e.Parent as DTEObject;
                    if (parent == null) throw new DTOException("表达式错误" + findExp);

                    var query = QueryExpression.Create(e.Name);
                    parent.SetEntity(query, (name) =>
                    {
                        return CreateEntity(name, value);
                    });
                }
            }
        }

        public void SetValue(object value)
        {
            SetValue(string.Empty, value);
        }

        public object GetValue(string findExp)
        {
            return GetValue(findExp, true);
        }

        public object GetValue(string findExp, bool throwError)
        {
            DTEntity entity = FindEntity(findExp, throwError);
            if (entity == null) return null;
            switch(entity.Type)
            {
                case DTEntityType.Value:
                    {
                        var ev = entity as DTEValue;
                        if (ev != null) return ev.Value;
                    }
                    break;
                case DTEntityType.Object:
                    {
                        var eo = entity as DTEObject;
                        if (eo != null) return DTOPool.CreateObject(eo, this.IsReadOnly, this.IsPinned);
                    }
                    break;
                case DTEntityType.List:
                    {
                        var el = entity as DTEList;
                        if (el != null) return el.GetObjects();
                    }
                    break;
            }
            return null;
        }

        public object GetValue(string findExp, object defaultValue)
        {
            var value = GetValue(findExp, false);
            if (IsValueEmpty(value)) return defaultValue;
            return value;
        }

        public T GetValue<T>(string findExp)
        {
            return DataUtil.ToValue<T>(GetValue(findExp));
        }

        public T GetValue<T>(string findExp, bool throwError)
        {
            return DataUtil.ToValue<T>(GetValue(findExp, throwError));
        }

        public T GetValue<T>(string findExp, T defaultValue)
        {
            DTEValue entity = FindEntity<DTEValue>(findExp, false);
            if (IsValueEmpty(entity)) return defaultValue;
            return DataUtil.ToValue<T>(entity.Value);
        }

        private bool IsValueEmpty(DTEValue entity)
        {
            if (entity == null) return true;
            return IsValueEmpty(entity.Value);
        }

        private bool IsValueEmpty(object value)
        {
            if (value == null) return true;
            var strValue = value as string;
            if (strValue != null) return string.IsNullOrEmpty(strValue);
            return false;
        }

        public bool TryGetValue<T>(string findExp, out T value)
        {
            DTEValue entity = FindEntity<DTEValue>(findExp, false);
            if (IsValueEmpty(entity))
            {
                value = default(T);
                return false;
            }
            value = DataUtil.ToValue<T>(entity.Value);
            return true;
        }

        public object GetValue()
        {
            return GetValue(string.Empty);
        }

        public object GetValue(bool throwError)
        {
            return GetValue(string.Empty, throwError);
        }

        public object GetValue(object defaultValue)
        {
            return GetValue(string.Empty, defaultValue);
        }

        public T GetValue<T>()
        {
            return DataUtil.ToValue<T>(GetValue());
        }

        public T GetValue<T>(bool throwError)
        {
            return DataUtil.ToValue<T>(GetValue(throwError));
        }

        public T GetValue<T>(T defaultValue)
        {
            return GetValue<T>(string.Empty, defaultValue);
        }

        #endregion

        #region 集合

        public void Push(string findExp, int count, Action<DTObject, int> action)
        {
            ValidateReadOnly();

            var entity = GetOrCreateList(findExp);

            for (int i = 0; i < count; i++)
            {
                DTObject dto = entity.CreateAndPush();
                action(dto, i);
            }
        }

        public void Push(int count, Action<DTObject, int> action)
        {
            this.Push(string.Empty, count, action);
        }

        #region 填充dto成员，然后追加到集合，不用重复查找，比较高效


        public void Push<T>(string findExp, IEnumerable<T> list, Action<DTObject, T> action)
        {
            ValidateReadOnly();

            DTEList entity = GetOrCreateList(findExp);
            foreach (T item in list)
            {
                DTObject dto = entity.CreateAndPush();
                action(dto, item);
            }
        }

        public void Push<T>(IEnumerable<T> list, Action<DTObject, T> action)
        {
            this.Push<T>(string.Empty, list, action);
        }

        public void Push(string findExp, IEnumerable list, Action<DTObject, object> action)
        {
            ValidateReadOnly();

            DTEList entity = GetOrCreateList(findExp);
            foreach (object item in list)
            {
                DTObject dto = entity.CreateAndPush();
                action(dto, item);
            }
        }

        public void Push(IEnumerable list, Action<DTObject, object> action)
        {
            this.Push(string.Empty, list, action);
        }

        #endregion

        #region 创建dto成员，然后追加到集合，不用重复查找，比较高效

        /// <summary>
        /// 不用重复查找，比较高效
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="findExp"></param>
        /// <param name="list"></param>
        /// <param name="factory"></param>
        public void Push<T>(string findExp, IEnumerable<T> list, Func<T, DTObject> factory)
        {
            ValidateReadOnly();

            var entity = GetOrCreateList(findExp);

            foreach (T item in list)
            {
                DTObject dto = factory(item);
                entity.Push(dto);
            }
        }

        public void Push<T>(IEnumerable<T> list, Func<T, DTObject> factory)
        {
            this.Push<T>(string.Empty, list, factory);
        }


        public void Push(string findExp, IEnumerable list, Func<object, DTObject> factory)
        {
            ValidateReadOnly();

            var entity = GetOrCreateList(findExp);

            foreach (object item in list)
            {
                DTObject dto = factory(item);
                entity.Push(dto);
            }
        }

        public void Push(IEnumerable list, Func<object, DTObject> factory)
        {
            this.Push(string.Empty, list, factory);
        }

        #endregion

        /// <summary>
        /// 向集合追加一个成员
        /// </summary>
        /// <param name="findExp"></param>
        /// <param name="member"></param>
        public void Push(string findExp, DTObject member)
        {
            ValidateReadOnly();

            DTEList entity = FindEntity<DTEList>(findExp, false);
            if (entity == null)
            {
                var query = QueryExpression.Create(findExp);
                _root.SetEntity(query, (name) =>
                {
                    var dte = DTOPool.CreateDTEList(this.IsPinned);
                    dte.Name = name;
                    return dte;
                });
                entity = FindEntity<DTEList>(findExp, true);
            };
            if (member == null) return;

            entity.Push(member);
        }

        public void SetList(string findExp, IList<DTObject> items)
        {
            ValidateReadOnly();

            Push(findExp, null);//以此来防止当items个数为0时，没有创建的bug
            foreach (var item in items)
            {
                Push(findExp, item);
            }
        }

        /// <summary>
        /// 如果不存在findExp对应的列表，那么创建
        /// </summary>
        /// <param name="findExp"></param>
        public void SetList(string findExp)
        {
            ValidateReadOnly();

            DTEList entity = FindEntity<DTEList>(findExp, false);
            if (entity == null)
            {
                var query = QueryExpression.Create(findExp);
                _root.SetEntity(query, (name) =>
                {
                    var dte = DTOPool.CreateDTEList(this.IsPinned);
                    dte.Name = name;
                    return dte;
                });
            };
        }

        public DTObject CreateAndPush(string findExp)
        {
            ValidateReadOnly();

            DTEList entity = GetOrCreateList(findExp);
            return entity.CreateAndPush();
        }

        private DTEList GetOrCreateList(string findExp)
        {
            DTEList entity = FindEntity<DTEList>(findExp, false);
            if (entity == null)
            {
                var query = QueryExpression.Create(findExp);
                _root.SetEntity(query, (name) =>
                {
                    var dte = DTOPool.CreateDTEList(this.IsPinned);
                    dte.Name = name;
                    return dte;
                });
                entity = FindEntity<DTEList>(findExp, true);
            }
            return entity;
        }


        public DTObject CreateAndPush()
        {
            return this.CreateAndPush(string.Empty);
        }

        public void Each(string findExp, Action<DTObject> action)
        {
            var list = GetList(findExp, false);
            if (list == null) return;
            foreach (var dto in list)
            {
                action(dto);
            }
        }

        public void Each(string findExp, Func<DTObject, bool> action)
        {
            var list = GetList(findExp, false);
            if (list == null) return;
            foreach (var dto in list)
            {
                if (!action(dto)) return; //如果返回false，表示中断遍历操作
            }
        }

        /// <summary>
        /// 移除集合条目
        /// </summary>
        /// <param name="listExp">集合表达式</param>
        /// <param name="indexs">需要移除的项目的序号</param>
        public void RemoveAts(string listExp, IList<int> indexs)
        {
            ValidateReadOnly();

            DTEList entity = FindEntity<DTEList>(listExp, false);
            if (entity != null)
            {
                entity.RemoveAts(indexs);
            }
        }

        /// <summary>
        /// 保留集合指定序号的条目，移除其他项
        /// </summary>
        /// <param name="listExp"></param>
        /// <param name="indexs"></param>
        public void RetainAts(string listExp, IList<int> indexs)
        {
            ValidateReadOnly();

            DTEList entity = FindEntity<DTEList>(listExp, false);
            if (entity != null)
            {
                entity.RetainAts(indexs);
            }
        }

        public DTObjects GetList(string findExp)
        {
            return GetList(findExp, true);
        }

        public DTObjects GetList(string findExp, bool throwError)
        {
            DTEList entity = FindEntity<DTEList>(findExp, throwError);
            if (entity == null) return null;
            return entity.GetObjects();
        }

        public DTObjects GetList()
        {
            return GetList(string.Empty);
        }

        public DTObjects GetList(bool throwError)
        {
            return GetList(string.Empty, throwError);
        }

        public int Count(string findExp)
        {
            return GetList(findExp).Count;
        }

        public int Count()
        {
            return GetList().Count;
        }


        #endregion

        #region 对象

        public void SetObject(string findExp, DTObject obj)
        {
            ValidateReadOnly();

            if (string.IsNullOrEmpty(findExp))
            {
                //dto.Set(newDTO) 这种表达式下说明此时需要替换整个dto
                //为了保证数据安全，需要克隆，{xxx:{a,b}},如果不克隆，那么b=xxx就会出现错误
                var newRoot = obj.GetRoot().Clone() as DTEObject;
                newRoot.Parent = _root.Parent;
                _root = newRoot;
            }
            else
            {
                DTEObject entity = FindEntity<DTEObject>(findExp, false);
                if (entity == null)
                {
                    var query =  QueryExpression.Create(findExp);
                    _root.SetEntity(query, (name) =>
                    {
                        var e = obj.GetRoot().Clone();
                        e.Name = name;
                        return e;
                    });
                }
            }
        }

        public void SetObject(DTObject obj)
        {
            SetObject(string.Empty, obj);
        }

        public DTObject GetObject(string findExp)
        {
            return GetObject(findExp, true);
        }

        public DTObject GetObject(string findExp, DTObject defaultValue)
        {
            var entity = this.FindEntity<DTEObject>(findExp, false);
            if (entity == null) return defaultValue;
            return DTOPool.CreateObject(entity, this.IsReadOnly, this.IsPinned);
        }

        public DTObject GetObject(string findExp, bool throwError)
        {
            var entity = this.FindEntity<DTEObject>(findExp, throwError);
            if (entity == null) return null;
            return DTOPool.CreateObject(entity, this.IsReadOnly, this.IsPinned);
        }

        public bool TryGetObject(string findExp, out DTObject value)
        {
            value = GetObject(findExp, false);
            return value != null;
        }

        #endregion

        #region 键值对

        public Dictionary<string, object> GetDictionary()
        {
            return GetDictionary(string.Empty, false);
        }

        public Dictionary<string, object> GetDictionary(string findExp)
        {
            return GetDictionary(findExp, false);
        }

        public Dictionary<string, object> GetDictionary(string findExp, bool throwError)
        {
            var entities = this.FindEntities(findExp, throwError);
            var dictionary = DTOPool.CreateDictionary(this.IsPinned);
            foreach (var entity in entities)
            {
                var key = entity.Name;
                var value = CreateEntityValue(entity);
                dictionary.Add(key, value);
            }
            return dictionary;
        }


        //public Dictionary<string, T> GetDictionary<T>()
        //{
        //    return GetDictionary<T>(string.Empty, false);
        //}

        //public Dictionary<string, T> GetDictionary<T>(string findExp)
        //{
        //    return GetDictionary<T>(findExp, false);
        //}

        ///// <summary>
        ///// 本质上来说,json就是一组键值对，因此可以获取键值对形式的值
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="findExp"></param>
        ///// <param name="throwError"></param>
        ///// <returns></returns>
        //public Dictionary<string, T> GetDictionary<T>(string findExp, bool throwError)
        //{
        //    var entities = this.FindEntities(findExp, throwError);
        //    var dictionary = new Dictionary<string, T>(entities.Length);
        //    foreach (var entity in entities)
        //    {
        //        var key = entity.Name;
        //        var value = CreateEntityValue(entity);
        //        if (value is T)
        //            dictionary.Add(key, (T)value);
        //    }
        //    return dictionary;
        //}

        private object CreateEntityValue(DTEntity entity)
        {
            switch(entity.Type)
            {
                case DTEntityType.Value:
                    {
                        var temp = entity as DTEValue;
                        if (temp != null) return temp.Value;
                    }
                    break;
                case DTEntityType.Object:
                    {
                        var temp = entity as DTEObject;
                        if (temp != null) return DTOPool.CreateObject(temp, this.IsReadOnly, this.IsPinned);
                    }
                    break;
                case DTEntityType.List:
                    {
                        var temp = entity as DTEList;
                        if (temp != null) return temp.GetObjects();
                    }
                    break;
            }
            throw new DTOException("在CreateEntityValue发生未知的错误,entity类型为" + entity.GetType());
        }

        #endregion

        #region 转换

        /// <summary>
        /// 批量变换dto结构
        /// </summary>
        /// <param name="express">
        /// findExp=>name;findExp=>name
        /// </param>
        public void Transform(string express)
        {
            var expresses = TransformExpressions.Create(express);
            foreach (var exp in expresses)
            {
                exp.Execute(this);
            }
        }

        /// <summary>
        /// 该方法主要用于更改成员值
        /// </summary>
        /// <param name="express">
        /// findExp=valueFindExp
        /// 说明：
        /// valueFindExp 可以包含检索方式，默认的方式是在findExp检索出来的结果中所在的DTO对象中进行检索
        /// 带“@”前缀，表示从根级开始检索
        /// 带“*”前缀，表示返回值所在的对象
        /// </param>
        /// <param name="transformValue"></param>
        public void Transform(string express, Func<object, object> transformValue)
        {
            AssignExpression exp = AssignExpression.Create(express) as AssignExpression;
            if (exp == null) throw new DTOException("变换表达式错误" + express);
            exp.Execute(this, transformValue);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="listName"></param>
        /// <param name="action"></param>
        /// <param name="self">自身是否参与遍历</param>
        public void DeepEach(string listName, Action<DTObject> action, bool self = false)
        {
            this.Each(listName, (child) =>
            {
                child.DeepEach(listName, action);
                action(child);
            });
            if (self) action(this);
        }       


        #endregion

        #region 辅助

        public bool Exist(string findExp)
        {
            return FindEntity(findExp, false) != null || GetObject(findExp) != null;
        }

        internal DTEntity FindEntity(string findExp, bool throwError)
        {
            var query = QueryExpression.Create(findExp);

            DTEntity entity = null;
            var es = _root.FindEntities(query);
            if (es.Count() > 0) entity = es.First();

            if (entity == null)
            {
                if (throwError)
                    throw new NotFoundDTEntityException("没有找到" + findExp + "对应的DTO实体！");
                return null;
            }
            return entity;
        }

        internal DTEntity[] FindEntities(string findExp, bool throwError)
        {
            var query = QueryExpression.Create(findExp);

            List<DTEntity> list = DTOPool.CreateDTEntities(this.IsPinned);
            var es = _root.FindEntities(query);
            list.AddRange(es);

            if (list.Count == 0)
            {
                if (throwError)
                    throw new NotFoundDTEntityException("没有找到" + findExp + "对应的DTO实体！");
                return list.ToArray();
            }
            return list.ToArray();
        }

        //internal T[] FindEntities<T>(string findExp, bool throwError) where T : DTEntity
        //{
        //    List<T> list = new List<T>();
        //    var query = QueryExpression.Create(findExp);
        //    var es = _root.FindEntities(query);
        //    foreach (var e in es)
        //    {
        //        var temp = e as T;
        //        if (temp != null) list.Add(temp);
        //    }

        //    if (list.Count == 0)
        //    {
        //        if (throwError)
        //            throw new NotFoundDTEntityException("没有找到" + findExp + "对应的DTO实体！");
        //        return list.ToArray();
        //    }
        //    return list.ToArray();
        //}

        private T FindEntity<T>(string findExp, bool throwError) where T : DTEntity
        {
            DTEntity e = FindEntity(findExp, throwError);
            if (e == null) return null;
            T entity = e as T;
            if (entity == null && throwError)
                throw new DTOTypeErrorException("表达式" + findExp + "对应的DTO不是" + typeof(T).FullName + "！");
            return entity;
        }

        /// <summary>
        /// 是否为单值dto，即：{value}的形式
        /// </summary>
        public bool IsSingleValue
        {
            get
            {
                return _root.IsSingleValue();
            }
        }

        //internal void OrderEntities()
        //{
        //    _root.OrderEntities();
        //}


        /// <summary>
        /// 是否为纯值
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool IsPureValue(object value)
        {
            return !(value is DTObject || IsList(value));
        }

        private static bool IsList(object value)
        {
            return value is IEnumerable && !(value is string);
            //return value != null && (value is IList || value.GetType().IsAchieveOrEquals(typeof(IList<>)));
        }

        #endregion

        #region 数据

        public bool ContainsData()
        {
            return _root.ContainsData();
        }

        public void ClearData()
        {
            ValidateReadOnly();
            _root.ClearData();
        }

        /// <summary>
        /// 无视只读标记，强制清理数据
        /// </summary>
        internal void ForceClearData()
        {
            _root.ClearData();
        }

        public DTObject Clone()
        {
            return DTOPool.CreateObject(_root.Clone() as DTEObject, this.IsReadOnly, this.IsPinned);
        }

        #endregion

        #region 代码

        public string GetCode()
        {
            return GetCode(false);
        }

        public string GetCode(bool sequential)
        {
            return _root.GetCode(sequential);
        }

        public string GetSchemaCode()
        {
            return GetSchemaCode(false);
        }

        public string GetSchemaCode(bool sequential)
        {
            return _root.GetSchemaCode(sequential);
        }

        #endregion

        /// <summary>
        /// 是否为固定的
        /// </summary>
        public bool IsPinned
        {
            get;
            internal set;
        }

        #region 可重复使用的dto创建方法

        /// <summary>
        /// 完整的创建方法
        /// </summary>
        /// <param name="code"></param>
        /// <param name="isReadOnly"></param>
        /// <param name="isPinned"></param>
        /// <returns></returns>
        private static DTObject CreateComplete(string code, bool isReadOnly, bool isPinned)
        {
            var root = EntityDeserializer.Deserialize(code, isReadOnly, isPinned);
            return DTOPool.CreateObject(root, isReadOnly, isPinned);
        }

        /// <summary>
        /// 创建非只读的可重复使用的dto对象，该对象的使用周期与共生器同步
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public static DTObject CreateReusable(string code)
        {
            return CreateComplete(code, false, false);
        }

        /// <summary>
        /// 创建可重复使用的dto对象，该对象的使用周期与共生器同步
        /// </summary>
        /// <param name="code"></param>
        /// <param name="isReadOnly"></param>
        /// <returns></returns>
        public static DTObject CreateReusable(string code, bool isReadOnly)
        {
            return CreateComplete(code, isReadOnly, false);
        }

        /// <summary>
        /// 创建非只读的可重复使用的dto对象，该对象的使用周期与共生器同步
        /// </summary>
        /// <returns></returns>
        public static DTObject CreateReusable()
        {
            return CreateComplete("{}", false, false);
        }

        /// <summary>
        /// 创建非只读的可重复使用的dto对象，该对象的使用周期与共生器同步
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static DTObject CreateReusable(byte[] data)
        {
            var code = data.GetString(Encoding.UTF8);
            return CreateReusable(code);
        }

        /// <summary>
        /// 根据架构代码将对象的信息加载到dto中，该对象的使用周期与共生器同步
        /// </summary>
        /// <param name="schemaCode"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        public static DTObject CreateReusable(string schemaCode, object target)
        {
            return DTObjectMapper.Instance.Load(schemaCode, target, false);
        }

        #endregion

        #region 不会被共生器回收的dto创建方法


        /// <summary>
        /// 创建非只读的、不会被共生器回收的dto对象
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public static DTObject Create(string code)
        {
            return CreateComplete(code, false, true);
        }

        /// <summary>
        /// 创建不会被共生器回收的dto对象
        /// </summary>
        /// <param name="code"></param>
        /// <param name="isReadOnly"></param>
        /// <returns></returns>
        public static DTObject Create(string code, bool isReadOnly)
        {
            return CreateComplete(code, isReadOnly, true);
        }

        /// <summary>
        /// 创建非只读的、不会被共生器回收的dto对象
        /// </summary>
        /// <returns></returns>
        public static DTObject Create()
        {
            return CreateComplete("{}", false, true);
        }

        /// <summary>
        /// 创建非只读的、不会被共生器回收的dto对象
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static DTObject Create(byte[] data)
        {
            var code = data.GetString(Encoding.UTF8);
            return Create(code);
        }

        /// <summary>
        /// 根据架构代码将对象的信息加载到dto中，该对象不会被共生器回收
        /// </summary>
        /// <param name="schemaCode"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        public static DTObject Create(string schemaCode, object target)
        {
            return DTObjectMapper.Instance.Load(schemaCode, target, true);
        }

        #endregion

        /// <summary>
        /// 得到对象的不会被共生回收的版本
        /// </summary>
        /// <returns></returns>
        public DTObject ToPinned()
        {
            return this.IsPinned ? this : DTObject.Create(this.GetCode(), this.IsReadOnly);
        }


        #region 对象映射

        /// <summary>
        /// 根据架构代码，将dto的数据创建到新实例<paramref name="instanceType"/>中
        /// </summary>
        /// <param name="instanceType"></param>
        /// <param name="schemaCode"></param>
        /// <param name="dto"></param>
        /// <returns></returns>
        public object Save(Type instanceType, string schemaCode)
        {
            return DTObjectMapper.Instance.Save(instanceType, schemaCode, this);
        }

        /// <summary>
        /// 根据架构代码，将dto的数据创建到新实例<paramref name="instanceType"/>中
        /// </summary>
        /// <param name="instanceType"></param>
        /// <returns></returns>
        public object Save(Type instanceType)
        {
            return Save(instanceType, string.Empty);
        }

        /// <summary>
        /// 根据架构代码，将dto中的数据全部保存到类型为<typeparamref name="T"/>的实例中
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="schemaCode"></param>
        /// <returns></returns>
        public void Save<T>(T obj, string schemaCode)
        {
            var instanceType = typeof(T);
            DTObjectMapper.Instance.Save(obj, schemaCode, this);
        }

        /// <summary>
        /// 将dto中的数据全部保存到类型为<typeparamref name="T"/>的实例中
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public void Save<T>(T obj)
        {
            Save<T>(obj, string.Empty);
        }

        /// <summary>
        /// 根据架构代码，将dto中的数据全部保存到类型为<typeparamref name="T"/>的实例中
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="schemaCode"></param>
        /// <returns></returns>
        public T Save<T>(string schemaCode)
        {
            var instanceType = typeof(T);
            return (T)Save(instanceType, schemaCode);
        }

        /// <summary>
        /// 将dto中的数据全部保存到类型为<typeparamref name="T"/>的实例中
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Save<T>()
        {
            return Save<T>(string.Empty);
        }

        /// <summary>
        /// 根据架构代码将对象的信息加载到dto中
        /// </summary>
        /// <param name="schemaCode"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        public void Load(string schemaCode, object target)
        {
            DTObjectMapper.Instance.Load(this, schemaCode, target);
        }

        /// <summary>
        /// 将<paramref name="target"/>里面的所有属性的值加载到dto中
        /// </summary>
        /// <param name="target"></param>
        public void Load(object target)
        {
            Load(string.Empty, target);
        }

        #endregion

        #region 动态支持

        /// <summary>  
        /// 实现动态对象属性成员访问的方法，得到返回指定属性的值  
        /// </summary>  
        /// <param name="binder"></param>  
        /// <param name="result"></param>  
        /// <returns></returns>  
        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            result = GetValue(binder.Name, false);

            var objs = result as DTObjects;
            PrimitiveValueList values = null;
            if (objs != null && objs.TryGetSingleValues(out values))
            {
                result = values;
            }
            //return result != null;
            return true; //无论什么情况下都返回true，表示就算dto没有定义值，也可以获取null
        }

        /// <summary>  
        /// 实现动态对象属性值设置的方法。  
        /// </summary>  
        /// <param name="binder"></param>  
        /// <param name="value"></param>  
        /// <returns></returns>  
        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            SetValue(binder.Name, value);
            return true;
        }


        public override bool TryConvert(ConvertBinder binder, out object result)
        {
            result = Save(binder.Type);
            return true;
        }

        #endregion

        #region 序列化/反序列化

        /// <summary>
        /// 将dto对象反序列化到一个实体对象中,对象需要配置dto序列化特性
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Deserialize<T>()
        {
            return DTObject.Deserialize<T>(this);
        }

        /// <summary>
        /// 将dto对象反序列化到一个实体对象中,对象需要配置dto序列化特性
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dto"></param>
        /// <returns></returns>
        public static T Deserialize<T>(DTObject dto)
        {
            return DTObjectDeserializer.Instance.Deserialize<T>(dto);
        }


        /// <summary>
        /// 将对象的数据序列化到dto对象中,对象需要配置dto序列化特性
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="isPinned">对象生命周期是否依赖于共生器，true:不依赖 ，false:依赖</param>
        /// <returns></returns>
        public static DTObject Serialize(object obj, bool isPinned)
        {
            return DTObjectSerializer.Instance.Serialize(obj, isPinned);
        }

        #endregion


        #region 类型解析

        public static TypeMetadata GetMetadata(string metadataCode)
        {
            return new TypeMetadata(metadataCode);
        }


        #endregion

        public static readonly Type Type = typeof(DTObject);

        #region 空对象

        public static readonly DTObject Empty = DTObject.Create("{__empty:true}", true);

        public bool IsEmpty()
        {
            return this.GetValue<bool>("__empty", false);
        }

        #endregion

        public byte[] ToData()
        {
            return this.GetCode(false).GetBytes(Encoding.UTF8);
        }

        #region 唯一性

        public override bool Equals(object obj)
        {
            var target = obj as DTObject;
            if (target == null) return false;
            //sequential为true表示统一了键值对的排序，所以可以直接通过代码来比较是否相等
            return string.Equals(this.GetCode(true), target.GetCode(true), StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return this.GetCode(true).GetHashCode();
        }

        public static bool operator ==(DTObject a, DTObject b)
        {
            if ((object)a == null) return (object)b == null;
            if ((object)b == null) return false;
            return a.Equals(b);
        }

        public static bool operator !=(DTObject a, DTObject b)
        {
            return !(a == b);
        }

        #endregion

    }
}
