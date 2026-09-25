using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TNovCommon
{
    /// <summary>
    /// Параметр N_Эт.Номер: общий для плагина Эт.Номер (TNovUtilsAR) и автопроверки (TNovUtils).
    /// </summary>
    public static class LevelNumberParam
    {
        public const string CommandName = "Эт.Номер";
        public static readonly Guid Guid = new Guid("4d2aa1b8-727c-43a1-8b1e-8c22dd484e11");

        /// <summary>Номер этажа -> значение параметра во внутренних единицах</summary>
        public static double Encode(double number) => number / 0.3048 / 0.3048;

        /// <summary>Значение параметра во внутренних единицах -> номер этажа</summary>
        public static double Decode(double raw) => raw * 0.3048 * 0.3048;
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
        /// Группа элемента: Wall, Floor, Ceiling, Room, Stairs, Railing, FamilyInstance_* или Default (не обрабатывается)
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
        public int maxOffset { get; set; } = 3000;           //мм: допустимое смещение от уровня
        public int offsetCheckFromFloor { get; set; } = 2;   //проверять смещение начиная с этажа

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
        GeometryWrongOffset, //уровень есть, но смещение от него слишком большое, пересчитан по отметке
        Failed               //определить не удалось
    }

    public class LevelResolveResult
    {
        public Level Level { get; }
        public LevelSource Source { get; }
        public string Info { get; }
        public LevelResolveResult(Level level, LevelSource source, string info)
        {
            Level = level; Source = source; Info = info;
        }
        public bool ByGeometry => Source == LevelSource.GeometryNoLevel || Source == LevelSource.GeometryWrongOffset;
    }

    /// <summary>
    /// Определение уровня элемента для параметра N_Эт.Номер.
    /// Порядок: основной параметр уровня -> запасные параметры и основа -> отметка элемента.
    /// Если уровень назначен, но элемент смещен от него больше допустимого, уровень пересчитывается по отметке.
    /// </summary>
    public class LevelResolver
    {
        private readonly Document doc;
        private readonly List<Level> levels;       //по возрастанию отметки
        private readonly bool useGeometry;
        private readonly double nearTolerance;     //футы
        private readonly double maxOffset;         //футы
        private readonly int offsetCheckFromFloor;

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
            this.levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).ToList();
            this.useGeometry = settings.useGeometry;
            this.nearTolerance = Math.Max(0, settings.nearTolerance) / 304.8;
            this.maxOffset = Math.Max(0, settings.maxOffset) / 304.8;
            this.offsetCheckFromFloor = settings.offsetCheckFromFloor;
        }

        public LevelResolveResult Resolve(Element elem)
        {
            string kind = LevelNumberElements.GetKind(elem);
            Level level = GetAssignedLevel(elem, kind, out bool isFallback);

            if (level != null)
            {
                //проверка корректности привязки (у помещений уровень всегда корректен)
                if (useGeometry && kind != "Room" && ParseLevelNumber(level.Name) >= offsetCheckFromFloor)
                {
                    double? z = GetReferenceZ(elem);
                    if (z.HasValue)
                    {
                        double offset = z.Value - level.ProjectElevation;
                        if (Math.Abs(offset) > maxOffset)
                        {
                            Level levelByZ = GetLevelByZ(z.Value);
                            if (levelByZ != null && levelByZ.Id != level.Id)
                                return new LevelResolveResult(levelByZ, LevelSource.GeometryWrongOffset,
                                    "уровень " + level.Name + ", смещение " + (offset * 304.8).ToString("0") + " мм -> " + levelByZ.Name);
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
            Level levelByZ2 = GetLevelByZ(z2.Value);
            if (levelByZ2 == null)
                return new LevelResolveResult(null, LevelSource.Failed, "в проекте нет уровней");
            return new LevelResolveResult(levelByZ2, LevelSource.GeometryNoLevel,
                "уровень не назначен, отметка " + (z2.Value * 304.8).ToString("0") + " мм -> " + levelByZ2.Name);
        }

        /// <summary>
        /// Номер этажа из имени уровня: первая часть до пробела ("-01 -3.200 Подвал" -> -1)
        /// </summary>
        public static double ParseLevelNumber(string levelName)
        {
            string name = levelName.Replace("_", " ");
            name = name.Split(new char[] { ' ' })[0];
            if (name.Contains('.')) name = name.Split('.')[0];
            Double.TryParse(name, out double num);
            return num;
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
            else if (elem is Railing railing && railing.HostId != ElementId.InvalidElementId) host = doc.GetElement(railing.HostId);
            if (host is Level hostLevel) return hostLevel;
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
            //у моделей в контексте точка вставки не связана с геометрией - только BoundingBox
            FamilyInstance fi = elem as FamilyInstance;
            bool inPlace = fi != null && fi.Symbol?.Family?.IsInPlace == true;
            if (!inPlace)
            {
                Location loc = elem.Location;
                if (loc is LocationPoint lp && lp.Point != null) return lp.Point.Z;
                if (loc is LocationCurve lc && lc.Curve != null)
                    return Math.Min(lc.Curve.GetEndPoint(0).Z, lc.Curve.GetEndPoint(1).Z);
            }
            BoundingBoxXYZ bb = elem.get_BoundingBox(null);
            if (bb != null) return bb.Min.Z;
            return null;
        }

        /// <summary>
        /// Самый верхний уровень, отметка которого не выше Z + допуск.
        /// Т.е. уровень в пределах допуска считается ближайшим, иначе берется нижележащий.
        /// Если элемент ниже всех уровней - самый нижний уровень.
        /// </summary>
        private Level GetLevelByZ(double z)
        {
            if (levels.Count == 0) return null;
            Level result = null;
            foreach (Level level in levels)
            {
                if (level.ProjectElevation <= z + nearTolerance) result = level;
                else break;
            }
            return result ?? levels[0];
        }
    }
}
