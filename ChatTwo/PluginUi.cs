using System.Numerics;
using ChatTwo.Ui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using ImGuiNET;

namespace ChatTwo;

internal sealed class PluginUi : IDisposable {
    internal Plugin Plugin { get; }

    internal bool SettingsVisible;
    internal bool ScreenshotMode;
    internal string Salt { get; }

    internal IFontHandle Axis { get; private set; }
    internal IFontHandle AxisItalic { get; private set; }

    internal IFontHandle RegularFont { get; private set; }
    internal IFontHandle? ItalicFont { get; private set; }
    internal Vector4 DefaultText { get; private set; }

    internal Tab? CurrentTab {
        get {
            var i = ChatLog.LastTab;
            if (i > -1 && i < Plugin.Config.Tabs.Count) {
                return Plugin.Config.Tabs[i];
            }

            return null;
        }
    }

    private List<IUiComponent> Components { get; }

    private FaceData _regularFont;
    private FaceData? _italicFont;
    private FaceData _jpFont;
    private FaceData _gameSymFont;

    private ushort[] _ranges;
    private ushort[] _jpRange;
    private ushort[] _symRange = [0xE020, 0xE0DB, 0];

    public readonly ChatLog ChatLog;

    internal PluginUi(Plugin plugin) {
        Plugin = plugin;
        Salt = new Random().Next().ToString();

        ChatLog = new ChatLog(this);
        Components = new List<IUiComponent> {
            new Settings(this),
            ChatLog,
        };

        var gameSym = new HttpClient().GetAsync("https://img.finalfantasyxiv.com/lds/pc/global/fonts/FFXIV_Lodestone_SSF.ttf")
            .Result
            .Content
            .ReadAsByteArrayAsync()
            .Result;
        _gameSymFont = new FaceData(gameSym);

        Plugin.Interface.UiBuilder.DisableCutsceneUiHide = true;
        Plugin.Interface.UiBuilder.DisableGposeUiHide = true;
    }

    public void Dispose() {
        foreach (var component in Components) {
            component.Dispose();
        }
    }

    public void Draw() {
        if (Plugin.Config.DatabaseMigration != Configuration.LatestDbVersion) {
            return;
        }

        Plugin.Interface.UiBuilder.DisableUserUiHide = !Plugin.Config.HideWhenUiHidden;
        DefaultText = ImGui.GetStyle().Colors[(int) ImGuiCol.Text];

        using ((Plugin.Config.FontsEnabled ? RegularFont : Axis).Push())
        {
            foreach (var component in Components) {
                try {
                    component.Draw();
                } catch (Exception ex) {
                    Plugin.Log.Error(ex, "Error drawing component");
                }
            }
        }
    }

