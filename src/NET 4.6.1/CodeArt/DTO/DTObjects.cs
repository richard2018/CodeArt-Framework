﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Text;
using System.Linq;
using System.Diagnostics;

namespace CodeArt.DTO
{
    [DebuggerDisplay("{GetCode(false)}")]
    public class DTObjects : IEnumerable<DTObject>
    {
        private IList<DTObject> _list;

        internal void SetList(IList<DTObject> items)
        {
            _list = items;
        }

        internal DTObjects()
        {
        }

        internal DTObjects(List<DTObject> items)
        {
            SetList(items);
        }

        public DTObject this[int index]
        {
            get
            {
                return _list[index];
            }
        }

        public IEnumerator<DTObject> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        public int Count
        {
            get
            {
                return _list.Count;
            }
        }

        public bool Contains(DTObject item)
        {
            return _list.Contains(item);
        }

        public void CopyTo(DTObject[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        public int IndexOf(DTObject item)
        {
            return _list.IndexOf(item);
        }

        public bool IsReadOnly
        {
            get
            {
                return true;
            }
        }

        public void Reset()
        {
            _list.Clear();
            _list = null;
        }

        #region 自定义方法

        public DTObject[] ToArray()
        {
            return _list.ToArray();
        }

        public T[] ToArray<T>()
        {
            T[] data = new T[_list.Count];
            for (var i = 0; i < _list.Count; i++)
            {
                data[i] = _list[i].GetValue<T>();
            }
            return data;
        }

        public T[] ToArray<T>(Func<DTObject, T> func)
        {
            T[] data = new T[_list.Count];
            for (var i = 0; i < _list.Count; i++)
            {
                data[i] = func(_list[i]);
            }
            return data;
        }

        public string GetCode(bool sequential)
        {
            StringBuilder code = new StringBuilder();
            code.Append("[");
            foreach (DTObject item in _list)
            {
                code.Append(item.GetCode(sequential));
                code.Append(",");
            }
            if (_list.Count > 0) code.Length--;
            code.Append("]");
            return code.ToString();
        }

        #endregion

        /// <summary>
        /// 集合项是否为单值的
        /// </summary>
        /// <returns></returns>
        public bool ItemIsSingleValue()
        {
            return this.Count > 0 && this[0].IsSingleValue;
        }

        /// <summary>
        /// 尝试将集合转换为单值集合
        /// </summary>
        /// <param name="values"></param>
        /// <returns></returns>
        public bool TryGetSingleValues(out PrimitiveValueList values)
        {
            values = null;
            if (ItemIsSingleValue())
            {
                values = new PrimitiveValueList(this.Select((v) => v.GetValue()));
                return true;
            }
            return false;
        }


        public readonly static DTObjects Empty = new DTObjects(DTOPool.CreateObjects(true));

    }
}