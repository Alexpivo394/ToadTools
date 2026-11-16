namespace ToadTools.Desktop.Core.Settings;

public sealed class SettingsStore(string file) : JsonFileStore<UpdateSettings>(file);
