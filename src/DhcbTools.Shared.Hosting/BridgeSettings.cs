using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Công tắc HTTP Bridge trong <c>%APPDATA%\DHCB\settings.json</c>: <c>{"bridge": {"enabled": false}}</c>.
    /// Mặc định BẬT (giữ hành vi cũ — agent/MCP/panel đều dựa vào Bridge); máy không dùng agent thì tắt để
    /// không mở cổng 8765/8766 nào. Cùng file với công tắc updater của Revit.
    /// </summary>
    public sealed class BridgeSettings
    {
        public bool Enabled { get; private set; } = true;

        /// <summary>Lý do file không đọc được (Bridge vẫn bật theo mặc định) — vỏ ghi vào log để người dùng biết công tắc không ăn.</summary>
        public string? Warning { get; private set; }

        public static string DefaultPath => Path.Combine(BridgeTokenStore.DefaultDirectory, "settings.json");

        public static BridgeSettings Load(string? path = null)
        {
            var file = path ?? DefaultPath;
            var settings = new BridgeSettings();
            if (!File.Exists(file))
            {
                return settings;
            }

            try
            {
                var bridge = JObject.Parse(File.ReadAllText(file))["bridge"];
                var enabled = (bridge as JObject)?["enabled"];
                if (bridge == null || (bridge is JObject && enabled == null))
                {
                    return settings;
                }

                if (enabled == null || enabled.Type != JTokenType.Boolean)
                {
                    settings.Warning = "settings.json: cần dạng {\"bridge\": {\"enabled\": false}} — Bridge vẫn BẬT.";
                    return settings;
                }

                settings.Enabled = enabled.Value<bool>();
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            {
                settings.Warning = "Không đọc được " + file + " (" + ex.Message + ") — Bridge vẫn BẬT.";
            }

            return settings;
        }
    }
}
