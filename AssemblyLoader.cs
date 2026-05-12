using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace TNovCommon
{
    public static class AssemblyLoader
    {
        public static void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
        }

        private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            // Определяем, какую сборку ищем
            string assemblyName = new AssemblyName(args.Name).Name;
            if (assemblyName != "Npgsql")
                return null;

            // Загружаем из ресурсов
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = "TNovCommon.Resources.Npgsql.dll"; // замените на точное имя ресурса
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                byte[] rawAssembly = new byte[stream.Length];
                stream.Read(rawAssembly, 0, rawAssembly.Length);
                return Assembly.Load(rawAssembly);
            }
        }
    }
}
