namespace PairTap
{
    internal static class Product
    {
        internal const string Name = "PairTap";
        internal const string ChineseName = "对符输入";
        internal const string DisplayName = ChineseName + " · " + Name;
        internal const string Version = "1.3.7.0";
        internal const string SettingsKey = @"Software\" + Name;
        internal const string InstanceName = @"Local\" + Name + ".63BA74EF";
        internal const string OpenEventName = @"Local\" + Name + ".Open.63BA74EF";
        internal const string ExitEventName = @"Local\" + Name + ".Exit.63BA74EF";

        // Retain the former identifier so an upgrade or portable launch can read existing settings.
        internal const string LegacyId = "SurroundSelection";
        internal const string LegacySettingsKey = @"Software\" + LegacyId;
    }
}
