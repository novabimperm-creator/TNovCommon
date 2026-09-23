using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TNovCommon
{
    /// <summary>
    /// Постпроверка ADSK для моделей ВК/ОВ: ADSK_Количество и ADSK_Группирование.
    /// Общая для сценария ВК ОВ в MEPSpec и автопроверки Чек-листа — правила меняются только здесь.
    /// Только чтение, транзакция не нужна.
    /// </summary>
    public static class VkovPostcheck
    {
        static readonly Guid adskGparamGuid = new Guid("3de5f1a4-d560-4fa8-a74f-25d250fb3401");//ADSK_Группирование
        static readonly Guid adskNparamGuid = new Guid("e6e0f5cd-3e26-485b-9342-23882b20eb43");//ADSK_Наименование
        static readonly Guid adskCparamGuid = new Guid("8d057bb3-6ccd-4655-9165-55526691fe3a");//ADSK_Количество

        static readonly BuiltInCategory[] Categories =
        {
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_DuctLinings,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctInsulations,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeInsulations,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PlumbingFixtures
        };

        static readonly HashSet<BuiltInCategory> LengthCategories = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_PipeInsulations,
            BuiltInCategory.OST_DuctInsulations
        };

        public const string SkipNaimValue = "!Не учитывать";
        public const double LengthLimitMm = 500;

        public static List<Element> Collect(Document doc)
        {
            var result = new List<Element>();
            if (doc == null) return result;
            foreach (BuiltInCategory cat in Categories)
            {
                result.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements());
            }
            return result;
        }

        public static List<VkovPostcheckIssue> FindIssues(IEnumerable<Element> elements)
        {
            var issues = new List<VkovPostcheckIssue>();
            foreach (Element elem in elements ?? Enumerable.Empty<Element>())
            {
                if (elem == null) continue;

                string naim = GetGuidStringInstanceOrType(elem, adskNparamGuid);
                if (naim != null && naim.Trim() == SkipNaimValue)
                    continue;

                var problems = new List<string>();

                // Изоляция с нерассчитанной «Длиной»: ADSK_Количество = 0 / не назначено — не ошибка
                if (!IsInsulationWithUncalculatedLength(elem))
                {
                    Parameter qtyParam = GetAssignedParamInstanceOrType(elem, adskCparamGuid);
                    if (qtyParam == null)
                        problems.Add("Количество не назначено");
                    else if (IsNumericZero(qtyParam))
                    {
                        if (IsLengthCategory(elem) && TryGetLengthMm(elem, out double lengthMm) && lengthMm > LengthLimitMm)
                            problems.Add("Количество = 0 при длине > 500 мм");
                    }
                }

                Parameter groupParam = GetAssignedParamInstanceOrType(elem, adskGparamGuid);
                string grouping = GetParameterString(groupParam);
                if (groupParam == null || string.IsNullOrWhiteSpace(grouping))
                    problems.Add("Группирование не заполнено");

                if (problems.Count == 0) continue;

                issues.Add(new VkovPostcheckIssue
                {
                    ElementId = elem.Id,
                    Category = elem.Category != null ? elem.Category.Name : "",
                    Grouping = grouping ?? "",
                    Problems = problems
                });
            }
            return issues;
        }

        static Parameter GetAssignedParamInstanceOrType(Element elem, Guid guid)
        {
            if (elem == null) return null;
            Parameter instance = null;
            if (Param.ParamExistByGuid(guid, elem))
                instance = elem.get_Parameter(guid);
            if (instance != null && instance.HasValue)
                return instance;

            ElementId typeId = elem.GetTypeId();
            if (typeId != null)
            {
                Element type = elem.Document.GetElement(typeId);
                if (type != null && Param.ParamExistByGuid(guid, type))
                {
                    Parameter typeParam = type.get_Parameter(guid);
                    if (typeParam != null && typeParam.HasValue)
                        return typeParam;
                }
            }

            return null;
        }

        static string GetGuidStringInstanceOrType(Element elem, Guid guid)
        {
            if (elem == null) return null;
            Parameter instance = null;
            if (Param.ParamExistByGuid(guid, elem))
                instance = elem.get_Parameter(guid);
            string instanceVal = GetParameterString(instance);
            if (!string.IsNullOrWhiteSpace(instanceVal))
                return instanceVal;

            ElementId typeId = elem.GetTypeId();
            if (typeId == null) return instanceVal;
            Element type = elem.Document.GetElement(typeId);
            if (type == null || !Param.ParamExistByGuid(guid, type))
                return instanceVal;
            string typeVal = GetParameterString(type.get_Parameter(guid));
            return !string.IsNullOrWhiteSpace(typeVal) ? typeVal : instanceVal;
        }

        static string GetParameterString(Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            string value = p.AsString();
            if (value == null) value = p.AsValueString();
            return value;
        }

        static bool IsNumericZero(Parameter p)
        {
            if (p == null || !p.HasValue) return false;
            if (p.StorageType == StorageType.Double)
                return Math.Abs(p.AsDouble()) < 0.0000001;
            if (p.StorageType == StorageType.Integer)
                return p.AsInteger() == 0;
            return false;
        }

        static bool IsLengthCategory(Element elem)
        {
            if (elem?.Category == null) return false;
            return LengthCategories.Contains((BuiltInCategory)elem.Category.Id.IntValue());
        }

        static bool IsInsulationWithUncalculatedLength(Element elem)
        {
            if (!(elem is InsulationLiningBase)) return false;
            Parameter dlina = elem.LookupParameter("Длина");
            return dlina != null && !dlina.HasValue;
        }

        static bool TryGetLengthMm(Element elem, out double lengthMm)
        {
            lengthMm = 0;
            if (elem == null) return false;

            if (elem is InsulationLiningBase)
            {
                Parameter isolLength = elem.LookupParameter("Длина");
                if (isolLength != null && !isolLength.HasValue)
                    return false;
                if (isolLength != null && isolLength.HasValue && isolLength.StorageType == StorageType.Double)
                {
                    lengthMm = isolLength.AsDouble() * 304.8;
                    return true;
                }
            }

            Parameter curveLength = elem.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (curveLength != null && curveLength.HasValue)
            {
                lengthMm = curveLength.AsDouble() * 304.8;
                return true;
            }

            Parameter dlina = elem.LookupParameter("Длина");
            if (dlina != null && dlina.HasValue && dlina.StorageType == StorageType.Double)
            {
                lengthMm = dlina.AsDouble() * 304.8;
                return true;
            }

            return false;
        }
    }

    public sealed class VkovPostcheckIssue
    {
        public ElementId ElementId { get; set; }
        public string Category { get; set; }
        public string Grouping { get; set; }
        public List<string> Problems { get; set; }
    }
}
