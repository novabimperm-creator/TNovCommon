namespace TNovCommon
{

    public static class nova
    {

#if config1
        public static string novaserver = "//fs-nova/Distr/0.For Admin/";
        public static string novafolder = "//fs-nova/Distr/0.For Admin/_TNov/actual/config1/";
        public static string revitserver = "rvt-nova";
#elif config2
        public static string novaserver = "//fs27/NOVA-VOSTOK/NovaService/";
        public static string novafolder = "//fs27/NOVA-VOSTOK/NovaService/_TNov/actual/config2/";
        public static string revitserver = "rvt27";
#else
        public static string novaserver = "//fs27/NOVA-VOSTOK/NovaService/";
        public static string novafolder = "//192.168.159.2/Distr/0.For Admin/_TNov/actual/config2";
        public static string revitserver = "rvt27";
#endif

    }
}
