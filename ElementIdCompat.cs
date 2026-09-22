using Autodesk.Revit.DB;

namespace TNovCommon
{
    /// <summary>
    /// Совместимость ElementId между Revit 2022 и 2027.
    ///
    /// В Revit 2027 из API убрали ElementId.IntegerValue (устаревшее с 2024);
    /// замена — ElementId.Value типа long. Здесь одно место, где эта разница
    /// закрыта, чтобы не расставлять #if по всему решению.
    ///
    /// IntValue сознательно возвращает int, а не long: так сохраняется тип и
    /// значение, к которым привязан существующий код (ключи словарей, приведения
    /// к BuiltInCategory, сравнения с -1). Приведение checked — если id когда-то
    /// не уложится в int, это упадёт громко, а не обрежется молча.
    /// </summary>
    public static class ElementIdCompat
    {
        /// <summary>Числовое значение id как int — прямая замена ElementId.IntegerValue.</summary>
        public static int IntValue(this ElementId id)
        {
#if R2022
            return id.IntegerValue;
#else
            return checked((int)id.Value);
#endif
        }

        /// <summary>Числовое значение id без сужения. Для сравнений, логов и ToString.</summary>
        public static long LongValue(this ElementId id)
        {
#if R2022
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }

        /// <summary>ElementId из числа: в 2027 конструктор принимает long.</summary>
        public static ElementId ToElementId(int value)
        {
#if R2022
            return new ElementId(value);
#else
            return new ElementId((long)value);
#endif
        }

        /// <summary>ElementId из числа, когда источник уже long.</summary>
        public static ElementId ToElementId(long value)
        {
#if R2022
            return new ElementId(checked((int)value));
#else
            return new ElementId(value);
#endif
        }
    }
}
