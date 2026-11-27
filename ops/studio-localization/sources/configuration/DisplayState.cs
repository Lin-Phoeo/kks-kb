using System;
using System.Reflection;
using ConfigurationManager;

namespace Codex.StudioConfigLocalization
{
    internal sealed class DisplayState
    {
        private static readonly PropertyInfo NameProperty = typeof(SettingEntryBase).GetProperty("DispName");
        private static readonly PropertyInfo DescriptionProperty = typeof(SettingEntryBase).GetProperty("Description");
        private readonly SettingEntryBase _setting;
        private readonly string _name;
        private readonly string _description;

        internal DisplayState(SettingEntryBase setting, string name, string description)
        {
            _setting = setting;
            _name = setting.DispName;
            _description = setting.Description;
            try
            {
                if (name != null) NameProperty.SetValue(setting, name, null);
                if (description != null) DescriptionProperty.SetValue(setting, description, null);
            }
            catch { Restore(); throw; }
        }

        internal void Restore()
        {
            NameProperty.SetValue(_setting, _name, null);
            DescriptionProperty.SetValue(_setting, _description, null);
        }
    }
}
