namespace TNovCommon
{
    /// <summary>
    /// Пути, которые не зависят от офиса (TNovConfig.ServerPath): общий журнал релизов
    /// и корпоративная BIM-библиотека. Держим их в одном месте, чтобы при переезде
    /// серверов менять одну строку, а не искать хардкоды по модулям.
    /// </summary>
    public static class TNovPaths
    {
        public const string ReleasesFile = @"\\fs-nova\Distr\0.For Admin\_TNov\releases";

        /// <summary>Корень корпоративной BIM-библиотеки.</summary>
        public const string BimLibrary = @"\\fs-nova\NOVA\04_БИБЛИОТЕКА\BIM";

        /// <summary>Excel-справочники ВК/ОВ (толщины воздуховодов, PEX, немоделируемые).</summary>
        public const string MepTables = BimLibrary + @"\ВК_ОВ_Семейства\_TNov";

        /// <summary>ФОП Новация.</summary>
        public const string SharedParametersFile = BimLibrary + @"\_ФОП\ФОП Новация.txt";
    }
}
