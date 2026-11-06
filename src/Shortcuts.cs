using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PairTap
{
    internal enum PunctuationShape { Half, Full, Chinese }
    internal enum PunctuationPreference { Auto, Chinese, Half, Full }

    internal sealed class Shortcut
    {
        internal readonly string Id, Group, Name;
        internal readonly Pair Pair;
        internal readonly Keys DefaultKey;
        internal readonly char OriginalKey;
        private readonly string chineseId, halfId, fullId;

        private Shortcut(string id, string name, Keys key)
        {
            Id = id;
            Name = name;
            DefaultKey = key;
        }

        private Shortcut(string id, string group, string name, string left, string right, Keys key = Keys.None)
            : this(id, name, key)
        {
            Group = group;
            Pair = new Pair(name, left, right);
        }

        private Shortcut(string id, string name, char key, string chineseId, string halfId, string fullId)
            : this(id, name, Keys.None)
        {
            OriginalKey = key;
            this.chineseId = chineseId;
            this.halfId = halfId;
            this.fullId = fullId;
        }

        internal Pair Resolve(PunctuationShape shape)
        {
            if (Pair != null) return Pair;
            string id = shape == PunctuationShape.Chinese ? chineseId : shape == PunctuationShape.Full ? fullId : halfId;
            return Array.Find(All, entry => entry.Id == id).Pair;
        }

        internal Keys Preset(IntPtr layout)
        {
            return OriginalKey == '\0' ? DefaultKey : Native.PunctuationHotkey(OriginalKey, layout);
        }

        private const string Quotes = "引号";
        private const string Chinese = "中文括号与书名号";
        private const string Half = "半角符号";
        private const Keys Common = Keys.Control | Keys.Alt;

        internal static readonly Shortcut[] All = {
            new Shortcut("palette", "打开标点面板", Common | Keys.F6),
            new Shortcut("double-curly", Quotes, "弯双引号", "“", "”"),
            new Shortcut("single-curly", Quotes, "弯单引号", "‘", "’"),
            new Shortcut("corner", Quotes, "直角引号", "「", "」"),
            new Shortcut("double-corner", Quotes, "双直角引号", "『", "』"),
            new Shortcut("guillemets", Quotes, "双尖引号", "«", "»"),
            new Shortcut("single-guillemets", Quotes, "单尖引号", "‹", "›"),
            new Shortcut("double-wide-quote", Quotes, "全角直双引号", "＂", "＂"),
            new Shortcut("single-wide-quote", Quotes, "全角直单引号", "＇", "＇"),
            new Shortcut("wide-parens", Chinese, "全角圆括号", "（", "）"),
            new Shortcut("black-brackets", Chinese, "实心方头括号", "【", "】"),
            new Shortcut("white-brackets", Chinese, "空心方头括号", "〖", "〗"),
            new Shortcut("tortoise", Chinese, "六角括号", "〔", "〕"),
            new Shortcut("book", Chinese, "双书名号", "《", "》"),
            new Shortcut("single-book", Chinese, "单书名号", "〈", "〉"),
            new Shortcut("wide-square", Chinese, "全角方括号", "［", "］"),
            new Shortcut("wide-braces", Chinese, "全角花括号", "｛", "｝"),
            new Shortcut("wide-angle", Chinese, "全角尖括号", "＜", "＞"),
            new Shortcut("parens", Half, "半角圆括号", "(", ")"),
            new Shortcut("square", Half, "半角方括号", "[", "]"),
            new Shortcut("braces", Half, "半角花括号", "{", "}"),
            new Shortcut("angle", Half, "半角尖括号", "<", ">"),
            new Shortcut("double-straight", Half, "直双引号", "\"", "\""),
            new Shortcut("single-straight", Half, "直单引号", "'", "'"),
            new Shortcut("auto-double", "跟随样式 · 双引号", '"', "double-curly", "double-straight", "double-wide-quote"),
            new Shortcut("auto-single", "跟随样式 · 单引号", '\'', "single-curly", "single-straight", "single-wide-quote"),
            new Shortcut("auto-parens", "跟随样式 · 圆括号", '(', "wide-parens", "parens", "wide-parens"),
            new Shortcut("auto-square", "跟随样式 · 方括号键", '[', "black-brackets", "square", "wide-square"),
            new Shortcut("auto-braces", "跟随样式 · 花括号键", '{', "corner", "braces", "wide-braces"),
            new Shortcut("auto-angle", "跟随样式 · 尖括号键", '<', "book", "angle", "wide-angle"),
            new Shortcut("auto-parens-right", "跟随样式 · 右圆括号键", ')', "wide-parens", "parens", "wide-parens"),
            new Shortcut("auto-square-right", "跟随样式 · 右方括号键", ']', "black-brackets", "square", "wide-square"),
            new Shortcut("auto-braces-right", "跟随样式 · 右花括号键", '}', "corner", "braces", "wide-braces"),
            new Shortcut("auto-angle-right", "跟随样式 · 右尖括号键", '>', "book", "angle", "wide-angle")
        };
    }

    internal static class ShortcutSettings
    {
        private const Keys Required = Keys.Control | Keys.Alt;

        private static RegistryKey OpenSettings()
        {
            return Registry.CurrentUser.OpenSubKey(Product.SettingsKey) ??
                Registry.CurrentUser.OpenSubKey(Product.LegacySettingsKey);
        }

        internal static Keys[] Defaults()
        {
            IntPtr layout = Native.KeyboardLayout(Native.GetForegroundWindow());
            return Array.ConvertAll(Shortcut.All, entry => entry.Preset(layout));
        }

        internal static Keys[] Load()
        {
            Keys[] result = Defaults();
            try
            {
                using (RegistryKey key = OpenSettings())
                {
                    var values = key == null ? null : key.GetValue("Shortcuts") as string[];
                    if (values == null) return result;
                    bool legacy = Array.IndexOf(values, "version=2") < 0;
                    var items = new Dictionary<string, Keys>();
                    foreach (string value in values)
                    {
                        string[] parts = value.Split('=');
                        int code;
                        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out code) || !IsValid((Keys)code)) continue;
                        Keys binding = (Keys)code;
                        if (!legacy || !IsFormerPreset(parts[0], binding)) items[parts[0]] = binding;
                    }

                    var used = new HashSet<Keys>();
                    for (int i = 0; i < result.Length; i++)
                    {
                        Keys custom;
                        if (items.TryGetValue(Shortcut.All[i].Id, out custom))
                            result[i] = custom == Keys.None || used.Add(custom) ? custom : Keys.None;
                    }
                    for (int i = 0; i < result.Length; i++)
                        if (!items.ContainsKey(Shortcut.All[i].Id) && result[i] != Keys.None && !used.Add(result[i]))
                            result[i] = Keys.None;
                }
            }
            catch (System.Security.SecurityException) { return Defaults(); }
            catch (System.IO.IOException) { return Defaults(); }
            return result;
        }

        private static bool IsFormerPreset(string id, Keys key)
        {
            const Keys common = Keys.Control | Keys.Alt;
            return (id == "double-curly" && key == (common | Keys.F7)) ||
                (id == "wide-parens" && key == (common | Keys.F8)) ||
                (id == "book" && key == (common | Keys.F9));
        }

        internal static PunctuationPreference Preference()
        {
            try
            {
                using (RegistryKey key = OpenSettings())
                {
                    var values = key == null ? null : key.GetValue("Shortcuts") as string[];
                    if (values == null) return PunctuationPreference.Auto;
                    foreach (string value in values)
                    {
                        int code;
                        if (value.StartsWith("style=", StringComparison.Ordinal) &&
                            int.TryParse(value.Substring(6), NumberStyles.None, CultureInfo.InvariantCulture, out code) &&
                            Enum.IsDefined(typeof(PunctuationPreference), code))
                            return (PunctuationPreference)code;
                    }
                }
            }
            catch (System.Security.SecurityException) { }
            return PunctuationPreference.Auto;
        }

        internal static void Save(Keys[] keys, PunctuationPreference preference)
        {
            var defaults = Defaults();
            var values = new List<string> { "version=2", "style=" + ((int)preference).ToString(CultureInfo.InvariantCulture) };
            for (int i = 0; i < keys.Length; i++)
                if (keys[i] != defaults[i])
                    values.Add(Shortcut.All[i].Id + "=" + ((int)keys[i]).ToString(CultureInfo.InvariantCulture));
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Product.SettingsKey))
                key.SetValue("Shortcuts", values.ToArray(), RegistryValueKind.MultiString);
        }

        internal static bool IsValid(Keys keys)
        {
            if (keys == Keys.None) return true;
            Keys key = keys & Keys.KeyCode;
            Keys modifiers = keys & Keys.Modifiers;
            if ((modifiers & Required) == 0 || (modifiers & ~(Required | Keys.Shift)) != 0) return false;
            if ((modifiers == Keys.Control && (key == Keys.C || key == Keys.V)) || key == Keys.F12) return false;
            return (key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9)
                || (key >= Keys.NumPad0 && key <= Keys.Divide) || (key >= Keys.F1 && key <= Keys.F24)
                || (key >= Keys.OemSemicolon && key <= Keys.Oemtilde)
                || (key >= Keys.OemOpenBrackets && key <= Keys.OemBackslash)
                || key == Keys.Space || key == Keys.Enter || key == Keys.Back || key == Keys.Delete
                || key == Keys.Insert || (key >= Keys.PageUp && key <= Keys.Down);
        }

        internal static uint NativeModifiers(Keys keys)
        {
            uint result = 0x4000;
            if ((keys & Keys.Alt) != 0) result |= 1;
            if ((keys & Keys.Control) != 0) result |= 2;
            if ((keys & Keys.Shift) != 0) result |= 4;
            return result;
        }

        internal static string Describe(Keys keys)
        {
            if (keys == Keys.None) return "";
            var parts = new List<string>();
            if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keys & Keys.Alt) != 0) parts.Add("Alt");
            if ((keys & Keys.Shift) != 0) parts.Add("Shift");
            Keys key = keys & Keys.KeyCode;
            if (key >= Keys.D0 && key <= Keys.D9) parts.Add((key - Keys.D0).ToString());
            else if (key >= Keys.NumPad0 && key <= Keys.NumPad9) parts.Add("Num" + (key - Keys.NumPad0));
            else
            {
                string name;
                if (!KeyNames.TryGetValue(key, out name)) name = key.ToString();
                parts.Add(name);
            }
            return string.Join("+", parts);
        }

        private static readonly Dictionary<Keys, string> KeyNames = new Dictionary<Keys, string> {
            { Keys.OemSemicolon, ";" }, { Keys.Oemplus, "=" }, { Keys.Oemcomma, "," },
            { Keys.OemMinus, "-" }, { Keys.OemPeriod, "." }, { Keys.OemQuestion, "/" },
            { Keys.Oemtilde, "~" }, { Keys.OemOpenBrackets, "[" }, { Keys.OemPipe, "\\" },
            { Keys.OemCloseBrackets, "]" }, { Keys.OemQuotes, "'" },
            { Keys.OemBackslash, "OEM102" }, { Keys.Back, "Backspace" }, { Keys.Return, "Enter" },
            { Keys.PageUp, "PgUp" }, { Keys.PageDown, "PgDn" }
        };
    }
}
