using AoE4OverlayCS;
using AoE4OverlayCS.Models;
using AoE4OverlayCS.Services;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Windows.Media.Imaging;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
    Console.WriteLine("PASS " + message);
}
foreach (var civ in new[] { (Key: "vikings", En: "Vikings", Zh: "维京人"), (Key: "scots", En: "Scots", Zh: "苏格兰人") })
{
    Check(CivNameTranslator.Translate(civ.Key, "zh-CN") == civ.Zh, civ.Key + " Chinese name");
    Check(CivNameTranslator.Translate(civ.Key, "en-US") == civ.En, civ.Key + " canonical English name");
    var path = CivIconResolver.Resolve(root, civ.Key, civ.Key);
    Check(path != null && File.Exists(path), civ.Key + " WPF flag path");
}
foreach (var file in Directory.GetFiles(Path.Combine(root, "img", "flags"), "*.png"))
{
    var civ = Path.GetFileNameWithoutExtension(file);
    Check(CivIconResolver.Resolve(root, civ, civ.Replace(' ', '_').ToLowerInvariant()) != null, civ + " local flag resolves");
    Check(CivNameTranslator.Translate(civ, "zh-CN") != civ, civ + " Chinese translation exists");
    var decoder = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    Check(decoder.Frames[0].PixelWidth > 0 && decoder.Frames[0].PixelHeight > 0, civ + " PNG decodes in WPF");
}
Check(CivIconResolver.Resolve(root, "jin_dynasty", "jin_dynasty") != null, "Jin Dynasty underscore alias");
Check(CivIconResolver.Resolve(root, "zhu_xis_legacy", "zhu_xis_legacy") != null, "Zhu Xi underscore alias");
Check(CivNameTranslator.Translate("Jeanne d’Arc", "zh-CN") == "贞德", "typographic apostrophe normalized");
Check(CivNameTranslator.Translate("zhu_xis_legacy", "en-US") == "Zhu Xi's Legacy", "English API slug normalized");
Check(CivNameTranslator.Translate("future_civilization", "zh-CN") == "future_civilization", "unknown civilization preserved");
var game = JObject.Parse("""
    { "map": "Dry Arabia", "kind": "rm_1v1", "teams": [
        [{ "name": "A", "civilization": "vikings", "profile_id": "1" }],
        [{ "name": "B", "civilization": "scots", "profile_id": "2" }]
    ] }
    """);
foreach (var language in new[] { "zh-CN", "en-US" })
{
    var processed = JObject.FromObject(GameProcessor.ProcessGame((JObject)game.DeepClone(), new AppSettings { Language = language }));
    Check(processed["players"]![0]!["civ_display"]!.ToString() == (language == "zh-CN" ? "维京人" : "Vikings"), language + " processor propagates Vikings display name");
    Check(processed["players"]![1]!["civ_display"]!.ToString() == (language == "zh-CN" ? "苏格兰人" : "Scots"), language + " processor propagates Scots display name");
}
foreach (var file in Directory.GetFiles(Path.Combine(root, "img", "aoe2-civs"), "*.png"))
{
    var key = Path.GetFileNameWithoutExtension(file);
    var ids = Enumerable.Range(0, 64).Where(id => Aoe2Catalog.CivEnglish(id).Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
    Check(ids.Count == 1 && Aoe2Catalog.CivDisplay(ids[0], true) != Aoe2Catalog.CivEnglish(ids[0]), "AOE2 " + key + " icon has bilingual catalog entry");
}
Exception? renderError = null;
var renderThread = new Thread(() =>
{
    try
    {
        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var window = new AoE4OverlayCS.Views.OverlayWindow(new AppSettings { Language = language });
            var createFlag = typeof(AoE4OverlayCS.Views.OverlayWindow).GetMethod("CreateCivFlag", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            foreach (var civ in new[] { "vikings", "scots", "jin_dynasty" })
            {
                var flag = (System.Windows.Controls.Image)createFlag.Invoke(window, new object[] { new JObject { ["civ"] = civ }, root, System.Windows.HorizontalAlignment.Left })!;
                Check(flag.Source != null && flag.Height == 36, language + " " + civ + " real WPF flag rendering");
                Check(flag.ToolTip?.ToString() == CivNameTranslator.Translate(civ, language), language + " " + civ + " localized flag label");
            }
            window.Close();
        }
    }
    catch (Exception ex) { renderError = ex; }
});
renderThread.SetApartmentState(ApartmentState.STA);
renderThread.Start();
renderThread.Join();
if (renderError != null) throw new Exception("Overlay rendering failed", renderError);
Console.WriteLine($"All {checks} overlay checks passed.");
