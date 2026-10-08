using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TNovCommon
{
    /// <summary>
    /// Параметр N_Эт.Номер: общий для плагина Эт.Номер (TNovUtilsAR) и автопроверки (TNovUtils).
    /// </summary>
    public static class LevelNumberParam
    {
        public const string CommandName = "Эт.Номер";
        public static readonly Guid Guid = new Guid("4d2aa1b8-727c-43a1-8b1e-8c22dd484e11");

        /// <summary>Категории, к которым плагин сам добавляет параметр, если он к ним не назначен</summary>
        public static readonly BuiltInCategory[] AutoBindCategories =
        {
            BuiltInCategory.OST_NurseCallDevices, //Устройства вызова и оповещения
            BuiltInCategory.OST_EdgeSlab,         //Ребра плит
        };

        /// <summary>Номер этажа -> значение параметра во внутренних единицах</summary>
        public static double Encode(double number) => number / 0.3048 / 0.3048;

        /// <summary>Значение параметра во внутренних единицах -> номер этажа</summary>
        public static double Decode(double raw) => raw * 0.3048 * 0.3048;

        /// <summary>
        /// Добавляет категории в назначение параметра проекта N_Эт.Номер (нужна открытая транзакция).
        /// Возвращает имена добавленных категорий; error - почему добавить нельзя.
        /// </summary>
        public static List<string> EnsureBinding(Document doc, IEnumerable<BuiltInCategory> categories, out string error)
        {
            error = null;
            var added = new List<string>();
            InternalDefinition def = SharedParameterElement.Lookup(doc, Guid)?.GetDefinition();
            ElementBinding binding = def == null ? null : doc.ParameterBindings.get_Item(def) as ElementBinding;
            if (binding == null) { error = "параметр N_Эт.Номер не добавлен в проект"; return added; }

            foreach (BuiltInCategory bic in categories)
            {
                Category cat = Category.GetCategory(doc, bic);
                if (cat == null || !cat.AllowsBoundParameters || binding.Categories.Contains(cat)) continue;
                binding.Categories.Insert(cat);
                added.Add(cat.Name);
            }
            if (added.Count > 0 && !doc.ParameterBindings.ReInsert(def, binding, def.GetGroupTypeId()))
            {
                error = "не удалось добавить параметр N_Эт.Номер к категориям " + string.Join(", ", added);
                added.Clear();
            }
            return added;
        }

        /// <summary>
        /// Разрешает разные значения N_Эт.Номер у экземпляров групп (иначе у элементов в группах параметр только для чтения).
        /// Нужна открытая транзакция. true - настройка изменена.
        /// </summary>
        public static bool AllowVaryBetweenGroups(Document doc)
        {
            InternalDefinition def = SharedParameterElement.Lookup(doc, Guid)?.GetDefinition();
            if (def == null || def.VariesAcrossGroups) return false;
            def.SetAllowVaryBetweenGroups(doc, true);
            return true;
        }

        /// <summary>
        /// Параметр вложенного семейства, значение которого задается родительским семейством - не заполняем и не проверяем
        /// </summary>
        public static bool IsDrivenByParent(Element elem, Parameter p) =>
            p != null && p.IsReadOnly && elem is FamilyInstance fi && fi.SuperComponent != null;
    }

    /// <summary>
    /// Элементы, которым плагин Эт.Номер заполняет N_Эт.Номер, и их группы (для переключателей в окне).
    /// </summary>
    public static class LevelNumberElements
    {
        public static List<Element> Collect(Document doc)
        {
            List<Element> elems = new List<Element>();
            elems.AddRange(Of<Wall>(doc, BuiltInCategory.OST_Walls));
            elems.AddRange(Of<FamilyInstance>(doc, BuiltInCategory.OST_Walls));                 //стены семействами
            elems.AddRange(Of<Floor>(doc, BuiltInCategory.OST_Floors));
            elems.AddRange(Of<FamilyInstance>(doc, BuiltInCategory.OST_Floors));                //плиты (полы) семействами
            elems.AddRange(Of<Ceiling>(doc, BuiltInCategory.OST_Ceilings));
            elems.AddRange(Of<FamilyInstance>(doc, BuiltInCategory.OST_Ceilings));              //потолки семействами
            elems.AddRange(All(doc, BuiltInCategory.OST_Windows));
            elems.AddRange(All(doc, BuiltInCategory.OST_Doors));
            elems.AddRange(All(doc, BuiltInCategory.OST_StructuralFraming));
            elems.AddRange(All(doc, BuiltInCategory.OST_Rooms));
            elems.AddRange(All(doc, BuiltInCategory.OST_Parking));
            elems.AddRange(All(doc, BuiltInCategory.OST_Furniture));
            elems.AddRange(Of<FamilyInstance>(doc, BuiltInCategory.OST_GenericModel));
            elems.AddRange(All(doc, BuiltInCategory.OST_MechanicalEquipment));
            elems.AddRange(All(doc, BuiltInCategory.OST_SpecialityEquipment));
            elems.AddRange(All(doc, BuiltInCategory.OST_PlumbingFixtures));
            elems.AddRange(All(doc, BuiltInCategory.OST_NurseCallDevices));                     //устройства вызова и оповещения
            elems.AddRange(Of<HostedSweep>(doc, BuiltInCategory.OST_EdgeSlab).OfType<SlabEdge>()); //ребра плит (OfClass(SlabEdge) Revit не поддерживает)
            elems.AddRange(Of<Stairs>(doc, BuiltInCategory.OST_Stairs));
            elems.AddRange(Of<FamilyInstance>(doc, BuiltInCategory.OST_Stairs));                //лестницы семействами
            elems.AddRange(Of<Railing>(doc, BuiltInCategory.OST_StairsRailing));
            elems.AddRange(Of<FamilyInstance>(doc, BuiltInCategory.OST_StairsRailing));         //ограждения семействами
            return elems;
        }

        private static IEnumerable<Element> All(Document doc, BuiltInCategory bic) =>
            new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().ToElements();

        private static IEnumerable<Element> Of<T>(Document doc, BuiltInCategory bic) where T : Element =>
            new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().OfClass(typeof(T)).ToElements();

        /// <summary>
        /// Группа элемента: Wall, Floor, Ceiling, Room, Stairs, Railing, SlabEdge, FamilyInstance_* или Default (не обрабатывается)
        /// </summary>
        public static string GetKind(Element elem)
        {
            Type elementType = elem.GetType();
            if (elementType == typeof(Wall)) return "Wall";
            if (elementType == typeof(Floor)) return "Floor";
            if (elementType == typeof(Ceiling)) return "Ceiling";
            if (elementType == typeof(Room)) return "Room";
            if (elementType == typeof(Stairs)) return "Stairs";
            if (elementType == typeof(Railing)) return "Railing";
            if (elementType == typeof(SlabEdge)) return "SlabEdge";
            if (elementType != typeof(FamilyInstance) || elem.Category == null) return "Default";
#if R2022
            long catId = elem.Category.Id.IntegerValue;
#else
            long catId = elem.Category.Id.Value;
#endif
            switch (catId)
            {
                case -2000011: return "FamilyInstance_Wall";
                case -2000032: return "FamilyInstance_Floor";
                case -2000038: return "FamilyInstance_Ceiling";
                case -2000014: return "FamilyInstance_DoorWindow";
                case -2000023: return "FamilyInstance_DoorWindow";
                case -2001320:
                    return elem.Name.Contains("Аэратор") ? "FamilyInstance_Other" : "FamilyInstance_Beam";
                case -2001180: return "FamilyInstance_Parking";
                case -2000151:
                    FamilyInstance fi = elem as FamilyInstance;
                    string gmvalue = fi.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString();
                    if (gmvalue != null && gmvalue.Contains("Отверстие")) return "FamilyInstance_Hole";
                    return "FamilyInstance_Other";
                default: return "FamilyInstance_Other";
            }
        }
    }

    /// <summary>
    /// Настройки определения уровня. Хранятся в JSON проекта плагина Эт.Номер
    /// (те же поля, что у LevelNumberViewModel), автопроверка читает их оттуда.
    /// </summary>
    public class LevelNumberSettings
    {
        public bool useGeometry { get; set; } = true;
        public int nearTolerance { get; set; } = 1000;       //мм: уровень в пределах допуска считается ближайшим

        /// <summary>Настройки проекта (как у json с forProject), при ошибке или отсутствии файла - по умолчанию</summary>
        public static LevelNumberSettings Load(Document doc)
        {
            try
            {
                string userName = doc.Application.Username;
                string docName = doc.Title.Replace(",", " ").Replace("_" + userName, "");
                TNovConfig config = TNovConfigLoad.LoadConfig();
                string path = config.ServerPath + "projects/" + docName + "," + LevelNumberParam.CommandName + ".json";
                if (File.Exists(path))
                    return JsonConvert.DeserializeObject<LevelNumberSettings>(File.ReadAllText(path)) ?? new LevelNumberSettings();
            }
            catch (Exception) { }
            return new LevelNumberSettings();
        }
    }

    /// <summary>
    /// Способ, которым определен уровень элемента
    /// </summary>
    public enum LevelSource
    {
        Parameter,           //основной параметр уровня категории
        Fallback,            //запасной параметр (базовый уровень, уровень основы и т.п.)
        GeometryNoLevel,     //уровня нет, определен по отметке элемента
        GeometryWrongOffset, //уровень есть, но элемент за пределами этажа, пересчитан по отметке
        Failed               //определить не удалось
    }

    public class LevelResolveResult
    {
        public Level Level { get; }
        public LevelSource Source { get; }
        public string Info { get; }
        /// <summary>Значение N_Эт.Номер (номер этажа, 0 не бывает); null - определить не удалось</summary>
        public double? Number { get; }
        public LevelResolveResult(Level level, LevelSource source, string info, double? number = null)
        {
            Level = level; Source = source; Info = info; Number = number;
        }
        public bool ByGeometry => Source == LevelSource.GeometryNoLevel || Source == LevelSource.GeometryWrongOffset;
    }

    /// <summary>
    /// Определение уровня элемента для параметра N_Эт.Номер.
    /// Порядок: основной параметр уровня -> запасные параметры и основа -> отметка элемента.
    /// Если уровень назначен, но низ элемента за пределами этажа (от уровня этажа до уровня следующего этажа),
    /// уровень пересчитывается по отметке.
    /// Этаж - все уровни с одним кодом (01 -0.010 Этаж 1, 01 0.560 Верх цоколя), низ этажа - самый нижний из них.
    /// </summary>
    public class LevelResolver
    {
        private const double ConcreteMaxOffset = 1000 / 304.8; //монолит выше уровня больше чем на 1000 мм - без -1
        private const double Epsilon = 5 / 304.8;               //погрешность на границе этажей
        private const double LocationTolerance = 1000 / 304.8;  //точка вставки дальше от габаритов - не связана с геометрией

        private readonly Document doc;
        private readonly List<FloorInfo> floors;   //этажи по возрастанию отметки низа
        private readonly bool useGeometry;
        private readonly double nearTolerance;     //футы

        private sealed class FloorInfo
        {
            public double Number;
            public double Base;   //отметка самого нижнего уровня этажа
            public Level Level;   //этот уровень
        }

        //запасные параметры уровня, в порядке приоритета
        private static readonly BuiltInParameter[] fallbackParams =
        {
            BuiltInParameter.WALL_BASE_CONSTRAINT,           //Зависимость снизу
            BuiltInParameter.SCHEDULE_LEVEL_PARAM,
            BuiltInParameter.LEVEL_PARAM,
            BuiltInParameter.FAMILY_LEVEL_PARAM,
            BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,        //Базовый уровень (колонны)
            BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, //Опорный уровень (балки)
            BuiltInParameter.STAIRS_BASE_LEVEL_PARAM,        //Базовый уровень (лестницы)
            BuiltInParameter.STAIRS_RAILING_BASE_LEVEL_PARAM,
        };

        public LevelResolver(Document doc, LevelNumberSettings settings)
        {
            this.doc = doc;
            this.useGeometry = settings.useGeometry;
            this.nearTolerance = Math.Max(0, settings.nearTolerance) / 304.8;

            var numbered = new List<(double Number, Level Level)>();
            foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
                if (TryParseLevelNumber(level.Name, out double n)) numbered.Add((n, level));
            this.floors = numbered
                .GroupBy(x => x.Number)
                .Select(g =>
                {
                    Level lowest = g.Select(x => x.Level).OrderBy(l => l.ProjectElevation).First();
                    return new FloorInfo { Number = g.Key, Base = lowest.ProjectElevation, Level = lowest };
                })
                .OrderBy(f => f.Base)
                .ToList();
        }

        /// <summary>
        /// Уровень элемента и значение N_Эт.Номер для него
        /// </summary>
        public LevelResolveResult Resolve(Element elem)
        {
            string kind = LevelNumberElements.GetKind(elem);
            LevelResolveResult r = ResolveLevel(elem, kind);
            if (r.Level == null) return r;

            if (!TryParseLevelNumber(r.Level.Name, out double number))
                return new LevelResolveResult(r.Level, LevelSource.Failed,
                    "не удалось получить номер этажа из имени уровня '" + r.Level.Name + "'");

            string info = r.Info;
            //монолитная плита перекрывает нижележащий этаж: на уровне 01 -> -1, на уровне 02 -> 1
            //кроме плит, поднятых над уровнем больше чем на 1000 мм
            if (IsConcreteFloor(elem, kind))
            {
                BoundingBoxXYZ bb = elem.get_BoundingBox(null);
                double topOffset = bb != null ? bb.Max.Z - r.Level.ProjectElevation : 0;
                if (topOffset <= ConcreteMaxOffset)
                {
                    number -= 1;
                    info += ", монолитное перекрытие: номер на 1 меньше";
                }
                else
                    info += ", монолитное перекрытие выше уровня на " + (topOffset * 304.8).ToString("0") + " мм: номер не уменьшается";
            }
            if (number == 0) number = -1; //0 не назначаем

            return new LevelResolveResult(r.Level, r.Source, info, number);
        }

        /// <summary>
        /// Перекрытие (системное или семейством) с Группой модели, содержащей "Бетон"
        /// </summary>
        private bool IsConcreteFloor(Element elem, string kind)
        {
            if (kind != "Floor" && kind != "FamilyInstance_Floor") return false;
            Element type = doc.GetElement(elem.GetTypeId());
            string gm = type?.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString();
            return gm != null && gm.IndexOf("Бетон", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private LevelResolveResult ResolveLevel(Element elem, string kind)
        {
            Level level = GetAssignedLevel(elem, kind, out bool isFallback);

            if (level != null)
            {
                //проверка: низ элемента в пределах своего этажа (у помещений уровень всегда корректен)
                if (useGeometry && kind != "Room" && TryParseLevelNumber(level.Name, out double levelNumber))
                {
                    int i = floors.FindIndex(f => f.Number == levelNumber);
                    double? z = GetReferenceZ(elem);
                    if (i >= 0 && z.HasValue)
                    {
                        double lower = floors[i].Base - nearTolerance;
                        double upper = i + 1 < floors.Count ? floors[i + 1].Base - Epsilon : double.MaxValue;
                        if (z.Value < lower || z.Value >= upper)
                        {
                            FloorInfo floorByZ = GetFloorByZ(z.Value);
                            if (floorByZ != null && floorByZ.Number != levelNumber)
                                return new LevelResolveResult(floorByZ.Level, LevelSource.GeometryWrongOffset,
                                    "уровень " + level.Name + ", низ элемента на " + ((z.Value - level.ProjectElevation) * 304.8).ToString("0")
                                    + " мм от уровня - вне этажа -> " + floorByZ.Level.Name);
                        }
                    }
                }
                return new LevelResolveResult(level, isFallback ? LevelSource.Fallback : LevelSource.Parameter, level.Name);
            }

            if (!useGeometry)
                return new LevelResolveResult(null, LevelSource.Failed, "уровень не назначен");

            double? z2 = GetReferenceZ(elem);
            if (!z2.HasValue)
                return new LevelResolveResult(null, LevelSource.Failed, "уровень не назначен, отметку определить не удалось");
            FloorInfo floorByZ2 = GetFloorByZ(z2.Value);
            if (floorByZ2 == null)
                return new LevelResolveResult(null, LevelSource.Failed, "в проекте нет уровней с кодом этажа");
            return new LevelResolveResult(floorByZ2.Level, LevelSource.GeometryNoLevel,
                "уровень не назначен, отметка " + (z2.Value * 304.8).ToString("0") + " мм -> " + floorByZ2.Level.Name);
        }

        //код уровня в начале имени: "-01 -3.200 Подвал", "05_+12.850_Этаж 5", "01.1 ..." (разделитель - любой не-цифровой символ, в т.ч. неразрывный пробел)
        private static readonly Regex LevelCode = new Regex(@"^\s*([+\-−]?)\s*(\d+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Номер этажа из имени уровня: код до первого разделителя ("-01 -3.200 Подвал" -> -1)
        /// </summary>
        public static bool TryParseLevelNumber(string levelName, out double number)
        {
            number = 0;
            Match m = LevelCode.Match(levelName ?? "");
            if (!m.Success || !Double.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number)) return false;
            if (m.Groups[1].Value.Length > 0 && m.Groups[1].Value != "+") number = -number;
            return true;
        }

        private Level GetAssignedLevel(Element elem, string kind, out bool isFallback)
        {
            isFallback = false;

            //основной параметр по категории
            BuiltInParameter mainParam = BuiltInParameter.LEVEL_PARAM;
            if (kind == "Wall") mainParam = BuiltInParameter.WALL_BASE_CONSTRAINT;
            else if (kind == "Room") mainParam = BuiltInParameter.ROOM_LEVEL_ID;
            else if (kind == "Stairs") mainParam = BuiltInParameter.STAIRS_BASE_LEVEL_PARAM;
            else if (kind == "Railing") mainParam = BuiltInParameter.STAIRS_RAILING_BASE_LEVEL_PARAM;
            else if (kind.Contains("FamilyInstance")) mainParam = BuiltInParameter.SCHEDULE_LEVEL_PARAM;

            Level level = GetLevelFromParam(elem, mainParam);
            if (level != null) return level;

            isFallback = true;
            level = GetLevelFromParams(elem);
            if (level != null) return level;

            //уровень основы
            Element host = null;
            if (elem is FamilyInstance fi) host = fi.Host;
            else if (elem is Railing railing && railing.HostId != ElementId.InvalidElementId) host = doc.GetElement(railing.HostId);            if (host is Level hostLevel) return hostLevel;
            if (host != null) return GetLevelFromParams(host);
            return null;
        }

        private Level GetLevelFromParams(Element elem)
        {
            foreach (BuiltInParameter bip in fallbackParams)
            {
                Level level = GetLevelFromParam(elem, bip);
                if (level != null) return level;
            }
            if (elem.LevelId != null && elem.LevelId != ElementId.InvalidElementId)
                return doc.GetElement(elem.LevelId) as Level;
            return null;
        }

        private Level GetLevelFromParam(Element elem, BuiltInParameter bip)
        {
            Parameter p = elem.get_Parameter(bip);
            if (p == null || p.StorageType != StorageType.ElementId) return null;
            ElementId id = p.AsElementId();
            if (id == null || id == ElementId.InvalidElementId) return null;
            return doc.GetElement(id) as Level;
        }

        /// <summary>
        /// Отметка низа элемента (футы, от начала координат проекта)
        /// </summary>
        private double? GetReferenceZ(Element elem)
        {
            BoundingBoxXYZ bb = elem.get_BoundingBox(null);
            double? z = null;

            //у моделей в контексте точка вставки не связана с геометрией - только BoundingBox
            FamilyInstance fi = elem as FamilyInstance;
            bool inPlace = fi != null && fi.Symbol?.Family?.IsInPlace == true;
            if (!inPlace)
            {
                Location loc = elem.Location;
                if (loc is LocationPoint lp && lp.Point != null) z = lp.Point.Z;
                else if (loc is LocationCurve lc && lc.Curve != null)
                    z = Math.Min(lc.Curve.GetEndPoint(0).Z, lc.Curve.GetEndPoint(1).Z);
            }

            //точка вставки далеко от геометрии (адаптивные семейства и т.п. - точка в начале координат)
            if (z.HasValue && bb != null && (z.Value < bb.Min.Z - LocationTolerance || z.Value > bb.Max.Z + LocationTolerance))
                z = null;

            if (!z.HasValue && bb != null) z = bb.Min.Z;
            return z;
        }

        /// <summary>
        /// Самый верхний этаж, низ которого не выше Z + допуск.
        /// Т.е. уровень в пределах допуска считается ближайшим, иначе берется нижележащий.
        /// Если элемент ниже всех этажей - самый нижний этаж.
        /// </summary>
        private FloorInfo GetFloorByZ(double z)
        {
            if (floors.Count == 0) return null;
            FloorInfo result = null;
            foreach (FloorInfo floor in floors)
            {
                if (floor.Base <= z + nearTolerance) result = floor;
                else break;
            }
            return result ?? floors[0];
        }
    }
}
