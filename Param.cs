using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TNovCommon
{
    public class Param
    {
        public static bool ParamExist(in string pName, Element elem)
        {
            foreach (Parameter p in elem.ParametersMap)
            {
                string paramName = p.Definition.Name;
                if (paramName == pName) { return true; }
            }
            return false;
        }
        public static bool ParamExistByGuid(in Guid pGuid, Element elem)
        {
            foreach (Parameter p in elem.ParametersMap)
            {
                if (p.IsShared)
                {
                    Guid paramGuid = p.GUID;
                    if (paramGuid == pGuid) { return true; }
                }
            }
            return false;
        }
        public static double GetDoubleParamValue(Document doc, in Guid pGuid, Element elem)
        {
            if (ParamExistByGuid(pGuid, elem))
            {
                Parameter param = elem.get_Parameter(pGuid);
                if (param != null && param.HasValue) return param.AsDouble();
            }
            else
            {
                ElementId typeId = elem.GetTypeId();
                if (typeId != null && typeId.IntegerValue != -1)
                {
                    Element type = doc.GetElement(typeId);
                    if (ParamExistByGuid(pGuid, type))
                    {
                        Parameter param = type.get_Parameter(pGuid);
                        if (param != null && param.HasValue) return param.AsDouble();
                    }
                }
            }
            return 0;
        }
        public static string GetStringParamValue(Document doc, in Guid pGuid, Element elem)
        {
            if (ParamExistByGuid(pGuid, elem))
            {
                Parameter param = elem.get_Parameter(pGuid);
                if (param != null && param.HasValue) return param.AsString();
            }
            else
            {
                ElementId typeId = elem.GetTypeId();
                if (typeId != null && typeId.IntegerValue != -1)
                {
                    Element type = doc.GetElement(typeId);
                    if (ParamExistByGuid(pGuid, type))
                    {
                        Parameter param = type.get_Parameter(pGuid);
                        if (param != null && param.HasValue) return param.AsString();
                    }
                }
            }
            return "";
        }
        public static string GetStringParamValue(Document doc, in BuiltInParameter builtInParameter, Element elem)
        {
            Parameter param = elem.get_Parameter(builtInParameter);
            if(param != null && param.HasValue) return param.AsString();
            else
            {
                ElementId typeId = elem.GetTypeId();
                if (typeId != null && typeId.IntegerValue != -1)
                {
                    Element type = doc.GetElement(typeId);
                    param = type.get_Parameter(builtInParameter);
                    if (param != null && param.HasValue) return param.AsString();
                }
            }
            return "";
        }
        public static string GetStringParamValue(Document doc, in string pName, Element elem)
        {
            if (ParamExist(pName, elem))
            {
                Parameter param = elem.LookupParameter(pName);
                if (param != null && param.HasValue) return param.AsString();
            }
            else
            {
                ElementId typeId = elem.GetTypeId();
                if (typeId != null && typeId.IntegerValue != -1)
                {
                    Element type = doc.GetElement(typeId);
                    if (ParamExist(pName, type))
                    {
                        Parameter param = type.LookupParameter(pName);
                        if (param != null && param.HasValue) return param.AsString();
                    }
                }
            }
            return "";
        }
    }
}