    private byte[] GetResource(string name) {
        var stream = GetType().Assembly.GetManifestResourceStream(name)!;
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private unsafe void SetUpRanges() {
        ushort[] BuildRange(IReadOnlyList<ushort>? chars, params IntPtr[] ranges) {
            var builder = new ImFontGlyphRangesBuilderPtr(ImGuiNative.ImFontGlyphRangesBuilder_ImFontGlyphRangesBuilder());
            // text
            foreach (var range in ranges) {
                builder.AddRanges(range);
            }

            // chars
            if (chars != null) {
                for (var i = 0; i < chars.Count; i += 2) {
                    if (chars[i] == 0) {
                        break;
                    }

                    for (var j = (uint) chars[i]; j <= chars[i + 1]; j++) {
                        builder.AddChar((ushort) j);
                    }
                }
            }

            // various symbols
            builder.AddText("←→↑↓《》■※☀★★☆♥♡ヅツッシ☀☁☂℃℉°♀♂♠♣♦♣♧®©™€$£♯♭♪✓√◎◆◇♦■□〇●△▽▼▲‹›≤≥<«“”─＼～");
            // French
            builder.AddText("Œœ");
            // Romanian
            builder.AddText("ĂăÂâÎîȘșȚț");

            // "Enclosed Alphanumerics" (partial) https://www.compart.com/en/unicode/block/U+2460
            for (var i = 0x2460; i <= 0x24B5; i++) {
                builder.AddChar((char) i);
            }

            builder.AddChar('⓪');
            return builder.BuildRangesToArray();
        }

        var ranges = new List<IntPtr> {
            ImGui.GetIO().Fonts.GetGlyphRangesDefault(),
        };

        foreach (var extraRange in Enum.GetValues<ExtraGlyphRanges>()) {
            if (Plugin.Config.ExtraGlyphRanges.HasFlag(extraRange)) {
                ranges.Add(extraRange.Range());
            }
        }

        _ranges = BuildRange(null, ranges.ToArray());
        _jpRange = BuildRange(GlyphRangesJapanese.GlyphRanges);
    }

    private void SetUpUserFonts() {
        FontData? fontData = null;
        if (Plugin.Config.GlobalFont.StartsWith(Fonts.IncludedIndicator)) {
            var globalFont = Fonts.GlobalFonts.FirstOrDefault(font => font.Name == Plugin.Config.GlobalFont);
            if (globalFont != null) {
                var regular = new FaceData(GetResource(globalFont.ResourcePath));
                var italic = new FaceData(GetResource(globalFont.ResourcePathItalic));
                fontData = new FontData(regular, italic);
            }
        } else {
            fontData = Fonts.GetFont(Plugin.Config.GlobalFont, true);
        }

        if (fontData == null) {
            Plugin.Config.GlobalFont = Fonts.GlobalFonts[0].Name;
            Plugin.SaveConfig();

            var globalFont = Fonts.GlobalFonts[0];
            var regular = new FaceData(GetResource(globalFont.ResourcePath));
            var italic = new FaceData(GetResource(globalFont.ResourcePathItalic));
            fontData = new FontData(regular, italic);
        }

        _regularFont = fontData.Regular;
        _italicFont = fontData.Italic ?? null;

        FontData? jpFontData = null;
        if (Plugin.Config.JapaneseFont.StartsWith(Fonts.IncludedIndicator)) {
            var jpFont = Fonts.JapaneseFonts.FirstOrDefault(item => item.Item1 == Plugin.Config.JapaneseFont);
            if (jpFont != default) {
                jpFontData = new FontData(
                    new FaceData(GetResource(jpFont.Item2)),
                    null
                );
            }
        } else {
            jpFontData = Fonts.GetFont(Plugin.Config.JapaneseFont, false);
        }

        if (jpFontData == null) {
            Plugin.Config.JapaneseFont = Fonts.JapaneseFonts[0].Item1;
            Plugin.SaveConfig();

            var jpFont = Fonts.JapaneseFonts[0];
            jpFontData = new FontData(
                new FaceData(GetResource(jpFont.Item2)),
                null
            );
        }

        _jpFont = jpFontData.Regular;
    }

    public void BuildFonts() {
        SetUpRanges();
        SetUpUserFonts();

        Axis = Plugin.Interface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, Plugin.Config.FontSize));
        AxisItalic = Plugin.Interface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, Plugin.Config.FontSize)
        {
            SkewStrength = Plugin.Config.FontSize / 6
        });

        RegularFont = Plugin.Interface.UiBuilder.FontAtlas.NewDelegateFontHandle(
            e => e.OnPreBuild(
                tk =>
                {
                    var config = new SafeFontConfig { SizePx = Plugin.Config.FontSize, GlyphRanges = _ranges };
                    config.MergeFont = tk.AddFontFromMemory(_regularFont.Data, config, "ChatTwo2 RegularFont");

                    config.SizePx = Plugin.Config.JapaneseFontSize;
                    config.GlyphRanges = _jpRange;
                    tk.AddFontFromMemory(_jpFont.Data, config, "ChatTwo2 JP Regular");

                    config.SizePx = Plugin.Config.SymbolsFontSize;
                    config.GlyphRanges = _symRange;
                    tk.AddFontFromMemory(_gameSymFont.Data, config, "ChatTwo2 Sym Font");

                    tk.Font = config.MergeFont;
                }
            ));

        // load italic noto sans and merge in jp + game icons
        ItalicFont = null;
        if (_italicFont != null)
        {
            ItalicFont = Plugin.Interface.UiBuilder.FontAtlas.NewDelegateFontHandle(
                e => e.OnPreBuild(
                    tk =>
                    {
                        var config = new SafeFontConfig { SizePx = Plugin.Config.FontSize, GlyphRanges = _ranges };
                        config.MergeFont = tk.AddFontFromMemory(_italicFont.Data, config, "ChatTwo2 ItalicFont");

                        config.SizePx = Plugin.Config.JapaneseFontSize;
                        config.GlyphRanges = _jpRange;
                        tk.AddFontFromMemory(_jpFont.Data, config, "ChatTwo2 JP Regular");

                        config.SizePx = Plugin.Config.SymbolsFontSize;
                        config.GlyphRanges = _symRange;
                        tk.AddFontFromMemory(_gameSymFont.Data, config, "ChatTwo2 Sym Font");

                        tk.Font = config.MergeFont;
                    }
                ));
        }
    }
}
