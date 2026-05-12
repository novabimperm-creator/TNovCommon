using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;


namespace TNovCommon
{
    
    public class CombinedElementFilter
    {
        public static ElementFilter CombinedFilterST()
        {
            List<ElementFilter> filters = new List<ElementFilter>();

            // 1. Основные категории через OR фильтр
            BuiltInCategory[] mainCategories = new BuiltInCategory[]
            {
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_StairsRailing,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_StructConnections
            };

            foreach (var category in mainCategories)
            {
                filters.Add(new ElementCategoryFilter(category));
            }

            // 2. Для маршей и площадок используем фильтр по классу (более надежный)
            filters.Add(new ElementClassFilter(typeof(StairsRun)));
            filters.Add(new ElementClassFilter(typeof(StairsLanding)));

            // 3. Для специальных категорий создаем фильтр по ID категории
            // (даже если ID может отличаться в разных версиях)
            try
            {
                filters.Add(new ElementCategoryFilter(new ElementId(-2000123))); // Опоры лестниц
            }
            catch { }

            try
            {
                filters.Add(new ElementCategoryFilter(new ElementId(-2001392))); // Ребра плит
            }
            catch { }

            // 4. Дополнительные фильтры по классам для специфичных элементов
            filters.Add(new ElementClassFilter(typeof(Wall)));
            filters.Add(new ElementClassFilter(typeof(Floor)));
            filters.Add(new ElementClassFilter(typeof(Autodesk.Revit.DB.Architecture.Stairs)));
            filters.Add(new ElementClassFilter(typeof(Autodesk.Revit.DB.Architecture.Railing)));
            filters.Add(new ElementClassFilter(typeof(WallFoundation)));

            // 5. Фильтр для FamilyInstance с дополнительными проверками
            // Этот фильтр будет поймать большинство элементов, включая специальные
            filters.Add(new ElementClassFilter(typeof(FamilyInstance)));

            return new LogicalOrFilter(filters);
        }

        public static ElementFilter CombinedFilterOVVK()
        {
            List<ElementFilter> filters = new List<ElementFilter>();

            // 1. Основные категории через OR фильтр
            BuiltInCategory[] mainCategories = new BuiltInCategory[]
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
            BuiltInCategory.OST_PlumbingFixtures,

            };

            foreach (var category in mainCategories)
            {
                filters.Add(new ElementCategoryFilter(category));
            }

            

            // 5. Фильтр для FamilyInstance с дополнительными проверками
            // Этот фильтр будет поймать большинство элементов, включая специальные
            filters.Add(new ElementClassFilter(typeof(FamilyInstance)));

            return new LogicalOrFilter(filters);
        }

        public static ElementFilter CombinedFilterAR()
        {
            List<ElementFilter> filters = new List<ElementFilter>();

            // 1. Основные категории через OR фильтр
            BuiltInCategory[] mainCategories = new BuiltInCategory[]
            {
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_StairsRailing,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_Ceilings
            };

            foreach (var category in mainCategories)
            {
                filters.Add(new ElementCategoryFilter(category));
            }

            // 2. Для маршей и площадок используем фильтр по классу (более надежный)
            filters.Add(new ElementClassFilter(typeof(StairsRun)));
            filters.Add(new ElementClassFilter(typeof(StairsLanding)));

            // 3. Для специальных категорий создаем фильтр по ID категории
            // (даже если ID может отличаться в разных версиях)
            try
            {
                filters.Add(new ElementCategoryFilter(new ElementId(-2000123))); // Опоры лестниц
            }
            catch { }

            try
            {
                filters.Add(new ElementCategoryFilter(new ElementId(-2001392))); // Ребра плит
            }
            catch { }

            // 4. Дополнительные фильтры по классам для специфичных элементов
            filters.Add(new ElementClassFilter(typeof(Wall)));
            filters.Add(new ElementClassFilter(typeof(Floor)));
            filters.Add(new ElementClassFilter(typeof(Autodesk.Revit.DB.Architecture.Stairs)));
            filters.Add(new ElementClassFilter(typeof(Autodesk.Revit.DB.Architecture.Railing)));
            filters.Add(new ElementClassFilter(typeof(WallFoundation)));

            // 5. Фильтр для FamilyInstance с дополнительными проверками
            // Этот фильтр будет поймать большинство элементов, включая специальные
            filters.Add(new ElementClassFilter(typeof(FamilyInstance)));

            return new LogicalOrFilter(filters);
        }

        // Методы для получения всех элементов
        public static List<Element> GetAllElementsST(Document doc)
        {
            ElementFilter combinedFilter = CombinedFilterST();
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(combinedFilter).ToList();
        }
        public static List<Element> GetAllElementsOVVK(Document doc)
        {
            ElementFilter combinedFilter = CombinedFilterOVVK();
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(combinedFilter).ToList();
        }
        public static List<Element> GetAllElementsAR(Document doc)
        {
            ElementFilter combinedFilter = CombinedFilterAR();
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(combinedFilter).ToList();
        }
    }
}
