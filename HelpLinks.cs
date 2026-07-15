using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace TNovCommon
{
    public static class HelpLinks
    {
        public static string GetHelpLink(string funcName)
        {
            switch (funcName)
            {
                case "-": 
                    return @"https://portal.talan.group/knowledge/proektirovanie/";
                case "Перемычки":
                    return @"https://portal.talan.group/knowledge/proektirovanie/vedomostperemychek/";
                case "Журнал проекта":
                    return @"https://portal.talan.group/knowledge/proektirovanie/reestrzamechaniy/";
                case "BIM Экспорт":
                    return @"https://portal.talan.group/knowledge/proektirovanie/eksportmodeleyvnavisworks/";
                case "Лотки":
                    return @"https://portal.talan.group/knowledge/proektirovanie/lotki/";
                case "Генератор полов":
                    return @"https://portal.talan.group/knowledge/proektirovanie/poly/";
                case "Сводная спека":
                    return @"https://portal.talan.group/knowledge/proektirovanie/MEPspec/";
                case "Парковки":
                    return @"https://portal.talan.group/knowledge/proektirovanie/parking/";
                case "Сваи":
                    return @"https://portal.talan.group/knowledge/proektirovanie/svai_xmqe/";
                case "Номера помещений Ручной":
                    return @"https://portal.talan.group/knowledge/proektirovanie/kladovye/";
                case "Помещения":
                    return @"https://portal.talan.group/knowledge/proektirovanie/pomeshcheniya/";
                case "Квартирография":
                    return @"https://portal.talan.group/knowledge/proektirovanie/kvartirografiya/";
                case "Помещения Резервные копии":
                    return @"https://portal.talan.group/knowledge/proektirovanie/pomeshcheniyarezervnoekopirovanieivosstanovlenie/";
                case "Отметки Вырезание":
                    return @"https://portal.talan.group/knowledge/proektirovanie/samostoyatelnoemodelirovanieotverstiy/";
                case "Задания":
                    return @"https://portal.talan.group/knowledge/proektirovanie/MEPtasks/";
                case "Закреплятор":
                    return @"https://portal.talan.group/knowledge/proektirovanie/zakreplyatorurovninabory/";
                case "Типофильтр":
                    return @"https://portal.talan.group/knowledge/proektirovanie/tipofiltr/";
                case "Вопросы":
                    return @"https://portal.talan.group/knowledge/proektirovanie/tnovpromodulvoprosy/";
                case "Группировка":
                    return @"https://portal.talan.group/knowledge/proektirovanie/skhemaraspolozheniyakonstruktsiy/";
                case "ВРС подчистить":
                    return @"https://portal.talan.group/knowledge/proektirovanie/vedomostraskhodastali/";
                case "ADSK Стенки":
                    return @"https://portal.talan.group/knowledge/proektirovanie/MEPductthickness/";
                case "Схемы вентиляции":
                    return @"https://portal.talan.group/knowledge/proektirovanie/MEPviews/";
                case "Изменения":
                    return @"https://portal.talan.group/knowledge/proektirovanie/oformlenie/";
                case "Менеджер листов":
                    return @"https://portal.talan.group/knowledge/proektirovanie/listynumeratsiyaikomplektynaeksport/";
                default: return @"https://portal.talan.group/knowledge/proektirovanie/";
            }
        }
    }
}
