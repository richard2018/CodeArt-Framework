﻿using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Text;


namespace CodeArt.DTO
{
    internal sealed class DTEList : DTEntity,IEnumerable<DTObject>
    {
        public override DTEntityType Type => DTEntityType.List;

        internal DTObject ItemTemplate
        {
            get;
            private set;
        }

        public DTObjectList Items
        {
            get;
            private set;
        }

        private void SetList(List<DTObject> items)
        {
            Items = DTOPool.CreateObjectList(this, this.IsPinned);
        
            if (items.Count == 0)
                this.ItemTemplate = this.IsPinned ? DTObject.Create() : DTObject.CreateReusable();
            else
            {
                if (items.Count == 1)
                {
                    DTObject item = items[0];
                    if (item.ContainsData()) Items.Add(item);
                    this.ItemTemplate = item.Clone();
                }
                else
                {
                    foreach (var item in items)
                    {
                        Items.Add(item);
                    }
                    this.ItemTemplate = Items[0].Clone();
                }
                this.ItemTemplate.ForceClearData();
            }
        }

        public DTEList()
        {
        }

        public void Init(bool isPinned, List<DTObject> items)
        {
            this.IsPinned = isPinned;
            this.SetList(items);
        }


        public override void Reset()
        {
            Items.Clear();
            this.Items = null;
            this.ItemTemplate = null;
            _objects = null;
            base.Reset();
        }

        #region 数据

        /// <summary>
        /// 仅克隆结构
        /// </summary>
        /// <returns></returns>
        public override DTEntity Clone()
        {
            return DTOPool.CreateDTEList(this.Name, this.ItemTemplate, this.Items, this.IsPinned);
        }

        /// <summary>
        /// 该构造函数仅供克隆时使用
        /// </summary>
        /// <param name="template"></param>
        /// <param name="items"></param>
        internal void InitByClone(string name, DTObject template, DTObjectList items,bool isPinned)
        {
            this.IsPinned = isPinned;
            this.Name = name;
            this.ItemTemplate = template;
            this.Items = DTOPool.CreateObjectList(this, this.IsPinned);
            if (items.Count > 0)
            {
                foreach (var item in items)
                {
                    this.Items.Add(item.Clone());
                }
            }
        }

        public override bool ContainsData()
        {
            foreach (DTObject obj in Items)
                if (obj.ContainsData()) return true;
            return false;
        }

        public override void ClearData()
        {
            Items.Clear();
            this.Changed();
        }

        #endregion

        #region 实体管理

        public override IEnumerable<DTEntity> FindEntities(QueryExpression query)
        {
            if (query.IsSelfEntities) return this.GetSelfEntities(); //*代表返回对象自己
            List<DTEntity> list = DTOPool.CreateDTEntities(this.IsPinned);
            foreach (var e in Items)
            {
                var es = e.GetRoot().FindEntities(query);
                list.AddRange(es);
            }
            return list;
        }


        public override void DeletEntity(DTEntity entity)
        {
            DTObject taget = null;
            foreach (var child in Items)
            {
                if (child.GetRoot() == entity)
                {
                    taget = child;
                    break;
                }
            }
            if(taget != null) Items.Remove(taget);
            this.Changed();
        }


        public override void SetEntity(QueryExpression query, Func<string, DTEntity> createEntity)
        {
            this.ItemTemplate.GetRoot().SetEntity(query, createEntity);
            this.ItemTemplate.ClearData();

            foreach (var item in Items)
            {
                item.GetRoot().SetEntity(query, createEntity);
            }
            this.Changed();
        }

        //public override void OrderEntities()
        //{
        //    foreach (DTObject obj in _list)
        //    {
        //        obj.OrderEntities();
        //    }
        //    Changed();
        //}

        public void RetainAts(IList<int> indexs)
        {
            Items.RetainAts(indexs);
            this.Changed();
        }

        public void RemoveAts(IList<int> indexs)
        {
            Items.RemoveAts(indexs);
            this.Changed();
        }

        #endregion

        /// <summary>
        /// 新建一个子项，并将新建的子项加入集合中
        /// </summary>
        /// <returns></returns>
        public void CreateAndPush(Action<DTObject> fill)
        {
            DTObject obj = this.ItemTemplate.Clone();
            Items.Add(obj);
            if (fill != null) fill(obj);
            this.Changed();
        }

        public DTObject CreateAndPush()
        {
            DTObject obj = this.ItemTemplate.Clone();
            Items.Add(obj);
            this.Changed();
            return obj;
        }

        public void Push(DTObject item)
        {
            Items.Add(item);
            this.Changed();
        }

        private DTObjects _objects;

        public DTObjects GetObjects()
        {
            if(_objects == null)
            {
                _objects = DTOPool.CreateDTOjects(Items,this.IsPinned);
            }
            return _objects;
        }

        public int Count
        {
            get
            {
                return Items.Count;
            }
        }

        public DTObject this[int index]
        {
            get
            {
                return Items[index];
            }
        }

        public IEnumerator<DTObject> GetEnumerator()
        {
            return Items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Items.GetEnumerator();
        }

        public override void Changed()
        {
            _objects = null;
            if (this.Parent != null)
                this.Parent.Changed();
        }



        #region 代码

        public override string GetCode(bool sequential)
        {
            StringBuilder code = new StringBuilder();
            if (!string.IsNullOrEmpty(this.Name))
                code.AppendFormat("\"{0}\"", this.Name);

            if (code.Length > 0) code.Append(":");
            code.Append("[");
            foreach (DTObject item in Items)
            {
                var itemCode = item.GetCode(sequential);
                code.Append(itemCode);
                code.Append(",");
            }
            if (Items.Count > 0) code.Length--;
            code.Append("]");
            return code.ToString();
        }

        public override string GetSchemaCode(bool sequential)
        {
            StringBuilder code = new StringBuilder();
            if (!string.IsNullOrEmpty(this.Name))
                code.AppendFormat("\"{0}\"", this.Name);

            if (code.Length > 0) code.Append(":");
            code.Append("[");
            code.Append(this.ItemTemplate.GetSchemaCode(sequential));
            code.Append("]");
            return code.ToString();
        }

        #endregion

    }
}