using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace HowToSuck
{
    // Labels describe physical binding paths, never the current OS keyboard layout.
    public static class InputBindingLabels
    {
        public const string Unbound = "—";
        public static string Binding(InputAction action, int index)
        {
            if (action == null || index < 0 || index >= action.bindings.Count) return Unbound;
            return Path(action.bindings[index].effectivePath);
        }
        public static string Action(InputAction action)
        {
            if (action == null) return Unbound;
            if (action.bindings.Count == 1) return Binding(action, 0);
            var labels = new List<string>();
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (action.bindings[i].isComposite) continue;
                string label = Binding(action, i);
                if (label != Unbound && !labels.Contains(label)) labels.Add(label);
            }
            return labels.Count == 0 ? Unbound : string.Join(" / ", labels);
        }
        public static string Path(string path)
        {
            if (string.IsNullOrEmpty(path)) return Unbound;
            bool mouse = path.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase);
            bool keyboard = path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase);
            if (!mouse && !keyboard) return Unbound;
            string key = path.Substring(mouse ? 8 : 11).ToLowerInvariant();
            if (mouse)
            {
                switch (key)
                {
                    case "leftbutton": return "LMB";
                    case "rightbutton": return "RMB";
                    case "middlebutton": return "MMB";
                    case "backbutton": return "MB4";
                    case "forwardbutton": return "MB5";
                    default: return Unbound;
                }
            }
            if (key.Length == 1 && key[0] >= 'a' && key[0] <= 'z') return key.ToUpperInvariant();
            if (key.StartsWith("digit") && key.Length == 6 && char.IsDigit(key[5])) return key.Substring(5);
            if (key.StartsWith("numpad") && key.Length == 7 && char.IsDigit(key[6])) return "NUM " + key.Substring(6);
            switch (key)
            {
                case "space": return "SPACE";
                case "enter": return "ENTER";
                case "tab": return "TAB";
                case "escape": return "ESC";
                case "backspace": return "BACKSPACE";
                case "leftshift": return "L SHIFT";
                case "rightshift": return "R SHIFT";
                case "leftctrl": return "L CTRL";
                case "rightctrl": return "R CTRL";
                case "leftalt": return "L ALT";
                case "rightalt": return "R ALT";
                case "leftmeta": case "leftwindows": case "leftcommand": return "L META";
                case "rightmeta": case "rightwindows": case "rightcommand": return "R META";
                case "capslock": return "CAPS LOCK";
                case "numlock": return "NUM LOCK";
                case "scrolllock": return "SCROLL LOCK";
                case "printscreen": return "PRINT SCREEN";
                case "pause": return "PAUSE";
                case "contextmenu": return "MENU";
                case "uparrow": return "UP";
                case "downarrow": return "DOWN";
                case "leftarrow": return "LEFT";
                case "rightarrow": return "RIGHT";
                case "pageup": return "PAGE UP";
                case "pagedown": return "PAGE DOWN";
                case "home": return "HOME";
                case "end": return "END";
                case "insert": return "INSERT";
                case "delete": return "DELETE";
                case "backquote": return "`";
                case "quote": return "'";
                case "semicolon": return ";";
                case "comma": return ",";
                case "period": return ".";
                case "slash": return "/";
                case "backslash": return "\\";
                case "leftbracket": return "[";
                case "rightbracket": return "]";
                case "minus": return "-";
                case "equals": return "=";
                case "numpadenter": return "NUM ENTER";
                case "numpaddivide": return "NUM /";
                case "numpadmultiply": return "NUM *";
                case "numpadplus": return "NUM +";
                case "numpadminus": return "NUM -";
                case "numpadperiod": return "NUM .";
                case "numpadequals": return "NUM =";
                case "oem1": return "OEM 1";
                case "oem2": return "OEM 2";
                case "oem3": return "OEM 3";
                case "oem4": return "OEM 4";
                case "oem5": return "OEM 5";
                case "imeselected": case "imeselectedobsoletekey": return "IME";
            }
            // Function and future concrete key names stay ASCII; never echo a localized display-name path.
            if (key.Length == 0) return Unbound;
            foreach (char c in key) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9')) return Unbound;
            return key.ToUpperInvariant();
        }
    }
}
