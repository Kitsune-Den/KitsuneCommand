namespace KitsuneCommand.Shared
{
    public static class ModConstants
    {
        public const string ModName = "KitsuneCommand";
        // The version isn't here: it's ModInfo.xml, read into ModEntry.ModVersion
        // at startup (and bumped by tools/bump-version).
        public const string CommandPrefix = "kc-";
        public const string HarmonyId = "com.kitsunecommand.mod";
    }
}
