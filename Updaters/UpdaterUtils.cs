using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TNovCommon
{
    /// <summary>
    /// Безопасные операции с параметрами для обновителей (IUpdater).
    /// Ни один метод не бросает исключений на штатных данных Revit:
    /// отсутствующая категория, отсутствующий или чужой по типу параметр — это null/false,
    /// а не NullReferenceException, который Revit превращает в предложение отключить обновитель.
    /// </summary>
    public static class UpdaterUtils
    {
        /// <summary>Общий параметр экземпляра по GUID или null.</summary>
        public static Parameter GetParam(Element elem, Guid guid)
        {
            if (elem == null) return null;
            try
            {
                Parameter p = elem.get_Parameter(guid);
                if (p != null) return p;
            }
            catch { }
            return null;
        }

        /// <summary>Общий параметр экземпляра по GUID, если он есть и доступен на запись.</summary>
        public static Parameter GetWritableParam(Element elem, Guid guid)
        {
            Parameter p = GetParam(elem, guid);
            return p != null && !p.IsReadOnly ? p : null;
        }

        /// <summary>Все общие параметры экземпляра одним обходом ParametersMap.</summary>
        public static Dictionary<Guid, Parameter> GetSharedParameters(Element elem)
        {
            var map = new Dictionary<Guid, Parameter>();
            if (elem == null) return map;
            try
            {
                foreach (Parameter p in elem.ParametersMap)
                {
                    if (p == null || !p.IsShared) continue;
                    Guid guid = p.GUID;
                    if (!map.ContainsKey(guid)) map.Add(guid, p);
                }
            }
            catch { }
            return map;
        }

        /// <summary>
        /// Признак "Т параметры не заполнять" (Н_Т параметры не назначать).
        /// Учитываются оба варианта хранения: целое и вещественное.
        /// </summary>
        public static bool IsSkipFlagSet(Element elem, Guid flagGuid)
        {
            return IsSkipFlagSet(GetParam(elem, flagGuid));
        }

        public static bool IsSkipFlagSet(Parameter p)
        {
            if (p == null || !p.HasValue) return false;
            try
            {
                if (p.StorageType == StorageType.Integer) return p.AsInteger() == 1;
                if (p.StorageType == StorageType.Double) return Math.Abs(p.AsDouble() - 1.0) < 1e-9;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Запись строки. Повторная запись того же значения всё равно помечает элемент
        /// изменённым и заново запускает обновители, поэтому пишем только при отличии.
        /// </summary>
        public static bool TrySetString(Parameter p, string value)
        {
            if (p == null || value == null) return false;
            if (p.IsReadOnly || p.StorageType != StorageType.String) return false;
            try
            {
                if (string.Equals(p.AsString(), value, StringComparison.Ordinal)) return false;
                return p.Set(value);
            }
            catch { return false; }
        }

        public static bool TrySetDouble(Parameter p, double value)
        {
            if (p == null) return false;
            if (p.IsReadOnly || p.StorageType != StorageType.Double) return false;
            try
            {
                if (p.HasValue && Math.Abs(p.AsDouble() - value) < 1e-9) return false;
                return p.Set(value);
            }
            catch { return false; }
        }

        public static bool TrySetInteger(Parameter p, int value)
        {
            if (p == null) return false;
            if (p.IsReadOnly || p.StorageType != StorageType.Integer) return false;
            try
            {
                if (p.HasValue && p.AsInteger() == value) return false;
                return p.Set(value);
            }
            catch { return false; }
        }

        public static bool TrySetElementId(Parameter p, ElementId value)
        {
            if (p == null || value == null) return false;
            if (p.IsReadOnly || p.StorageType != StorageType.ElementId) return false;
            try
            {
                if (p.HasValue && p.AsElementId() == value) return false;
                return p.Set(value);
            }
            catch { return false; }
        }

        /// <summary>Строковое значение параметра или "" (никогда не null).</summary>
        public static string GetStringSafe(Parameter p)
        {
            if (p == null || !p.HasValue) return "";
            try
            {
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                return p.AsValueString() ?? "";
            }
            catch { return ""; }
        }

        /// <summary>Id категории элемента или 0, если категории нет.</summary>
        public static long CategoryId(Element elem)
        {
            if (elem == null) return 0;
            Category category = elem.Category;
            if (category == null) return 0;
            return IdValue(category.Id);
        }

        public static long IdValue(ElementId id)
        {
            if (id == null) return -1;
#if R2022
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }

        public static string IdText(ElementId id)
        {
            return IdValue(id).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Типоразмер элемента или null.</summary>
        public static Element GetElementType(Document doc, Element elem)
        {
            if (doc == null || elem == null) return null;
            try
            {
                ElementId typeId = elem.GetTypeId();
                if (typeId == null || IdValue(typeId) == -1) return null;
                return doc.GetElement(typeId);
            }
            catch { return null; }
        }
    }
}
